using System.Diagnostics;
using ArkManager.Core.Models;
using ArkManager.Core.Util;

namespace ArkManager.Core.Services.Launchers;

/// <summary>
/// Общая логика для wine-подобных лаунчеров (Whisky и LocalWine):
/// запускают .exe через wine64 с WINEPREFIX, передают аргументы как есть.
/// </summary>
public abstract class WineLauncherBase : IServerLauncher
{
    public abstract Task<LauncherStatus> ProbeAsync(CancellationToken ct = default);

    protected abstract string GetWineBinary(AppSettings settings);
    protected abstract string? GetWinePrefix(AppSettings settings);

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
            throw new InvalidOperationException("ArkAscendedServer.exe не найден. Установите сервер на вкладке Install.");
        }

        var exe = Path.Combine(settings.ServerInstallPath, "ShooterGame", "Binaries", "Win64", "ArkAscendedServer.exe");
        var wine = GetWineBinary(settings);
        var prefix = GetWinePrefix(settings);

        var args = new List<string> { exe };
        args.AddRange(ServerCommandLine.Build(settings, modIds));

        var env = new Dictionary<string, string>();
        if (prefix != null) env["WINEPREFIX"] = prefix;
        env["WINEDEBUG"] = "-all";

        var workDir = Path.Combine(settings.ServerInstallPath, "ShooterGame", "Binaries", "Win64");

        Process? started = null;
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
                    onStarted: p =>
                    {
                        started = p;
                        tcs.TrySetResult(new RunningServer(p.Id, DateTime.UtcNow));
                    },
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
