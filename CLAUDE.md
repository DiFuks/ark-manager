# ArkManager — context for Claude

ASA (ARK: Survival Ascended) dedicated server manager для macOS. Аналог
Windows-only ASADedicatedManager, написан под Mac. Цель — нативный mac-GUI
для управления полным жизненным циклом сервера (install/config/mods/backup/
start/stop/RCON), сам ASA-сервер при этом крутится под Wine/Whisky/Parallels.

## Stack & commands

- .NET 10 SDK, Avalonia 12, CommunityToolkit.Mvvm 8.x, MS.Ext.DI, xUnit
- Solution — `.slnx` (XML-формат .NET 10), НЕ `.sln`

```bash
dotnet build ArkManager.slnx
dotnet test  ArkManager.slnx
dotnet run --project src/ArkManager.App/ArkManager.App.csproj
```

## Layout

- `src/ArkManager.Core/` — UI-агностичная бизнес-логика
  - `Models/` — AppSettings, ServerLaunchOptions, ServerProfile, Maps, Rates
  - `Services/{AppPaths,SettingsService,ServerManager}.cs`
  - `Services/Steam/SteamCmdService.cs`
  - `Services/Config/{IniFile,ConfigService,RatesService}.cs`
  - `Services/Backups/BackupService.cs`
  - `Services/Mods/{ModsService,CurseForgeClient}.cs`
  - `Services/Doctor/DoctorService.cs`
  - `Services/Launchers/{IServerLauncher,ServerCommandLine,WineLauncherBase,WhiskyLauncher,LocalWineLauncher,ParallelsLauncher,LauncherFactory}.cs`
  - `Services/Rcon/{RconClient,PlayerPoller}.cs`
  - `Util/ProcessRunner.cs`
- `src/ArkManager.App/` — Avalonia 12 GUI
  - `App.axaml(.cs)` — DI bootstrap, UiThread/OpenInFinder/OpenInBrowser, **force dark theme**
  - `AppServices.cs` — DI composition root
  - `Services/Browse.cs` — file/folder picker через `TopLevel.StorageProvider`
  - `Converters/OkIcon.cs` — bool → ✅/❌
  - `ViewModels/*ViewModel.cs` — partial + `[ObservableProperty]` / `[RelayCommand]`
  - `Views/*View.{axaml,axaml.cs}` — `ViewLocator` биндит по имени класса
- `tests/ArkManager.Core.Tests/` — IniFile / ServerCommandLine / Rates / PlayerPoller

## App-local state

Vendor-каталог приложения (соответствует правилу из user CLAUDE.md):

- macOS:  `~/Library/Application Support/ArkManager/`
- Linux:  `$XDG_DATA_HOME/ArkManager/`
- Win:    `%APPDATA%/ArkManager/`

Содержит: `settings.json`, `logs/`, `steamcmd/`, `backups/`, `server/` (default).

## Подводные камни кода (не очевидно из исходников)

### CommunityToolkit.Mvvm — naming source-generator

`[ObservableProperty] private T _camelField;` → property `CamelField`. Генератор
**капитализирует ТОЛЬКО первый символ** после `_`. Для acronyms нужно поднимать
заглавные явно в имени поля:

- `_xpMultiplier` → `XpMultiplier` ❌ (а ARK ini-key — `XPMultiplier`)
- `_xPMultiplier` → `XPMultiplier` ✅
- `_rconPort` → `RconPort` (это ок, RCON в коде везде PascalCase `RconPort`)
- если нужен `URL` — поле должно быть `_uRL`

Если уже всплыла такая ошибка с другой аббревиатурой — фикс однострочный.

### Avalonia 12

- `Grid.RowSpacing` / `Grid.ColumnSpacing` — единственное число. `RowSpacings`
  (plural) не существует, билд молча сломается на XAML compile.
- `TextBox.PlaceholderText` — НЕ `Watermark` (последний deprecated, валит warning).
- `TopLevel.StorageProvider` (Avalonia 11+) для файловых диалогов; владельцем
  нужен `TopLevel` (= MainWindow). У нас он сохраняется в `Services.Browse.Owner`
  в `App.OnFrameworkInitializationCompleted`.

### Dark theme — захардкожен

