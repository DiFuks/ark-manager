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
    private const int MaxLogChars = 500_000;
    private readonly ServerManager? _server;

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

    [ObservableProperty] private string _cpuUsage = "—";
    [ObservableProperty] private string _ramUsage = "—";

    private double? _totalRamGb;

    // Stateful — needs the previous TotalProcessorTime snapshot to compute %.
    // Initialised lazily in the Windows branch (we don't want to pay the cost on macOS/Linux
    // where we go through ps anyway).
    private CpuPercentSampler? _winCpuSampler;
    private int? _winSampledPid;

    public ServerViewModel() { }

    public ServerViewModel(ServerManager server, PlayerPoller poller, SettingsService settings, ConfigService config)
    {
        _server = server;
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
        server.LogLine += line => App.UiThread(() =>
        {
            if (!string.IsNullOrWhiteSpace(Filter) && !line.Contains(Filter, StringComparison.OrdinalIgnoreCase)) return;
            AppendLine(line);
        });

        poller.Sampled += s => App.UiThread(() =>
        {
            PlayersOnline = s.Count;
            PlayersDetail = s.Error != null
                ? s.Error
                : s.Names.Count == 0 ? "—" : string.Join(", ", s.Names);
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
            // Reset the Windows sampler so the next start doesn't carry a stale delta from
            // the previous run's TotalProcessorTime baseline.
            _winSampledPid = null;
            _winCpuSampler?.Reset();
            App.UiThread(() => { CpuUsage = "—"; RamUsage = "—"; });
            return;
        }
        try
        {
            // Windows: the server runs natively as a single process — no wine, no helpers — so
            // .NET Process APIs give us everything (no ps/footprint). Cross-platform Process.WorkingSet64
            // + dCpu/dWall/cores covers it; ps stays for macOS/Linux where we need the wine subtree.
            if (OperatingSystem.IsWindows())
            {
                SampleWindows(pid);
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

    private void SampleWindows(int pid)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            // First snapshot for this PID, or PID changed since last call — reset the delta.
            if (_winSampledPid != pid)
            {
                _winSampledPid = pid;
                _winCpuSampler = new CpuPercentSampler(Environment.ProcessorCount);
            }
            var cpu = _winCpuSampler!.Sample(proc.TotalProcessorTime, DateTime.UtcNow);
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
    public void ClearLog() => Log = "";

    [RelayCommand]
    public async Task CopyLog() => await Services.Browse.CopyToClipboardAsync(Log);

    private void AppendLine(string line)
    {
        Log += line + Environment.NewLine;
        if (Log.Length > MaxLogChars)
        {
            var cut = Log.IndexOf('\n', Log.Length - MaxLogChars);
            Log = cut > 0 ? Log[(cut + 1)..] : Log[^MaxLogChars..];
        }
    }

    private void UpdateUptime()
    {
        // Count from Ready (world loaded, accepting connections), not from process Start —
        // the loading phase isn't real uptime to the user.
        if (_server?.ReadyAt == null) { Uptime = "—"; return; }
        var t = DateTime.UtcNow - _server.ReadyAt.Value;
        Uptime = $"{(int)t.TotalHours:D2}:{t.Minutes:D2}:{t.Seconds:D2}";
    }
}
