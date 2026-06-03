# ArkManager

A dedicated server manager for ARK: Survival Ascended. Bundles for **macOS arm64**,
**Linux x64** and **Windows x64** — all self-contained, no external runtimes to
install.

**Website:** <https://arkmanager.org/>

The ASA dedicated server is a Windows `.exe` (there is no native mac/linux
build), so:

- macOS / Linux — runs through **wine** (gcenx wine-stable / lutris-wine,
  **bundled in the app**, verified via SHA256, no brew / apt needed);
- Windows — runs natively via `Process.Start`.

## Features

- install / update the server via **SteamCMD** (app id `2430930`);
- auto-resolve CurseForge mod IDs → names through the public cfwidget proxy
  (no API key required);
- edit `GameUserSettings.ini` / `Game.ini` (form view + raw tabs, auto-reload
  from disk when switching tabs);
- backups of `ShooterGame/Saved/` (zip, rotation, background auto-backups on
  an interval);
- start / stop the server, streaming log, uptime / PID, auto-restart on crash,
  graceful shutdown via RCON `saveworld` / `DoExit` before kill;
- **RCON client** (Source RCON / TCP) with one-click `saveworld`/`DoExit`/`Broadcast`;
- live player counter on the Server tab (background `ListPlayers` poll every 30s);
- **cluster** support (`-ClusterId` / `-ClusterDirOverride` with folder picker);
- 7 map presets (TheIsland / TheCenter / ScorchedEarth / Aberration /
  Extinction / Astraeos / Ragnarok); mod maps via raw `Game.ini` or by editing
  `Maps.cs`.

## Install

Download the `.zip` (mac/win) or `.tar.gz` (linux) from GitHub Releases, unpack,
run. The .NET runtime and wine are embedded.

- **macOS**: on first launch Gatekeeper will ask for confirmation — right-click
  → Open (the app is ad-hoc signed, not notarized).
- **Windows**: SmartScreen will show "Unknown publisher" → More info → Run
  anyway (no Authenticode certificate).

## Quick start

1. **Install** → `Install SteamCMD` → `Install server`. The first run is slow
   (~25 GB). Until the server is installed only the Install tab is shown in the
   sidebar; afterwards the rest of the navigation is unlocked.
2. **Config** → set `Session name`, `Admin password`, ports — you can start
   right away (defaults are sensible). RCON is on by default, port 27020.
3. **Mods** → CurseForge IDs comma-separated → `Add`. Names are resolved
   automatically when the tab opens.
4. **Server** → `▶ Start`. On the first launch wine spends ~30s creating its
   prefix in `~/Library/Application Support/ArkManager/server-runtime/` (mac) /
   `$XDG_DATA_HOME/ArkManager/server-runtime/` (linux) / `%LOCALAPPDATA%/...`
   (win), then ASA loads the world.
5. **Backups** → `Create` or enable auto-backup every N minutes (only ticks
   while the server is `Running`).

## Build from source

```bash
git clone <repo>
cd ark-manager

# dev iteration
dotnet build ArkManager.slnx
dotnet test  ArkManager.slnx
dotnet run --project src/ArkManager.Desktop/ArkManager.App.csproj

# production bundles for all 3 platforms (from a single mac/linux host)
./build.sh                     # mac+linux+win
./build.sh --target macos      # single target
make mac / make linux / make windows
make run                       # open the built .app from dist
make clean
```

Architecture, invariants and gotchas — see [`CLAUDE.md`](CLAUDE.md) at the
repo root.

### Running with `dotnet run` (no bundle)

`BundledWineLauncher` looks for wine first in the `ARKMANAGER_WINE_PATH`
environment variable, then inside the bundle, then in the build cache
`~/.cache/ark-manager/wine/<sha>/.../bin/`. So once you've run
`./build.sh --target macos` at least once, wine is on disk and `dotnet run`
finds it via the cache fallback — no env var required.

## Intentionally out of scope

- Multi-instance UI (the `Profiles` model is in place, the GUI only drives the
  first one).
- CurseForge browser / search (only ID → name resolution).
- GUI i18n (UI is English only).
- AppImage / `.dmg` / native installers — only `.zip` / `.tar.gz`.
- Code signing / notarization — ad-hoc on macOS only.
- Headless CLI — GUI only.
- ARM64 Linux, Intel Mac — untested.

## Tests

```bash
dotnet test ArkManager.slnx
```

Cover: `IniFile` round-trip, `ServerCommandLine.Build` (passwords / RCON not in
URL), SteamCMD parsers (`appmanifest_*.acf` + `app_info_print`), the bootstrap
URL helper, `PlayerPoller`. No mocking frameworks — pure-logic units only.

## Licensing

Wine sources: WineHQ (LGPL 2.1), gcenx macOS builds, lutris-wine Linux builds —
all redistributable. Pinned versions + SHA256 in `build/wine-sources.json`.
