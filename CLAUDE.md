# ArkManager — context for Claude

ASA (ARK: Survival Ascended) dedicated server manager для macOS. Аналог
Windows-only ASADedicatedManager, написан под Mac. Цель — нативный mac-GUI
для управления полным жизненным циклом сервера (install/config/mods/backup/
start/stop/RCON), сам ASA-сервер крутится через **wine64** (brew cask
`wine-stable`), потому что нативной mac-сборки сервера не существует.

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
  - `Models/` — AppSettings, ServerLaunchOptions, ServerProfile, Maps
  - `Services/{AppPaths,SettingsService,ServerManager}.cs`
  - `Services/Steam/SteamCmdService.cs` (+ `InstalledServerVersion` record)
  - `Services/Config/{IniFile,ConfigService}.cs`
  - `Services/Backups/{BackupService,AutoBackupWorker}.cs`
  - `Services/Mods/{ModsService,CurseForgeClient}.cs`
  - `Services/Doctor/DoctorService.cs`
  - `Services/Launchers/{IServerLauncher,ServerCommandLine,WineLauncher}.cs`
  - `Services/Rcon/{RconClient,PlayerPoller}.cs`
  - `Util/ProcessRunner.cs`
- `src/ArkManager.App/` — Avalonia 12 GUI
  - `App.axaml(.cs)` — DI bootstrap, UiThread/OpenInFinder/OpenInBrowser, **force dark theme**
  - `AppServices.cs` — DI composition root
  - `Services/Browse.cs` — file/folder picker через `TopLevel.StorageProvider`
  - `Converters/OkIcon.cs` — bool → ✅/❌
  - `ViewModels/*ViewModel.cs` — partial + `[ObservableProperty]` / `[RelayCommand]`
  - `Views/*View.{axaml,axaml.cs}` — `ViewLocator` биндит по имени класса
- `tests/ArkManager.Core.Tests/` — xUnit. `Core.csproj` имеет
  `<InternalsVisibleTo Include="ArkManager.Core.Tests" />` — internal-методы
  тестируемы.

## Табы UI (текущий список)

`MainWindowViewModel` собирает nav:

`Server → RCON → Install → Config → Mods → Backups → Doctor → Settings`

(Стартовый таб — `Server`. Dashboard был, но удалён: единственное уникальное
с него — players online / names — переехало в Server.)

## App-local state

Vendor-каталог приложения (соответствует правилу из user CLAUDE.md):

- macOS:  `~/Library/Application Support/ArkManager/`
- Linux:  `$XDG_DATA_HOME/ArkManager/`
- Win:    `%APPDATA%/ArkManager/`

Содержит: `settings.json`, `logs/`, `steamcmd/`, `backups/`, `server/` (default),
`wineprefix/` (default WINEPREFIX).

## Подводные камни кода (не очевидно из исходников)

### CommunityToolkit.Mvvm — naming source-generator

`[ObservableProperty] private T _camelField;` → property `CamelField`. Генератор
**капитализирует ТОЛЬКО первый символ** после `_`. Для acronyms нужно поднимать
заглавные явно в имени поля:

- `_xpMultiplier` → `XpMultiplier` ❌ (а ARK ini-key — `XPMultiplier`)
- `_xPMultiplier` → `XPMultiplier` ✅
- `_rconPort` → `RconPort` (это ок, RCON в коде везде PascalCase `RconPort`)
- если нужен `URL` — поле должно быть `_uRL`

### Дизейбл команд (Start/Stop по состоянию)

Pattern: `[NotifyCanExecuteChangedFor(nameof(StartCommand))]` на поле
`_state` + `[RelayCommand(CanExecute = nameof(CanStart))]` на методе. Avalonia
сам дизейблит `Button` через `ICommand.CanExecute` — `IsEnabled` в XAML
явно прописывать не надо. См. `ServerViewModel.CanStart/CanStop`.

### ServerCommandLine: пароли и RCON НЕ в URL

`ServerCommandLine.Build` строит URL-query вида
`TheIsland_WP?listen?SessionName=...?Port=N?QueryPort=M?MaxPlayers=K`.
В URL **специально не кладутся** `ServerPassword` / `ServerAdminPassword` /
`SpectatorPassword` / `RCONEnabled` / `RCONPort`. Причина: ASA URL-парсер
может склеить хвост строки в значение пароля и сохранить так в
`GameUserSettings.ini` — потом RCON-аутентификация ломается (приходит склеенный
пароль вида `2222?RCONEnabled=True?RCONPort=27020`).

Эти ключи пишутся **только в ini** через `ConfigService.ApplyLaunchOptionsToIni`
(`[ServerSettings]` секция). Оттуда сервер их и читает. RCON-клиент тоже
работает по ini-значению.

Тесты `Build_Passwords_NotInUrlQuery` / `Build_Rcon_NotInUrlQuery`
проверяют отсутствие этих ключей в URL.

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

- **App ID 2430930**, free anonymous download.
- **Нет native Mac/Linux build** — только Windows .exe. SteamCMD на маке
  требует **`+@sSteamCmdForcePlatformType windows`** перед `+login anonymous`,
  иначе откажет «invalid platform». Плюс **`+app_info_update 1`** — без него
  steamcmd падает с «Failed to install app — Missing configuration» (PICS-кэш
  не подтягивается).
- **`.exe` запускается через wine64** (cask `wine-stable`). Других режимов
  больше нет — Whisky-cask архивирован (Aug 2024), Parallels-launcher выпилен.
