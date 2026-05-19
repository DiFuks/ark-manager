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
    private bool _stopRequested;
    private CancellationTokenSource? _scheduleCts;

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
            _stopRequested = false;
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
                    bool autoRestart;
                    lock (_lock)
                    {
                        State = code == 0 ? ServerState.Stopped : ServerState.Crashed;
                        _running = null;
                        autoRestart = !_stopRequested
                                      && code != 0
                                      && _settings.Current.AutoRestartOnCrash;
                    }
                    StateChanged?.Invoke(State);
                    if (autoRestart) _ = AutoRestartLoopAsync();
                },
                ct: _cts.Token);

            lock (_lock) State = ServerState.Running;
            StateChanged?.Invoke(State);
            StartScheduledRestartTimer();
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
            _stopRequested = true;
        }
        StateChanged?.Invoke(State);
        _scheduleCts?.Cancel();

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

    private async Task AutoRestartLoopAsync()
    {
        var delay = Math.Max(1, _settings.Current.AutoRestartDelaySeconds);
        PushLog($"[auto-restart] жду {delay}s и стартую заново...");
        try { await Task.Delay(TimeSpan.FromSeconds(delay)); } catch { }
        try { await StartAsync(); }
        catch (Exception ex) { PushLog("[auto-restart] не получилось: " + ex.Message); }
    }

    private void StartScheduledRestartTimer()
    {
        _scheduleCts?.Cancel();
        var hours = _settings.Current.ScheduledRestartHours;
        if (hours <= 0) return;
        _scheduleCts = new CancellationTokenSource();
        var token = _scheduleCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromHours(hours), token);
                if (token.IsCancellationRequested) return;
                PushLog("[scheduled restart]");
                await StopAsync();
                await Task.Delay(2000, token);
                await StartAsync();
            }
            catch (OperationCanceledException) { /* normal */ }
            catch (Exception ex) { PushLog("[scheduled restart failed] " + ex.Message); }
        });
    }

    private void PushLog(string line)
    {
        _ringLog.Enqueue(line);
        // Кольцо ~5000 строк
        while (_ringLog.Count > 5000 && _ringLog.TryDequeue(out _)) { }
        LogLine?.Invoke(line);
    }
}
