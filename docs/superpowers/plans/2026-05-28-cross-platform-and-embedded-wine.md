# Cross-platform builds + embedded wine — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship ArkManager as standalone bundles for macOS arm64, Linux x64, and Windows x64 — with wine embedded inside the Mac/Linux bundles so end users don't install it externally.

**Architecture:** Two sequential phases.
- **Phase 1** — Abstract `IServerLauncher` into a Windows-native variant (no wine) and a wine variant; replace `build-app.sh` with a cross-OS `build.sh`; add GitHub Actions matrix CI; drop the Doctor tab entirely. Mac/Linux bundles in Phase 1 still rely on system wine (early-access).
- **Phase 2** — Extend `build.sh` to download portable wine (gcenx for Mac, Lutris-wine for Linux) and embed it inside the bundles; switch `BundledWineLauncher` to embedded-only resolution; rename WINEPREFIX path; CI caches wine tarballs.

**Tech Stack:** .NET 10, Avalonia 12, CommunityToolkit.Mvvm, xUnit, bash, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-05-28-cross-platform-and-embedded-wine-design.md`

---

## File structure

**Created:**
- `Directory.Build.props` — single source of truth for `<Version>`.
- `build.sh` — cross-OS build script (mac/linux/win), replaces `build-app.sh`.
- `build/wine-sources.json` — pinned wine tarball URLs + SHA256.
- `.github/workflows/release.yml` — CI release matrix.
- `src/ArkManager.Core/Services/Launchers/NativeWindowsLauncher.cs` — native Windows server launcher.
- `tests/ArkManager.Core.Tests/SteamCmdBootstrapTests.cs` — per-OS URL selection test.

**Modified:**
- `src/ArkManager.Core/Services/Launchers/IServerLauncher.cs` — drop `ProbeAsync` + `LauncherStatus`.
- `src/ArkManager.Core/Services/Launchers/WineLauncher.cs` → renamed to `BundledWineLauncher.cs`; in Phase 2 the system-wine fallback is removed.
- `src/ArkManager.Core/Services/Launchers/ServerCommandLine.cs` — comment update only.
- `src/ArkManager.Core/Services/AppPaths.cs` — Windows uses `LocalApplicationData`; `DefaultWinePrefixDir` renamed to `ServerRuntimeDir` in Phase 2.
- `src/ArkManager.Core/Services/Steam/SteamCmdService.cs` — add Windows bootstrap URL, extract helper, guard chmod, skip `+@sSteamCmdForcePlatformType windows` on Windows hosts.
- `src/ArkManager.Desktop/AppServices.cs` — DI picks `NativeWindowsLauncher` on Windows, `BundledWineLauncher` elsewhere; remove `DoctorService`/`DoctorViewModel` registrations.
- `src/ArkManager.Desktop/ViewModels/MainWindowViewModel.cs` — remove `DoctorViewModel` ctor param and nav entry.
- `src/ArkManager.Desktop/ArkManager.App.csproj` — `<AssemblyName>ArkManager</AssemblyName>`.
- `src/ArkManager.Desktop/Themes/Icons.axaml` — remove `IconDoctor` key.
- `Makefile` — thin wrapper around `build.sh`.
- `.gitignore` — add `build/.cache/`, ensure `dist/` is ignored.
- `CLAUDE.md` — Phase 2: drop brew/Doctor mentions, add embedded-wine section.

**Deleted:**
- `src/ArkManager.Core/Services/Doctor/DoctorService.cs`
- `src/ArkManager.Desktop/Services/Doctor/` (the directory may be empty after — also remove)
- `src/ArkManager.Desktop/Views/DoctorView.axaml` + `.axaml.cs`
- `src/ArkManager.Desktop/ViewModels/DoctorViewModel.cs`
- `build-app.sh`

---

## Sanity checks (run after every task)

```bash
dotnet build ArkManager.slnx
dotnet test ArkManager.slnx
```

Both must succeed before the commit step. If either fails, fix before moving on.

---

# Phase 1 — Cross-platform builds with system wine

## Task 1: Add `Directory.Build.props` with version

**Files:**
- Create: `Directory.Build.props`

- [ ] **Step 1: Create the file**

```xml
<Project>
  <PropertyGroup>
    <Version>1.0.0</Version>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: Verify build picks it up**

```bash
dotnet build ArkManager.slnx -v:minimal 2>&1 | grep -i version || true
dotnet build ArkManager.slnx
```

Expected: build succeeds. The version property is applied to all csproj outputs automatically (MSBuild auto-imports `Directory.Build.props` from parent dirs).

- [ ] **Step 3: Commit**

```bash
git add Directory.Build.props
git commit -m "Add Directory.Build.props with shared Version"
```

---

## Task 2: Rename assembly output to `ArkManager`

The desktop csproj is `ArkManager.App.csproj`; today its output is `ArkManager.App.exe/dll`. Bare `ArkManager` looks cleaner on Windows/Linux.

**Files:**
- Modify: `src/ArkManager.Desktop/ArkManager.App.csproj`
- Modify: `build-app.sh` (only temporarily — fully replaced in Task 11; here we make sure the existing script keeps working on this PR)

- [ ] **Step 1: Edit csproj — add `<AssemblyName>`**

In `src/ArkManager.Desktop/ArkManager.App.csproj` under the existing `<PropertyGroup>`, add:

```xml
<AssemblyName>ArkManager</AssemblyName>
```

So the block reads (full, for context):

```xml
<PropertyGroup>
  <OutputType>WinExe</OutputType>
  <TargetFramework>net10.0</TargetFramework>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
  <ApplicationManifest>app.manifest</ApplicationManifest>
  <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>
  <AssemblyName>ArkManager</AssemblyName>
</PropertyGroup>
```

- [ ] **Step 2: Update `build-app.sh` to use the new bin name**

In `build-app.sh`, change `EXECUTABLE="ArkManager.App"` to `EXECUTABLE="ArkManager"`. (This script gets deleted in Task 13; we touch it here only so the old build still works in the meantime if anyone runs it.)

- [ ] **Step 3: Verify build**

```bash
dotnet build ArkManager.slnx
ls src/ArkManager.Desktop/bin.noindex/Debug/net10.0/ArkManager.dll
```

Expected: file `ArkManager.dll` exists (not `ArkManager.App.dll`).

- [ ] **Step 4: Commit**

```bash
git add src/ArkManager.Desktop/ArkManager.App.csproj build-app.sh
git commit -m "Rename Desktop assembly output to ArkManager"
```

---

## Task 3: Simplify `IServerLauncher` (drop `ProbeAsync`, `LauncherStatus`)

`ProbeAsync` was only consumed by `DoctorService`. Doctor is being deleted (Task 9). The interface gets leaner; the existing `WineLauncher` implementation removes the override.

**Files:**
- Modify: `src/ArkManager.Core/Services/Launchers/IServerLauncher.cs`
- Modify: `src/ArkManager.Core/Services/Launchers/WineLauncher.cs:42-60`

- [ ] **Step 1: Rewrite `IServerLauncher.cs`**

```csharp
using ArkManager.Core.Models;

namespace ArkManager.Core.Services.Launchers;

public sealed record RunningServer(int Pid, DateTime StartedAt);

public interface IServerLauncher
{
    /// <summary>
    /// Запускает ArkAscendedServer.exe. stdout/stderr идут в коллбеки.
    /// </summary>
    Task<RunningServer> StartAsync(
        AppSettings settings,
        IReadOnlyList<string> modIds,
        Action<string> onOutput,
        Action<int> onExit,
        CancellationToken ct = default);

    Task StopAsync(int pid, CancellationToken ct = default);

    Task<bool> IsRunningAsync(int pid, CancellationToken ct = default);
}
```

