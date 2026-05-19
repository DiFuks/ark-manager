using ArkManager.Core.Models;
using ArkManager.Core.Services;
using ArkManager.Core.Services.Rcon;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArkManager.App.ViewModels;

public partial class DashboardViewModel : ViewModelBase
{
    private readonly SettingsService? _settings;
    private readonly ServerManager? _server;
    private readonly PlayerPoller? _poller;

    [ObservableProperty] private string _serverState = "Stopped";
    [ObservableProperty] private string _serverPath = "(не задан)";
    [ObservableProperty] private string _launchMode = "Whisky";
    [ObservableProperty] private string _sessionName = "";
    [ObservableProperty] private int _modCount;
    [ObservableProperty] private int _playersOnline;
    [ObservableProperty] private string _playersDetail = "—";
    [ObservableProperty] private string _lastSample = "—";

    public DashboardViewModel() { }

    public DashboardViewModel(SettingsService settings, ServerManager server, PlayerPoller poller)
    {
        _settings = settings;
        _server = server;
        _poller = poller;
        Refresh();
        server.StateChanged += _ => App.UiThread(Refresh);
        settings.Changed += _ => App.UiThread(Refresh);
        poller.Sampled += s => App.UiThread(() =>
        {
            PlayersOnline = s.Count;
            PlayersDetail = s.Error != null
                ? "❗ " + s.Error
                : s.Names.Count == 0 ? "—" : string.Join(", ", s.Names);
            LastSample = s.SampledUtc.ToLocalTime().ToString("HH:mm:ss");
        });
    }

    [RelayCommand]
    public void Refresh()
    {
        if (_settings == null || _server == null) return;
        ServerState = _server.State.ToString();
        ServerPath = _settings.Current.ServerInstallPath ?? "(не задан)";
        LaunchMode = _settings.Current.LaunchMode.ToString();
        SessionName = _settings.Current.LaunchOptions.SessionName;
        ModCount = _settings.Current.Profiles.FirstOrDefault()?.ModIds.Count ?? 0;
    }

    [RelayCommand]
    public async Task StartAsync()
    {
        if (_server == null) return;
        await _server.StartAsync();
    }

    [RelayCommand]
    public async Task StopAsync()
    {
        if (_server == null) return;
        await _server.StopAsync();
    }
}
