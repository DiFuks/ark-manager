using ArkManager.Core.Models;

namespace ArkManager.Core.Services.Launchers;

public sealed record RunningServer(int Pid, DateTime StartedAt);

public interface IServerLauncher
{
    /// <summary>
    /// Launches ArkAscendedServer.exe. stdout/stderr are routed to the callbacks.
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
