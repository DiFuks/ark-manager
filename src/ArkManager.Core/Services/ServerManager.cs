using System.Collections.Concurrent;
using ArkManager.Core.Models;
using ArkManager.Core.Services.Config;
using ArkManager.Core.Services.Launchers;
using ArkManager.Core.Services.Mods;
using ArkManager.Core.Services.Rcon;
using ArkManager.Core.Util;

namespace ArkManager.Core.Services;

public enum ServerState { Stopped, Starting, Running, Stopping, Crashed }

/// <summary>
/// Coordinates the running server's state: PID, log, transitions.
/// The UI layer subscribes to StateChanged / LogLine.
/// </summary>
public sealed class ServerManager
{
    private readonly SettingsService _settings;
    private readonly IServerLauncher _launcher;
    private readonly ModsService _mods;
    private readonly ConfigService _config;

    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private RunningServer? _running;
    private bool _stopRequested;

    public ServerState State { get; private set; } = ServerState.Stopped;
    public int? Pid => _running?.Pid;
    public DateTime? StartedAt => _running?.StartedAt;

    /// <summary>
    /// Server is not just running (process alive) but has finished loading the world and accepts connections.
    /// Before that State=Running, but it's still the "yellow" phase. Detected from a log line.
    /// </summary>
    public bool IsReady { get; private set; }

    public event Action<ServerState>? StateChanged;
    public event Action<bool>? ReadyChanged;
    public event Action<string>? LogLine;

    private readonly ConcurrentQueue<string> _ringLog = new();
    public IReadOnlyCollection<string> Snapshot() => _ringLog.ToArray();

    public ServerManager(SettingsService settings, IServerLauncher launcher, ModsService mods, ConfigService config)
    {
        _settings = settings;
        _launcher = launcher;
        _mods = mods;
        _config = config;
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
            // Sync GameUserSettings.ini with the VM before starting.
            // Passwords, RCONEnabled/RCONPort, SessionName, Port/QueryPort, MaxPlayers
            // live ONLY in ini (see CLAUDE.md: they must not go into the URL — the ASA parser
            // glues the tail into the value). Without this the first launch after install
            // would pick up ASA defaults — RCON ends up disabled, even if settings.json says
            // RconEnabled=true. Save in the Config tab does the same plus a JSON update;
            // here we only make sure ini and settings.json haven't drifted apart.
            _config.ApplyLaunchOptionsToIni(_settings.Current.LaunchOptions);

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
                        // On an intentional stop (including the hard-kill fallback which yields
                        // a non-zero code) this is NOT a crash — state is Stopped.
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
            // Graceful: first ask the server to save the world and exit via RCON.
            // Without this the hard-kill (below and in ProcessRunner via ct) loses all progress
            // since the last auto-save — ASA flushes the world to disk only on saveworld/exit.
            await TryGracefulSaveAndExitAsync(ct);

            // Wait for the server to exit on its own after DoExit (ASA's graceful exit
            // takes ~20s). If it doesn't exit within 60s — finish it with a hard-kill (the
            // world is already saved by saveworld above, no data loss).
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
    /// Shutdown on manager exit (Ctrl+C / window close / SIGTERM). Does a best-effort
    /// graceful save via RCON, then GUARANTEES the process is killed — so we don't leave
    /// an orphaned server the manager wouldn't see afterwards. A short, blocking-awaitable operation.
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

        // Save the world (saveworld + DoExit). Has its own timeout inside; errors aren't critical.
        await TryGracefulSaveAndExitAsync(CancellationToken.None);

        // Wait briefly for a voluntary exit after DoExit, then finish it — kill is mandatory.
        if (pid is int p)
        {
            await WaitForExitAsync(p, TimeSpan.FromSeconds(10), CancellationToken.None);
            try { await _launcher.StopAsync(p); } catch { /* already dead */ }
        }
        _cts?.Cancel();

        lock (_lock) { _running = null; }
        SetReady(false);
        SetState(ServerState.Stopped);
    }

    /// <summary>
    /// "Adoption": if the manager was killed (Force Quit / SIGKILL / crash) and left a
    /// running ArkAscendedServer.exe behind — we pick it up on startup to show Running,
    /// allow Stop via RCON, and prevent launching a second server. Called once at startup.
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
        catch { return; } // not macOS / ps unavailable

        if (found is not DiscoveredServer d) return;

        lock (_lock)
        {
            if (State != ServerState.Stopped) return; // we started one ourselves in the meantime
            _running = new RunningServer(d.Pid, DateTime.UtcNow - d.Uptime);
            _stopRequested = false;
        }
        SetReady(true); // already running and accepting players
        SetState(ServerState.Running);
        PushLog($"[adopted running server pid={d.Pid}, up {d.Uptime:hh\\:mm\\:ss}]");
        StartAdoptedMonitor(d.Pid);
    }

    /// <summary>No log for an adopted process (stdout belongs to someone else) — poll whether it's alive.</summary>
    private void StartAdoptedMonitor(int pid)
    {
        _ = Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(3000);
                lock (_lock) { if (_running?.Pid != pid) return; } // we stopped it / replaced it
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

    /// <summary>Decides whether it makes sense to attempt a graceful save via RCON.</summary>
    internal static bool ShouldAttemptGracefulSave(ServerLaunchOptions o)
        => o.RconEnabled && !string.IsNullOrWhiteSpace(o.AdminPassword);

    /// <summary>
    /// saveworld (wait for disk flush) + DoExit via RCON. Any error is just
    /// logged: the hard-kill in StopAsync remains a guaranteed fallback.
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

            // DoExit — best-effort: while exiting the server often closes the RCON connection
            // without sending a response. That's NOT an error — the world is already saved by saveworld above.
            try { await rcon.SendAsync("DoExit", timeout.Token); }
            catch { /* connection closed during exit — expected */ }
            PushLog("[stop] DoExit sent, waiting for graceful exit...");
        }
        catch (Exception ex)
        {
            PushLog("[stop] graceful save failed: " + ex.Message + " — fallback hard-kill.");
        }
    }

    /// <summary>Polls until the process exits on its own. true — exited within the deadline.</summary>
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
        // Ring buffer ~5000 lines
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
    /// Changes state and fires StateChanged ONLY on an actual change. Without this Stopped
    /// arrived twice (from the process onExit and from the StopAsync finally) → duplicate notifications.
    /// The event is invoked outside the lock so the handler can't hit a deadlock/reentrancy.
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

    /// <summary>Log line meaning the world has loaded and the server is accepting connections.</summary>
    internal static bool IsServerReadyLine(string line)
        => line.Contains("advertising for join", StringComparison.OrdinalIgnoreCase);
}
