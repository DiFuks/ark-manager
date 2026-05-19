# ArkManager

Менеджер ARK: Survival Ascended dedicated server для macOS. Аналог
`ASADedicatedManager` (Windows), нативно работающий на маке:

* установка/обновление сервера через **SteamCMD** (app id `2430930`,
  с принудительной Windows-сборкой через `+@sSteamCmdForcePlatformType windows`);
* редактирование `GameUserSettings.ini` / `Game.ini` (форма + raw-tabs);
* создание и восстановление **бэкапов** (`ShooterGame/Saved`, zip, ротация);
* менеджмент модов CurseForge — список ID, добавление/удаление, порядок;
* запуск/остановка сервера, потоковый просмотр лога, uptime/PID;
* **RCON клиент** (Source RCON по TCP) с быстрыми кнопками `saveworld`/`DoExit`/`Broadcast`;
* **авто-рестарт** при креше + опциональный периодический рестарт каждые N часов;
* поддержка **кластера** (`-ClusterId` / `-ClusterDirOverride`);
* пресеты карт (TheIsland / Center / Scorched / Aberration / Extinction / Astraeos / Ragnarok);
* опциональный **CurseForge API** для резолва ID → имя/описание модов;
* runtime-абстракция: **Whisky** (по умолчанию), brew-wine, **Parallels** VM.

## Требования

* macOS (Apple Silicon или Intel)
* `.NET 10 SDK`
* один из вариантов запуска Windows-бинарника:
  * **Whisky** (рекомендуется) — `brew install --cask whisky`, GPTK-based wine;
  * **wine** через brew (например, `gcenx/wine`) — fallback;
  * **Parallels Desktop** — для запуска в гостевой Windows VM.

ASA-сервер заведомо работает под Wine/Proton при флаге `-NoBattlEye`
(BattlEye под Wine не запустится). Это включено по умолчанию.

## Запуск из исходников

```bash
git clone <repo>
cd ark-manager
dotnet build ArkManager.slnx
dotnet run --project src/ArkManager.App/ArkManager.App.csproj
```

При первом запуске создаётся `~/Library/Application Support/ArkManager/`:

* `settings.json` — все настройки (пути, runtime, опции запуска, моды);
* `logs/` — будущее место для логов приложения;
* `steamcmd/` — встроенный SteamCMD (можно переопределить путь в Settings);
* `backups/` — место по умолчанию для zip-бэкапов;
* `server/` — место по умолчанию для самого ASA-сервера.

## Быстрый старт

1. **Doctor** — нажми `Run checks`, посмотри, чего не хватает.
   Если Whisky нет — кнопка `Install Whisky (brew)` поставит его через `brew install --cask whisky`.
2. **Install** — `Install / Reinstall SteamCMD` (≈10 МБ),
   затем `Install / Update server` (~15–25 ГБ, долго в первый раз).
3. **Config** — задай `Session name`, пароль, порты. Сохрани.
4. **Mods** — вставь CurseForge ID (по одному или через запятую) → `Add`.
5. **Server** → `▶ Start`. Лог льётся в окно; `■ Stop` останавливает процесс.
6. **Backups** → `Create` сохранит `Saved/` в `asa-backup-YYYYMMDD-HHMMSS.zip`,
   с ротацией по N последних.

## Архитектура

```
ArkManager.sln(x)
├── src/ArkManager.Core/         # UI-агностичная бизнес-логика
│   ├── Models/AppSettings.cs    # настройки/профили/опции запуска
│   ├── Services/
│   │   ├── AppPaths.cs          # vendor-каталог в Application Support
│   │   ├── SettingsService.cs   # JSON I/O + событие Changed
│   │   ├── ServerManager.cs     # стейт-машина сервера + лог-кольцо
│   │   ├── Steam/SteamCmdService.cs
│   │   ├── Config/IniFile.cs    # round-trip ini parser (UE-style)
│   │   ├── Config/ConfigService.cs
│   │   ├── Backups/BackupService.cs
│   │   ├── Mods/ModsService.cs
│   │   ├── Doctor/DoctorService.cs
│   │   └── Launchers/
│   │       ├── IServerLauncher.cs
│   │       ├── ServerCommandLine.cs  # построение CLI ASA
│   │       ├── WineLauncherBase.cs
│   │       ├── WhiskyLauncher.cs
│   │       ├── LocalWineLauncher.cs
│   │       ├── ParallelsLauncher.cs  # prlctl exec
│   │       └── LauncherFactory.cs
│   └── Util/ProcessRunner.cs    # потоковый wrapper над Process
├── src/ArkManager.App/          # Avalonia 12 GUI, MVVM (CommunityToolkit.Mvvm)
│   ├── App.axaml(.cs)           # DI + helpers (UiThread/OpenInFinder/OpenInBrowser)
│   ├── AppServices.cs           # ServiceCollection composition root
│   ├── ViewModels/              # *ViewModel.cs per page
│   └── Views/                   # *.axaml + code-behind
└── tests/ArkManager.Core.Tests/ # xUnit
```

### Runtime-абстракция

`IServerLauncher.StartAsync(settings, modIds, onOutput, onExit, ct)` —
универсальный контракт. Реализации:

* `WhiskyLauncher` — запускает `wine64` из `Whisky.app/Contents/Resources/Libraries/Wine/bin/`
  с `WINEPREFIX = <боттл>`. Боттл авто-детектится либо настраивается в Settings.
* `LocalWineLauncher` — `/opt/homebrew/bin/wine64` или `/usr/local/bin/wine64`,
  `WINEPREFIX = ~/.wine` (можно переопределить).
* `ParallelsLauncher` — `prlctl exec <vm> cmd /c "<exe> <args>"`.
  Хост-путь до сервера транслируется в гостевой через `\\Mac\Home\…`
  (или префикс из `ARK_GUEST_PATH_PREFIX`).

### Командная строка сервера

Собирается в `ServerCommandLine.Build`. Формат:

```
"TheIsland_WP?listen?SessionName=...?Port=...?QueryPort=...?ServerPassword=...?RCONEnabled=True?RCONPort=..."
 -server -log -mods=ID1,ID2 -automanagedmods -NoBattlEye  <extra...>
```

Превью полной строки доступно во вкладке **Config → Preview CLI**.

## RCON

Вкладка **RCON**. По умолчанию подставляет порт/пароль из текущих настроек.
Используется протокол Source RCON (TCP). Команды:

* `ListPlayers`, `Broadcast <msg>`, `saveworld`, `DoExit`, `KickPlayer <id>` и т.д.
* быстрые кнопки `saveworld` и `DoExit` справа от Send.

## Что я пока не делал

* Multi-instance UI — модель `Profiles` уже есть, но UI работает только с первым (`Default`).
* Глобальный CurseForge browser — только резолв имени по ID, без поиска.
* In-app realtime-статус игроков (отдельный воркер с периодическим `ListPlayers` по RCON).
* GUI-локализация (сейчас русский в подсказках).

## Тесты

```bash
dotnet test ArkManager.slnx
```

Прогоняют `IniFile` round-trip и `ServerCommandLine.Build`.
