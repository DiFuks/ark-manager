# ArkManager — context for Claude

ASA (ARK: Survival Ascended) dedicated server manager для macOS. Аналог
Windows-only ASADedicatedManager, написан под Mac. Цель — нативный mac-GUI
для управления полным жизненным циклом сервера (install/config/mods/backup/
start/stop/RCON), сам ASA-сервер крутится через **wine64** (встроен в бандл),
потому что нативной mac-сборки сервера не существует.

## Stack & commands

- .NET 10 SDK, Avalonia 12, CommunityToolkit.Mvvm 8.x, MS.Ext.DI, xUnit
- Solution — `.slnx` (XML-формат .NET 10), НЕ `.sln`

```bash
dotnet build ArkManager.slnx
dotnet test  ArkManager.slnx
dotnet run --project src/ArkManager.Desktop/ArkManager.App.csproj
```

## Layout

- `src/ArkManager.Core/` — UI-агностичная бизнес-логика
  - `Models/` — AppSettings, ServerLaunchOptions, ServerProfile, Maps
  - `Services/{AppPaths,SettingsService,ServerManager}.cs`
  - `Services/Steam/SteamCmdService.cs` (+ `InstalledServerVersion` record)
  - `Services/Config/{IniFile,ConfigService}.cs`
  - `Services/Backups/{BackupService,AutoBackupWorker}.cs`
  - `Services/Mods/{ModsService,CurseForgeClient}.cs`
  - `Services/Launchers/{IServerLauncher,ServerCommandLine,WineLauncher}.cs`
  - `Services/Rcon/{RconClient,PlayerPoller}.cs`
  - `Util/ProcessRunner.cs`
- `src/ArkManager.Desktop/` — Avalonia 12 GUI
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

`Server → RCON → Install → Config → Mods → Backups`

(Стартовый таб — `Server`. Dashboard был, но удалён: единственное уникальное
с него — players online / names — переехало в Server.)

## App-local state

Vendor-каталог приложения (соответствует правилу из user CLAUDE.md):

- macOS:  `~/Library/Application Support/ArkManager/`
- Linux:  `$XDG_DATA_HOME/ArkManager/`
- Win:    `%APPDATA%/ArkManager/`

Содержит: `settings.json`, `logs/`, `steamcmd/`, `backups/`, `server/` (default),
`server-runtime/` (WINEPREFIX, создаётся wine'ом при первом запуске).

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

### Дизайн-система «Field Manual» (слой темы)

UI переведён на единый визуальный язык C1 «Field Manual» (тёплый уголь +
ember-янтарь, слэб-заголовки). Весь дизайн вынесен в `src/ArkManager.Desktop/Themes/`:

- `Tokens.axaml` — `SolidColorBrush`-токены (`BgBrush`, `PanelBrush`, `AccentBrush`,
  `MutedBrush`, `OkBrush`, `DangerBrush`, …). **Хардкод-хексов в Views больше нет** —
  только `{DynamicResource …}`/`Classes`.
- `Icons.axaml` — `StreamGeometry`-глифы (solid, под `PathIcon`). Эмодзи в UI запрещены.
- `Resources.axaml` — мёрджит Tokens+Icons+ControlThemes, плюс `FontFamily` ключи
  (`DisplayFont`=Zilla Slab, `UiFont`=IBM Plex Sans, `MonoFont`=IBM Plex Mono;
  ttf вшиты в `Assets/Fonts/`, `avares://…/#Family`). Подключён в `App.axaml` как
  `<Application.Resources>`.
- `TextStyles.axaml` — классы `TextBlock` (`h1`/`stat`/`section`/`meta`).
- `Controls.axaml` — стили: `Button` (база = ghost, `.primary`/`.icon`/`.danger`/`.chip`),
  `Border.panel`/`.tile`/`.console`/`.chip`/`.pill`, инпуты, `ListBox.nav`/`.rows`,
  `TabControl.seg` (сегмент-табы через **полный ретемплейт TabItem** — иначе лезет
  синяя Fluent-пипка).
- `ControlThemes.axaml` — `ControlTheme` для `ButtonSpinner` (NumericUpDown): Fluent-дефолт
  «стёсывает» скруглённый угол квадратными кнопками; внутренний бордер недостижим
  app-level стилями (двойной `/template/` не резолвится), поэтому переопределён целиком
  (скруглённый бордер + `ClipToBounds` + плоские шеврон-кнопки). `NumericUpDown /template/
  TextBox` гасится, чтобы не было двойного бордера.

`App.axaml`: `RequestedThemeVariant="Dark"` (токены dark-only). UI — **английский**
(копирайт VM/Core переведён; единственная кириллица в репо — комментарии Core).

`MainWindowViewModel`: env `ARKMANAGER_START_TAB=<TabTitle>` открывает приложение
сразу на нужном табе (для тестов/скриншотов; по умолчанию выключено).

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
- **`.exe` запускается через wine64** (встроен в бандл). Whisky-cask
  архивирован (Aug 2024), Parallels-launcher выпилен; brew-cask wine-stable
  тоже убран — wine теперь bundled.
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

## Wine (embedded)

Wine идёт встроенным в бандл — юзер ничего не ставит, слова «wine» в UI нет.

- macOS: `<App>.app/Contents/Resources/wine/bin/wine64` (Intel x86_64 от gcenx,
  запускается под Rosetta 2 на Apple Silicon).
- Linux: `<install-dir>/wine/bin/wine64` (Lutris-wine static build).
- Windows: wine не используется — `NativeWindowsLauncher` запускает .exe нативно.
- Источники пиним в `build/wine-sources.json` (URL + SHA256). `build.sh` качает,
  проверяет хэш, кладёт в `~/.cache/ark-manager/wine/` и копирует в бандл.

WINEPREFIX живёт в `<DataDir>/server-runtime/`, создаётся wine'ом при первом
запуске сервера (slow first-run ~30s). Старая папка `<DataDir>/wineprefix/`
от прежней brew-версии чистится автоматически на старте `AppPaths`.

В env-переменных запуска: `WINEDEBUG=-all`, `WINEDLLOVERRIDES=winemac.drv=`
(без последнего wine на macOS рисует Server Console-окно с белым на белом).

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
- GUI i18n / переключение языка в рантайме (resx/ResourceManager). UI сейчас
  статически английский; мультиязычность отложена.
- ARK Game.ini secondary settings (OverrideEngramEntries, EngramOverrides и
  тонна других кастомизаций) — есть raw-редактор Game.ini как fallback.
- Лимит RAM сервера. У ASA нет CLI-флага, на macOS нет cgroups, `ulimit -v`
  ломает wine. Workaround — `ScheduledRestartHours` (есть в Settings).
- AppImage / .dmg / installers — дистрибуция пока только через прямой запуск.
- Code signing / notarization — не подписано, Gatekeeper требует ручного разрешения.
- Headless CLI — нет, только GUI.
- ARM64 Linux — не тестировалось, wine-бандл x86_64.
- Intel Mac — не тестировалось (только Apple Silicon + Rosetta).

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
