using System.Collections.ObjectModel;
using ArkManager.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArkManager.App.ViewModels;

public partial class ServerViewModel : ViewModelBase
{
    private readonly ServerManager? _server;

    public ObservableCollection<string> Log { get; } = new();
    [ObservableProperty] private string _state = "Stopped";
    [ObservableProperty] private int? _pid;
    [ObservableProperty] private string _uptime = "—";
    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private bool _autoScroll = true;

    public ServerViewModel() { }

    public ServerViewModel(ServerManager server)
    {
        _server = server;
        foreach (var l in server.Snapshot()) Log.Add(l);
        server.StateChanged += s => App.UiThread(() => { State = s.ToString(); Pid = server.Pid; });
        server.LogLine += line => App.UiThread(() =>
        {
            if (!string.IsNullOrWhiteSpace(Filter) && !line.Contains(Filter, StringComparison.OrdinalIgnoreCase)) return;
            Log.Add(line);
            while (Log.Count > 5000) Log.RemoveAt(0);
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

    [RelayCommand]
    public async Task StartAsync()
    {
        if (_server == null) return;
        try { await _server.StartAsync(); }
        catch (Exception ex) { Log.Add("[start failed] " + ex.Message); }
    }

    [RelayCommand]
    public async Task StopAsync()
    {
        if (_server == null) return;
        try { await _server.StopAsync(); }
        catch (Exception ex) { Log.Add("[stop failed] " + ex.Message); }
    }

    [RelayCommand]
    public void ClearLog() => Log.Clear();

    private void UpdateUptime()
    {
        if (_server?.StartedAt == null) { Uptime = "—"; return; }
        var t = DateTime.UtcNow - _server.StartedAt.Value;
        Uptime = $"{(int)t.TotalHours:D2}:{t.Minutes:D2}:{t.Seconds:D2}";
    }
}
