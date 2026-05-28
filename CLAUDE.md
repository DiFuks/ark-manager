# ArkManager — context for Claude

ASA (ARK: Survival Ascended) dedicated server manager. Originally built as a
Mac alternative to the Windows-only ASADedicatedManager; now cross-platform —
bundles for **macOS arm64**, **Linux x64** and **Windows x64**. The ASA server
is a Windows `.exe` (there is no native mac/linux build), so on mac/linux it
runs through **wine** (gcenx wine-stable / lutris-wine, **bundled in the
app**), and on Windows it runs natively via `Process.Start`.

## Stack & commands

- .NET 10 SDK, Avalonia 12, CommunityToolkit.Mvvm 8.x, MS.Ext.DI, xUnit
- Solution — `.slnx` (the .NET 10 XML format), NOT `.sln`

```bash
# dev iteration
dotnet build ArkManager.slnx
dotnet test  ArkManager.slnx
dotnet run --project src/ArkManager.Desktop/ArkManager.App.csproj

# production bundles (self-contained .NET + embedded wine on mac/linux)
./build.sh                         # mac+linux+win
./build.sh --target macos          # single target
make mac / make linux / make windows
make run                           # open dist/.../ArkManager.app
make clean                         # rm -rf dist/
```

Version lives in `Directory.Build.props` (`<Version>`), the single
source-of-truth for apphost, Info.plist and archive filenames.

## Layout

- `src/ArkManager.Core/` — UI-agnostic business logic
  - `Models/` — AppSettings, ServerLaunchOptions, ServerProfile, Maps
  - `Services/{AppPaths,SettingsService,ServerManager}.cs`
  - `Services/Steam/SteamCmdService.cs` (+ `InstalledServerVersion` record)
  - `Services/Config/{IniFile,ConfigService}.cs`
  - `Services/Backups/{BackupService,AutoBackupWorker}.cs`
  - `Services/Mods/{ModsService,CurseForgeClient}.cs`
  - `Services/Launchers/{IServerLauncher,ServerCommandLine,BundledWineLauncher,NativeWindowsLauncher}.cs`
  - `Services/Rcon/{RconClient,PlayerPoller}.cs`
  - `Util/ProcessRunner.cs`
- `src/ArkManager.Desktop/` — Avalonia 12 GUI
  - `App.axaml(.cs)` — DI bootstrap, UiThread/OpenInFinder/OpenInBrowser, **forced dark theme**
  - `AppServices.cs` — DI composition root
  - `Services/Browse.cs` — file/folder picker via `TopLevel.StorageProvider`
  - `Converters/OkIcon.cs` — bool → ✅/❌
  - `ViewModels/*ViewModel.cs` — partial + `[ObservableProperty]` / `[RelayCommand]`
  - `Views/*View.{axaml,axaml.cs}` — `ViewLocator` binds by class name
- `tests/ArkManager.Core.Tests/` — xUnit. `Core.csproj` has
  `<InternalsVisibleTo Include="ArkManager.Core.Tests" />` so internal members
  are testable.

## UI tabs (current set)

`MainWindowViewModel` assembles the nav:

`Server → RCON → Install → Config → Mods → Backups`

(There used to be a Dashboard; it was removed — its only unique content,
players online / names, moved to the Server tab.)

**Nav gated by install state.** While the server isn't installed (no
`appmanifest_2430930.acf` per `InstallViewModel.IsServerInstalled`), the
sidebar shows **only** `Install`. After install the nav expands to the full
list. Switching is done by in-place mutation of `NavItems` (`Clear()` resets
the ListBox selection through `CollectionChanged(Reset)`; reference equality
in `Selected` then means no PropertyChanged fires, so the selection
visually "drops").

## App-local state

The app's vendor folder (per the user-CLAUDE.md rule):

- macOS:  `~/Library/Application Support/ArkManager/`
- Linux:  `$XDG_DATA_HOME/ArkManager/`
- Win:    `%APPDATA%/ArkManager/`

- Windows fix: we use `LocalApplicationData` (`%LOCALAPPDATA%`), NOT
  `Roaming` — the ASA server is 25 GB and that doesn't belong in Roaming.

Contains: `settings.json`, `logs/`, `steamcmd/`, `backups/`, `server/`
(default), `server-runtime/` (WINEPREFIX, created by wine on first launch).

Legacy cleanup: the `AppPaths` ctor wipes any leftover `<DataDir>/wineprefix/`
from the old brew-based version — one-shot migration, no UI notification.

## Code gotchas (not obvious from sources)

### CommunityToolkit.Mvvm — naming source generator

`[ObservableProperty] private T _camelField;` → property `CamelField`. The
generator **only capitalises the very first character** after `_`. For acronyms
you need to lift uppercase letters explicitly in the field name:

- `_xpMultiplier` → `XpMultiplier` ❌ (ARK ini key is `XPMultiplier`)
- `_xPMultiplier` → `XPMultiplier` ✅
- `_rconPort` → `RconPort` (fine — `RconPort` is PascalCase everywhere in code)
- if you need `URL`, the field has to be `_uRL`

### Disabling commands (Start/Stop / Install by state)

Pattern: `[NotifyCanExecuteChangedFor(nameof(StartCommand))]` on the `_state`
field + `[RelayCommand(CanExecute = nameof(CanStart))]` on the method. Avalonia
disables the `Button` itself through `ICommand.CanExecute` — no need to spell
out `IsEnabled` in XAML. See `ServerViewModel.CanStart/CanStop` and
`InstallViewModel.CanInstallSteamCmd`/`CanInstallOrUpdateServer`. The latter
keeps `Install server` disabled while there's no steamcmd / empty path.

### Config ↔ ini auto-reload

ASA writes its own defaults into `GameUserSettings.ini` on startup and
sometimes overwrites our values. To save the user from hitting Reload,
`ConfigViewModel` refreshes its buffers on:

- **switching to the Config tab as a whole** —
  `MainWindowViewModel.OnSelectedChanged` calls
  `ConfigViewModel.RefreshFromDisk()` (reads both raw tabs + Basic from ini);
- **sub-tab switch** (Basic ↔ GUS.ini ↔ Game.ini) —
  `OnSelectedTabIndexChanged` re-reads only the active sub-tab.

The Basic tab picks up
`[ServerSettings]`/`[SessionSettings]`/`[/Script/Engine.GameSession]` **only**
for the fields that `ApplyLaunchOptionsToIni` writes: passwords, RCON,
SessionName, ports, MaxPlayers. Everything else (Map, NoBattlEye,
AutoManagedMods, ClusterId, Extra*) lives only in the VM / settings.json.

Trade-off: unsaved edits in the current sub-tab are lost when you come back to
it. The user explicitly asked for this ("no Reload presses").

### ServerManager.StartAsync syncs the ini

Before `_launcher.StartAsync` we call `_config.ApplyLaunchOptionsToIni(...)`.
Without it, the very first launch after a fresh install would pick up ASA's
defaults for RCON / passwords — RCON would end up disabled even if
`RconEnabled=true` in settings.json. Save in the Config tab does the same plus
a JSON update; here we just guarantee that ini and settings.json haven't
drifted.

### ServerCommandLine: passwords and RCON are NOT in the URL

`ServerCommandLine.Build` builds a URL query like
`TheIsland_WP?listen?SessionName=...?Port=N?QueryPort=M?MaxPlayers=K`.
We **deliberately do not** put `ServerPassword` / `ServerAdminPassword` /
`SpectatorPassword` / `RCONEnabled` / `RCONPort` into the URL. Reason: the ASA
URL parser can splice the rest of the string into a password value and
persist it that way into `GameUserSettings.ini` — then RCON auth breaks (it
gets a glued password like `2222?RCONEnabled=True?RCONPort=27020`).

These keys are written **only into the ini** via
`ConfigService.ApplyLaunchOptionsToIni` (`[ServerSettings]` section). The
server reads them from there. The RCON client also uses the ini value.

The `Build_Passwords_NotInUrlQuery` / `Build_Rcon_NotInUrlQuery` tests assert
these keys are absent from the URL.

### Avalonia 12

- `Grid.RowSpacing` / `Grid.ColumnSpacing` — singular. `RowSpacings` (plural)
  does not exist; the build will silently fail at XAML compile.
- `TextBox.PlaceholderText` — NOT `Watermark` (the latter is deprecated and
  warns at compile time).
- `TopLevel.StorageProvider` (Avalonia 11+) for file dialogs; the owner has to
  be a `TopLevel` (= MainWindow). We stash it in `Services.Browse.Owner` in
  `App.OnFrameworkInitializationCompleted`.

### "Field Manual" design system (theme layer)

The UI sits on a single visual language C1 "Field Manual" (warm charcoal +
ember amber, slab headings). All design lives in
`src/ArkManager.Desktop/Themes/`:

- `Tokens.axaml` — `SolidColorBrush` tokens (`BgBrush`, `PanelBrush`,
  `AccentBrush`, `MutedBrush`, `OkBrush`, `DangerBrush`, …). **No hex
  literals in Views any more** — only `{DynamicResource …}` / `Classes`.
- `Icons.axaml` — `StreamGeometry` glyphs (solid, for `PathIcon`). Emoji in UI
  is banned.
- `Resources.axaml` — merges Tokens+Icons+ControlThemes, plus `FontFamily`
  keys (`DisplayFont`=Zilla Slab, `UiFont`=IBM Plex Sans,
  `MonoFont`=IBM Plex Mono; ttfs embedded in `Assets/Fonts/`,
  `avares://…/#Family`). Wired into `App.axaml` as `<Application.Resources>`.
