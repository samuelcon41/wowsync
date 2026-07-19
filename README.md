# WoW Sync

Keep your World of Warcraft **addons _and_ addon settings** identical on every
PC — using the cloud storage you already have (Google Drive, OneDrive,
Dropbox…). No server, no subscription, no third-party sync service.

Update an addon on one machine, it's updated on all of them. Move a unit frame
on one machine, it's moved on all of them.

## How it works

Your `Interface\AddOns` folder and your `WTF\Account` folder (addon settings,
UI layout, WeakAuras, keybinds) are moved into a folder your cloud app already
syncs. A **directory junction** — a filesystem-level folder link the game can't
tell apart from a real folder — is placed at the original locations. WoW reads
and writes through the link; your cloud app keeps every PC in step.

Machine-specific settings (`WTF\Config.wtf` — graphics, sound) are left alone,
so each PC keeps its own video config.

## Quick start

Grab **WoWSyncSetup.exe** from the [latest release](../../releases/latest).

1. **Main PC** (the one whose addons/settings you want to keep): close WoW,
   double-click the exe. It auto-detects your WoW install and cloud folder —
   check them, click **Set Up Sync**. Your setup is copied to the cloud and
   becomes the master.
2. In your cloud app, mark the new *WoW Sync* folder **"Available offline"**
   (Google Drive) / **"Always keep on this device"** (OneDrive) so the game
   reads real local files at full speed.
3. **Every other PC**: install the same cloud client, wait for the folder to
   finish downloading, run the same exe. It backs up that PC's folders and
   links it to the shared copy.

Prefer scripts? `Setup-WoWSync.ps1` does exactly the same thing:

```powershell
powershell -ExecutionPolicy Bypass -File .\Setup-WoWSync.ps1 -CloudFolder "G:\My Drive\WoW Sync"
```

Works with retail, Classic, and Classic Era — pick the game version in the
dropdown (or `-Flavor _classic_`) and run once per version.

## Good to know

- **Don't play on two PCs at the same time.** Settings save on logout; the
  last logout wins, and cloud clients may create conflict copies.
- **Nothing is deleted.** Originals are kept next to the links as
  `*.pre-sync-backup`. Delete them yourself once you're happy.
- **SmartScreen:** the exe is unsigned, so Windows may warn on first run —
  *More info → Run anyway*. Or build it yourself (below) — it's one command.
- **Undo:** close WoW, delete the two links (they're just shortcuts), rename
  the `.pre-sync-backup` folders back.

## Building from source

No toolchain needed — the C# compiler ships with Windows:

```
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:winexe ^
  /out:WoWSyncSetup.exe /win32manifest:app.manifest ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll WoWSyncSetup.cs
```

Or just run `build.cmd`.

## License

[MIT](LICENSE)
