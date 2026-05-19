using ArkManager.Core.Models;

namespace ArkManager.Core.Services.Launchers;

public sealed record LauncherStatus(bool Available, string? DiagnosticMessage);

public sealed record RunningServer(int Pid, DateTime StartedAt);

public interface IServerLauncher
{
    /// <summary>Диагностика: установлен ли runtime, готов ли к запуску.</summary>
    Task<LauncherStatus> ProbeAsync(CancellationToken ct = default);

    /// <summary>
    /// Запускает ArkAscendedServer.exe. Возвращает PID. stdout/stderr идут в коллбеки.
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
