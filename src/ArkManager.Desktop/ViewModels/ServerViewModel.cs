using System.Diagnostics;
using System.Globalization;
using ArkManager.Core.Services;
using ArkManager.Core.Services.Config;
using ArkManager.Core.Services.Rcon;
using ArkManager.Core.Util;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArkManager.App.ViewModels;

public partial class ServerViewModel : ViewModelBase
{
    private readonly ServerManager? _server;
    private readonly Services.ConsoleLog? _console;

    [ObservableProperty] private string _log = "";
    [ObservableProperty] private string _identity = "—";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyPropertyChangedFor(nameof(StatusBrush))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(PlayersDisplay))]
    [NotifyPropertyChangedFor(nameof(HasPlayersDetail))]
    private string _state = "Stopped";

    // Server finished loading the world and is accepting connections (the ARK "green dot" equivalent).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusBrush))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(PlayersDisplay))]
    [NotifyPropertyChangedFor(nameof(HasPlayersDetail))]
    private bool _ready;

    // Status dot like in the ARK window: grey stopped → yellow loading → green ready → red crash.
    // Colours come from design tokens (Themes/Tokens.axaml) rather than Brushes.*, so the hue
    // matches the rest of the OK/Warn/Danger across the UI.
    public IBrush StatusBrush => State switch
    {
        "Running"  => Ready ? Tok("OkBrush") : Tok("WarnBrush"),
        "Starting" => Tok("WarnBrush"),
        "Stopping" => Tok("WarnBrush"),
        "Crashed"  => Tok("DangerBrush"),
        _          => Tok("MutedBrush"),
    };

    private static IBrush Tok(string key)
    {
        if (Avalonia.Application.Current?.Resources is { } res
            && res.TryGetResource(key, null, out var v) && v is IBrush b)
            return b;
        return Brushes.Gray;
    }

    // While the process is alive but the world is still loading — it's more honest to show "Loading…" than "Running".
    public string StatusText => State == "Running" && !Ready ? "Loading…" : State;

    [ObservableProperty] private int? _pid;
    [ObservableProperty] private string _uptime = "—";
    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private bool _autoScroll = true;

    // PlayersOnline-as-a-number is only meaningful once the world has actually loaded: while the
    // process is alive but still in the yellow "Loading…" phase the RCON port isn't open yet,
    // so PlayerPoller can't produce a real count. Showing "0" there is misleading. Gate on Ready,
    // not just Running, so the tile flips to a number only once the server is accepting players.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayersDisplay))]
    private int _playersOnline;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlayersDetail))]
    private string _playersDetail = "—";

    // Full comma-joined name list — bound to the tile's tooltip. PlayersDetail itself is a short
    // summary (first N names + "+M more") so the tile doesn't bloat when a full server (70 names)
    // is online. Tooltip is the escape hatch when the user wants the whole roster.
    [ObservableProperty] private string _playersDetailFull = "—";

    // Cap from LaunchOptions.MaxPlayers — kept in sync via SettingsService.Changed so a Config-tab
    // edit reflects on the Server tile without restart.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayersDisplay))]
    private int _maxPlayers;

    public string PlayersDisplay => State == "Running" && Ready ? $"{PlayersOnline}/{MaxPlayers}" : "—";

    public bool HasPlayersDetail =>
        State == "Running"
        && Ready
        && !string.IsNullOrWhiteSpace(PlayersDetail)
        && PlayersDetail != "—";

    // Pure function — returned as (short, full): short goes into the PLAYERS tile under the count,
    // full is the tooltip. Above PreviewLimit names we condense to "A, B, …, E +M more" so the
    // tile stays readable on a 70-slot server while the tooltip still surfaces every name.
    internal const int PlayersPreviewLimit = 5;
    internal static (string Short, string Full) BuildPlayersDetail(PlayerSample s)
    {
        if (s.Error != null) return (s.Error, s.Error);
        if (s.Names.Count == 0) return ("—", "—");

        var full = string.Join(", ", s.Names);
        if (s.Names.Count <= PlayersPreviewLimit) return (full, full);

        var head = string.Join(", ", s.Names.Take(PlayersPreviewLimit));
        var more = s.Names.Count - PlayersPreviewLimit;
        return ($"{head} +{more} more", full);
    }

    [ObservableProperty] private string _cpuUsage = "—";
    [ObservableProperty] private string _ramUsage = "—";

    private double? _totalRamGb;

    // Stateful — needs the previous TotalProcessorTime snapshot to compute %.
    // Used on Windows AND Linux: under wine on Linux `wine64` execve's into `wine64-preloader`
    // (PID stays), so .NET's Process API (which reads /proc/<pid>/stat for utime+stime and
    // /proc/<pid>/status for VmRSS) sees the running game correctly. macOS stays on ps because
    // we need the wineserver subtree there and phys_footprint vs RSS divergence under Rosetta.
    private CpuPercentSampler? _procCpuSampler;
    private int? _procSampledPid;

    public ServerViewModel() { }

    public ServerViewModel(ServerManager server, PlayerPoller poller, SettingsService settings, ConfigService config)
    {
        _server = server;
        // Batched, capped console — folds a flood of server output into ~10 re-renders/sec
        // instead of one O(buffer) string copy + TextBox re-render per line (the UI-freeze case).
        _console = new Services.ConsoleLog(s => Log = s);
        var o = settings.Current.LaunchOptions;
        Identity = $"{config.Snapshot.SessionName} · {o.Map}";
        MaxPlayers = o.MaxPlayers;
        settings.Changed += s => App.UiThread(() => MaxPlayers = s.LaunchOptions.MaxPlayers);
        foreach (var l in server.Snapshot()) AppendLine(l);
        server.StateChanged += s => App.UiThread(() => { State = s.ToString(); Pid = server.Pid; });
        server.ReadyChanged += r => App.UiThread(() => Ready = r);
        // Initialise from the current state — in case the server is already running
        // (adopted at startup, or the tab was opened while it was running).
        State = server.State.ToString();
        Pid = server.Pid;
        Ready = server.IsReady;
        // No per-line UiThread hop: ConsoleLog.Append is thread-safe and the timer publishes the
        // batched result on the UI thread. The filter (live, applies to new lines only) stays here.
        server.LogLine += line =>
        {
            if (!string.IsNullOrWhiteSpace(Filter) && !line.Contains(Filter, StringComparison.OrdinalIgnoreCase)) return;
            AppendLine(line);
        };

        poller.Sampled += s => App.UiThread(() =>
        {
            PlayersOnline = s.Count;
            (PlayersDetail, PlayersDetailFull) = BuildPlayersDetail(s);
        });

        _ = Task.Run(async () =>
        {
            var tick = 0;
            while (true)
            {
                await Task.Delay(1000);
                App.UiThread(UpdateUptime);
                if (tick++ % 2 == 0) await SampleResourcesAsync(); // CPU/RAM roughly every 2s
            }
        });
    }

    /// <summary>
    /// Server CPU/RAM = sum across the whole process tree rooted at PID (under wine that's exe + wineserver + helpers).
    /// CPU is normalised by core count → "CPU load percent" 0..100. RAM — % of physical + GB.
    /// </summary>
    private async Task SampleResourcesAsync()
    {
        if (_server?.Pid is not int pid)
        {
            // Reset the sampler so the next start doesn't carry a stale delta from
            // the previous run's TotalProcessorTime baseline.
            _procSampledPid = null;
            _procCpuSampler?.Reset();
            App.UiThread(() => { CpuUsage = "—"; RamUsage = "—"; });
            return;
        }
        try
        {
            // Windows: native single process. Linux: under wine the launched PID is the running
            // game (wine64 execve's to wine64-preloader, same PID), so /proc/<pid>/stat gives us
            // everything. .NET's Process.TotalProcessorTime + WorkingSet64 wrap that uniformly.
            // macOS stays on ps because under wine via Rosetta we need to sum the subtree and
            // use phys_footprint instead of RSS.
            if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
            {
                SampleViaProcessApi(pid);
                return;
            }

            var lcC = new Dictionary<string, string> { ["LC_ALL"] = "C" };

            // CPU — sum of %cpu over the process tree (ps; %cpu uses a dot thanks to LC_ALL=C).
            var ps = await ProcessRunner.RunCaptureAsync("/bin/ps",
                new[] { "-axo", "pid=,ppid=,rss=,%cpu=" }, env: lcC);
            var st = ProcessTreeStats.Sum(ps.StdOut, pid);
            var cpu = st.CpuPercent / Math.Max(1, Environment.ProcessorCount);

            // RAM — phys_footprint (like Activity Monitor): under wine RSS is understated by an order
            // of magnitude because macOS compresses memory. Fall back to RSS if footprint is unavailable.
            long memBytes = st.RssKb * 1024;
            try
            {
                var fp = await ProcessRunner.RunCaptureAsync("/usr/bin/footprint",
                    new[] { pid.ToString() }, env: lcC);
                if (MacMemory.PhysFootprintBytes(fp.StdOut) is long b && b > 0) memBytes = b;
            }
            catch { /* footprint unavailable — RSS fallback remains */ }

            _totalRamGb ??= await ReadTotalRamGbAsync();
            var gb = memBytes / 1024.0 / 1024.0 / 1024.0;

            App.UiThread(() =>
            {
                CpuUsage = $"{cpu:0}%";
                RamUsage = _totalRamGb is > 0
                    ? $"{gb / _totalRamGb.Value * 100:0}% ({gb:0.0} GB)"
                    : $"{gb:0.0} GB";
            });
        }
        catch { /* ps unavailable — not critical */ }
    }

    private void SampleViaProcessApi(int pid)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            // First snapshot for this PID, or PID changed since last call — reset the delta.
            if (_procSampledPid != pid)
            {
                _procSampledPid = pid;
                _procCpuSampler = new CpuPercentSampler(Environment.ProcessorCount);
            }
            var cpu = _procCpuSampler!.Sample(proc.TotalProcessorTime, DateTime.UtcNow);
            var memBytes = proc.WorkingSet64;

            _totalRamGb ??= SystemMemory.GetTotalRamBytes() is long b && b > 0
                ? b / 1024.0 / 1024.0 / 1024.0
                : null;
            var gb = memBytes / 1024.0 / 1024.0 / 1024.0;

            App.UiThread(() =>
            {
                CpuUsage = $"{cpu:0}%";
                RamUsage = _totalRamGb is > 0
                    ? $"{gb / _totalRamGb.Value * 100:0}% ({gb:0.0} GB)"
                    : $"{gb:0.0} GB";
            });
        }
        catch (ArgumentException)
        {
            // PID gone since we read it — server just exited, the next tick will see Pid=null.
            App.UiThread(() => { CpuUsage = "—"; RamUsage = "—"; });
        }
    }

    private static async Task<double?> ReadTotalRamGbAsync()
    {
        try
        {
            var r = await ProcessRunner.RunCaptureAsync("/usr/sbin/sysctl", new[] { "-n", "hw.memsize" });
            if (long.TryParse(r.StdOut.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytes))
                return bytes / 1024.0 / 1024.0 / 1024.0;
        }
        catch { /* not macOS / no sysctl — show GB only */ }
        return null;
    }

    public bool CanStart => _server != null && State is "Stopped" or "Crashed";
    public bool CanStop  => _server != null && State is "Running" or "Starting";

    [RelayCommand(CanExecute = nameof(CanStart))]
    public async Task StartAsync()
    {
        if (_server == null) return;
        try { await _server.StartAsync(); }
        catch (Exception ex) { AppendLine("[start failed] " + ex.Message); }
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    public async Task StopAsync()
    {
        if (_server == null) return;
        try { await _server.StopAsync(); }
        catch (Exception ex) { AppendLine("[stop failed] " + ex.Message); }
    }

    [RelayCommand]
    public void ClearLog() => _console?.Clear();

    [RelayCommand]
    public async Task CopyLog() => await Services.Browse.CopyToClipboardAsync(Log);

    private void AppendLine(string line) => _console?.Append(line);

    private void UpdateUptime()
    {
        // Count from Ready (world loaded, accepting connections), not from process Start —
        // the loading phase isn't real uptime to the user.
        if (_server?.ReadyAt == null) { Uptime = "—"; return; }
        var t = DateTime.UtcNow - _server.ReadyAt.Value;
        Uptime = $"{(int)t.TotalHours:D2}:{t.Minutes:D2}:{t.Seconds:D2}";
    }
}
