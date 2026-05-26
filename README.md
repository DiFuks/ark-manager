# ArkManager

Менеджер ARK: Survival Ascended dedicated server для macOS. Аналог
`ASADedicatedManager` (Windows), нативно работающий на маке:

* установка/обновление сервера через **SteamCMD** (app id `2430930`,
  с принудительной Windows-сборкой через `+@sSteamCmdForcePlatformType windows`);
* отображение **версии сервера** (`buildid` из `appmanifest_2430930.acf`) +
  кнопка `Check for updates` тянет latest build через `steamcmd +app_info_print`;
* редактирование `GameUserSettings.ini` / `Game.ini` (форма + raw-tabs);
* создание и восстановление **бэкапов** (`ShooterGame/Saved`, zip, ротация);
* **автобэкап** с настраиваемым интервалом (опция «только когда сервер запущен»);
* менеджмент модов CurseForge — список ID, добавление/удаление, порядок;
* запуск/остановка сервера, потоковый просмотр лога, uptime/PID,
  кнопки Start/Stop дизейблятся по состоянию;
* **RCON клиент** (Source RCON по TCP) с быстрыми кнопками `saveworld`/`DoExit`/`Broadcast`;
* **live players** на Server-табе — бэкграунд-опрос `ListPlayers` раз в 30с;
* **авто-рестарт** при креше + опциональный периодический рестарт каждые N часов;
* поддержка **кластера** (`-ClusterId` / `-ClusterDirOverride`);
* пресеты карт (TheIsland / Center / Scorched / Aberration / Extinction / Astraeos / Ragnarok);
* опциональный **CurseForge API** для резолва ID → имя/описание модов;
* запуск .exe-сервера через **wine64** (cask `wine-stable`); установку wine
  Doctor делает сам через Terminal-скрипт.

## Требования

* macOS (Apple Silicon или Intel, на Apple Silicon — нужна Rosetta 2)
* `.NET 10 SDK`
* `wine64` — ставится из Doctor одной кнопкой:
  * `brew install --cask wine-stable` (+ снятие quarantine через `xattr`);
  * скрипт автоматически предложит поставить Rosetta 2, если её нет.

ASA-сервер заведомо работает под Wine при флаге `-NoBattlEye`
(BattlEye под Wine не запустится). Это включено по умолчанию.

> **Note:** cask `wine-stable` помечен deprecated, отключение `2026-09-01`. Уже
> установленный wine продолжит работать; после этой даты потребуется ручная
> миграция на `gcenx/wine/game-porting-toolkit` или альтернативный cask.

## Запуск из исходников

```bash
git clone <repo>
cd ark-manager
dotnet build ArkManager.slnx
dotnet run --project src/ArkManager.App/ArkManager.App.csproj
```

При первом запуске создаётся `~/Library/Application Support/ArkManager/`:

* `settings.json` — все настройки (пути, опции запуска, моды, автобэкап);
* `logs/` — место для логов приложения;
* `steamcmd/` — встроенный SteamCMD (можно переопределить путь в Settings);
* `backups/` — место по умолчанию для zip-бэкапов;
* `server/` — место по умолчанию для самого ASA-сервера;
* `wineprefix/` — WINEPREFIX по умолчанию (wine инициализирует автоматически
  при первом запуске сервера, ~30 сек).

## Быстрый старт

1. **Doctor** → `Run checks`. Если wine не установлен — `Install wine (brew)`
   откроет окно Terminal, попросит sudo-пароль для `gstreamer-runtime` и
   подтверждение установки Rosetta 2.
2. **Install** — `Install / Reinstall SteamCMD` (≈10 МБ),
   затем `Install / Update server` (~15–25 ГБ, долго в первый раз). После
   установки внизу показан `installed buildid`; кнопка `🔄 Check for updates`
   сравнит с актуальным.
3. **Config** — задай `Session name`, `Admin password`, порты. Сохрани.
   Пароли пишутся **только в ini**, не в URL-query сервера (известный
   foot-gun ASA: URL-парсер ломает RCON-аутентификацию).
4. **Mods** — вставь CurseForge ID (по одному или через запятую) → `Add`.
5. **Server** → `▶ Start`. Лог льётся в окно; `■ Stop` останавливает процесс.
   Под кнопками — live-счётчик игроков из RCON.
6. **Backups** → `Create` сохранит `Saved/` в `asa-backup-YYYYMMDD-HHMMSS.zip`,
   с ротацией по N последних. В Settings можно включить автобэкап раз в N минут.

## Архитектура

