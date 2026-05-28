# Cross-platform builds + embedded wine

**Дата:** 2026-05-28
**Статус:** Approved

## Проблема

ArkManager сейчас — macOS-only, framework-dependent:

- `build-app.sh` собирает только `osx-arm64`, юзеру нужен установленный .NET 10 runtime.
- Wine — внешняя зависимость: ставится через `brew install --cask wine-stable`, путь резолвится в системе. Brew-cask `wine-stable` deprecated, отключается 2026-09-01. Установка завязана на sudo (gstreamer .pkg) и требует ручного запуска через Terminal.app.
- На Windows и Linux приложение не собирается вовсе.

Хочется:

1. Избавиться от внешней зависимости wine — он должен быть зашит в дистрибутив.
2. Собирать релизы для macOS, Linux и Windows.

## Решение (high-level)

Два связанных изменения, разнесённые на две фазы:

**Фаза 1 — кросс-платформенная сборка на системном wine.** Рефакторим `IServerLauncher` так, чтобы на Windows запускался native-launcher (без wine), на Mac/Linux — текущий wine-launcher с системным wine. Заменяем `build-app.sh` на единый кросс-OS `build.sh`, который умеет собирать все три RID. GitHub Actions matrix для релизов. После фазы 1 Windows-билд работает полностью autonomously, Mac/Linux-билды — early-access (всё ещё требуют системный wine, но рабочие). Doctor-таб уже удалён в фазе 1, так что Mac/Linux-юзеры просто не получат UI-индикатора отсутствия wine — попытка старта сервера упадёт с понятной ошибкой в логе.

**Фаза 2 — embedded wine.** Build-скрипт скачивает портативные wine-сборки (gcenx для Mac, Lutris-wine для Linux), упаковывает внутрь бандла. Launcher резолвит wine из бандла. Doctor-таб удаляется (он существовал только для диагностики wine/brew/steamcmd, теперь не нужен). После фазы 2 — полностью самодостаточные дистрибутивы.

Self-contained .NET 10 runtime (`--self-contained true`) на всех трёх RID — юзеру не надо ничего ставить.

## Дизайн

### 1. Абстракция запуска сервера

`IServerLauncher` упрощается — `ProbeAsync` / `LauncherStatus` выкидываются (этим пользовался только Doctor). Остаются `StartAsync`, `StopAsync`, `IsRunningAsync`.

Две реализации:

- **`NativeWindowsLauncher`** (Windows) — `Process.Start(serverExe, args)` напрямую, без env-переопределений, без WINEPREFIX.
- **`BundledWineLauncher`** (Mac/Linux) — резолвит wine64 **только** из встроенной в бандл папки:
  - macOS: `<App>.app/Contents/Resources/wine/bin/wine64` (relative от `AppContext.BaseDirectory` через `../Resources/wine/`).
  - Linux: `<exe-dir>/wine/bin/wine64`.
  - Никаких системных fallback, никакого override через `Settings.WineBinaryPath` (это поле удаляется).
  - Если бинарь не найден — это битая инсталляция. Кидаем понятную ошибку в server-лог («Server runtime missing — reinstall ArkManager»). Слово «wine» в UI не светим — только в сорцах и env-переменных.

В фазе 1 `BundledWineLauncher` временно ищет wine в системных путях (текущий `EnumerateWineCandidates`) — это позволяет получить рабочие Mac/Linux билды до того, как заработает download wine-тарбола. В фазе 2 фолбэк убирается, остаётся только embedded путь.

DI:

```csharp
if (OperatingSystem.IsWindows())
    services.AddSingleton<IServerLauncher, NativeWindowsLauncher>();
else
    services.AddSingleton<IServerLauncher, BundledWineLauncher>();
```

**WINEPREFIX:**

- Финальный путь (после фазы 2): `<DataDir>/server-runtime/` (нейтральное имя, без слова «wine»).
- В фазе 1 путь остаётся `<DataDir>/wineprefix/` (существующие юзеры не теряют setup при апгрейде на фазу 1).
- В фазе 2 — переименование пути + legacy cleanup: если при старте обнаружится старая папка `<DataDir>/wineprefix/` — `rm -rf` без вопросов и без UI-уведомлений. Новый префикс создаётся wine'ом при первом старте сервера (slow first-run ~30s).
- Версионирование префикса под wine version не делаем — wine 10 stable существует и не меняет формат префикса часто; если когда-нибудь bump'нем wine и обнаружим несовместимость, тогда и добавим инвалидацию.

**Удаляется из `IServerLauncher.cs`:**

- `LauncherStatus` record.
- `ProbeAsync` метод.

### 2. Wine bundling

**Источники:**

- **macOS arm64**: gcenx wine-stable `.app` от github.com/Gcenx/macOS_Wine_builds. Intel x86_64, работает на M-series через Rosetta 2.
- **Linux x64**: Lutris-wine tarball от github.com/lutris/wine, полностью статически собранный.
- **Windows**: не нужен.

