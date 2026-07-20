<#
.SYNOPSIS
  WoW Sync v1.1 - sync World of Warcraft addons + addon settings across PCs
  using any cloud-synced folder (Google Drive, OneDrive, Dropbox). No server.

.DESCRIPTION
  Two-layer design:

    * AddOns  -> moved into the cloud folder, with a directory junction left at
                 Interface\AddOns. Addon installs/updates sync live.
    * Settings (WTF\Account) -> stays a REAL local folder; WoW never depends on
                 the cloud drive. A tiny background agent (wowsync-agent.ps1,
                 installed to %LOCALAPPDATA%\WoWSync) pushes settings to the
                 cloud when WoW closes and pulls them when the cloud is newer.

  Run on your MAIN PC first (seeds the cloud), then on every other PC after
  the cloud folder has finished downloading.

  If v1.0 of WoW Sync linked your WTF\Account folder, this script converts it
  back to a normal local folder automatically.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\Setup-WoWSync.ps1 -CloudFolder "G:\My Drive\WoW Sync"

.NOTES
  - Close WoW before running. Battle.net can stay open.
  - Keep wowsync-agent.ps1 next to this script.
  - Originals are kept as "<folder>.pre-sync-backup" - nothing is deleted.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$CloudFolder,

    [string]$WowPath = "C:\Program Files (x86)\World of Warcraft",

    [string]$Flavor = "_retail_"
)

$ErrorActionPreference = 'Stop'

function Fail($msg) { Write-Host "ERROR: $msg" -ForegroundColor Red; exit 1 }

function FileCount($path) {
    if (-not (Test-Path $path)) { return 0 }
    return (Get-ChildItem $path -Recurse -File -ErrorAction SilentlyContinue | Measure-Object).Count
}

function NewestWrite($path) {
    if (-not (Test-Path $path)) { return [datetime]::MinValue }
    $f = Get-ChildItem $path -Recurse -File -ErrorAction SilentlyContinue |
         Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if ($f) { return $f.LastWriteTimeUtc } else { return [datetime]::MinValue }
}

function Robo($src, $dst, $mirror, $label) {
    New-Item -ItemType Directory -Force $dst | Out-Null
    $flags = @("/E"); if ($mirror) { $flags = @("/MIR") }
    $null = robocopy $src $dst @flags /MT:8 /R:2 /W:2 /NFL /NDL /NJH /NP
    if ($LASTEXITCODE -ge 8) { Fail "$label failed (robocopy code $LASTEXITCODE)." }
}

function UniqueBackup($folder) {
    $b = "$folder.pre-sync-backup"
    if (Test-Path $b) { $b = "$b-" + (Get-Date -Format "yyyyMMdd-HHmmss") }
    return $b
}

# --- Preflight ---------------------------------------------------------------
if (Get-Process -Name "Wow", "WowClassic", "WowT", "WowB" -ErrorAction SilentlyContinue) {
    Fail "World of Warcraft is running. Close the game completely, then re-run this script."
}

$flavorPath = Join-Path $WowPath $Flavor
if (-not (Test-Path $flavorPath)) {
    Fail "Can't find '$flavorPath'. Pass -WowPath (and -Flavor) matching your install."
}
if (-not (Test-Path $CloudFolder)) {
    Fail "Cloud folder '$CloudFolder' doesn't exist. Create it inside your synced drive first (and make sure your cloud app is running)."
}

$agentSource = Join-Path $PSScriptRoot "wowsync-agent.ps1"
if (-not (Test-Path $agentSource)) {
    Fail "wowsync-agent.ps1 must sit next to this script."
}

$cloudRoot   = Join-Path $CloudFolder $Flavor
$gameAddons  = Join-Path $flavorPath "Interface\AddOns"
$cloudAddons = Join-Path $cloudRoot "AddOns"
$gameAcct    = Join-Path $flavorPath "WTF\Account"
$cloudAcct   = Join-Path $cloudRoot "WTF-Account"
New-Item -ItemType Directory -Force $cloudRoot | Out-Null

# --- AddOns: junction into the cloud ----------------------------------------
Write-Host ""
Write-Host ">>> AddOns" -ForegroundColor Yellow

