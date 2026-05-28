using System.Diagnostics;
using ArkManager.Core.Models;
using ArkManager.Core.Util;

namespace ArkManager.Core.Services.Launchers;

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

    /// <summary>Стандартные пути, в которых ищем wine64. Первый найденный — используется.</summary>
    public static IEnumerable<string> EnumerateWineCandidates()
    {
        // wine-stable / wine@staging / wine@devel ставятся как .app:
        yield return "/Applications/Wine Stable.app/Contents/Resources/wine/bin/wine64";
        yield return "/Applications/Wine Staging.app/Contents/Resources/wine/bin/wine64";
        yield return "/Applications/Wine Devel.app/Contents/Resources/wine/bin/wine64";
        // GPTK (gcenx) — fallback на случай ручной установки:
        yield return "/Applications/Game Porting Toolkit.app/Contents/Resources/wine/bin/wine64";
        // Старый gcenx wine-crossover (если кто-то поставил вручную):
        yield return "/Applications/Wine Crossover.app/Contents/Resources/wine/bin/wine64";
        // Brew formula (не cask) — на всякий, для редких сборок:
        yield return "/opt/homebrew/bin/wine64";
        yield return "/usr/local/bin/wine64";
        yield return "/opt/homebrew/bin/wine";
        yield return "/usr/local/bin/wine";
    }

    public static string? FindWineBinary()
        => EnumerateWineCandidates().FirstOrDefault(File.Exists);

    public async Task<RunningServer> StartAsync(
        AppSettings settings,
        IReadOnlyList<string> modIds,
        Action<string> onOutput,
        Action<int> onExit,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(settings.ServerInstallPath) ||
            !File.Exists(Path.Combine(settings.ServerInstallPath, "ShooterGame", "Binaries", "Win64", "ArkAscendedServer.exe")))
        {
            throw new InvalidOperationException("ArkAscendedServer.exe not found. Install the server on the Install tab.");
        }

        var exe = Path.Combine(settings.ServerInstallPath, "ShooterGame", "Binaries", "Win64", "ArkAscendedServer.exe");
        var wine = FindWineBinary()
                   ?? throw new InvalidOperationException("Server runtime missing — reinstall ArkManager.");
        var prefix = _paths.DefaultWinePrefixDir;
        Directory.CreateDirectory(prefix);

        var args = new List<string> { exe };
        args.AddRange(ServerCommandLine.Build(settings, modIds));

        var env = new Dictionary<string, string>
        {
            ["WINEPREFIX"] = prefix,
            ["WINEDEBUG"] = "-all",
            // Отключаем графический драйвер wine — ASA dedicated server headless, окно ему
            // не нужно. Без этого wine рисует «Server Console»-окно, где текст нечитаемо
            // бел-на-бел (фон строк = дефолтный белый GDI-bk, реестром не правится).
            // Лог при этом идёт в stdout (-stdout -FullStdOutLogOutput) и виден в ArkManager.
            // Проверено: сервер полностью стартует без дисплея.
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
