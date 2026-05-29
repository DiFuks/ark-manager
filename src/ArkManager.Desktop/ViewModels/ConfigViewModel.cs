using ArkManager.Core.Models;
using ArkManager.Core.Services;
using ArkManager.Core.Services.Config;
using ArkManager.Core.Services.Firewall;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArkManager.App.ViewModels;

public partial class ConfigViewModel : ViewModelBase
{
    private readonly SettingsService? _settings;
    private readonly ConfigService? _config;
    private readonly IFirewallService? _firewall;

    public IReadOnlyList<MapPreset> KnownMaps { get; } = Maps.Known;

    // BattlEye does not work under wine/proton, so on a non-Windows host the
    // -NoBattlEye flag is mandatory and pinned in the UI.
    public bool IsNoBattlEyeEditable => OperatingSystem.IsWindows();

    // Basic launch settings (mapped into ini + CLI).
    [ObservableProperty] private string _map = "TheIsland_WP";
    [ObservableProperty] private MapPreset? _selectedMapPreset;
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

    // Auto-open Windows Firewall inbound rules on server Start. Persists on toggle.
    [ObservableProperty] private bool _manageFirewallRules;

    // Raw view and editing of ini files.
    [ObservableProperty] private string _gameUserSettingsRaw = "";
    [ObservableProperty] private string _gameIniRaw = "";

    [ObservableProperty] private string _status = "";

    // Active sub-tab (Basic / GameUserSettings.ini / Game.ini / Preview CLI).
    // Determines what the single Save button does.
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

    public bool FirewallIsSupported => _firewall?.IsSupported ?? false;
    public bool FirewallCanModify   => _firewall is { IsSupported: true, IsElevated: true };
    public string FirewallHelpText  =>
        _firewall is null                  ? ""
      : !_firewall.IsSupported             ? ""
      : !_firewall.IsElevated              ? "Run ArkManager as administrator to enable. Without admin rights, Windows Firewall changes are not allowed."
      :                                      "Creates inbound rules for Game (UDP), Query (UDP), and RCON (TCP) ports on each Start. Rules persist after Stop.";

    public ConfigViewModel() { }

    public ConfigViewModel(SettingsService settings, ConfigService config, IFirewallService firewall)
    {
        _settings = settings;
        _config = config;
        _firewall = firewall;
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
        // Mirror into ini (if the folder exists — otherwise it will be applied after the first launch).
        try
        {
            _config.ApplyLaunchOptionsToIni(_settings.Current.LaunchOptions);
            // The form writes [ServerSettings] straight into GameUserSettings.ini — re-read the raw tab
            // so it doesn't show stale text and then overwrite what we just saved.
            if (File.Exists(_config.GameUserSettingsPath))
                GameUserSettingsRaw = File.ReadAllText(_config.GameUserSettingsPath);
        }
        catch { /* server folder not created yet */ }

        Status = "Saved to settings.json";
        OnPropertyChanged(nameof(CommandLinePreview));
    }

    [RelayCommand]
    public void Reload()
    {
        LoadFromSettings();
        LoadIniFiles();
        Status = "Reloaded";
        OnPropertyChanged(nameof(CommandLinePreview));
    }

    private bool CanSaveContext() => SelectedTabIndex is 0 or 1 or 2;