`App.axaml`: `RequestedThemeVariant="Dark"`. Кастомные фоны в Views
(`#1f2230`, `#2a2f44`, `#11141d`, `#0d0f17`, `#262a3a`, `#181c28`) рассчитаны
на белый текст. Если переключить на `Default`/`Light` — будет «чёрное
на чёрном». Чтобы сделать theme-aware: заменить hex на `{DynamicResource ...}`
из FluentTheme.

### .NET 10 + Avalonia template

Шаблон `dotnet new avalonia.mvvm` создаёт проект **без** `ImplicitUsings`.
Если добавляешь файлы в `ArkManager.App`, рассчитывай: `System`, `System.IO`,
`System.Threading.Tasks`, `System.Linq` уже через `ImplicitUsings=enable`
в csproj. В Core — там тоже включено.

### Sln нюанс

`dotnet sln add ...` работает только если в текущей папке есть `*.sln`/`*.slnx`.
У нас `ArkManager.slnx` в корне репо.

## ASA technical quirks (что НЕ интуитивно)

- **App ID 2430930**, free anonymous download
- **Нет native Mac/Linux build** — только Windows .exe. SteamCMD на маке
  требует **`+@sSteamCmdForcePlatformType windows`** перед `+login anonymous`,
  иначе откажет «invalid platform».
- **`.exe` запускается через Wine** (Whisky / brew wine / Parallels VM).
- **BattlEye под Wine не работает** → флаг **`-NoBattlEye`** обязателен, включён
  в `ServerLaunchOptions` по умолчанию.
- **Моды через CurseForge** (не Steam Workshop). Передаются как
  `-mods=id1,id2,...` + `-automanagedmods` для auto-download.
- **Cluster**: `-ClusterId=<name>` + опционально `-ClusterDirOverride=<path>`,
  одинаковый ID на нескольких серверах = общие трансферы.
- **Save папка**: `<ServerInstallPath>/ShooterGame/Saved/SavedArks/<Map>/`
- **Конфиги**: `<ServerInstallPath>/ShooterGame/Saved/Config/WindowsServer/{GameUserSettings,Game}.ini`
- **RCON**: Source RCON (TCP). Маркер-пакет нужен для склейки многосегментных
  ответов — у нас в `RconClient.SendAsync` он есть.
- **CurseForge API**: ASA gameId = 83374, endpoint `/v1/mods/{id}`, header
  `x-api-key`. Без ключа резолв имён не работает, но это не блокирует ничего.

## Whisky paths

- wine: `/Applications/Whisky.app/Contents/Resources/Libraries/Wine/bin/wine64`
- bottles (новая версия): `~/Library/Containers/com.isaacmarovitz.Whisky/Bottles/`
- bottles (старая): `~/Library/Application Support/com.isaacmarovitz.Whisky/Bottles/`

`WhiskyLauncher.EnumerateBottleRoots()` перебирает оба.

Bottle создаётся юзером один раз вручную через Whisky.app — программно мы его
не создаём (планировалось «bottle-creation helper», но не реализовано).

## Что НЕ сделано (намеренно out of scope для текущего скоупа)

- Multi-instance UI (модель `Profiles` готова, GUI работает только с первым).
- Bottle-creation helper для Whisky.
- CurseForge browser/search (только resolve ID → имя).
- GUI локализация (микс ru/en).
- ARK Game.ini secondary settings (OverrideEngramEntries, EngramOverrides и
  тонна других кастомизаций) — есть raw-редактор Game.ini как fallback.

## Code style

- Комментарии в Core/ на русском, в XAML/тестах преимущественно английский.
- Не плодить null-проверки на boundary внутри Core — `SettingsService` гарантирует
  defaults через `Defaults()`.
- `catch { /* ignore */ }` применять только для несущественных вещей (cleanup,
  фоновый UI hint, открытие в Finder без последствий при сбое).
- VM имеют параметрless конструктор для XAML-дизайнера; реальные инстансы
  через DI.
- Tests xUnit, без mock-фреймворков. Парсеры / pure logic — приоритет.

## Существующие коммиты (для контекста)

```
d437477 Force dark theme
1d3d5d3 Fix ObservableProperty name for XPMultiplier
51f2da0 Update README: live players, Rates tab
26bbed0 Rates tab: difficulty + common multipliers
0af81c3 PlayerPoller + live player count on Dashboard
baa3a76 Maps presets, cluster CLI, CurseForge name lookup
ae1a241 Browse buttons, RCON client, auto-restart
f89e125 Initial ArkManager skeleton
```

Branch `main`, без remote (юзер пушит сам по желанию).
