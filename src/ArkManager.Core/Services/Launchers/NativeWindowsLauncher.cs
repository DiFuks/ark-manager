using System.Diagnostics;
using ArkManager.Core.Models;
using ArkManager.Core.Util;

namespace ArkManager.Core.Services.Launchers;

/// <summary>
/// Launches ArkAscendedServer.exe natively on Windows — no wine, no WINEPREFIX.
/// Used by DI only when OperatingSystem.IsWindows().
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