    // Single Save button: the action depends on the active sub-tab.
    // On "Preview CLI" there is nothing to save — the command is disabled via CanSaveContext.
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
            Status = label + " saved";
        }
        catch (Exception ex) { Status = "Error: " + ex.Message; }
    }

    private void LoadFromSettings()
    {
        if (_settings == null) return;
        var o = _settings.Current.LaunchOptions;
        Map = o.Map; SessionName = o.SessionName;
        SelectedMapPreset = Maps.Known.FirstOrDefault(m => m.Map == Map);
        Port = o.Port; QueryPort = o.QueryPort; RconPort = o.RconPort;
        RconEnabled = o.RconEnabled;
        ServerPassword = o.ServerPassword ?? "";
        AdminPassword = o.AdminPassword ?? "";
        SpectatorPassword = o.SpectatorPassword ?? "";
        MaxPlayers = o.MaxPlayers;
        // On non-Windows -NoBattlEye is mandatory — ignore the stored value.
        NoBattlEye = !OperatingSystem.IsWindows() || o.NoBattlEye;
        AutoManagedMods = o.AutoManagedMods;
        ClusterId = o.ClusterId ?? "";
        ClusterDirOverride = o.ClusterDirOverride ?? "";
        ExtraCommandLineArgs = o.ExtraCommandLineArgs;
        ExtraQueryString = o.ExtraQueryString;
        // Effective value: only show checked when admin rights are present. The JSON-stored intent is preserved.
        ManageFirewallRules = FirewallCanModify && _settings.Current.ManageFirewallRules;
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
        // Take the current snapshot from the VM (even before Save) for the preview.
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

    [RelayCommand]
    public async Task BrowseClusterDirAsync()
    {
        var picked = await Services.Browse.PickFolderAsync("Select cluster directory", ClusterDirOverride);
        if (!string.IsNullOrEmpty(picked)) ClusterDirOverride = picked;
    }

    // Auto-save on toggle. Idempotent: skips if values already match (e.g. during LoadFromSettings)
    // or if elevation is missing (we never persist changes the user can't make).
    partial void OnManageFirewallRulesChanged(bool value)
    {
        if (_settings is null) return;
        if (!FirewallCanModify) return;
        if (_settings.Current.ManageFirewallRules == value) return;
        _settings.Update(s => s.ManageFirewallRules = value);
    }

    // ASA appends a bunch of its own keys into GameUserSettings.ini at startup and may
    // overwrite the values we put there. To spare the user from pressing Reload —
    // re-read the active sub-tab from disk whenever it is switched to.
    // Unsaved edits in the current sub-tab are lost on return (the user explicitly asked for this).
    partial void OnSelectedTabIndexChanged(int value)
    {
        if (_config == null) return;
        try
        {
            switch (value)
            {
                case 0: ReloadBasicFromIni(); break;
                case 1:
                    if (File.Exists(_config.GameUserSettingsPath))
                        GameUserSettingsRaw = File.ReadAllText(_config.GameUserSettingsPath);
                    break;
                case 2:
                    if (File.Exists(_config.GamePath))
                        GameIniRaw = File.ReadAllText(_config.GamePath);
                    break;
            }
        }
        catch { /* no access / race — keep the current buffer */ }
    }

    /// <summary>
    /// Called by MainWindowViewModel when the user switches to the Config tab as a whole (not a sub-tab) —
    /// refreshes both raw buffers and Basic from ini in a single pass. Without this, the first visit
    /// to Config after starting the server would show stale values until the user toggled
    /// to another sub-tab and back.
    /// </summary>
    public void RefreshFromDisk()
    {
        if (_config == null) return;
        LoadIniFiles();
        ReloadBasicFromIni();
    }

    // Re-reads the Basic fields from GameUserSettings.ini for keys written by
    // ApplyLaunchOptionsToIni. Non-ini fields (Map, NoBattlEye, AutoManagedMods, ClusterId,
    // ExtraCommandLineArgs/QueryString) aren't touched — they live only in settings.json/VM.
    private void ReloadBasicFromIni()
    {
        if (_config == null || !File.Exists(_config.GameUserSettingsPath)) return;
        var ini = _config.LoadGameUserSettings();

        var server = ini.TryGetSection("ServerSettings");
        if (server != null)
        {
            ServerPassword = server.GetSingle("ServerPassword") ?? ServerPassword;
            AdminPassword = server.GetSingle("ServerAdminPassword") ?? AdminPassword;
            SpectatorPassword = server.GetSingle("SpectatorPassword") ?? SpectatorPassword;
            if (bool.TryParse(server.GetSingle("RCONEnabled"), out var rconEnabled)) RconEnabled = rconEnabled;
            if (int.TryParse(server.GetSingle("RCONPort"), out var rconPort)) RconPort = rconPort;
        }

        var session = ini.TryGetSection("SessionSettings");
        if (session != null)
        {
            SessionName = session.GetSingle("SessionName") ?? SessionName;
            if (int.TryParse(session.GetSingle("Port"), out var port)) Port = port;
            if (int.TryParse(session.GetSingle("QueryPort"), out var queryPort)) QueryPort = queryPort;
        }

        var gameSession = ini.TryGetSection("/Script/Engine.GameSession");
        if (gameSession != null && int.TryParse(gameSession.GetSingle("MaxPlayers"), out var maxPlayers))
            MaxPlayers = maxPlayers;
    }

    partial void OnSelectedMapPresetChanged(MapPreset? value)
    {
        if (value != null) Map = value.Map;
    }

    // Any property change refreshes the preview.
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