```
ArkManager.slnx
├── src/ArkManager.Core/         # UI-агностичная бизнес-логика
│   ├── Models/AppSettings.cs    # настройки/профили/опции запуска
│   ├── Services/
│   │   ├── AppPaths.cs          # vendor-каталог в Application Support
│   │   ├── SettingsService.cs   # JSON I/O + событие Changed
│   │   ├── ServerManager.cs     # стейт-машина сервера + лог-кольцо
│   │   ├── Steam/SteamCmdService.cs     # install + version probe (acf/app_info)
│   │   ├── Config/IniFile.cs            # round-trip ini parser (UE-style)
│   │   ├── Config/ConfigService.cs      # GameUserSettings/Game.ini
│   │   ├── Backups/BackupService.cs     # zip create/restore/rotate
│   │   ├── Backups/AutoBackupWorker.cs  # фоновый таймер автобэкапов
│   │   ├── Mods/ModsService.cs
│   │   ├── Doctor/DoctorService.cs      # probes + Terminal-скрипт wine-install
│   │   ├── Rcon/{RconClient,PlayerPoller}.cs
│   │   └── Launchers/
│   │       ├── IServerLauncher.cs
│   │       ├── ServerCommandLine.cs     # сборка CLI ASA (без паролей в URL)
│   │       └── WineLauncher.cs          # wine64 + WINEPREFIX
│   └── Util/ProcessRunner.cs    # потоковый wrapper над Process
├── src/ArkManager.App/          # Avalonia 12 GUI, MVVM (CommunityToolkit.Mvvm)
│   ├── App.axaml(.cs)           # DI + helpers (UiThread/OpenInFinder/OpenInBrowser)
│   ├── AppServices.cs           # ServiceCollection composition root
│   ├── ViewModels/              # *ViewModel.cs per таб
│   └── Views/                   # *.axaml + code-behind
└── tests/ArkManager.Core.Tests/ # xUnit
```

Табы: `Server → RCON → Install → Config → Mods → Backups → Doctor → Settings`.

### Launcher

`WineLauncher` ищет `wine64` в этом порядке:

1. `settings.WineBinaryPath` (override из Settings).
2. `/Applications/Wine Stable.app/Contents/Resources/wine/bin/wine64`
3. `Wine Staging.app` / `Wine Devel.app` / `Game Porting Toolkit.app` /
   `Wine Crossover.app` — fallback для альтернативных установок.
4. `/opt/homebrew/bin/wine64`, `/usr/local/bin/wine64`.

`WINEPREFIX` — `settings.WinePrefixPath` (по умолчанию
`~/Library/Application Support/ArkManager/wineprefix`). Wine сам ребилдит
префикс при первом старте.

### Командная строка сервера

Собирается в `ServerCommandLine.Build`. Формат:

```
"TheIsland_WP?listen?SessionName=...?Port=N?QueryPort=M?MaxPlayers=K"
 -server -log -mods=ID1,ID2 -automanagedmods -NoBattlEye  <extra...>
```

Пароли (`ServerPassword`, `ServerAdminPassword`, `SpectatorPassword`) и
`RCONEnabled` / `RCONPort` **не кладутся в URL** — только в
`GameUserSettings.ini` через `ConfigService.ApplyLaunchOptionsToIni`. Причина:
ASA URL-парсер склеивает хвост строки в значение пароля, RCON-аутентификация
после этого ломается. Превью CLI — на вкладке **Config**.

## RCON

Вкладка **RCON**. По умолчанию подставляет порт/пароль из текущих настроек.
Используется протокол Source RCON (TCP). Команды:

* `ListPlayers`, `Broadcast <msg>`, `saveworld`, `DoExit`, `KickPlayer <id>` и т.д.
* быстрые кнопки `saveworld` и `DoExit` справа от Send.

RCON-пароль — это `[ServerSettings].ServerAdminPassword` в `GameUserSettings.ini`.

## Что я пока не делал

* Multi-instance UI — модель `Profiles` есть, но GUI работает только с первым (`Default`).
* Глобальный CurseForge browser — только резолв имени по ID, без поиска.
* GUI-локализация (сейчас русский в подсказках).
* Жёсткий лимит RAM сервера. У ASA нет CLI-флага; на macOS нет cgroups;
  `ulimit -v` ломает wine. Альтернатива — `Settings → Периодический рестарт каждые N часов`.

## Тесты

```bash
dotnet test ArkManager.slnx
```

Прогоняют `IniFile` round-trip, `ServerCommandLine.Build` (включая
проверку, что пароли/RCON отсутствуют в URL), `SteamCmdService` парсеры
(`appmanifest_*.acf` + `app_info_print` для public-ветки), `PlayerPoller`.
