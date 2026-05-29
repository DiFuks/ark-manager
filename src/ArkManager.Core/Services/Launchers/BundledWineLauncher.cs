using System.Diagnostics;
using ArkManager.Core.Models;
using ArkManager.Core.Services.Config;
using ArkManager.Core.Util;

namespace ArkManager.Core.Services.Launchers;

/// <summary>
/// Launches ArkAscendedServer.exe via wine64 embedded in our bundle.
/// macOS: &lt;App&gt;.app/Contents/Resources/wine/bin/wine64 (x86_64 Intel binary, runs via Rosetta 2).
/// Linux: &lt;publish-dir&gt;/wine/bin/wine64.
/// WINEPREFIX — &lt;DataDir&gt;/server-runtime (created by wine on first launch).
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
        // Dev escape hatch: under `dotnet run`/Rider AppContext.BaseDirectory points into
        // bin.noindex/Debug/..., which has no wine next to it. ARKMANAGER_WINE_PATH=<file>
        // lets you point at the build-script cache (~/.cache/ark-manager/wine/<sha>/.../bin/wine)
        // and work without rebuilding the bundle. In release env isn't set, we fall through
        // to the embedded path below. If env is set — return it as-is (even if the file is
        // missing) so the error message shows that exact path and the user immediately knows what's wrong.
        var envOverride = Environment.GetEnvironmentVariable("ARKMANAGER_WINE_PATH");
        if (!string.IsNullOrWhiteSpace(envOverride))
            return envOverride;

        var baseDir = AppContext.BaseDirectory;
        string binDir;
        if (OperatingSystem.IsMacOS())
            // macOS apphost lives in *.app/Contents/MacOS; wine lives in *.app/Contents/Resources/wine.
            binDir = Path.GetFullPath(Path.Combine(baseDir, "..", "Resources", "wine", "bin"));
        else
            // Linux: wine sits next to the apphost in a `wine/` subdir.
            binDir = Path.Combine(baseDir, "wine", "bin");

        // Modern wine (10+) uses unified wow64 — `wine` runs both 32- and 64-bit exes.
        // Older builds (e.g. lutris-wine 7.2) split `wine` (32-bit) and `wine64` (64-bit).
        // ASA is 64-bit, so we try wine64 first and fall back to wine.
        foreach (var name in new[] { "wine64", "wine" })
        {
            var candidate = Path.Combine(binDir, name);
            if (File.Exists(candidate)) return candidate;
        }

        // Dev fallback: the bundle path is empty (`dotnet run` from the repo), but the
        // build script downloaded wine into `~/.cache/ark-manager/wine/` — take it from there.
        // A prod user does not have this folder, so the code quietly falls through to the
        // return below and produces an error with a hint.
        var cached = TryFindCachedWine();
        if (cached != null) return cached;

        return Path.Combine(binDir, "wine64");
    }

    private static string? TryFindCachedWine()
    {
        var cache = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cache", "ark-manager", "wine");
        if (!Directory.Exists(cache)) return null;

        // Layout: <cache>/<sha-prefix>/<extracted-dir>/.../bin/{wine64,wine}.
        // On mac extracted-dir is a .app bundle, where bin lives at Contents/Resources/wine.
        // On Linux extracted-dir is a regular folder with bin/ directly inside.
        foreach (var shaDir in Directory.EnumerateDirectories(cache))
        {
            foreach (var topDir in Directory.EnumerateDirectories(shaDir))
            {
                var binDir = topDir.EndsWith(".app", StringComparison.OrdinalIgnoreCase)
                    ? Path.Combine(topDir, "Contents", "Resources", "wine", "bin")
                    : Path.Combine(topDir, "bin");
                foreach (var name in new[] { "wine64", "wine" })
                {
                    var candidate = Path.Combine(binDir, name);
                    if (File.Exists(candidate)) return candidate;
                }
            }
        }
        return null;
    }

    public async Task<RunningServer> StartAsync(
        AppSettings settings,
        ServerConfigSnapshot snapshot,
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
        {
            // Hint for dev: did we see ARKMANAGER_WINE_PATH or resolve from the bundle.
            var envOverride = Environment.GetEnvironmentVariable("ARKMANAGER_WINE_PATH");
            var source = string.IsNullOrWhiteSpace(envOverride)
                ? $"embedded path: {wine}"
                : $"ARKMANAGER_WINE_PATH: {wine}";
            throw new InvalidOperationException(
                $"Server runtime missing — reinstall ArkManager. ({source})");
        }

        var prefix = _paths.ServerRuntimeDir;
        Directory.CreateDirectory(prefix);

        var args = new List<string> { exe };
        args.AddRange(ServerCommandLine.Build(settings, snapshot, modIds));

        var env = new Dictionary<string, string>
        {
            ["WINEPREFIX"] = prefix,
            ["WINEDEBUG"] = "-all",
            // Disable wine-mac-driver: dedicated server is headless, no window needed
            // (without this wine draws a Server Console window with white-on-white text).
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
        catch (ArgumentException) { /* already dead */ }
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
