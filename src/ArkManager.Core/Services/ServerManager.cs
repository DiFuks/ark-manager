using System.Collections.Concurrent;
using ArkManager.Core.Models;
using ArkManager.Core.Services.Launchers;
using ArkManager.Core.Services.Mods;
using ArkManager.Core.Services.Rcon;
using ArkManager.Core.Util;

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

    public ServerState State { get; private set; } = ServerState.Stopped;
    public int? Pid => _running?.Pid;
    public DateTime? StartedAt => _running?.StartedAt;

    /// <summary>
    /// Сервер не просто запущен (процесс жив), а закончил загрузку мира и принимает подключения.
    /// До этого State=Running, но это ещё «жёлтая» фаза. Определяется по строке лога.
    /// </summary>
    public bool IsReady { get; private set; }

    public event Action<ServerState>? StateChanged;
    public event Action<bool>? ReadyChanged;
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
                throw new InvalidOperationException("Server is already running.");
            _stopRequested = false;
        }
        SetReady(false);
        SetState(ServerState.Starting);

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
                    ServerState target;
                    lock (_lock)
                    {
                        // При намеренной остановке (включая хард-кил fallback, дающий
                        // ненулевой код) это НЕ краш — состояние Stopped.
                        target = (code == 0 || _stopRequested) ? ServerState.Stopped : ServerState.Crashed;
                        _running = null;
                    }
                    SetReady(false);
                    SetState(target);
                },
                ct: _cts.Token);

            SetState(ServerState.Running);
        }
        catch (Exception ex)
        {
            PushLog("[start failed] " + ex.Message);
            lock (_lock) { _running = null; }
            SetState(ServerState.Crashed);
            throw;
        }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        int? pid;
        lock (_lock)
        {
            if (State is ServerState.Stopped or ServerState.Crashed) return;
            pid = _running?.Pid;
            _stopRequested = true;
        }
        SetState(ServerState.Stopping);

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
            lock (_lock) { _running = null; }
            SetReady(false);
            SetState(ServerState.Stopped);
            PushLog("[server stopped by user]");
        }
    }

    /// <summary>
    /// Остановка при выходе менеджера (Ctrl+C / закрытие окна / SIGTERM). Делает best-effort
    /// graceful-сейв по RCON, затем ГАРАНТИРОВАННО убивает процесс — чтобы не оставить
    /// осиротевший сервер, который менеджер потом не видит. Блокируемо-ожидаемая короткая операция.
    /// </summary>
    public async Task ShutdownAsync()
    {
        int? pid;
        lock (_lock)
        {
            if (State is ServerState.Stopped or ServerState.Crashed) return;
            _stopRequested = true;
            pid = _running?.Pid;
        }
        SetState(ServerState.Stopping);

        // Сохранить мир (saveworld + DoExit). Внутри свой таймаут, ошибки не критичны.
        await TryGracefulSaveAndExitAsync(CancellationToken.None);

        // Немного ждём добровольного выхода после DoExit, потом добиваем — kill обязателен.
        if (pid is int p)
        {
            await WaitForExitAsync(p, TimeSpan.FromSeconds(10), CancellationToken.None);
            try { await _launcher.StopAsync(p); } catch { /* уже мёртв */ }
        }
        _cts?.Cancel();

        lock (_lock) { _running = null; }
        SetReady(false);
        SetState(ServerState.Stopped);
    }

    /// <summary>
    /// «Усыновление»: если менеджер был убит (Force Quit / SIGKILL / краш) и оставил
    /// работающий ArkAscendedServer.exe — подхватываем его на старте, чтобы показать Running,
    /// дать Stop по RCON и не дать запустить второй сервер. Зовётся один раз при запуске.
    /// </summary>
    public async Task AdoptIfRunningAsync()
    {
        lock (_lock) { if (State != ServerState.Stopped) return; }

        var installPath = _settings.Current.ServerInstallPath;
        if (string.IsNullOrWhiteSpace(installPath)) return;
        var exe = Path.Combine(installPath, "ShooterGame", "Binaries", "Win64", "ArkAscendedServer.exe");

        DiscoveredServer? found;
        try
        {
            var ps = await ProcessRunner.RunCaptureAsync("/bin/ps",
                new[] { "-axww", "-o", "pid=,etime=,command=" });
            found = ServerDiscovery.Find(ps.StdOut, exe);
        }
        catch { return; } // не macOS / ps недоступен

        if (found is not DiscoveredServer d) return;

        lock (_lock)
        {
            if (State != ServerState.Stopped) return; // успели запустить сами
            _running = new RunningServer(d.Pid, DateTime.UtcNow - d.Uptime);
            _stopRequested = false;
        }
        SetReady(true); // уже работает и принимает игроков
        SetState(ServerState.Running);
        PushLog($"[adopted running server pid={d.Pid}, up {d.Uptime:hh\\:mm\\:ss}]");
        StartAdoptedMonitor(d.Pid);
    }

    /// <summary>Лога у усыновлённого процесса нет (stdout чужой) — поллим, жив ли он.</summary>
    private void StartAdoptedMonitor(int pid)
    {
        _ = Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(3000);
                lock (_lock) { if (_running?.Pid != pid) return; } // остановили сами / заменили
                if (await _launcher.IsRunningAsync(pid)) continue;

                bool changed;
                lock (_lock)
                {
                    changed = _running?.Pid == pid;
                    if (changed) _running = null;
                }
                if (!changed) return;
                SetReady(false);
                SetState(ServerState.Stopped);
                PushLog("[adopted server exited]");
                return;
            }
        });
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
            PushLog("[stop] RCON disabled or no admin password — graceful save skipped " +
                    "(hard-kill; progress since last auto-save may be lost).");
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
            PushLog("[stop] DoExit sent, waiting for graceful exit...");
        }
        catch (Exception ex)
        {
            PushLog("[stop] graceful save failed: " + ex.Message + " — fallback hard-kill.");
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

    private void PushLog(string line)
    {
        _ringLog.Enqueue(line);
        // Кольцо ~5000 строк
        while (_ringLog.Count > 5000 && _ringLog.TryDequeue(out _)) { }
        if (!IsReady && IsServerReadyLine(line)) SetReady(true);
        LogLine?.Invoke(line);
    }

    private void SetReady(bool value)
    {
        if (IsReady == value) return;
        IsReady = value;
        ReadyChanged?.Invoke(value);
    }

    /// <summary>
    /// Меняет состояние и шлёт StateChanged ТОЛЬКО при реальной смене. Без этого Stopped
    /// прилетал дважды (из onExit процесса и из finally StopAsync) → дубль уведомлений.
    /// Событие вызываем вне lock, чтобы обработчик не мог поймать дедлок/реентранси.
    /// </summary>
    private void SetState(ServerState s)
    {
        lock (_lock)
        {
            if (State == s) return;
            State = s;
        }
        StateChanged?.Invoke(s);
    }

    /// <summary>Строка лога, означающая, что мир загружен и сервер принимает подключения.</summary>
    internal static bool IsServerReadyLine(string line)
        => line.Contains("advertising for join", StringComparison.OrdinalIgnoreCase);
}
