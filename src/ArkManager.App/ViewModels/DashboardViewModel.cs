using ArkManager.Core.Models;
using ArkManager.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArkManager.App.ViewModels;

public partial class DashboardViewModel : ViewModelBase
{
    private readonly SettingsService? _settings;
    private readonly ServerManager? _server;

    [ObservableProperty] private string _serverState = "Stopped";
    [ObservableProperty] private string _serverPath = "(не задан)";
    [ObservableProperty] private string _launchMode = "Whisky";
    [ObservableProperty] private string _sessionName = "";
    [ObservableProperty] private int _modCount;

    public DashboardViewModel() { }

    public DashboardViewModel(SettingsService settings, ServerManager server)
    {
        _settings = settings;
        _server = server;
        Refresh();
        server.StateChanged += _ => App.UiThread(Refresh);
        settings.Changed += _ => App.UiThread(Refresh);
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
