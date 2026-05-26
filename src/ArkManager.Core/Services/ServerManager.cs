using System.Collections.Concurrent;
using ArkManager.Core.Models;
using ArkManager.Core.Services.Launchers;
using ArkManager.Core.Services.Mods;
using ArkManager.Core.Services.Rcon;

namespace ArkManager.Core.Services;

public enum ServerState { Stopped, Starting, Running, Stopping, Crashed }

/// <summary>
/// Координатор состояния запущенного сервера: PID, лог, переходы.
/// Слой UI подписывается на StateChanged / LogLine.
/// </summary>
public sealed class ServerManager
{
    private readonly SettingsService _settings;
    private readonly IServerLauncher _launcher;
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

    public ServerManager(SettingsService settings, IServerLauncher launcher, ModsService mods)
    {
        _settings = settings;
        _launcher = launcher;
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

            _running = await _launcher.StartAsync(
                _settings.Current,
                _mods.Ids(),
                onOutput: PushLog,
                onExit: code =>
                {
                    PushLog($"[server exited code={code}]");
                    bool autoRestart;
                    lock (_lock)
                    {
                        // При намеренной остановке (включая хард-кил fallback, дающий
                        // ненулевой код) это НЕ краш — состояние Stopped.
                        State = (code == 0 || _stopRequested) ? ServerState.Stopped : ServerState.Crashed;
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
            // Graceful: сначала просим сервер сохранить мир и выйти через RCON.
            // Без этого hard-kill (ниже и в ProcessRunner по ct) теряет весь прогресс
            // с последнего автосейва — ASA флашит мир на диск только по saveworld/выходу.
            await TryGracefulSaveAndExitAsync(ct);

            // Ждём, пока сервер сам завершится после DoExit (у ASA graceful-выход
            // занимает ~20с). Не дождались за 60с — добиваем хард-килом (мир уже
            // сохранён через saveworld выше, потери данных нет).
            if (pid is int p)
            {
                if (!await WaitForExitAsync(p, TimeSpan.FromSeconds(60), ct))
                    await _launcher.StopAsync(p, ct);
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

    /// <summary>Решает, имеет ли смысл пытаться graceful-сейв по RCON.</summary>
    internal static bool ShouldAttemptGracefulSave(ServerLaunchOptions o)
        => o.RconEnabled && !string.IsNullOrWhiteSpace(o.AdminPassword);

    /// <summary>
    /// saveworld (ждём флаш на диск) + DoExit через RCON. Любая ошибка —
    /// просто логируется: hard-kill в StopAsync остаётся гарантированным fallback'ом.
    /// </summary>
    private async Task TryGracefulSaveAndExitAsync(CancellationToken ct)
    {
        var o = _settings.Current.LaunchOptions;
        if (!ShouldAttemptGracefulSave(o))
        {
            PushLog("[stop] RCON выключен или нет admin-пароля — graceful save пропущен " +
                    "(hard-kill, возможна потеря прогресса с последнего автосейва).");
            return;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));

            await using var rcon = new RconClient();
            await rcon.ConnectAsync("127.0.0.1", o.RconPort, o.AdminPassword!, timeout.Token);

            PushLog("[stop] saveworld...");
            var resp = await rcon.SendAsync("saveworld", timeout.Token);
            PushLog("[stop] " + (string.IsNullOrWhiteSpace(resp) ? "(saveworld ok)" : resp.Trim()));

            // DoExit — best-effort: сервер при выходе часто закрывает RCON-соединение,
            // не присылая ответ. Это НЕ ошибка — мир уже сохранён выше через saveworld.
            try { await rcon.SendAsync("DoExit", timeout.Token); }
            catch { /* соединение закрыто в процессе выхода — ожидаемо */ }
            PushLog("[stop] DoExit отправлен, жду graceful-выход...");
        }
        catch (Exception ex)
        {
            PushLog("[stop] graceful save не удался: " + ex.Message + " — fallback hard-kill.");
        }
    }

    /// <summary>Поллит, пока процесс не завершится сам. true — завершился в срок.</summary>
    private async Task<bool> WaitForExitAsync(int pid, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (!await _launcher.IsRunningAsync(pid, ct)) return true;
            try { await Task.Delay(500, ct); } catch { return false; }
        }
        return !await _launcher.IsRunningAsync(pid, ct);
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