- [ ] **Step 2: Remove `ProbeAsync` from `WineLauncher.cs`**

Delete the entire `ProbeAsync` method body (lines ~42–60 in the current file, the whole block ending with the catch returning `LauncherStatus`).

- [ ] **Step 3: Verify build**

```bash
dotnet build ArkManager.slnx
```

Expected: build fails — `DoctorService.cs` still references `ProbeAsync` and `LauncherStatus`. That is OK; we delete it in Task 9. To keep this commit green, **also** comment out the lines in `DoctorService.cs` that touch `_launcher.ProbeAsync`/`LauncherStatus`:

In `src/ArkManager.Core/Services/Doctor/DoctorService.cs`, replace the wine-probe block (around lines 40–43) with:

```csharp
// 3. Wine runtime
results.Add(new("Wine", true, "(probe removed — Doctor is being deleted)"));
```

And remove the `using ArkManager.Core.Services.Launchers;` only if it becomes unused (probably still used for `IServerLauncher` field type — keep).

Re-run build:

```bash
dotnet build ArkManager.slnx
dotnet test ArkManager.slnx
```

Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add src/ArkManager.Core/Services/Launchers/IServerLauncher.cs \
        src/ArkManager.Core/Services/Launchers/WineLauncher.cs \
        src/ArkManager.Core/Services/Doctor/DoctorService.cs
git commit -m "IServerLauncher: drop ProbeAsync/LauncherStatus (Doctor consumer only)"
```

---

## Task 4: `SteamCmdService` — add Windows support + extract testable URL helper + guard chmod

**Files:**
- Create: `tests/ArkManager.Core.Tests/SteamCmdBootstrapTests.cs`
- Modify: `src/ArkManager.Core/Services/Steam/SteamCmdService.cs`

- [ ] **Step 1: Write failing test for URL selection**

Create `tests/ArkManager.Core.Tests/SteamCmdBootstrapTests.cs`:

```csharp
using ArkManager.Core.Services.Steam;
using Xunit;

namespace ArkManager.Core.Tests;

public class SteamCmdBootstrapTests
{
    [Fact]
    public void Mac_returns_osx_tarball_url()
    {
        var url = SteamCmdService.SelectBootstrapUrl(SteamCmdHostOs.MacOS);
        Assert.Equal("https://steamcdn-a.akamaihd.net/client/installer/steamcmd_osx.tar.gz", url);
    }

    [Fact]
    public void Linux_returns_linux_tarball_url()
    {
        var url = SteamCmdService.SelectBootstrapUrl(SteamCmdHostOs.Linux);
        Assert.Equal("https://steamcdn-a.akamaihd.net/client/installer/steamcmd_linux.tar.gz", url);
    }

    [Fact]
    public void Windows_returns_windows_zip_url()
    {
        var url = SteamCmdService.SelectBootstrapUrl(SteamCmdHostOs.Windows);
        Assert.Equal("https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip", url);
    }

    [Fact]
    public void ForcePlatformWindows_arg_omitted_on_windows_host()
    {
        var args = SteamCmdService.BuildInstallArgs("/path", SteamCmdHostOs.Windows);
        Assert.DoesNotContain("+@sSteamCmdForcePlatformType", args);
    }

    [Fact]
    public void ForcePlatformWindows_arg_present_on_mac_and_linux()
    {
        var macArgs = SteamCmdService.BuildInstallArgs("/path", SteamCmdHostOs.MacOS);
        var linuxArgs = SteamCmdService.BuildInstallArgs("/path", SteamCmdHostOs.Linux);
        Assert.Contains("+@sSteamCmdForcePlatformType", macArgs);
        Assert.Contains("windows", macArgs);
        Assert.Contains("+@sSteamCmdForcePlatformType", linuxArgs);
        Assert.Contains("windows", linuxArgs);
    }
}
```

- [ ] **Step 2: Run test, verify failure**

```bash
dotnet test ArkManager.slnx --filter "FullyQualifiedName~SteamCmdBootstrapTests"
```

Expected: FAIL with compile error (`SteamCmdHostOs`, `SelectBootstrapUrl`, `BuildInstallArgs` don't exist).

- [ ] **Step 3: Add the helper + enum to `SteamCmdService.cs`**

At the top of `SteamCmdService.cs` after the `InstalledServerVersion` record, add:

```csharp
public enum SteamCmdHostOs { MacOS, Linux, Windows }
```

Inside the `SteamCmdService` class, replace the two `SteamCmd*Url` constants block with:

```csharp
private const string SteamCmdMacUrl = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd_osx.tar.gz";
private const string SteamCmdLinuxUrl = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd_linux.tar.gz";
private const string SteamCmdWindowsUrl = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip";

internal static SteamCmdHostOs DetectHostOs()
    => OperatingSystem.IsWindows() ? SteamCmdHostOs.Windows
     : OperatingSystem.IsMacOS()   ? SteamCmdHostOs.MacOS
     :                               SteamCmdHostOs.Linux;

public static string SelectBootstrapUrl(SteamCmdHostOs os) => os switch
{
    SteamCmdHostOs.MacOS   => SteamCmdMacUrl,
    SteamCmdHostOs.Linux   => SteamCmdLinuxUrl,
    SteamCmdHostOs.Windows => SteamCmdWindowsUrl,
    _ => throw new ArgumentOutOfRangeException(nameof(os)),
};

