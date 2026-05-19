using ArkManager.Core.Models;
using ArkManager.Core.Services;
using ArkManager.Core.Services.Config;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArkManager.App.ViewModels;

public partial class ConfigViewModel : ViewModelBase
{
    private readonly SettingsService? _settings;
    private readonly ConfigService? _config;

    // Базовые настройки запуска (мапятся в ini + CLI).
    [ObservableProperty] private string _map = "TheIsland_WP";
    [ObservableProperty] private string _sessionName = "My ASA Server";
    [ObservableProperty] private int _port = 7777;
    [ObservableProperty] private int _queryPort = 27015;
    [ObservableProperty] private int _rconPort = 27020;
    [ObservableProperty] private bool _rconEnabled = true;
    [ObservableProperty] private string _serverPassword = "";
    [ObservableProperty] private string _adminPassword = "";
    [ObservableProperty] private string _spectatorPassword = "";
    [ObservableProperty] private int _maxPlayers = 70;
    [ObservableProperty] private bool _noBattlEye = true;
    [ObservableProperty] private bool _autoManagedMods = true;
    [ObservableProperty] private string _extraCommandLineArgs = "";
    [ObservableProperty] private string _extraQueryString = "";

    // Raw-просмотр и редактирование ini.
    [ObservableProperty] private string _gameUserSettingsRaw = "";
    [ObservableProperty] private string _gameIniRaw = "";

    [ObservableProperty] private string _status = "";

    public string CommandLinePreview => string.Join(" ", Quote(BuildCli()));

    public ConfigViewModel() { }

    public ConfigViewModel(SettingsService settings, ConfigService config)
    {
        _settings = settings;
        _config = config;
        LoadFromSettings();
        LoadIniFiles();
    }

    [RelayCommand]
    public void Save()
    {
        if (_settings == null || _config == null) return;
        _settings.Update(s =>
        {
            var o = s.LaunchOptions;
            o.Map = Map;
            o.SessionName = SessionName;
            o.Port = Port;
            o.QueryPort = QueryPort;
            o.RconPort = RconPort;
            o.RconEnabled = RconEnabled;
            o.ServerPassword = ServerPassword;
            o.AdminPassword = AdminPassword;
            o.SpectatorPassword = SpectatorPassword;
            o.MaxPlayers = MaxPlayers;
            o.NoBattlEye = NoBattlEye;
            o.AutoManagedMods = AutoManagedMods;
            o.ExtraCommandLineArgs = ExtraCommandLineArgs;
            o.ExtraQueryString = ExtraQueryString;
            if (s.Profiles.Count > 0) s.Profiles[0].Options = o;
        });
        // Зеркало в ini (если папка существует — иначе будет применено после первого запуска).
        try { _config.ApplyLaunchOptionsToIni(_settings.Current.LaunchOptions); }
        catch { /* server папка ещё не создана */ }

        Status = "Сохранено в " + (_settings != null ? "settings.json" : "");
        OnPropertyChanged(nameof(CommandLinePreview));
    }

    [RelayCommand]
    public void Reload()
    {
        LoadFromSettings();
        LoadIniFiles();
        Status = "Перечитано";
        OnPropertyChanged(nameof(CommandLinePreview));
    }

    [RelayCommand]
    public void SaveIniFiles()
    {
        if (_config == null) return;
        try
        {
            File.WriteAllText(_config.GameUserSettingsPath, GameUserSettingsRaw);
            File.WriteAllText(_config.GamePath, GameIniRaw);
            Status = "ini-файлы сохранены";
        }
        catch (Exception ex) { Status = "Ошибка: " + ex.Message; }
    }

    private void LoadFromSettings()
    {
        if (_settings == null) return;
        var o = _settings.Current.LaunchOptions;
        Map = o.Map; SessionName = o.SessionName;
        Port = o.Port; QueryPort = o.QueryPort; RconPort = o.RconPort;
        RconEnabled = o.RconEnabled;
        ServerPassword = o.ServerPassword ?? "";
        AdminPassword = o.AdminPassword ?? "";
        SpectatorPassword = o.SpectatorPassword ?? "";
        MaxPlayers = o.MaxPlayers;
        NoBattlEye = o.NoBattlEye;
        AutoManagedMods = o.AutoManagedMods;
        ExtraCommandLineArgs = o.ExtraCommandLineArgs;
        ExtraQueryString = o.ExtraQueryString;
    }

    private void LoadIniFiles()
    {
        if (_config == null) return;
        GameUserSettingsRaw = File.Exists(_config.GameUserSettingsPath)
            ? File.ReadAllText(_config.GameUserSettingsPath) : "";
        GameIniRaw = File.Exists(_config.GamePath)
            ? File.ReadAllText(_config.GamePath) : "";
    }

    private IReadOnlyList<string> BuildCli()
    {
        if (_settings == null) return Array.Empty<string>();
        // Берём текущий снимок из VM (даже до Save) для preview.
        var s = new AppSettings { LaunchOptions = new ServerLaunchOptions
        {
            Map = Map, SessionName = SessionName, Port = Port, QueryPort = QueryPort,
            RconPort = RconPort, RconEnabled = RconEnabled,
            ServerPassword = ServerPassword, AdminPassword = AdminPassword,
            SpectatorPassword = SpectatorPassword, MaxPlayers = MaxPlayers,
            NoBattlEye = NoBattlEye, AutoManagedMods = AutoManagedMods,
            ExtraCommandLineArgs = ExtraCommandLineArgs,
            ExtraQueryString = ExtraQueryString,
        }};
        return ArkManager.Core.Services.Launchers.ServerCommandLine.Build(
            s,
            _settings.Current.Profiles.FirstOrDefault()?.ModIds ?? new List<string>());
    }

    private static IEnumerable<string> Quote(IEnumerable<string> args)
        => args.Select(a => a.Contains(' ') ? "\"" + a + "\"" : a);

    // Любое изменение свойства обновляет preview.
    partial void OnMapChanged(string value) => OnPropertyChanged(nameof(CommandLinePreview));
    partial void OnSessionNameChanged(string value) => OnPropertyChanged(nameof(CommandLinePreview));
    partial void OnPortChanged(int value) => OnPropertyChanged(nameof(CommandLinePreview));
    partial void OnQueryPortChanged(int value) => OnPropertyChanged(nameof(CommandLinePreview));
    partial void OnMaxPlayersChanged(int value) => OnPropertyChanged(nameof(CommandLinePreview));
    partial void OnServerPasswordChanged(string value) => OnPropertyChanged(nameof(CommandLinePreview));
    partial void OnAdminPasswordChanged(string value) => OnPropertyChanged(nameof(CommandLinePreview));
    partial void OnSpectatorPasswordChanged(string value) => OnPropertyChanged(nameof(CommandLinePreview));
    partial void OnRconEnabledChanged(bool value) => OnPropertyChanged(nameof(CommandLinePreview));
    partial void OnRconPortChanged(int value) => OnPropertyChanged(nameof(CommandLinePreview));
    partial void OnNoBattlEyeChanged(bool value) => OnPropertyChanged(nameof(CommandLinePreview));
    partial void OnAutoManagedModsChanged(bool value) => OnPropertyChanged(nameof(CommandLinePreview));
    partial void OnExtraCommandLineArgsChanged(string value) => OnPropertyChanged(nameof(CommandLinePreview));
    partial void OnExtraQueryStringChanged(string value) => OnPropertyChanged(nameof(CommandLinePreview));
}
