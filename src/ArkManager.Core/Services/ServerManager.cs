using System.Collections.Concurrent;
using ArkManager.Core.Models;
using ArkManager.Core.Services.Launchers;
using ArkManager.Core.Services.Mods;

namespace ArkManager.Core.Services;

public enum ServerState { Stopped, Starting, Running, Stopping, Crashed }

/// <summary>
/// Координатор состояния запущенного сервера: PID, лог, переходы.
/// Слой UI подписывается на StateChanged / LogLine.
/// </summary>
public sealed class ServerManager
{
    private readonly SettingsService _settings;
    private readonly LauncherFactory _launchers;
    private readonly ModsService _mods;

    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private RunningServer? _running;

    public ServerState State { get; private set; } = ServerState.Stopped;
    public int? Pid => _running?.Pid;
    public DateTime? StartedAt => _running?.StartedAt;

    public event Action<ServerState>? StateChanged;
    public event Action<string>? LogLine;

    private readonly ConcurrentQueue<string> _ringLog = new();
    public IReadOnlyCollection<string> Snapshot() => _ringLog.ToArray();

    public ServerManager(SettingsService settings, LauncherFactory launchers, ModsService mods)
    {
        _settings = settings;
        _launchers = launchers;
        _mods = mods;
    }

    public async Task StartAsync(CancellationToken externalCt = default)
    {
        lock (_lock)
        {
            if (State is ServerState.Running or ServerState.Starting)
                throw new InvalidOperationException("Сервер уже запущен.");
            State = ServerState.Starting;
        }
        StateChanged?.Invoke(State);

        try
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
            var launcher = _launchers.Resolve(_settings.Current.LaunchMode);

            _running = await launcher.StartAsync(
                _settings.Current,
                _mods.Ids(),
                onOutput: PushLog,
                onExit: code =>
                {
                    PushLog($"[server exited code={code}]");
                    lock (_lock)
                    {
                        State = code == 0 ? ServerState.Stopped : ServerState.Crashed;
                        _running = null;
                    }
                    StateChanged?.Invoke(State);
                },
                ct: _cts.Token);

            lock (_lock) State = ServerState.Running;
            StateChanged?.Invoke(State);
        }
        catch (Exception ex)
        {
            PushLog("[start failed] " + ex.Message);
            lock (_lock) { State = ServerState.Crashed; _running = null; }
            StateChanged?.Invoke(State);
            throw;
        }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        int? pid;
        lock (_lock)
        {
            if (State is ServerState.Stopped or ServerState.Crashed) return;
            State = ServerState.Stopping;
            pid = _running?.Pid;
        }
        StateChanged?.Invoke(State);

        try
        {
            if (pid is int p)
            {
                var launcher = _launchers.Resolve(_settings.Current.LaunchMode);
                await launcher.StopAsync(p, ct);
            }
            _cts?.Cancel();
        }
        finally
        {
            lock (_lock) { State = ServerState.Stopped; _running = null; }
            StateChanged?.Invoke(State);
            PushLog("[server stopped by user]");
        }
    }

    private void PushLog(string line)
    {
        _ringLog.Enqueue(line);
        // Кольцо ~5000 строк
        while (_ringLog.Count > 5000 && _ringLog.TryDequeue(out _)) { }
        LogLine?.Invoke(line);
    }
}