public static IReadOnlyList<string> BuildInstallArgs(string installDir, SteamCmdHostOs os)
{
    var args = new List<string>();
    // На mac/linux заставляем steamcmd качать Windows-сборку (нативного билда ASA нет).
    // На Windows-хосте этот флаг не нужен и не применяется.
    if (os != SteamCmdHostOs.Windows)
    {
        args.Add("+@sSteamCmdForcePlatformType");
        args.Add("windows");
    }
    args.AddRange(new[]
    {
        "+force_install_dir", installDir,
        "+login", "anonymous",
        "+app_info_update", "1",
        "+app_update", AsaDedicatedServerAppId.ToString(), "validate",
        "+quit",
    });
    return args;
}
```

- [ ] **Step 4: Update `InstallSteamCmdAsync` to use `SelectBootstrapUrl` and handle Windows zip**

Replace the URL-pick line and the extraction block:

```csharp
public async Task InstallSteamCmdAsync(Action<string> onLog, CancellationToken ct = default)
{
    var os = DetectHostOs();
    var url = SelectBootstrapUrl(os);
    onLog("Downloading steamcmd...");
    var ext = os == SteamCmdHostOs.Windows ? ".zip" : ".tar.gz";
    var archive = Path.Combine(_paths.SteamCmdDir, "steamcmd" + ext);

    using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
    await using (var resp = await http.GetStreamAsync(url, ct))
    await using (var fs = File.Create(archive))
    {
        await resp.CopyToAsync(fs, ct);
    }
    onLog("Downloaded. Extracting...");

    if (os == SteamCmdHostOs.Windows)
    {
        ZipFile.ExtractToDirectory(archive, _paths.SteamCmdDir, overwriteFiles: true);
    }
    else
    {
        await using var fs = File.OpenRead(archive);
        await using var gz = new GZipStream(fs, CompressionMode.Decompress);
        await TarFile.ExtractToDirectoryAsync(gz, _paths.SteamCmdDir, overwriteFiles: true, cancellationToken: ct);
    }

    // chmod +x только на Unix — на Windows execute-бита нет.
    if (os != SteamCmdHostOs.Windows)
    {
        var sh = Path.Combine(_paths.SteamCmdDir, "steamcmd.sh");
        if (File.Exists(sh))
        {
            await ProcessRunner.RunCaptureAsync("/bin/chmod", new[] { "+x", sh }, ct: ct);
            foreach (var f in Directory.EnumerateFiles(_paths.SteamCmdDir, "steamcmd", SearchOption.AllDirectories))
                await ProcessRunner.RunCaptureAsync("/bin/chmod", new[] { "+x", f }, ct: ct);
        }
    }

    try { File.Delete(archive); } catch { /* ignore */ }
    var binary = ResolveSteamCmdBinary();
    onLog("steamcmd ready: " + binary);
}
```

- [ ] **Step 5: Update `ResolveSteamCmdBinary` for Windows**

Replace the body:

```csharp
public string ResolveSteamCmdBinary()
{
    if (!string.IsNullOrWhiteSpace(_settings.Current.SteamCmdPath) && File.Exists(_settings.Current.SteamCmdPath))
        return _settings.Current.SteamCmdPath;

    var bundledName = OperatingSystem.IsWindows() ? "steamcmd.exe" : "steamcmd.sh";
    var bundled = Path.Combine(_paths.SteamCmdDir, bundledName);
    if (File.Exists(bundled)) return bundled;

    var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
    foreach (var dir in pathVar.Split(Path.PathSeparator))
    {
        var c = Path.Combine(dir, bundledName);
        if (File.Exists(c)) return c;
        if (!OperatingSystem.IsWindows())
        {
            var bare = Path.Combine(dir, "steamcmd");
            if (File.Exists(bare)) return bare;
        }
    }
    return bundled;
}
```

- [ ] **Step 6: Update `InstallOrUpdateServerAsync` to use `BuildInstallArgs`**

Replace its `args` initialization with:

```csharp
var args = BuildInstallArgs(installDir, DetectHostOs());
```

(Remove the inline list.)

- [ ] **Step 7: Update `QueryLatestBuildIdAsync` to drop the force-platform flag on Windows**

Wrap the existing `args` initialization so it omits `+@sSteamCmdForcePlatformType windows` on Windows:

```csharp
var os = DetectHostOs();
var args = new List<string>();
if (os != SteamCmdHostOs.Windows)
{
    args.Add("+@sSteamCmdForcePlatformType");
    args.Add("windows");
}
args.AddRange(new[]
{
    "+login", "anonymous",
    "+app_info_update", "1",
    "+app_info_print", AsaDedicatedServerAppId.ToString(),
    "+quit",
});
```

- [ ] **Step 8: Run tests, verify pass**

```bash
dotnet test ArkManager.slnx
```

Expected: PASS, including the new `SteamCmdBootstrapTests`.

- [ ] **Step 9: Commit**

```bash
git add src/ArkManager.Core/Services/Steam/SteamCmdService.cs \
        tests/ArkManager.Core.Tests/SteamCmdBootstrapTests.cs
git commit -m "SteamCmdService: cross-platform bootstrap (Mac/Linux/Windows)"
```

---

## Task 5: `AppPaths` — use `LocalApplicationData` on Windows

ApplicationData maps to `%APPDATA%` (Roaming). ASA server is ~25GB — that belongs in Local.

**Files:**
- Modify: `src/ArkManager.Core/Services/AppPaths.cs:53`

- [ ] **Step 1: Edit `ResolveDataDir`**

In `AppPaths.cs` change the Windows branch:

```csharp
return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArkManager");
```

Update the comment at the top of the class:

```csharp
/// <summary>
/// Все пути приложения. По правилам пользователя: user-wide state живёт в одном vendor-каталоге.
/// macOS: ~/Library/Application Support/ArkManager. Linux: $XDG_DATA_HOME/ArkManager.
/// Windows: %LOCALAPPDATA%/ArkManager (а не Roaming — у нас 25GB ASA-сервера).
/// </summary>
```

- [ ] **Step 2: Verify build**

```bash
dotnet build ArkManager.slnx
dotnet test ArkManager.slnx
```

Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add src/ArkManager.Core/Services/AppPaths.cs
git commit -m "AppPaths: use LocalApplicationData on Windows (not Roaming)"
```

---

## Task 6: Rename `WineLauncher` → `BundledWineLauncher`

Keeps Phase 1 behavior (system-wine fallback) — switching to embedded-only happens in Task 18.

**Files:**
- Rename: `src/ArkManager.Core/Services/Launchers/WineLauncher.cs` → `BundledWineLauncher.cs`
- Modify: contents of that file (rename type, update error message)
- Modify: `src/ArkManager.Core/Services/Launchers/ServerCommandLine.cs:38`

- [ ] **Step 1: Rename file via git**

```bash
git mv src/ArkManager.Core/Services/Launchers/WineLauncher.cs \
       src/ArkManager.Core/Services/Launchers/BundledWineLauncher.cs
```

- [ ] **Step 2: Rename type inside the file**

Open `src/ArkManager.Core/Services/Launchers/BundledWineLauncher.cs` and replace **both** occurrences of `WineLauncher` (class declaration + constructor) with `BundledWineLauncher`. Update the XML-doc summary above the class:

```csharp
/// <summary>
/// Запускает ArkAscendedServer.exe через wine64. В Phase 1 резолвит wine из системы;
/// в Phase 2 будет искать только встроенный в бандл бинарь. WINEPREFIX живёт в DataDir.
/// </summary>
public sealed class BundledWineLauncher : IServerLauncher
{
    private readonly AppPaths _paths;

    public BundledWineLauncher(AppPaths paths)
    {
        _paths = paths;
    }
    ...
}
```

Also replace the user-facing error string mentioning Doctor:

```csharp
var wine = FindWineBinary()
           ?? throw new InvalidOperationException("Server runtime missing — reinstall ArkManager.");
```

- [ ] **Step 3: Update `ServerCommandLine.cs` comment**

In `src/ArkManager.Core/Services/Launchers/ServerCommandLine.cs:38`, change:

```csharp
// Сервер запускается headless (winemac.drv отключён в WineLauncher → окна нет).
```

to:

```csharp
// Сервер запускается headless (winemac.drv отключён в BundledWineLauncher → окна нет).
```

- [ ] **Step 4: Update DI registration (temporarily, full launcher selection lands in Task 8)**

In `src/ArkManager.Desktop/AppServices.cs:32`, change:

```csharp
sc.AddSingleton<IServerLauncher, WineLauncher>();
```

to:

```csharp
sc.AddSingleton<IServerLauncher, BundledWineLauncher>();
```

- [ ] **Step 5: Verify build + tests**

```bash
dotnet build ArkManager.slnx
dotnet test ArkManager.slnx
```

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ArkManager.Core/Services/Launchers/ \
        src/ArkManager.Desktop/AppServices.cs
git commit -m "Rename WineLauncher to BundledWineLauncher"
```

---

## Task 7: Create `NativeWindowsLauncher`

**Files:**
- Create: `src/ArkManager.Core/Services/Launchers/NativeWindowsLauncher.cs`

- [ ] **Step 1: Write the launcher**

```csharp
using System.Diagnostics;
using ArkManager.Core.Models;
using ArkManager.Core.Util;

