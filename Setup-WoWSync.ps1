<#
.SYNOPSIS
  Sync World of Warcraft addons + addon settings across PCs using any
  cloud-synced folder (Google Drive, OneDrive, Dropbox, etc). No server needed.

.DESCRIPTION
  Moves your AddOns folder and your WTF\Account folder (addon settings, UI
  layout, saved variables) into a cloud-synced folder, then puts a "junction"
  (a folder shortcut WoW can't tell apart from a real folder) at the original
  locations. Your cloud client does the syncing; every PC sees the same files.

  Run it on your FIRST PC (the one whose addons/settings you want to keep):
  it seeds the cloud folder. Run it on every OTHER PC after the cloud client
  has finished downloading: it backs up the local folders and links to the
  cloud copy.

  Machine-specific graphics settings (WTF\Config.wtf) are NOT synced - each
  PC keeps its own.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\Setup-WoWSync.ps1 -CloudFolder "G:\My Drive\WoW Sync"

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\Setup-WoWSync.ps1 -CloudFolder "C:\Users\you\OneDrive\WoW Sync" -Flavor _classic_

.NOTES
  - Close WoW before running. Battle.net can stay open.
  - IMPORTANT: mark the cloud folder "Available offline" (Google Drive) or
    "Always keep on this device" (OneDrive) so the game reads local files.
  - Don't play on two PCs at the same time with synced settings - last one
    to log out wins, and some cloud clients create conflict copies.
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

# --- Preflight ---------------------------------------------------------------
if (Get-Process -Name "Wow*" -ErrorAction SilentlyContinue) {
    Fail "World of Warcraft is running. Close the game completely, then re-run this script."
}

$flavorPath = Join-Path $WowPath $Flavor
if (-not (Test-Path $flavorPath)) {
    Fail "Can't find '$flavorPath'. Pass -WowPath (and -Flavor) matching your install."
}
if (-not (Test-Path $CloudFolder)) {
    Fail "Cloud folder '$CloudFolder' doesn't exist. Create it inside your synced drive first."
}

$cloudRoot = Join-Path $CloudFolder $Flavor

# game-side path => cloud-side folder name
$pairs = @(
    @{ Game = Join-Path $flavorPath "Interface\AddOns"; Cloud = Join-Path $cloudRoot "AddOns";       Label = "AddOns" },
    @{ Game = Join-Path $flavorPath "WTF\Account";      Cloud = Join-Path $cloudRoot "WTF-Account";  Label = "Settings (WTF\Account)" }
)

# Seed or join? If the cloud copy already has files, this is a second machine.
$cloudSeeded = (Test-Path (Join-Path $cloudRoot "AddOns")) -and
               ((Get-ChildItem (Join-Path $cloudRoot "AddOns") -ErrorAction SilentlyContinue | Measure-Object).Count -gt 0)

if ($cloudSeeded) {
    Write-Host "Cloud folder already seeded - linking this PC to the existing cloud copy." -ForegroundColor Cyan
} else {
    Write-Host "Cloud folder is empty - seeding it from this PC's files." -ForegroundColor Cyan
}

foreach ($p in $pairs) {
    $game  = $p.Game
    $cloud = $p.Cloud
    Write-Host ""
    Write-Host ">>> $($p.Label)" -ForegroundColor Yellow

    # Already linked? Nothing to do.
    $item = Get-Item $game -ErrorAction SilentlyContinue
    if ($item -and ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        Write-Host "    Already a link - skipping."
        continue
    }

    # Seed the cloud copy from this machine if needed.
    if (-not $cloudSeeded) {
        if (-not (Test-Path $game)) {
            Fail "'$game' doesn't exist, so there's nothing to seed. Run WoW once, or seed from your main PC first."
        }
        New-Item -ItemType Directory -Force $cloud | Out-Null
        Write-Host "    Copying to cloud (can take a few minutes)..."
        robocopy $game $cloud /E /MT:16 /R:2 /W:2 /NFL /NDL /NJH /NP | Out-Null
        if ($LASTEXITCODE -ge 8) { Fail "Copy to '$cloud' failed (robocopy code $LASTEXITCODE)." }
    } elseif (-not (Test-Path $cloud)) {
        Fail "Cloud copy '$cloud' not found. Wait for your cloud client to finish downloading, then re-run."
    }

    # Back up the local folder (never delete).
    if (Test-Path $game) {
        $backup = "$game.pre-sync-backup"
        if (Test-Path $backup) { $backup = "$game.pre-sync-backup-" + (Get-Date -Format "yyyyMMdd-HHmmss") }
        Rename-Item $game $backup
        Write-Host "    Local folder kept as: $backup"
    } else {
        # Fresh install may not have Interface\ or WTF\ yet.
        New-Item -ItemType Directory -Force (Split-Path $game) | Out-Null
    }

    # Create the junction; fall back to a symlink (needs admin) if that fails.
    try {
        New-Item -ItemType Junction -Path $game -Value $cloud | Out-Null
        Write-Host "    Linked: $game -> $cloud" -ForegroundColor Green
    } catch {
        cmd /c mklink /D "$game" "$cloud" | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Fail "Couldn't create the link. Re-run this script from an ADMINISTRATOR PowerShell."
        }
        Write-Host "    Linked (symlink): $game -> $cloud" -ForegroundColor Green
    }

    # Verify WoW will see files through the link.
    $count = (Get-ChildItem $game -Recurse -File -ErrorAction SilentlyContinue | Measure-Object).Count
    if ($count -eq 0) { Fail "Link created but no files visible through it - check your cloud client." }
    Write-Host "    Verified: $count files visible through the link."
}

Write-Host ""
Write-Host "Done! Reminders:" -ForegroundColor Green
Write-Host " 1. Mark '$CloudFolder' as 'Available offline' / 'Always keep on this device' in your cloud app."
Write-Host " 2. On your other PC(s): install your cloud client, wait for the folder to download, then run this same script there."
Write-Host " 3. Don't play on two PCs at the same time."
