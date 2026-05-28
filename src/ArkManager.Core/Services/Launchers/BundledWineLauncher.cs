using System.Diagnostics;
using ArkManager.Core.Models;
using ArkManager.Core.Util;

namespace ArkManager.Core.Services.Launchers;

/// <summary>
/// Запускает ArkAscendedServer.exe через wine64, встроенный в наш бандл.
/// macOS: &lt;App&gt;.app/Contents/Resources/wine/bin/wine64 (x86_64 Intel-бинарь, идёт через Rosetta 2).
/// Linux: &lt;publish-dir&gt;/wine/bin/wine64.
/// WINEPREFIX — &lt;DataDir&gt;/server-runtime (создаётся wine'ом при первом запуске).
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
        // Dev escape hatch: при `dotnet run`/Rider AppContext.BaseDirectory указывает в
        // bin.noindex/Debug/..., где рядом нет wine. ARKMANAGER_WINE_PATH=<file> позволяет
        // ткнуть в кэш билд-скрипта (~/.cache/ark-manager/wine/<sha>/.../bin/wine) и работать
        // без пересборки бандла. В релизе env не задают, идём embedded-путём ниже.
        // Если env задан — возвращаем именно его (даже если файла нет), чтобы ошибка
        // показала тот самый путь и юзер сразу понял что не так.
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

        // Современный wine (10+) использует unified wow64 — `wine` запускает и 32-, и 64-битные exe.
        // Старые сборки (например, lutris-wine 7.2) разделяют `wine` (32-bit) и `wine64` (64-bit).
        // ASA — 64-битный, поэтому пробуем wine64 первым, потом fallback на wine.
        foreach (var name in new[] { "wine64", "wine" })
        {
            var candidate = Path.Combine(binDir, name);
            if (File.Exists(candidate)) return candidate;
        }

        // Dev fallback: бандл-путь пуст (`dotnet run` из репо), но build-скрипт скачивал
        // wine в `~/.cache/ark-manager/wine/` — берём оттуда. Прод-юзер этой папки не имеет,
        // поэтому код тихо сваливается в return ниже и в ошибку с подсказкой.
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

        // Структура: <cache>/<sha-prefix>/<extracted-dir>/.../bin/{wine64,wine}.
        // На маке extracted-dir — это .app-бандл, в нём bin лежит в Contents/Resources/wine.
        // На Linux extracted-dir — обычная папка с bin/ сразу внутри.
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
            // Подсказка для dev: видим ли мы ARKMANAGER_WINE_PATH или резолвили из бандла.
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