namespace ArkManager.Core.Services.Launchers;

/// <summary>
/// Запуск ArkAscendedServer.exe нативно на Windows — без wine, без WINEPREFIX.
/// Используется DI только когда OperatingSystem.IsWindows().
/// </summary>
public sealed class NativeWindowsLauncher : IServerLauncher
{
    public async Task<RunningServer> StartAsync(
        AppSettings settings,
        IReadOnlyList<string> modIds,
        Action<string> onOutput,
        Action<int> onExit,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(settings.ServerInstallPath))
            throw new InvalidOperationException("Server install path is not set.");

        var exe = Path.Combine(
            settings.ServerInstallPath, "ShooterGame", "Binaries", "Win64", "ArkAscendedServer.exe");
        if (!File.Exists(exe))
            throw new InvalidOperationException(
                "ArkAscendedServer.exe not found. Install the server on the Install tab.");

        var args = new List<string>();
        args.AddRange(ServerCommandLine.Build(settings, modIds));

        var workDir = Path.Combine(settings.ServerInstallPath, "ShooterGame", "Binaries", "Win64");

        var tcs = new TaskCompletionSource<RunningServer>();
        _ = Task.Run(async () =>
        {
            try
            {
                var exit = await ProcessRunner.RunStreamingAsync(
                    exe, args,
                    line => onOutput(line),
                    line => onOutput(line),
                    workingDir: workDir,
                    onStarted: p => tcs.TrySetResult(new RunningServer(p.Id, DateTime.UtcNow)),
                    ct: ct);
                onExit(exit);
            }
            catch (Exception ex)
            {
                onOutput("[launcher error] " + ex.Message);
                onExit(-1);
                tcs.TrySetException(ex);
            }
        }, CancellationToken.None);

        return await tcs.Task;
    }

    public Task StopAsync(int pid, CancellationToken ct = default)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            if (!p.HasExited) p.Kill(entireProcessTree: true);
        }
        catch (ArgumentException) { /* уже мёртв */ }
        return Task.CompletedTask;
    }

    public Task<bool> IsRunningAsync(int pid, CancellationToken ct = default)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return Task.FromResult(!p.HasExited);
        }
        catch { return Task.FromResult(false); }
    }
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build ArkManager.slnx
dotnet test ArkManager.slnx
```

Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add src/ArkManager.Core/Services/Launchers/NativeWindowsLauncher.cs
git commit -m "Add NativeWindowsLauncher (Windows server launch without wine)"
```

---

## Task 8: DI — pick launcher by host OS

**Files:**
- Modify: `src/ArkManager.Desktop/AppServices.cs:32`

- [ ] **Step 1: Replace the registration**

In `src/ArkManager.Desktop/AppServices.cs`, change:

```csharp
sc.AddSingleton<IServerLauncher, BundledWineLauncher>();
```

to:

```csharp
if (OperatingSystem.IsWindows())
    sc.AddSingleton<IServerLauncher, NativeWindowsLauncher>();
else
    sc.AddSingleton<IServerLauncher, BundledWineLauncher>();
```

- [ ] **Step 2: Verify build + tests**

```bash
dotnet build ArkManager.slnx
dotnet test ArkManager.slnx
```

Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add src/ArkManager.Desktop/AppServices.cs
git commit -m "DI: select launcher implementation by host OS"
```

---

## Task 9: Remove the Doctor tab (full delete)

**Files:**
- Delete: `src/ArkManager.Core/Services/Doctor/DoctorService.cs`
- Delete: `src/ArkManager.Core/Services/Doctor/` directory (if empty afterwards)
- Delete: `src/ArkManager.Desktop/ViewModels/DoctorViewModel.cs`
- Delete: `src/ArkManager.Desktop/Views/DoctorView.axaml`
- Delete: `src/ArkManager.Desktop/Views/DoctorView.axaml.cs`
- Modify: `src/ArkManager.Desktop/AppServices.cs` — drop `DoctorService` + `DoctorViewModel` registrations
- Modify: `src/ArkManager.Desktop/ViewModels/MainWindowViewModel.cs` — drop ctor param, nav entry, designer-ctor arg
- Modify: `src/ArkManager.Desktop/Themes/Icons.axaml` — drop `IconDoctor`

- [ ] **Step 1: Delete files**

```bash
git rm src/ArkManager.Core/Services/Doctor/DoctorService.cs
git rm src/ArkManager.Desktop/ViewModels/DoctorViewModel.cs
git rm src/ArkManager.Desktop/Views/DoctorView.axaml
git rm src/ArkManager.Desktop/Views/DoctorView.axaml.cs
# remove directory if empty:
rmdir src/ArkManager.Core/Services/Doctor 2>/dev/null || true
```

- [ ] **Step 2: Clean `AppServices.cs`**

Remove these two lines (around lines 35 and 44):

```csharp
sc.AddSingleton<DoctorService>();
sc.AddTransient<DoctorViewModel>();
```

Remove the now-unused `using ArkManager.Core.Services.Doctor;` at the top (line 5).

- [ ] **Step 3: Clean `MainWindowViewModel.cs`**

Remove `DoctorViewModel doctor` from the constructor parameter list, the `new("Doctor", ...)` entry from `NavItems`, and `new DoctorViewModel()` from the designer-only parameterless constructor. After the edit, the relevant portions look like:

```csharp
public MainWindowViewModel(
    InstallViewModel install,
    ConfigViewModel config,
    ModsViewModel mods,
    BackupsViewModel backups,
    ServerViewModel server,
    RconViewModel rcon)
{
    NavItems = new ObservableCollection<NavItem>
    {
        new("Server",   G("M7 5 L19 12 L7 19 Z"), server),
        new("RCON",     G("M3 5 H21 V19 H3 Z M6 9 L10 12 L6 15 V13 L8 12 L6 11 Z M12 14 H17 V16 H12 Z"), rcon),
        new("Install",  G("M11 4 H13 V11 H16 L12 16 L8 11 H11 Z M5 18 H19 V20 H5 Z"), install),
        new("Config",   G("M3 6 H21 V8 H3 Z M3 11 H21 V13 H3 Z M3 16 H15 V18 H3 Z"), config),
        new("Mods",     G("M12 3 L20 7 V17 L12 21 L4 17 V7 Z M12 8 L16 10 V14 L12 16 L8 14 V10 Z"), mods),
        new("Backups",  G("M4 4 H20 V8 H4 Z M5 9 H19 V20 H5 Z M9 12 H15 V14 H9 Z"), backups),
    };
    _selected = NavItems[0];
    ...
}

public MainWindowViewModel() : this(
    new InstallViewModel(),
    new ConfigViewModel(),
    new ModsViewModel(),
    new BackupsViewModel(),
    new ServerViewModel(),
    new RconViewModel())
{
}
```

- [ ] **Step 4: Clean `Themes/Icons.axaml`**

Open `src/ArkManager.Desktop/Themes/Icons.axaml`, find the line:

```xml
<StreamGeometry x:Key="IconDoctor">M10 3 H14 V9 H20 V13 H14 V21 H10 V13 H4 V9 H10 Z</StreamGeometry>
```

Delete it.

- [ ] **Step 5: Verify build + tests**

```bash
dotnet build ArkManager.slnx
dotnet test ArkManager.slnx
```

Expected: PASS. If any other file still references Doctor (e.g., ViewLocator), fix it now (`grep -rn "Doctor" src` to confirm clean).

- [ ] **Step 6: Commit**

```bash
git add -A src/ArkManager.Core/Services/ \
       src/ArkManager.Desktop/AppServices.cs \
       src/ArkManager.Desktop/ViewModels/MainWindowViewModel.cs \
       src/ArkManager.Desktop/Themes/Icons.axaml