**Source-of-truth — `build/wine-sources.json`:**

```json
{
  "macos-arm64": {
    "url": "https://github.com/Gcenx/macOS_Wine_builds/releases/download/.../wine-stable-X.Y-osx64.tar.xz",
    "sha256": "...",
    "extractedWineDir": "Wine Stable.app/Contents/Resources/wine"
  },
  "linux-x64": {
    "url": "https://github.com/lutris/wine/releases/download/.../lutris-wine-X.Y-x86_64.tar.xz",
    "sha256": "...",
    "extractedWineDir": "lutris-wine-X.Y-x86_64"
  }
}
```

Точные URL и хэши вписываются при имплементации (нужно посмотреть актуальные релизы gcenx и lutris).

**Pipeline:**

1. Build-скрипт читает `wine-sources.json` для нужного RID.
2. Кэш в `~/.cache/ark-manager/wine/<sha256-prefix>/` — переиспользуется между билдами. Если уже распакован — skip скачивание.
3. Скачивание + sha256-проверка + распаковка xz-тарбола.
4. Копирование нужного подкаталога (`extractedWineDir`) в publish-output после `dotnet publish`:
   - Mac: в `<App>.app/Contents/Resources/wine/`.
   - Linux: в `<publish-dir>/wine/`.

**Стриппинг wine** (`winemac.drv`, gstreamer, mono, gecko) — out of scope первой итерации. Если бандл окажется слишком тяжёлым, оптимизируем отдельно.

**Codesigning embedded wine на macOS:** gcenx wine .app уже ad-hoc-подписан. Когда мы оборачиваем его в свой бандл и делаем `codesign --force --deep --sign - "$APP"` — Gatekeeper принимает без notarization (для ad-hoc-подписи notarization не нужна; юзеру при первом запуске right-click → Open).

### 3. Build pipeline + CI

**Локальный `build.sh`** (запускается с Mac/Linux хоста, заменяет `build-app.sh`):

```bash
./build.sh                              # все 3 платформы
./build.sh --target macos               # только Mac
./build.sh --target windows linux       # выборочно
```

Для каждого таргета:

1. Читает `<Version>` из `Directory.Build.props`.
2. Если таргет требует wine (mac/linux) — скачивает по `wine-sources.json` (с кэшем).
3. `dotnet publish -c Release -r <rid> --self-contained true /p:PublishSingleFile=false /p:PublishTrimmed=false`.
4. Укладывает publish-output + wine в правильную структуру.
5. Упаковывает:
   - Mac: `ArkManager-{ver}-macos-arm64.zip` (внутри `.app`).
   - Windows: `ArkManager-{ver}-windows-x64.zip`.
   - Linux: `ArkManager-{ver}-linux-x64.tar.gz`.
6. Mac: ad-hoc codesign бандла.

`Makefile` остаётся тонкой обёрткой: `make`, `make mac`, `make clean`.

**Trimming / PublishSingleFile** не включаем в первой итерации (Avalonia 12 капризен к trim, single-file замедляет старт). Опция на будущее.

**Версия проекта — `Directory.Build.props`:**

```xml
<Project>
  <PropertyGroup>
    <Version>1.0.0</Version>
  </PropertyGroup>
</Project>
```

И `dotnet`, и `build.sh`, и `Info.plist` (через подстановку в скрипте) берут версию отсюда.

**Переименование assembly:** `<AssemblyName>ArkManager</AssemblyName>` в `ArkManager.App.csproj`. Файл csproj называется `ArkManager.App.csproj` (чтобы не ломать solution-ссылку), но выходной бинарь — `ArkManager` (`ArkManager.exe` на Win, `ArkManager` на Linux, `ArkManager.app/Contents/MacOS/ArkManager` на Mac). `CFBundleExecutable` в Info.plist обновляется.

**GitHub Actions — `.github/workflows/release.yml`:**

- Триггер: push тега `v*.*.*`.
- Matrix: `macos-latest`, `ubuntu-latest`, `windows-latest`.
- На каждом раннере: `actions/checkout@v4`, `actions/setup-dotnet@v4` (10.x), запуск `./build.sh --target <os>` (на Windows через git-bash, который установлен на windows-latest by default).
- `actions/cache@v4` для wine-тарболов по ключу-хэшу.
- Артефакты сливаются в один draft GitHub Release с тегом.
- Concurrency: `release-${{ github.ref }}`.
- PR-builds не делаем (экономим минуты).

**Signing:**

- Mac: ad-hoc (как сейчас). Юзер при первом запуске делает right-click → Open.
- Windows: unsigned. SmartScreen warning.
- Linux: no signing.

Apple Developer ID ($99/yr) и Authenticode ($200+/yr) — out of scope.

**Remote:** репа без remote. Workflow-файл лежит в репе готовый, активируется когда репа окажется на GitHub.

### 4. Прочее (мелкие изменения)

