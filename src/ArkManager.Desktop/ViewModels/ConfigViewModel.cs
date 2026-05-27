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

    public IReadOnlyList<MapPreset> KnownMaps { get; } = Maps.Known;

    [ObservableProperty] private MapPreset? _selectedMap;

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
    [ObservableProperty] private string _clusterId = "";
    [ObservableProperty] private string _clusterDirOverride = "";
    [ObservableProperty] private string _extraCommandLineArgs = "";
    [ObservableProperty] private string _extraQueryString = "";

    // Raw-просмотр и редактирование ini.
    [ObservableProperty] private string _gameUserSettingsRaw = "";
    [ObservableProperty] private string _gameIniRaw = "";

    [ObservableProperty] private string _status = "";

    // Активный под-таб (Основное / GameUserSettings.ini / Game.ini / Preview CLI).
    // От него зависит, что делает единственная кнопка Save.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveContextCommand))]
    [NotifyPropertyChangedFor(nameof(SaveButtonText))]
    private int _selectedTabIndex;

    public string SaveButtonText => SelectedTabIndex switch
    {
        0 => "Save settings",
        1 => "Save GameUserSettings.ini",
        2 => "Save Game.ini",
        _ => "Save"
    };

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
            o.ClusterId = string.IsNullOrWhiteSpace(ClusterId) ? null : ClusterId;
            o.ClusterDirOverride = string.IsNullOrWhiteSpace(ClusterDirOverride) ? null : ClusterDirOverride;
            o.ExtraCommandLineArgs = ExtraCommandLineArgs;
            o.ExtraQueryString = ExtraQueryString;
            if (s.Profiles.Count > 0) s.Profiles[0].Options = o;
        });
        // Зеркало в ini (если папка существует — иначе будет применено после первого запуска).
        try
        {
            _config.ApplyLaunchOptionsToIni(_settings.Current.LaunchOptions);
            // Форма пишет [ServerSettings] прямо в GameUserSettings.ini — перечитываем raw-таб,
            // чтобы он не показывал устаревший текст и потом не затёр только что записанное.
            if (File.Exists(_config.GameUserSettingsPath))
                GameUserSettingsRaw = File.ReadAllText(_config.GameUserSettingsPath);
        }
        catch { /* server папка ещё не создана */ }

        Status = "Сохранено в settings.json";
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

    private bool CanSaveContext() => SelectedTabIndex is 0 or 1 or 2;

    // Единственная кнопка Save: действие зависит от активного под-таба.
    // На «Preview CLI» сохранять нечего — команда дизейблится через CanSaveContext.
    [RelayCommand(CanExecute = nameof(CanSaveContext))]
    public void SaveContext()
    {
        switch (SelectedTabIndex)
        {
            case 0: Save(); break;
            case 1: SaveRawIni(_config?.GameUserSettingsPath, GameUserSettingsRaw, "GameUserSettings.ini"); break;
            case 2: SaveRawIni(_config?.GamePath, GameIniRaw, "Game.ini"); break;
        }
    }

    private void SaveRawIni(string? path, string content, string label)
    {
        if (path == null) return;
        try
        {
            File.WriteAllText(path, content);
            Status = label + " сохранён";
        }
        catch (Exception ex) { Status = "Ошибка: " + ex.Message; }
    }

    partial void OnSelectedMapChanged(MapPreset? value)
    {
        if (value != null && Map != value.Map) Map = value.Map;
    }

    private void LoadFromSettings()
    {
        if (_settings == null) return;
        var o = _settings.Current.LaunchOptions;
        Map = o.Map; SessionName = o.SessionName;
        SelectedMap = KnownMaps.FirstOrDefault(m => m.Map.Equals(o.Map, StringComparison.OrdinalIgnoreCase));
        Port = o.Port; QueryPort = o.QueryPort; RconPort = o.RconPort;
        RconEnabled = o.RconEnabled;
        ServerPassword = o.ServerPassword ?? "";
        AdminPassword = o.AdminPassword ?? "";
        SpectatorPassword = o.SpectatorPassword ?? "";
        MaxPlayers = o.MaxPlayers;
        NoBattlEye = o.NoBattlEye;
        AutoManagedMods = o.AutoManagedMods;
        ClusterId = o.ClusterId ?? "";
        ClusterDirOverride = o.ClusterDirOverride ?? "";
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
            ClusterId = string.IsNullOrWhiteSpace(ClusterId) ? null : ClusterId,
            ClusterDirOverride = string.IsNullOrWhiteSpace(ClusterDirOverride) ? null : ClusterDirOverride,
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
