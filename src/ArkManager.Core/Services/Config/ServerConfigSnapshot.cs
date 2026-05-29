using CommunityToolkit.Mvvm.ComponentModel;

namespace ArkManager.Core.Services.Config;

/// <summary>
/// Reactive view of the 8 fields ArkManager owns inside GameUserSettings.ini.
/// Only ConfigService mutates this; consumers bind read-only via INotifyPropertyChanged.
/// </summary>
public sealed partial class ServerConfigSnapshot : ObservableObject
{
    [ObservableProperty] private string _sessionName = "My ASA Server";
    [ObservableProperty] private int _port = 7777;
    [ObservableProperty] private int _queryPort = 27015;
    [ObservableProperty] private int _rconPort = 27020;
    [ObservableProperty] private bool _rconEnabled = true;
    [ObservableProperty] private string _serverPassword = "";
    [ObservableProperty] private string _adminPassword = "";
    [ObservableProperty] private string _spectatorPassword = "";
}
