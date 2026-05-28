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

    // Сервер закончил загрузку мира и принимает подключения (ARK-аналог зелёного кружка).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusBrush))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private bool _ready;

    // Кружок статуса как в окне ARK: серый stopped → жёлтый загрузка → зелёный готов → красный краш.
    // Цвета берём из дизайн-токенов (Themes/Tokens.axaml), а не из Brushes.*, чтобы оттенок
    // совпадал с остальным OK/Warn/Danger в UI.
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

    // Пока процесс жив, но мир ещё грузится — честнее показать «Loading…», а не «Running».
    public string StatusText => State == "Running" && !Ready ? "Loading…" : State;

    [ObservableProperty] private int? _pid;
    [ObservableProperty] private string _uptime = "—";
    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private bool _autoScroll = true;

    // PlayersOnline-как-число валидно только когда сервер реально работает: «0 онлайн» — это
    // конкретная информация, «0» при остановленном сервере — мусор. PlayersDisplay скрывает
    // ноль за em-dash в любом состоянии кроме Running. PlayersDetail заодно скрывается тоже
    // (был случай: остановили сервер с 5 игроками — стейл-имена продолжали висеть под нулём).
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
        // Инициализируемся из текущего состояния — на случай, что сервер уже идёт
        // (усыновлён при старте, либо таб открыт во время работы).
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
                if (tick++ % 2 == 0) await SampleResourcesAsync(); // CPU/RAM раз в ~2с
            }
        });
    }

    /// <summary>
    /// CPU/RAM сервера = сумма по всему дереву процессов от PID (под wine это exe + wineserver + хелперы).
    /// CPU нормализуем на число ядер → «процент загрузки ЦП» 0..100. RAM — % от физической + ГБ.
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

            // CPU — сумма %cpu по дереву процессов (ps; %cpu с точкой благодаря LC_ALL=C).
            var ps = await ProcessRunner.RunCaptureAsync("/bin/ps",
                new[] { "-axo", "pid=,ppid=,rss=,%cpu=" }, env: lcC);
            var st = ProcessTreeStats.Sum(ps.StdOut, pid);
            var cpu = st.CpuPercent / Math.Max(1, Environment.ProcessorCount);

            // RAM — phys_footprint (как Activity Monitor): под wine RSS на порядок занижен,
            // т.к. macOS компрессит память. Фолбэк на RSS, если footprint недоступен.
            long memBytes = st.RssKb * 1024;
            try
            {
                var fp = await ProcessRunner.RunCaptureAsync("/usr/bin/footprint",
                    new[] { pid.ToString() }, env: lcC);
                if (MacMemory.PhysFootprintBytes(fp.StdOut) is long b && b > 0) memBytes = b;
            }
            catch { /* footprint недоступен — остаётся RSS-фолбэк */ }

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
        catch { /* ps недоступен — не критично */ }
    }

    private static async Task<double?> ReadTotalRamGbAsync()
    {
        try
        {
            var r = await ProcessRunner.RunCaptureAsync("/usr/sbin/sysctl", new[] { "-n", "hw.memsize" });
            if (long.TryParse(r.StdOut.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytes))
                return bytes / 1024.0 / 1024.0 / 1024.0;
        }
        catch { /* не macOS / нет sysctl — покажем только ГБ */ }
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
