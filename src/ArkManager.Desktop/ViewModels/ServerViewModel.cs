using System.Globalization;
using ArkManager.Core.Services;
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

    // PlayersOnline-as-a-number is only meaningful while the server is actually running: "0 online"
    // is concrete info, but "0" with the server stopped is noise. PlayersDisplay hides the zero
    // behind an em-dash in any state other than Running. PlayersDetail is hidden too
    // (we hit a case: server stopped with 5 players — stale names kept hanging under the zero).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayersDisplay))]
    private int _playersOnline;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlayersDetail))]
    private string _playersDetail = "—";

    public string PlayersDisplay => State == "Running" ? PlayersOnline.ToString() : "—";

    public bool HasPlayersDetail =>
        State == "Running"
        && !string.IsNullOrWhiteSpace(PlayersDetail)
        && PlayersDetail != "—";

    [ObservableProperty] private string _cpuUsage = "—";
    [ObservableProperty] private string _ramUsage = "—";

    private double? _totalRamGb;

    public ServerViewModel() { }

    public ServerViewModel(ServerManager server, PlayerPoller poller, SettingsService settings)
    {
        _server = server;
        var s = settings.Current.LaunchOptions;
        Identity = $"{s.SessionName} · {s.Map}";
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
            App.UiThread(() => { CpuUsage = "—"; RamUsage = "—"; });
            return;
        }
        try
        {
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
        if (_server?.StartedAt == null) { Uptime = "—"; return; }
        var t = DateTime.UtcNow - _server.StartedAt.Value;
        Uptime = $"{(int)t.TotalHours:D2}:{t.Minutes:D2}:{t.Seconds:D2}";
    }
}