git commit -m "Drop the Doctor tab and the diagnostics it owned"
```

---

## Task 10: Cross-OS `build.sh` (publish only — no wine bundling yet)

`build.sh` runs on Mac (or Linux). It accepts `--target` with one or more of `macos|linux|windows|all`. For each target it does `dotnet publish` with self-contained, then packages the output:

- **macOS** → `.app` bundle inside a `.zip`.
- **Windows** → folder zipped flat (`ArkManager.exe` + dlls).
- **Linux** → folder tarballed with gzip.

The packaging follows what `build-app.sh` currently does for Mac, generalized.

**Files:**
- Create: `build.sh`

- [ ] **Step 1: Write `build.sh`**

```bash
#!/usr/bin/env bash
set -euo pipefail

# Cross-OS build script. Runs on macOS or Linux hosts.
#   ./build.sh                        # all 3 targets
#   ./build.sh --target macos linux   # subset
#
# Phase 1: builds self-contained .NET bundles. Wine is NOT bundled yet —
# Mac/Linux outputs still require system wine. Phase 2 (Task 17/18) adds
# wine into the package.

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$ROOT/src/ArkManager.Desktop/ArkManager.App.csproj"
CONFIG="Release"
APP_NAME="ArkManager"
BUNDLE_ID="com.arkmanager.app"

# Read <Version> from Directory.Build.props.
VERSION=$(awk -F '[<>]' '/<Version>/{print $3; exit}' "$ROOT/Directory.Build.props")
[[ -z "$VERSION" ]] && { echo "Failed to read Version from Directory.Build.props"; exit 1; }

DIST="$ROOT/dist"
mkdir -p "$DIST"

# --- parse args --------------------------------------------------------------
TARGETS=()
if [[ $# -eq 0 ]]; then
  TARGETS=(macos linux windows)
else
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --target) shift; while [[ $# -gt 0 && "$1" != --* ]]; do TARGETS+=("$1"); shift; done ;;
      all) TARGETS=(macos linux windows); shift ;;
      *) echo "Unknown arg: $1"; exit 1 ;;
    esac
  done
fi

rid_of() {
  case "$1" in
    macos)   echo "osx-arm64" ;;
    linux)   echo "linux-x64" ;;
    windows) echo "win-x64" ;;
    *) echo "Unknown target: $1"; exit 1 ;;
  esac
}

publish_for() {
  local target="$1" rid; rid=$(rid_of "$target")
  echo "==> dotnet publish ($CONFIG / $rid / self-contained)"
  dotnet publish "$PROJECT" -c "$CONFIG" -r "$rid" \
    --self-contained true \
    /p:PublishSingleFile=false \
    /p:PublishTrimmed=false
  echo "$ROOT/src/ArkManager.Desktop/bin.noindex/$CONFIG/net10.0/$rid/publish"
}

# --- package: macOS ----------------------------------------------------------
package_macos() {
  local publish="$1"
  local app="$DIST/$APP_NAME-$VERSION-macos-arm64/$APP_NAME.app"
  rm -rf "$DIST/$APP_NAME-$VERSION-macos-arm64"
  mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
  cp -R "$publish/." "$app/Contents/MacOS/"

  # Icon (best-effort).
  local ICON_SRC="$ROOT/src/ArkManager.Desktop/Assets/AppIcon.png"
  local ICON_NAME=""
  if [[ -f "$ICON_SRC" ]]; then
    local WORK; WORK="$(mktemp -d)"
    local ICONSET="$WORK/AppIcon.iconset"; mkdir -p "$ICONSET"
    if sips -s format png "$ICON_SRC" --out "$WORK/icon.png" >/dev/null 2>&1; then
      gen() { sips -z "$1" "$1" "$WORK/icon.png" --out "$ICONSET/$2" >/dev/null 2>&1 || true; }
      gen 16   icon_16x16.png;     gen 32   icon_16x16@2x.png
      gen 32   icon_32x32.png;     gen 64   icon_32x32@2x.png
      gen 128  icon_128x128.png;   gen 256  icon_128x128@2x.png
      gen 256  icon_256x256.png;   gen 512  icon_256x256@2x.png
      gen 512  icon_512x512.png;   gen 1024 icon_512x512@2x.png
      iconutil -c icns "$ICONSET" -o "$app/Contents/Resources/AppIcon.icns" 2>/dev/null && ICON_NAME="AppIcon" || true
    fi
  fi
  local ICON_KEY=""
  [[ -n "$ICON_NAME" ]] && ICON_KEY="	<key>CFBundleIconFile</key>
	<string>$ICON_NAME</string>"

  cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>CFBundleName</key><string>$APP_NAME</string>
	<key>CFBundleDisplayName</key><string>$APP_NAME</string>
	<key>CFBundleIdentifier</key><string>$BUNDLE_ID</string>
	<key>CFBundleVersion</key><string>$VERSION</string>
	<key>CFBundleShortVersionString</key><string>$VERSION</string>
	<key>CFBundleExecutable</key><string>$APP_NAME</string>
	<key>CFBundlePackageType</key><string>APPL</string>
	<key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
	<key>NSHighResolutionCapable</key><true/>
	<key>LSMinimumSystemVersion</key><string>11.0</string>
$ICON_KEY
</dict>
</plist>
PLIST

  # Ad-hoc sign so Gatekeeper accepts on Apple Silicon.
  codesign --force --deep --sign - "$app" 2>/dev/null || echo "    codesign skipped"

  ( cd "$DIST/$APP_NAME-$VERSION-macos-arm64" && zip -qr "../$APP_NAME-$VERSION-macos-arm64.zip" "$APP_NAME.app" )
  echo "    -> $DIST/$APP_NAME-$VERSION-macos-arm64.zip"
}

# --- package: Windows --------------------------------------------------------
package_windows() {
  local publish="$1"
  local out="$DIST/$APP_NAME-$VERSION-windows-x64"
  rm -rf "$out"; mkdir -p "$out"
  cp -R "$publish/." "$out/"
  ( cd "$DIST" && zip -qr "$APP_NAME-$VERSION-windows-x64.zip" "$APP_NAME-$VERSION-windows-x64" )
  echo "    -> $DIST/$APP_NAME-$VERSION-windows-x64.zip"
}

# --- package: Linux ----------------------------------------------------------
package_linux() {
  local publish="$1"
  local out="$DIST/$APP_NAME-$VERSION-linux-x64"
  rm -rf "$out"; mkdir -p "$out"
  cp -R "$publish/." "$out/"
  # Ensure the apphost has +x (publish output usually already has it on Unix).
  chmod +x "$out/$APP_NAME" 2>/dev/null || true
  ( cd "$DIST" && tar -czf "$APP_NAME-$VERSION-linux-x64.tar.gz" "$APP_NAME-$VERSION-linux-x64" )
  echo "    -> $DIST/$APP_NAME-$VERSION-linux-x64.tar.gz"
}