- **BattlEye под Wine не работает** → флаг **`-NoBattlEye`** обязателен, включён
  в `ServerLaunchOptions` по умолчанию.
- **Моды через CurseForge** (не Steam Workshop). Передаются как
  `-mods=id1,id2,...` + `-automanagedmods` для auto-download.
- **Cluster**: `-ClusterId=<name>` + опционально `-ClusterDirOverride=<path>`,
  одинаковый ID на нескольких серверах = общие трансферы.
- **Save папка**: `<ServerInstallPath>/ShooterGame/Saved/SavedArks/<Map>/`
- **Конфиги**: `<ServerInstallPath>/ShooterGame/Saved/Config/WindowsServer/{GameUserSettings,Game}.ini`
- **Server build version**: `<ServerInstallPath>/steamapps/appmanifest_2430930.acf`
  (top-level keys `buildid` + `LastUpdated`). Latest build тянется через
  `steamcmd +app_info_print 2430930`, регэксп ищет `public` → `buildid`.
- **RCON**: Source RCON (TCP). Маркер-пакет нужен для склейки многосегментных
  ответов — у нас в `RconClient.SendAsync` он есть. RCON-пароль =
  `ServerAdminPassword` из `[ServerSettings]` в ini (не из CLI!).
- **CurseForge API**: ASA gameId = 83374, endpoint `/v1/mods/{id}`, header
  `x-api-key`. Без ключа резолв имён не работает, но это не блокирует ничего.

## Wine setup (cask `wine-stable`)

- wine64 path (искаем в этом порядке через `WineLauncher.EnumerateWineCandidates`):
  - `/Applications/Wine Stable.app/Contents/Resources/wine/bin/wine64`
  - `/Applications/Wine Staging.app/...` / `/Applications/Wine Devel.app/...`
  - `/Applications/Game Porting Toolkit.app/...` (если ставился GPTK вручную)
  - `/Applications/Wine Crossover.app/...` (старый gcenx, fallback)
  - `/opt/homebrew/bin/wine64` / `/usr/local/bin/wine64`
- WINEPREFIX = `~/Library/Application Support/ArkManager/wineprefix` (по умолчанию).
  Wine инициализирует префикс автоматически при первом запуске сервера —
  займёт ~30 сек, в логе будут «mountmgr/winemenubuilder» — это нормально.

### Doctor → Install wine

Brew в version ≥4.6 убрал `--no-quarantine`. Плюс cask `wine-stable` тянет
`gstreamer-runtime` как .pkg-инсталлер, который **требует sudo**. Запускать
brew как child-процесс ArkManager бесполезно: stdio редиректнут, sudo
не видит tty → виснет.

Решение в `DoctorService.InstallWineViaBrewAsync`: пишем скрипт в
`/tmp/ark-manager-install-wine.sh` и запускаем `open -a Terminal <script>`.
Скрипт:
1. Проверяет Rosetta 2 (`arch -x86_64 /usr/bin/true`); ставит если нет
   (`softwareupdate --install-rosetta --agree-to-license`). wine-stable —
   Intel-only.
2. `brew install --cask wine-stable`.
3. `xattr -dr com.apple.quarantine "/Applications/Wine Stable.app"` — снимает
   карантин (Gatekeeper иначе блокирует wine64).

UI просит юзера дождаться окончания в Terminal и нажать `↻ Run checks`
обратно в Doctor.

**Важно:** cask wine-stable помечен deprecated с отключением **2026-09-01**.
После этой даты `brew install --cask wine-stable` сломается — нужно будет
переезжать на `gcenx/wine/game-porting-toolkit` (10 GB + Rosetta).

## Автобэкап

`AutoBackupWorker` (singleton, pre-resolved в `App.OnFrameworkInitializationCompleted`).
Фоновый цикл `Task.Run`. Параметры из settings:
- `AutoBackupIntervalMinutes` — 0 выключает.
- `AutoBackupOnlyWhenRunning` — если true, пропускаем тики когда
  `ServerManager.State != Running` (избегаем гонять одинаковые снимки
  у простаивающего сервера).

Подписка на `SettingsService.Changed` — текущий sleep кенселится через
linked CTS, новый интервал применяется немедленно (а не на следующем тике).

События `BackupCreated` / `BackupFailed` / `Log` + публичное `NextRunUtc`.
`BackupsViewModel` показывает «автобэкап через MM:SS», тикает раз в 5с.

## Что НЕ сделано (намеренно out of scope)

- Multi-instance UI (модель `Profiles` готова, GUI работает только с первым).
- CurseForge browser/search (только resolve ID → имя).
- GUI локализация (микс ru/en).
- ARK Game.ini secondary settings (OverrideEngramEntries, EngramOverrides и
  тонна других кастомизаций) — есть raw-редактор Game.ini как fallback.
- Лимит RAM сервера. У ASA нет CLI-флага, на macOS нет cgroups, `ulimit -v`
  ломает wine. Workaround — `ScheduledRestartHours` (есть в Settings).

## Code style

- Комментарии в Core/ на русском, в XAML/тестах преимущественно английский.
- Не плодить null-проверки на boundary внутри Core — `SettingsService` гарантирует
  defaults через `Defaults()`.
- `catch { /* ignore */ }` применять только для несущественных вещей (cleanup,
  фоновый UI hint, открытие в Finder без последствий при сбое).
- VM имеют параметрless конструктор для XAML-дизайнера; реальные инстансы
  через DI.
- Tests xUnit, без mock-фреймворков. Парсеры / pure logic — приоритет.

Branch `main`, без remote (юзер пушит сам по желанию).