**`AppSettings`:**

- Удаляется поле `WineBinaryPath` (если есть). System.Text.Json игнорирует unknown properties — старые `settings.json` юзеров не сломаются.
- Sanity-check: проверить, не остались ли мёртвые поля от удалённого Settings-таба / Doctor.

**`AppPaths` Windows-фикс:**

- Сейчас `SpecialFolder.ApplicationData` = `%APPDATA%` (Roaming). 25GB ASA сервера в Roaming — некрасиво.
- Меняется на `SpecialFolder.LocalApplicationData` = `%LOCALAPPDATA%/ArkManager`.

**`SteamCmdService` cross-platform:**

- Bootstrap URL по host OS:
  - Mac: `steamcmd_osx.tar.gz`.
  - Linux: `steamcmd_linux.tar.gz`.
  - Windows: `steamcmd.zip`.
- Флаг `+@sSteamCmdForcePlatformType windows` нужен на Mac **и Linux** (форсит загрузку Windows-сборки ASA). На Windows-хосте не применяется.
- Бинарь после распаковки: `steamcmd.sh` (mac/linux) vs `steamcmd.exe` (Win) — Windows-ветка добавляется в `ResolveSteamCmdBinary()`.

**`OpenInFinder` / `OpenInBrowser` (в `App.axaml.cs`):**

- Mac: `open <path|url>`.
- Linux: `xdg-open <path|url>`.
- Windows: `Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })`.

UI-label «Open in Finder» → нейтральное «Open folder» (для всех платформ одной строкой).

**`ProcessRunner` / `File.SetUnixFileMode`:**

- Места взвода executable-бита гардятся `if (!OperatingSystem.IsWindows())`.

**Тесты:**

- Удаляются `DoctorServiceTests` (если есть).
- Сохраняются все существующие `ServerCommandLine`-тесты (password-not-in-URL и пр).
- Добавляется тест на launcher selection (DI резолвит правильный лаунчер по OS).
- Добавляется тест на парсер `wine-sources.json` (простой record).

## Удаляемые файлы

- `src/ArkManager.Core/Services/Doctor/DoctorService.cs`
- `src/ArkManager.Desktop/Views/DoctorView.axaml` + `.axaml.cs`
- `src/ArkManager.Desktop/ViewModels/DoctorViewModel.cs`
- `tests/ArkManager.Core.Tests/DoctorServiceTests.cs` (если существует)
- `build-app.sh` (заменяется на `build.sh`)
- Запись «Doctor» из nav в `MainWindowViewModel`.

## Новые файлы

- `Directory.Build.props`
- `build.sh`
- `build/wine-sources.json`
- `.github/workflows/release.yml`
- `src/ArkManager.Core/Services/Launchers/NativeWindowsLauncher.cs`
- (Rename) `WineLauncher.cs` → `BundledWineLauncher.cs`

## Phasing

**Фаза 1 (можно мерджить отдельно):**

- `IServerLauncher` refactor (Probe убран, две реализации, DI-выбор).
- `NativeWindowsLauncher`.
- `BundledWineLauncher` с временным fallback на системный wine.
- Cross-OS изменения: `AppPaths` Windows-фикс, `SteamCmdService` per-OS, `OpenInFinder`/`OpenInBrowser`.
- `build.sh` с тремя RID (без wine bundling — пока используется system wine на Mac/Linux).
- `.github/workflows/release.yml`.
- `Directory.Build.props`, переименование assembly.
- Удаление Doctor.
- Тесты + чистка `AppSettings`.

**Фаза 2:**

- `build/wine-sources.json` + download/cache/unpack логика в `build.sh`.
- `BundledWineLauncher` — убирается system-fallback, остаётся только embedded путь.
- Legacy cleanup старой папки `<DataDir>/wineprefix/`.
- Переименование WINEPREFIX-пути с `wineprefix` на `server-runtime`.
- Обновление CLAUDE.md (выпиливаем упоминания brew/Doctor/wine-paths, добавляем секцию про embedded wine).

## Out of scope

- Auto-update механизм.
- Apple Developer ID, notarization, Authenticode.
- AppImage / .dmg / native installers (Inno Setup / WiX).
- `PublishTrimmed` / `PublishSingleFile`.
- Стриппинг wine (winemac.drv / gstreamer / mono / gecko).
- Headless-mode CLI (ArkManager без GUI для запуска на серверах без X11/Wayland).
- macOS Intel хосты.
- Linux ARM64.
- Миграция данных юзера между major-апдейтами wine.

## Размер артефактов (грубо)

- macOS `.zip`: ~80MB .NET + ~50MB код/Avalonia + ~400MB wine ≈ 530MB сырой, ~300MB сжатый.
- Linux `.tar.gz`: ~80MB + ~50MB + ~300MB wine ≈ 430MB сырой, ~250MB сжатый.
- Windows `.zip`: ~80MB + ~50MB ≈ 130MB сырой, ~60MB сжатый.