# --- run ---------------------------------------------------------------------
for t in "${TARGETS[@]}"; do
  echo ""
  echo "### Target: $t ###"
  publish_path=$(publish_for "$t")
  case "$t" in
    macos)   package_macos   "$publish_path" ;;
    linux)   package_linux   "$publish_path" ;;
    windows) package_windows "$publish_path" ;;
  esac
done

echo ""
echo "Done. Artifacts in $DIST"
```

- [ ] **Step 2: Make executable**

```bash
chmod +x build.sh
```

- [ ] **Step 3: Smoke-test on the host machine (macOS)**

```bash
./build.sh --target macos
open "dist/ArkManager-1.0.0-macos-arm64/ArkManager.app" || true
```

Expected: bundle builds; `.app` contains `Contents/MacOS/ArkManager` (the renamed apphost), `.zip` exists. Launching `.app` shows the GUI (close the window after verifying).

Then build the other two RIDs (cross-RID publish works from Mac, but the artifacts can't be launched here):

```bash
./build.sh --target linux windows
unzip -l "dist/ArkManager-1.0.0-windows-x64.zip" | head -20
tar -tzf "dist/ArkManager-1.0.0-linux-x64.tar.gz" | head -20
```

Expected: zip contains `ArkManager-1.0.0-windows-x64/ArkManager.exe`; tarball contains `ArkManager-1.0.0-linux-x64/ArkManager` apphost.

- [ ] **Step 4: Commit**

```bash
git add build.sh
git commit -m "Add cross-OS build.sh (Mac/Linux/Windows publish, no wine bundling yet)"
```

---

## Task 11: Update `Makefile` to wrap `build.sh`

**Files:**
- Modify: `Makefile`

- [ ] **Step 1: Replace Makefile content**

```makefile
.PHONY: build mac linux windows run clean

build:
	@./build.sh

mac:
	@./build.sh --target macos

linux:
	@./build.sh --target linux

windows:
	@./build.sh --target windows

# Запустить собранный .app из dist/
run:
	@open "dist/$(shell awk -F '[<>]' '/<Version>/{print $$3; exit}' Directory.Build.props | xargs -I{} echo "ArkManager-{}-macos-arm64")/ArkManager.app"

clean:
	@rm -rf dist
	@echo "dist/ удалён"
```

- [ ] **Step 2: Smoke-test**

```bash
make mac
make clean
```

Expected: same artifact as `./build.sh --target macos`; `make clean` removes `dist/`.

- [ ] **Step 3: Commit**

```bash
git add Makefile
git commit -m "Makefile: wrap build.sh, add per-target shortcuts"
```

---

## Task 12: Delete `build-app.sh`

**Files:**
- Delete: `build-app.sh`

- [ ] **Step 1: Remove the file**

```bash
git rm build-app.sh
```

- [ ] **Step 2: Verify build pipeline still works**

```bash
./build.sh --target macos
```

Expected: succeeds.

- [ ] **Step 3: Commit**

```bash
git commit -m "Drop legacy build-app.sh (replaced by build.sh)"
```

---

## Task 13: GitHub Actions release workflow

**Files:**
- Create: `.github/workflows/release.yml`

- [ ] **Step 1: Write the workflow**

```yaml
name: Release

on:
  push:
    tags:
      - 'v*.*.*'

permissions:
  contents: write

concurrency:
  group: release-${{ github.ref }}
  cancel-in-progress: false

