# WoW Sync

> **Status, 2026-10-28: retired.** The author stopped using WoW Sync on their own PCs on 2026-08-25. On those PCs, Windows 11 24H2 blocked directory junctions into Google Drive for desktop's virtual drive, and Drive's mirror mode moved `G:\` to `C:\Users\<you>\My Drive`, which breaks the junction layer described below. The code is unchanged since v1.1.1 and is not being updated.

Keep your World of Warcraft **addons _and_ addon settings** identical on every
PC — using the cloud storage you already have (Google Drive, OneDrive,
Dropbox…). No server, no subscription, no third-party sync service.

Update an addon on one machine, it's updated on all of them. Move a unit frame
on one machine, it's moved on all of them.

## How it works (v1.1)

Two layers, each using the safest mechanism for what it protects:

- **Addons** (`Interface\AddOns`) move into your cloud-synced folder, and a
  **directory junction** — a folder link the game can't tell from a real
  folder — is left in place. Addon installs and updates sync live.
- **Settings** (`WTF\Account` — addon config, UI layout, WeakAuras, keybinds)
  **stay in a normal local folder**. A tiny background agent pushes them to
  the cloud right after WoW closes, and pulls them down when the cloud copy
  is newer (i.e. you played on your other PC last). WoW itself never depends
  on the cloud drive being online — if your cloud app is off, you just play
  with local files and the sync catches up later.

Machine-specific settings (`WTF\Config.wtf` — graphics, sound) are never
touched, so each PC keeps its own video config.

> **Upgrading from v1.0?** v1.0 linked `WTF\Account` directly into the cloud.
> Just run the new setup — it detects the old link and converts your settings
> back to a normal local folder automatically. Nothing is deleted.

## Quick start

Grab **WoWSyncSetup.exe** from the [latest release](../../releases/latest).

1. **Main PC** (the one whose addons/settings you want to keep): close WoW,
   double-click the exe. It auto-detects your WoW install and cloud folder —
   check them, click **Set Up Sync**. Your setup is copied to the cloud and
   becomes the master.
2. In your cloud app, mark the new *WoW Sync* folder **"Available offline"**
   (Google Drive) / **"Always keep on this device"** (OneDrive) so the game
   reads real local files at full speed.
3. **Every other PC**: install the same cloud client, then — **before running
   setup and before launching WoW** — mark the *WoW Sync* folder **"Available
   offline"** / **"Always keep on this device"** and wait for it to finish
   downloading. Then run the same exe. It backs up that PC's folders and joins
   it to the shared copy.

> ### ⚠️ On the second PC: make the folder available offline *before* you start WoW
>
> Google Drive and OneDrive normally keep files "online-only" — the names are
> there, but the contents are not on disk until something opens them. Your
> addons are read through a folder link into that folder, so if you launch WoW
> before the download has finished, **the game sees missing or half-written
> addon files**. Typical symptoms: addons missing from the list, a wall of Lua
> errors, or a UI that loads with default Blizzard frames.
>
> Nothing is corrupted when this happens — the files simply weren't there yet.
> Close WoW, let the folder finish downloading, and start the game again.
>
> To do it: right-click the *WoW Sync* folder →
> **Offline access → Available offline** (Google Drive), or
> **Always keep on this device** (OneDrive). Wait for every item to show the
> solid green/check "on this device" icon, not a cloud icon.

Prefer scripts? `Setup-WoWSync.ps1` does exactly the same thing (keep
`wowsync-agent.ps1` next to it):

```powershell
powershell -ExecutionPolicy Bypass -File .\Setup-WoWSync.ps1 -CloudFolder "G:\My Drive\WoW Sync"
```

Works with retail, Classic, and Classic Era — pick the game version in the
dropdown (or `-Flavor _classic_`) and run once per version.

## The background agent

Installed to `%LOCALAPPDATA%\WoWSync\wowsync-agent.ps1`, started at login via
a `HKCU\...\Run` entry, ~0% CPU (checks for the game every 15 seconds).

- WoW closes → waits 10 s for SavedVariables to flush → pushes local → cloud.
  If the push fails (cloud app not running), it retries until it succeeds and
  **never pulls over unsaved local changes**.
- Idle and the cloud copy is ≥2 min newer → pulls cloud → local.
- Never syncs while the game is running; never copies from an empty folder.
- Log: `%LOCALAPPDATA%\WoWSync\agent.log`

## Good to know

- **Don't play on two PCs at the same time.** Settings save on logout; the
  last logout wins.
- **Nothing is deleted.** Originals are kept next to the links as
  `*.pre-sync-backup`. Delete them yourself once you're happy.
- **SmartScreen:** the exe is unsigned, so Windows may warn on first run —
  *More info → Run anyway*. Or build it yourself (below) — it's one command.
- **If WoW ever loads with no addons:** your cloud app isn't running. Start
  it and restart the game.

## Uninstall

1. Close WoW. Delete the `Interface\AddOns` link (it's just a shortcut), then
   copy the `AddOns` folder out of your cloud *WoW Sync* folder back into
   `Interface\` (or rename `AddOns.pre-sync-backup` back to `AddOns`).
2. Settings are already a normal local folder — nothing to undo.
3. Remove the agent: delete the `WoWSyncAgent-*` entry under
   `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` (Task Manager →
   Startup apps), and delete `%LOCALAPPDATA%\WoWSync`.

## Building from source

No toolchain needed — the C# compiler ships with Windows:

```
build.cmd
```

(It embeds `wowsync-agent.ps1` into the exe as a resource, so keep the two
files together.)

## License

[MIT](LICENSE)