$addonsItem = Get-Item $gameAddons -ErrorAction SilentlyContinue
if ($addonsItem -and ($addonsItem.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
    Write-Host "    Already linked - skipping."
} else {
    $cloudSeeded = (FileCount $cloudAddons) -gt 0
    if (-not $cloudSeeded) {
        if ((FileCount $gameAddons) -eq 0) { Fail "No addons found locally and none in the cloud. Run this on your main PC first." }
        Write-Host "    Cloud is empty - copying this PC's addons up (can take a few minutes)..."
        Robo $gameAddons $cloudAddons $false "addons copy to cloud"
    }

    if (Test-Path $gameAddons) {
        $backup = UniqueBackup $gameAddons
        $renamed = $false
        for ($i = 0; $i -lt 5; $i++) {
            try { Rename-Item $gameAddons (Split-Path $backup -Leaf) -ErrorAction Stop; $renamed = $true; break }
            catch { Start-Sleep -Seconds 3 }
        }
        if (-not $renamed) { Fail "Windows is holding the AddOns folder open. Reboot and re-run this script." }
        Write-Host "    Old AddOns folder kept as: $backup"
    } else {
        New-Item -ItemType Directory -Force (Split-Path $gameAddons) | Out-Null
    }

    try {
        New-Item -ItemType Junction -Path $gameAddons -Value $cloudAddons | Out-Null
    } catch {
        # Junction (/J), not symlink (/D): junctions don't require elevation
        # or Developer Mode, and WoW treats them as ordinary folders.
        cmd /c mklink /J "$gameAddons" "$cloudAddons" | Out-Null
        if ($LASTEXITCODE -ne 0) { Fail "Couldn't create the AddOns link. Re-run from an ADMINISTRATOR PowerShell." }
    }
    Write-Host "    Linked: $gameAddons -> $cloudAddons" -ForegroundColor Green
    Write-Host "    Verified: $(FileCount $gameAddons) addon files visible to the game."
}

# Cloud clients keep files "online-only" by default: the name exists but the
# contents aren't on disk until something opens them. WoW reading addons through
# the link would then see missing/partial files. Windows flags such files with
# recall attributes - check for them and say so plainly, because the symptom
# (missing addons, Lua errors) looks like a broken install rather than an
# unfinished download.
$placeholders = 0
$checkedFiles = 0
try {
    foreach ($f in (Get-ChildItem $cloudAddons -Recurse -File -ErrorAction SilentlyContinue | Select-Object -First 5000)) {
        $a = [int]$f.Attributes
        # RECALL_ON_OPEN | RECALL_ON_DATA_ACCESS | OFFLINE
        if (($a -band 0x00040000) -or ($a -band 0x00400000) -or ($a -band 0x00001000)) { $placeholders++ }
        $checkedFiles++
    }
} catch { $checkedFiles = 0 }

if ($checkedFiles -gt 0 -and $placeholders -eq 0) {
    Write-Host "    Addon files are stored on this PC (not online-only) - good." -ForegroundColor Green
} elseif ($placeholders -gt 0) {
    Write-Host ""
    Write-Host "*** ACTION NEEDED BEFORE YOU START WOW ***" -ForegroundColor Red
    Write-Host "    $placeholders of the $checkedFiles addon files checked are still ONLINE-ONLY -"
    Write-Host "    your cloud app hasn't downloaded them to this PC yet."
    Write-Host "    If you launch WoW now, addons will be missing or throw Lua errors."
    Write-Host ""
    Write-Host "    Fix: right-click your WoW Sync folder ->"
    Write-Host "         'Offline access' -> 'Available offline'  (Google Drive)"
    Write-Host "         'Always keep on this device'             (OneDrive)"
    Write-Host "    Wait until the folder stops showing cloud icons, then start the game."
}

# --- Settings: real local folder + reconcile ---------------------------------
Write-Host ""
Write-Host ">>> Settings (WTF\Account)" -ForegroundColor Yellow

$agentDir   = Join-Path $env:LOCALAPPDATA "WoWSync"
$joinMarker = Join-Path $agentDir ("joined-" + $Flavor.Trim('_') + ".marker")
New-Item -ItemType Directory -Force $agentDir | Out-Null

# Stop any agent for this game version FIRST - a running agent could push or
# pull underneath the reconciliation below.
function StopAgents($localPath) {
    # Match the -File argument specifically: a bare "*wowsync-agent*" test also
    # matches any shell that merely MENTIONS the agent, and we must not kill
    # someone's console. Agents for another flavor are left running.
    Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" -ErrorAction SilentlyContinue |
        Where-Object {
            $_.ProcessId -ne $PID -and
            $_.CommandLine -match '-File\s+"?[^"]*wowsync-agent\.ps1' -and
            ( $_.CommandLine -notlike "*-LocalSettings*" -or
              $_.CommandLine -like ("*" + [Management.Automation.WildcardPattern]::Escape($localPath) + "*") )
        } |
        ForEach-Object { try { Stop-Process -Id $_.ProcessId -Force } catch {} }
}
StopAgents $gameAcct

# v1.0 migration: convert a linked WTF\Account back to a real folder.
# Copy the data out BEFORE removing the link - otherwise a missing or still-
# downloading cloud folder would leave you with no settings at all.
$acctItem = Get-Item $gameAcct -ErrorAction SilentlyContinue
if ($acctItem -and ($acctItem.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
    Write-Host "    Found a v1.0 settings link - converting back to a normal local folder..."
    if ((FileCount $cloudAcct) -eq 0) {
        Fail "That link points at a cloud folder I can't read right now, so there's nothing to restore from. Make sure your cloud app is running and the folder has finished downloading, then re-run. (Your settings are NOT lost - they're in the cloud folder.)"
    }
    $staging = "$gameAcct.restoring"
    if (Test-Path $staging) { Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue }
    Robo $cloudAcct $staging $false "settings restore staging"
    if ((FileCount $staging) -eq 0) { Fail "Restore copy came out empty - stopping before changing anything." }
    cmd /c rmdir "$gameAcct"   # removes only the link, never the target
    if (Test-Path $gameAcct) { Fail "Couldn't remove the old settings link. Reboot and re-run. (Nothing was changed.)" }
    Rename-Item $staging (Split-Path $gameAcct -Leaf)
    Write-Host "    Restored $(FileCount $gameAcct) settings files locally."
    Set-Content -Path $joinMarker -Value (Get-Date -Format u)   # part of an established sync
}

$cloudHas = (FileCount $cloudAcct) -gt 0
$localHas = (FileCount $gameAcct) -gt 0

if (-not $cloudHas -and -not $localHas) {
    Write-Host "WARNING: No settings found on either side. Log into the game once, then re-run." -ForegroundColor Yellow
} elseif (-not $cloudHas) {
    Write-Host "    Cloud is empty - this PC becomes the master copy."
    Robo $gameAcct $cloudAcct $false "settings copy to cloud"
} elseif (-not $localHas) {
    Write-Host "    No local settings - downloading the cloud copy..."
    Robo $cloudAcct $gameAcct $false "settings download"
} else {
    # Both sides have settings. NEVER use "newest wins" for a PC joining an
    # existing sync: a fresh WoW install that's been logged into once has
    # brand-new default settings, which would look "newer" than the master
    # copy and mirror straight over it - wiping the real config everywhere.
    if (-not (Test-Path $joinMarker)) {
        Write-Host "    This PC hasn't synced before and the cloud already has settings,"
        Write-Host "    so the cloud copy wins (it's the established master)."
        $backup = UniqueBackup $gameAcct
        Robo $gameAcct $backup $false "settings backup"
        Write-Host "    This PC's old settings kept as: $backup"
        Robo $cloudAcct $gameAcct $true "settings download"
    } else {
        $localNewest = NewestWrite $gameAcct
        $cloudNewest = NewestWrite $cloudAcct
        if ($cloudNewest -gt $localNewest.AddMinutes(2)) {
            Write-Host "    Cloud settings are newer - using the cloud copy."
            $backup = UniqueBackup $gameAcct
            Robo $gameAcct $backup $false "settings backup"
            Write-Host "    This PC's old settings kept as: $backup"
            Robo $cloudAcct $gameAcct $true "settings download"
        } else {
            Write-Host "    This PC's settings are current - pushing them to the cloud."
            $cloudBackup = "$cloudAcct.replaced-" + (Get-Date -Format "yyyyMMdd-HHmmss")
            Robo $cloudAcct $cloudBackup $false "cloud settings backup"
            Write-Host "    Previous cloud copy kept as: $(Split-Path $cloudBackup -Leaf)"
            Robo $gameAcct $cloudAcct $true "settings push"
        }
    }
}
Set-Content -Path $joinMarker -Value (Get-Date -Format u)
Write-Host "    Settings stay in a normal local folder - the background agent keeps them synced." -ForegroundColor Green

# --- Background agent --------------------------------------------------------
Write-Host ""
Write-Host ">>> Background sync agent" -ForegroundColor Yellow

$agentDest = Join-Path $agentDir "wowsync-agent.ps1"
Copy-Item $agentSource $agentDest -Force

$agentArgs = "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$agentDest`" -CloudSettings `"$cloudAcct`" -LocalSettings `"$gameAcct`""
$runName = "WoWSyncAgent-" + $Flavor.Trim('_')
Set-ItemProperty -Path "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" -Name $runName -Value "powershell.exe $agentArgs"

# Agents for this game version were already stopped above; catch any that
# started since (e.g. a login race) before launching the new one.
StopAgents $gameAcct

Start-Process powershell.exe -ArgumentList $agentArgs -WindowStyle Hidden
Write-Host "    Agent installed and running (auto-starts at login)." -ForegroundColor Green
Write-Host "    Log files: $agentDir\agent-*.log"

Write-Host ""
Write-Host "Done!" -ForegroundColor Green
Write-Host ""
Write-Host "IMPORTANT - do this now, before you start WoW:" -ForegroundColor Yellow
Write-Host "  Right-click '$CloudFolder'"
Write-Host "  -> Offline access -> Available offline   (Google Drive)"
Write-Host "  -> Always keep on this device            (OneDrive)"
Write-Host "  Then wait until every item shows the 'on this device' icon rather than a"
Write-Host "  cloud icon. Your addons are read through a link into that folder, so if the"
Write-Host "  download hasn't finished, WoW will start with addons missing or Lua errors."
Write-Host "  (Nothing is broken if that happens - close WoW, let it finish, start again.)"
Write-Host ""
Write-Host "Also:"
Write-Host "  - On your other PC(s): install your cloud client, mark the folder available"
Write-Host "    offline, let it finish downloading, THEN run this script there."
Write-Host "  - Don't play WoW on two PCs at the same time."
