using ArkManager.Core.Services;
using ArkManager.Core.Services.Rcon;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArkManager.App.ViewModels;

public partial class ServerViewModel : ViewModelBase
{
    private const int MaxLogChars = 500_000;
    private readonly ServerManager? _server;

    [ObservableProperty] private string _log = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private string _state = "Stopped";

    [ObservableProperty] private int? _pid;
    [ObservableProperty] private string _uptime = "—";
    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private bool _autoScroll = true;

    [ObservableProperty] private int _playersOnline;
    [ObservableProperty] private string _playersDetail = "—";
    [ObservableProperty] private string _lastSample = "—";

    public ServerViewModel() { }

    public ServerViewModel(ServerManager server, PlayerPoller poller)
    {
        _server = server;
        foreach (var l in server.Snapshot()) AppendLine(l);
        server.StateChanged += s => App.UiThread(() => { State = s.ToString(); Pid = server.Pid; });
        server.LogLine += line => App.UiThread(() =>
        {
            if (!string.IsNullOrWhiteSpace(Filter) && !line.Contains(Filter, StringComparison.OrdinalIgnoreCase)) return;
            AppendLine(line);
        });

        poller.Sampled += s => App.UiThread(() =>
        {
            PlayersOnline = s.Count;
            PlayersDetail = s.Error != null
                ? "❗ " + s.Error
                : s.Names.Count == 0 ? "—" : string.Join(", ", s.Names);
            LastSample = s.SampledUtc.ToLocalTime().ToString("HH:mm:ss");
        });

        _ = Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(1000);
                App.UiThread(UpdateUptime);
            }
        });
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