jobs:
  build:
    strategy:
      fail-fast: false
      matrix:
        include:
          - os: macos-latest
            target: macos
            artifact_glob: dist/ArkManager-*-macos-arm64.zip
          - os: ubuntu-latest
            target: linux
            artifact_glob: dist/ArkManager-*-linux-x64.tar.gz
          - os: windows-latest
            target: windows
            artifact_glob: dist/ArkManager-*-windows-x64.zip
    runs-on: ${{ matrix.os }}
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.x'

      # build.sh is bash; on Windows the runner has Git Bash on PATH as `bash.exe`.
      - name: Build
        shell: bash
        run: ./build.sh --target ${{ matrix.target }}

      - uses: actions/upload-artifact@v4
        with:
          name: ${{ matrix.target }}
          path: ${{ matrix.artifact_glob }}
          if-no-files-found: error

  release:
    needs: build
    runs-on: ubuntu-latest
    steps:
      - uses: actions/download-artifact@v4
        with:
          path: dist
          merge-multiple: true

      - uses: softprops/action-gh-release@v2
        with:
          draft: true
          files: dist/*
          generate_release_notes: true
```

- [ ] **Step 2: Sanity-check YAML**

```bash
python3 -c "import yaml,sys; yaml.safe_load(open('.github/workflows/release.yml')); print('ok')" || \
  ruby -e "require 'yaml'; YAML.load_file('.github/workflows/release.yml'); puts 'ok'"
```

Expected: `ok` (one or the other will be installed on macOS by default).

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/release.yml
git commit -m "CI: add release workflow (matrix Mac/Linux/Windows on tag push)"
```

---

## Task 14: `.gitignore` — add Phase-2 cache directory

`dist/` is already ignored. Add the wine-cache directory we'll use in Phase 2 so the working tree stays clean even if someone overrides the default cache location into the repo.

**Files:**
- Modify: `.gitignore`

- [ ] **Step 1: Append**

Append to `.gitignore`:

```
## Build caches
build/.cache/
```

- [ ] **Step 2: Commit**

```bash
git add .gitignore
git commit -m ".gitignore: add build/.cache/ for wine tarball cache"
```

---

# Phase 2 — Embedded wine

## Task 15: Pin wine sources (`build/wine-sources.json`)

Values verified at plan-write time (2026-05-28). If the implementor wants newer wine, they replace the URL + SHA256 in one place. Note: gcenx after 11.x stopped publishing `wine-stable` (only `wine-devel`/`wine-staging`); for headless ASA wine-stable is the safer pick.

**Files:**
- Create: `build/wine-sources.json`

- [ ] **Step 1: Write `build/wine-sources.json`**

```json
{
  "macos-arm64": {
    "url": "https://github.com/Gcenx/macOS_Wine_builds/releases/download/11.0_1/wine-stable-11.0_1-osx64.tar.xz",
    "sha256": "b50dc50ec7f41d58b115a6b685d4d1315ba3c797bd3aa0f49213f2703cb82388",
    "extractedWineDir": "Wine Stable.app/Contents/Resources/wine"
  },
  "linux-x64": {
    "url": "https://github.com/lutris/wine/releases/download/lutris-wine-7.2-2/wine-lutris-7.2-2-x86_64.tar.xz",
    "sha256": "3a1428358f52c055f7b8f4368291746e9fd9d1db85ae63d5145157f9ed1a8a12",
    "extractedWineDir": "lutris-7.2-2-x86_64"
  }
}
```

Notes for whoever reads this later:
- `extractedWineDir` for Linux is `lutris-7.2-2-x86_64` (no `wine-` prefix), even though the filename starts with `wine-lutris-`. Verified by inspecting the tarball.
- gcenx 11.0_1 ships unified wow64 — only `bin/wine` exists, no `bin/wine64`. Lutris-wine 7.2 (older Wine 7.2) ships both. The launcher resolution in Task 17 tries `wine64` first, then `wine`.

- [ ] **Step 2: Commit**

```bash
git add build/wine-sources.json
git commit -m "Pin wine sources for Mac/Linux (gcenx wine-stable 11.0_1 + lutris-wine 7.2-2)"
```

---

## Task 16: Extend `build.sh` to download + embed wine

**Files:**
- Modify: `build.sh`

- [ ] **Step 1: Add wine-download helper near the top of `build.sh`**

After the variable declarations (before `# --- parse args ---`), insert:

```bash
WINE_SOURCES="$ROOT/build/wine-sources.json"
WINE_CACHE="${ARKMANAGER_WINE_CACHE:-$HOME/.cache/ark-manager/wine}"
mkdir -p "$WINE_CACHE"

# json_field <key1> <key2>  e.g. json_field macos-arm64 url
json_field() {
  python3 -c "import json,sys; print(json.load(open('$WINE_SOURCES'))['$1']['$2'])"
}

ensure_wine() {
  # ensure_wine <macos-arm64|linux-x64> → echoes absolute path to the extracted wine root.
  local key="$1"
  local url sha extracted_dir
  url=$(json_field "$key" url)
  sha=$(json_field "$key" sha256)
  extracted_dir=$(json_field "$key" extractedWineDir)

  local cache_dir="$WINE_CACHE/${sha:0:12}"
  local root="$cache_dir/$extracted_dir"
  if [[ -d "$root" && -x "$root/bin/wine64" ]]; then
    echo "$root"
    return
  fi

  mkdir -p "$cache_dir"
  local archive="$cache_dir/wine.tar"
  echo "==> downloading wine for $key" >&2
  curl -L --fail --silent --show-error -o "$archive" "$url"

  local actual_sha
  actual_sha=$(shasum -a 256 "$archive" | awk '{print $1}')
  if [[ "$actual_sha" != "$sha" ]]; then
    echo "wine $key sha256 mismatch: expected $sha, got $actual_sha" >&2
    rm -f "$archive"
    exit 1
  fi

  echo "==> extracting wine for $key" >&2
  # Auto-detect tar compression by extension.
  case "$url" in
    *.tar.xz)  tar -xJf "$archive" -C "$cache_dir" ;;
    *.tar.gz)  tar -xzf "$archive" -C "$cache_dir" ;;
    *.tar.zst) tar --use-compress-program=zstd -xf "$archive" -C "$cache_dir" ;;
    *) echo "Unknown wine archive format: $url" >&2; exit 1 ;;
  esac
  rm -f "$archive"

  if [[ ! -x "$root/bin/wine64" ]]; then
    echo "wine $key extracted but $root/bin/wine64 missing" >&2
    exit 1
  fi
  echo "$root"
}
```

- [ ] **Step 2: Wire wine into the Mac packaging**

In `package_macos`, after the `cp -R "$publish/." "$app/Contents/MacOS/"` line, insert:

```bash
local wine_root; wine_root=$(ensure_wine macos-arm64)
mkdir -p "$app/Contents/Resources/wine"
cp -R "$wine_root/." "$app/Contents/Resources/wine/"
```

- [ ] **Step 3: Wire wine into the Linux packaging**

In `package_linux`, after `cp -R "$publish/." "$out/"`, insert:

```bash
local wine_root; wine_root=$(ensure_wine linux-x64)
mkdir -p "$out/wine"
cp -R "$wine_root/." "$out/wine/"
```

- [ ] **Step 4: Smoke-test**

```bash
./build.sh --target macos
ls "dist/ArkManager-1.0.0-macos-arm64/ArkManager.app/Contents/Resources/wine/bin/wine64"
```

Expected: file exists. Then run the .app — it should still launch (in Phase 2 the launcher resolves wine from this path; until Task 17 lands the launcher still uses the system fallback, so this is just a packaging check).

```bash
./build.sh --target linux
tar -tzf "dist/ArkManager-1.0.0-linux-x64.tar.gz" | grep "wine/bin/wine64$"
```

Expected: matches.

- [ ] **Step 5: Commit**

```bash
git add build.sh
git commit -m "build.sh: download + embed wine into Mac/Linux bundles"
```

---

## Task 17: `BundledWineLauncher` — embedded-only resolution + new prefix path

Switch from system-wine fallback to embedded-only. Rename the WINEPREFIX path. Add legacy cleanup.

**Files:**
- Modify: `src/ArkManager.Core/Services/AppPaths.cs`
- Modify: `src/ArkManager.Core/Services/Launchers/BundledWineLauncher.cs`

- [ ] **Step 1: Rename `DefaultWinePrefixDir` → `ServerRuntimeDir` in `AppPaths.cs`**

In `src/ArkManager.Core/Services/AppPaths.cs`:

```csharp
public string ServerRuntimeDir { get; }
```

and

```csharp
ServerRuntimeDir = Path.Combine(DataDir, "server-runtime");
```

Also add a one-shot legacy cleanup. At the end of the constructor (after the `CreateDirectory` calls):

```csharp
// Legacy cleanup: предыдущие версии держали wineprefix тут.
// Embedded wine — это «server runtime»; имени wine в UI больше нет.
var legacy = Path.Combine(DataDir, "wineprefix");
if (Directory.Exists(legacy))
{
    try { Directory.Delete(legacy, recursive: true); } catch { /* ignore */ }
}
```

- [ ] **Step 2: Rewrite `BundledWineLauncher.cs`**

```csharp
using System.Diagnostics;
using ArkManager.Core.Models;
using ArkManager.Core.Util;

namespace ArkManager.Core.Services.Launchers;

/// <summary>
/// Запускает ArkAscendedServer.exe через wine64, встроенный в наш бандл.
/// macOS: <App>.app/Contents/Resources/wine/bin/wine64 (x86_64 Intel-бинарь, идёт через Rosetta 2).
/// Linux: <publish-dir>/wine/bin/wine64.
/// WINEPREFIX — <DataDir>/server-runtime (создаётся wine'ом при первом запуске).
/// </summary>
public sealed class BundledWineLauncher : IServerLauncher
{
    private readonly AppPaths _paths;

    public BundledWineLauncher(AppPaths paths)
    {
        _paths = paths;
    }

    internal static string ResolveEmbeddedWineBinary()
    {
        var baseDir = AppContext.BaseDirectory;
        string binDir;
        if (OperatingSystem.IsMacOS())
            // macOS apphost lives in *.app/Contents/MacOS; wine lives in *.app/Contents/Resources/wine.
            binDir = Path.GetFullPath(Path.Combine(baseDir, "..", "Resources", "wine", "bin"));
        else
            // Linux: wine sits next to the apphost in a `wine/` subdir.
            binDir = Path.Combine(baseDir, "wine", "bin");

        // Современный wine (10+) использует unified wow64 — `wine` запускает и 32-, и 64-битные exe.
        // Старые сборки (например, lutris-wine 7.2) разделяют `wine` (32-bit) и `wine64` (64-bit).
        // ASA — 64-битный, поэтому пробуем wine64 первым, потом fallback на wine.
        foreach (var name in new[] { "wine64", "wine" })
        {
            var candidate = Path.Combine(binDir, name);
            if (File.Exists(candidate)) return candidate;
        }
        return Path.Combine(binDir, "wine64");
    }

    public async Task<RunningServer> StartAsync(
        AppSettings settings,
        IReadOnlyList<string> modIds,
        Action<string> onOutput,
        Action<int> onExit,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(settings.ServerInstallPath))
            throw new InvalidOperationException("Server install path is not set.");

