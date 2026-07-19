<#
  WoW Sync Agent v1.1 - settings sync that never touches your WTF folder.

  Your WTF\Account folder stays a REAL local folder. WoW always reads and
  writes real local files, so nothing depends on the cloud being online while
  you play. This agent simply:

    * PUSHES  local settings -> cloud, right after WoW closes
    * PULLS   cloud -> local, when WoW isn't running and the cloud copy is newer
              (i.e. you played on your other PC last)

  Addons are handled separately by a folder junction and sync live.

  Safety rules baked in:
    - never syncs while WoW is running
    - never mirrors FROM an empty/missing folder
    - an owed push (e.g. cloud app was offline at logout) is recorded ON DISK,
      so a reboot can't lose it, and a pull can never run over unpushed local
      changes

  Runs quietly in the background, started at login by a Run-key entry.
#>
param(
    [string]$CloudSettings = "G:\My Drive\WoW Sync\_retail_\WTF-Account",
    [string]$LocalSettings = "C:\Program Files (x86)\World of Warcraft\_retail_\WTF\Account",
    [int]$PollSeconds = 15,
    [switch]$Once,          # run one cycle and exit (used for testing)
    [switch]$ForcePush,
    [switch]$ForcePull
)

$ErrorActionPreference = 'Stop'

# Per-install state, keyed by the local settings path so multiple game
# versions (retail / classic / era) each get their own log and flags.
$stateDir = Join-Path $env:LOCALAPPDATA "WoWSync"
New-Item -ItemType Directory -Force $stateDir | Out-Null
$slug = ($LocalSettings -replace '[^A-Za-z0-9]', '_').Trim('_')
if ($slug.Length -gt 60) { $slug = $slug.Substring($slug.Length - 60) }
$logFile     = Join-Path $stateDir "agent-$slug.log"
$pendingFile = Join-Path $stateDir "pending-push-$slug.flag"

function Log($msg) {
    $line = "{0}  {1}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), $msg
    # Logging must never be able to kill the agent (concurrent writers, full
    # disk, roaming profile hiccups). Write-Host - NOT Write-Output - so log
    # lines can't leak into a calling function's return value.
    try { Add-Content -Path $logFile -Value $line -Encoding utf8 -ErrorAction Stop } catch { }
    Write-Host $line
}

function WowRunning {
    return [bool](Get-Process -Name "Wow", "WowClassic", "WowT", "WowB" -ErrorAction SilentlyContinue)
}

# Newest file timestamp in a tree - our "how fresh is this copy" signal.
function NewestWrite($path) {
    if (-not (Test-Path $path)) { return [datetime]::MinValue }
    $f = Get-ChildItem $path -Recurse -File -ErrorAction SilentlyContinue |
         Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if ($f) { return $f.LastWriteTimeUtc } else { return [datetime]::MinValue }
}

function FileCount($path) {
    if (-not (Test-Path $path)) { return 0 }
    return (Get-ChildItem $path -Recurse -File -ErrorAction SilentlyContinue | Measure-Object).Count
}

# Mirror with guards: never mirror FROM an empty tree (that would wipe the
# destination), and never mirror while the game is running.
function SafeMirror($src, $dst, $label) {
    try {
        $n = FileCount $src
        if ($n -eq 0)   { Log "ABORT $label - source '$src' is empty or missing."; return $false }
        if (WowRunning) { Log "ABORT $label - WoW is running."; return $false }

        New-Item -ItemType Directory -Force $dst -ErrorAction Stop | Out-Null
        $null = robocopy $src $dst /MIR /MT:8 /R:2 /W:2 /NFL /NDL /NJH /NP
        if ($LASTEXITCODE -ge 8) { Log "FAILED $label (robocopy $LASTEXITCODE)"; return $false }

        # Re-check: if WoW started mid-copy the tree may be inconsistent.
        if (WowRunning) { Log "WARNING $label - WoW launched during the copy; will re-sync next time."; return $false }

        Log "$label OK - $n files"
        return $true
    }
    catch {
        Log "FAILED $label - $($_.Exception.Message)"
        return $false
    }
}

function Push() { return SafeMirror $LocalSettings $CloudSettings "PUSH local->cloud" }
function Pull() { return SafeMirror $CloudSettings $LocalSettings "PULL cloud->local" }

# An owed push lives on disk: losing it would let a later pull overwrite
# settings that were never uploaded.
function SetPending()   { try { Set-Content -Path $pendingFile -Value (Get-Date -Format s) -ErrorAction Stop } catch { } }
function ClearPending() { try { Remove-Item $pendingFile -Force -ErrorAction Stop } catch { } }
function IsPending()    { return (Test-Path $pendingFile) }

if ($ForcePush) { Push | Out-Null; exit }
if ($ForcePull) { Pull | Out-Null; exit }

# Single-instance guard, scoped to THIS game version. Agents for other
# flavors must be allowed to keep running.
#
# Match on the -File argument specifically: a plain "*wowsync-agent*" test also
# matches any console or script that merely MENTIONS the agent (a diagnostic
# command, an editor, this project's own tooling), which would make a real
# agent exit believing a duplicate exists.
$dupe = Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" -ErrorAction SilentlyContinue |
        Where-Object {
            $_.ProcessId -ne $PID -and
            $_.CommandLine -match '-File\s+"?[^"]*wowsync-agent\.ps1' -and
            $_.CommandLine -like ("*" + [Management.Automation.WildcardPattern]::Escape($LocalSettings) + "*")
        }
if ($dupe) {
    Log "Another agent for this game version is already running (PID $(($dupe | Select-Object -First 1).ProcessId)) - exiting."
    exit
}

Log "Agent v1.1 started (poll ${PollSeconds}s)"
Log "  local : $LocalSettings"
Log "  cloud : $CloudSettings"
if (IsPending) { Log "A push from a previous session is still owed - will retry before any pull." }

$wasRunning = WowRunning
if ($wasRunning) { Log "WoW already running at startup - will push when it closes." }

while ($true) {
    try {
        $running = WowRunning

        if ($running -and -not $wasRunning) {
            Log "WoW launched."
        }
        elseif (-not $running -and $wasRunning) {
            # Just closed: local settings are authoritative. Record the debt
            # BEFORE attempting the push, so a crash or shutdown mid-push still
            # leaves a record that local has unsynced changes.
            Log "WoW closed - waiting for files to settle..."
            SetPending
            Start-Sleep -Seconds 10
            if (Push) { ClearPending } else { Log "Push failed - will keep retrying; no pull until it lands." }
        }
        elseif (-not $running) {
            if (IsPending) {
                # Local is authoritative until the owed push succeeds.
                # Never pull here - that would destroy unsynced changes.
                if (Push) { ClearPending; Log "Owed push completed." }
            }
            elseif (Test-Path $CloudSettings) {
                # Idle and nothing owed: if the other PC played more recently,
                # pull its settings down.
                $cloud = NewestWrite $CloudSettings
                $local = NewestWrite $LocalSettings
                if ($cloud -gt $local.AddMinutes(2)) {
                    Log ("Cloud is newer (cloud {0:u} vs local {1:u}) - pulling." -f $cloud, $local)
                    Pull | Out-Null
                }
            }
        }

        $wasRunning = $running
    }
    catch {
        Log "ERROR: $($_.Exception.Message)"
    }

    if ($Once) { Log "Single cycle done."; break }
    Start-Sleep -Seconds $PollSeconds
}