- `TextStyles.axaml` — `TextBlock` classes (`h1`/`stat`/`section`/`meta`).
- `Controls.axaml` — styles: `Button` (base = ghost,
  `.primary`/`.icon`/`.danger`/`.chip`),
  `Border.panel`/`.tile`/`.console`/`.chip`/`.pill`, inputs,
  `ListBox.nav`/`.rows`, `TabControl.seg` (segment tabs via **full TabItem
  re-template** — otherwise the Fluent blue selection chip bleeds through).
- `ControlThemes.axaml` — `ControlTheme` for the `ButtonSpinner`
  (NumericUpDown): the Fluent default chips the rounded corner away with
  square buttons; the inner border can't be reached from app-level styles
  (a double `/template/` doesn't resolve), so we override it entirely
  (rounded border + `ClipToBounds` + flat chevron buttons). The
  `NumericUpDown /template/ TextBox` is silenced so we don't get a double
  border.

`App.axaml`: `RequestedThemeVariant="Dark"` (tokens are dark-only). UI is
**English** (the Core/VM copy is translated; comments are English too).

`MainWindowViewModel`: env `ARKMANAGER_START_TAB=<TabTitle>` opens the app
directly on the named tab (for tests / screenshots; off by default).

### .NET 10 + Avalonia template

The `dotnet new avalonia.mvvm` template creates the project **without**
`ImplicitUsings`. If you add files in `ArkManager.App`, count on `System`,
`System.IO`, `System.Threading.Tasks`, `System.Linq` being available via
`ImplicitUsings=enable` in the csproj. Core has it on too.

### Sln quirk

`dotnet sln add ...` only works if the current directory contains a
`*.sln`/`*.slnx`. Ours is `ArkManager.slnx` at the repo root.

### Cross-OS build / CI

- `build.sh` — a single bash script, run from a mac/linux host, builds any
  combination of `--target macos|linux|windows`. Each target:
  `dotnet publish --self-contained` → wine (if needed) → pack into `dist/`.
  Internally it has a `publish_for()` helper — its stdout is captured via
  `$(...)`, so every informational line (`echo "==>"`, `dotnet publish`
  output) is redirected to stderr (`>&2`); only the publish path stays on
  stdout.
- `Makefile` — a thin wrapper over `build.sh`. Targets: `build`, `mac`,
  `linux`, `windows`, `run`, `clean`. Recipe lines have to be tabs (GNU make).
- `.github/workflows/release.yml` — matrix `macos-latest` / `ubuntu-latest` /
  `windows-latest`, triggered on pushing a `v*.*.*` tag; creates a draft
  release with all three archives. Wine tarballs are cached by the hash of
  `wine-sources.json`.
- Artifact sizes: mac/linux ~300–400 MB (including wine + .NET),
  Win ~130 MB.

## ASA technical quirks (NOT intuitive)

- **App ID 2430930**, free anonymous download.
- **No native Mac/Linux build** — only a Windows .exe. SteamCMD on mac/linux
  requires **`+@sSteamCmdForcePlatformType windows`** before
  `+login anonymous`, otherwise it refuses with "invalid platform" (on a
  Windows host the flag is NOT applied). Plus **`+app_info_update 1`** —
  without it steamcmd fails with "Failed to install app — Missing
  configuration" (the PICS cache doesn't get pulled). See
  `SteamCmdService.BuildInstallArgs(installDir, SteamCmdHostOs)` — covered
  separately in `SteamCmdBootstrapTests`.
- **The `.exe` runs through wine** on mac/linux (bundled, see the Wine section
  below), natively on Windows via `NativeWindowsLauncher`. The Whisky cask is
  archived (Aug 2024), the Parallels launcher was dropped; the brew-cask
  wine-stable is gone too — wine is bundled, no third-party installs.
- **BattlEye does not work under Wine** → the **`-NoBattlEye`** flag is
  mandatory, on by default in `ServerLaunchOptions`.
- **Mods via CurseForge** (not Steam Workshop). Passed as
  `-mods=id1,id2,...` + `-automanagedmods` for auto-download.
- **Cluster**: `-ClusterId=<name>` + optionally `-ClusterDirOverride=<path>`;
  the same ID across several servers means shared transfers.
- **Save folder**: `<ServerInstallPath>/ShooterGame/Saved/SavedArks/<Map>/`
- **Configs**: `<ServerInstallPath>/ShooterGame/Saved/Config/WindowsServer/{GameUserSettings,Game}.ini`
- **Server build version**:
  `<ServerInstallPath>/steamapps/appmanifest_2430930.acf` (top-level keys
  `buildid` + `LastUpdated`). The latest build is pulled via
  `steamcmd +app_info_print 2430930`; a regex picks up
  `public` → `buildid`.
- **RCON**: Source RCON (TCP). A marker packet is needed to splice multi-
  segment responses — we have it in `RconClient.SendAsync`. The RCON password
  = `ServerAdminPassword` from `[ServerSettings]` in the ini (not from the
  CLI!).
- **CurseForge API**: ASA gameId = 83374, endpoint `/v1/mods/{id}`, header
  `x-api-key`. Without a key the name resolution stops working, but it
  doesn't block anything.

## Wine (embedded)

Wine ships bundled inside the app — the user installs nothing, and the word
"wine" never appears in the UI.

- **macOS**: gcenx **wine-stable 11.0_1** in
  `<App>.app/Contents/Resources/wine/bin/` (Intel x86_64 via Rosetta 2).
  11.x uses unified wow64, so there's ONLY `bin/wine`, no `bin/wine64`.
- **Linux**: **lutris-wine 7.2-2** in `<install-dir>/wine/bin/` (statically
  built). Older wine still splits `wine` (32-bit) and `wine64` (64-bit).
- **Windows**: wine is not used — `NativeWindowsLauncher` runs the .exe
  natively.

`BundledWineLauncher.ResolveEmbeddedWineBinary` tries, in order:

1. `$ARKMANAGER_WINE_PATH` — dev escape hatch for Rider / `dotnet run`,
   where `AppContext.BaseDirectory` points into `bin.noindex/Debug/` with no
   wine next to it.
2. The bundle path; inside it tries names `wine64` → `wine` (ASA is 64-bit,
   so `wine64` goes first for compatibility with old lutris-wine; modern
   gcenx is caught by the `wine` fallback).
3. `~/.cache/ark-manager/wine/<sha-prefix>/<extracted-dir>/.../bin/{wine64,wine}` —
   **dev fallback** to the build cache, so `dotnet run` without env vars also
   works. End users do not have this folder.

Sources are pinned in `build/wine-sources.json` (URL + SHA256). `build.sh`
downloads, verifies the hash, drops the result in
`~/.cache/ark-manager/wine/<sha-prefix>/` and copies it into the bundle.

WINEPREFIX = `<DataDir>/server-runtime/`, created by wine on the first server
launch (slow first-run, ~30s).

Launch env: `WINEDEBUG=-all`, `WINEDLLOVERRIDES=winemac.drv=` (without the
latter, wine on macOS paints a Server Console window as white-on-white).

## Auto-backup

`AutoBackupWorker` (singleton, pre-resolved in
`App.OnFrameworkInitializationCompleted`). Background `Task.Run` loop.
Parameters from settings:

- `AutoBackupIntervalMinutes` — 0 turns it off. A tick is always skipped when
  `ServerManager.State != Running` (idle snapshots are pointless, hard-coded).

Subscribes to `SettingsService.Changed` — the current sleep is cancelled via
a linked CTS, the new interval applies immediately (not on the next tick).

Events: `BackupCreated` / `BackupFailed` / `Log` + public `NextRunUtc`.
`BackupsViewModel` renders "auto-backup in MM:SS", refreshing every 5s.

## Intentionally out of scope

- Multi-instance UI (the `Profiles` model is in place, the GUI only drives
  the first one).
- CurseForge browser/search (only ID → name resolution).
- GUI i18n / runtime language switching (resx/ResourceManager). The UI is
  English-only for now; multi-language deferred.
- ARK Game.ini secondary settings (`OverrideEngramEntries`,
  `EngramOverrides` and a pile of other customisations) — the raw Game.ini
  editor is the fallback.
- Server RAM limit. ASA has no CLI flag, macOS has no cgroups, `ulimit -v`
  breaks wine. No workaround.
- AppImage / .dmg / native installers (Inno Setup / WiX) — only
  `.zip` / `.tar.gz`.
- Code signing / notarization — ad-hoc on macOS only; Gatekeeper requires
  right-click → Open on the first launch.
- Headless CLI — none, GUI only.
- ARM64 Linux — untested; the wine bundle is x86_64.
- Intel Mac — untested (Apple Silicon + Rosetta only).

## Code style

- Comments are English everywhere (Core, Desktop, tests, XAML).
- Don't pile up null checks on Core boundaries — `SettingsService`
  guarantees defaults via `Defaults()`.
- Use `catch { /* ignore */ }` only for inessential things (cleanup,
  background UI hint, "open in Finder" with no consequence on failure).
- VMs have a parameterless ctor for the XAML designer; real instances come
  from DI.
- Tests are xUnit, no mock frameworks. Parsers / pure logic come first.

Branch `main`, pushed to `origin` on GitHub (`DiFuks/ark-manager`).