        var exe = Path.Combine(
            settings.ServerInstallPath, "ShooterGame", "Binaries", "Win64", "ArkAscendedServer.exe");
        if (!File.Exists(exe))
            throw new InvalidOperationException(
                "ArkAscendedServer.exe not found. Install the server on the Install tab.");

        var wine = ResolveEmbeddedWineBinary();
        if (!File.Exists(wine))
            throw new InvalidOperationException("Server runtime missing — reinstall ArkManager.");

        var prefix = _paths.ServerRuntimeDir;
        Directory.CreateDirectory(prefix);

        var args = new List<string> { exe };
        args.AddRange(ServerCommandLine.Build(settings, modIds));

        var env = new Dictionary<string, string>
        {
            ["WINEPREFIX"] = prefix,
            ["WINEDEBUG"] = "-all",
            // Отключаем wine-mac-driver: dedicated server headless, окно не нужно
            // (без этого wine рисует Server Console-окно с белым-на-белом текстом).
            ["WINEDLLOVERRIDES"] = "winemac.drv=",
        };

        var workDir = Path.Combine(settings.ServerInstallPath, "ShooterGame", "Binaries", "Win64");

        var tcs = new TaskCompletionSource<RunningServer>();
        _ = Task.Run(async () =>
        {
            try
            {
                var exit = await ProcessRunner.RunStreamingAsync(
                    wine, args,
                    line => onOutput(line),
                    line => onOutput(line),
                    workingDir: workDir,
                    env: env,
                    onStarted: p => tcs.TrySetResult(new RunningServer(p.Id, DateTime.UtcNow)),
                    ct: ct);
                onExit(exit);
            }
            catch (Exception ex)
            {
                onOutput("[launcher error] " + ex.Message);
                onExit(-1);
                tcs.TrySetException(ex);
            }
        }, CancellationToken.None);

        return await tcs.Task;
    }

    public Task StopAsync(int pid, CancellationToken ct = default)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            if (!p.HasExited) p.Kill(entireProcessTree: true);
        }
        catch (ArgumentException) { /* уже мёртв */ }
        return Task.CompletedTask;
    }

    public Task<bool> IsRunningAsync(int pid, CancellationToken ct = default)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return Task.FromResult(!p.HasExited);
        }
        catch { return Task.FromResult(false); }
    }
}
```

The old `EnumerateWineCandidates` / `FindWineBinary` helpers are gone — that's intentional. No system-wine fallback.

- [ ] **Step 3: Verify build + tests**

```bash
dotnet build ArkManager.slnx
dotnet test ArkManager.slnx
```

Expected: PASS.

- [ ] **Step 4: End-to-end smoke test from the bundle**

```bash
rm -rf dist
./build.sh --target macos
open "dist/ArkManager-1.0.0-macos-arm64/ArkManager.app"
```

Inside the app, go to Server tab → Start. Watch the log:
- Expected first run: ~30s of wine initializing the prefix in `~/Library/Application Support/ArkManager/server-runtime/`, then ASA server starts. Check Activity Monitor that the `ArkAscendedServer.exe` (Intel binary, under Rosetta) is running.
- The old `~/Library/Application Support/ArkManager/wineprefix/` directory (if it existed before this run) should be gone — legacy cleanup happened on app start.

If anything misbehaves, capture the relevant log lines and roll the launcher changes back before continuing.

- [ ] **Step 5: Commit**

```bash
git add src/ArkManager.Core/Services/AppPaths.cs \
        src/ArkManager.Core/Services/Launchers/BundledWineLauncher.cs
git commit -m "BundledWineLauncher: resolve wine from bundle only; rename prefix path"
```

---

## Task 18: CI — cache wine tarballs

**Files:**
- Modify: `.github/workflows/release.yml`

- [ ] **Step 1: Insert a cache step before the Build step**

Add this block to each `build` matrix step (after `setup-dotnet`, before `Build`):

```yaml
      - name: Cache wine tarballs
        if: matrix.target != 'windows'
        uses: actions/cache@v4
        with:
          path: ~/.cache/ark-manager/wine
          key: wine-${{ matrix.target }}-${{ hashFiles('build/wine-sources.json') }}
```

The `if: matrix.target != 'windows'` keeps the cache step Windows-noop (no wine for that target). The key includes the hash of `wine-sources.json` so a wine bump invalidates the cache automatically.

- [ ] **Step 2: Sanity-check YAML**

```bash
python3 -c "import yaml; yaml.safe_load(open('.github/workflows/release.yml')); print('ok')"
```

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/release.yml
git commit -m "CI: cache wine tarballs per target and wine-sources hash"
```

---

## Task 19: Update `CLAUDE.md`

Bring the project notes in line with reality after Phase 2.

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: Edit the file**

Make these changes (apply each as a focused edit; preserve surrounding text):

1. **Stack section** — leave as is.

2. **Layout section** — remove the line `Services/Doctor/DoctorService.cs` and the Doctor-related Views/ViewModels references.

3. **Табы UI** — replace the nav listing with:

   ```
   Server → RCON → Install → Config → Mods → Backups
   ```

   (drop the `→ Doctor` and the parenthetical about Doctor.)

4. **App-local state** — change `wineprefix/ (default WINEPREFIX)` to `server-runtime/ (WINEPREFIX, создаётся wine'ом при первом запуске)`.

5. **Подводные камни кода** — keep CommunityToolkit naming gotcha, ServerCommandLine gotcha, Avalonia 12 notes. Update the "Дизайн-система" section — no changes needed there.

6. **Wine setup section** — replace its entire content with:

   ```markdown
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
   ```

7. **Doctor → Install wine** — удалить весь блок (это уже неактуально).

8. **Что НЕ сделано** — добавить пункты: AppImage / .dmg / installers, code signing / notarization, headless CLI, ARM64 Linux, Intel Mac.

9. **Branch** — `main`, без remote, как было.

- [ ] **Step 2: Commit**

```bash
git add CLAUDE.md
git commit -m "CLAUDE.md: update for embedded wine, drop Doctor/brew sections"
```

---

## Final verification

After all 19 tasks land:

- [ ] **Step 1: Clean build of all three targets**

```bash
make clean
./build.sh
ls dist/
```

Expected: three archives:
```
ArkManager-1.0.0-macos-arm64.zip
ArkManager-1.0.0-linux-x64.tar.gz
ArkManager-1.0.0-windows-x64.zip
```

- [ ] **Step 2: Run the Mac bundle from `dist/`**

```bash
open "dist/ArkManager-1.0.0-macos-arm64/ArkManager.app"
```

In the GUI:
- Server tab → Start. Watch the log. Server should start under embedded wine, prefix created in `~/Library/Application Support/ArkManager/server-runtime/`.
- Stop. Verify no orphaned wineserver processes.
- Nav has 6 tabs (no Doctor): Server, RCON, Install, Config, Mods, Backups.

- [ ] **Step 3: Inspect the Windows artifact structure**

```bash
unzip -l dist/ArkManager-1.0.0-windows-x64.zip | head -30
```

Expected: contains `ArkManager.exe` at top level, no `wine/` directory.

- [ ] **Step 4: Inspect the Linux artifact structure**

```bash
tar -tzf dist/ArkManager-1.0.0-linux-x64.tar.gz | grep -E "(wine/bin/wine64|/ArkManager$)" | head
```

Expected: matches both the apphost and `wine/bin/wine64`.

- [ ] **Step 5: All tests pass**

```bash
dotnet test ArkManager.slnx
```

Expected: green.
