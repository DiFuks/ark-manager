namespace ArkManager.Core.Services.Config;

/// <summary>
/// Reads/writes GameUserSettings.ini and Game.ini. Publishes a reactive
/// <see cref="Snapshot"/> of the 8 server-knob fields ArkManager owns;
/// a <see cref="FileSystemWatcher"/> keeps it in sync with external changes.
/// </summary>
public sealed class ConfigService : IDisposable
{
    private readonly SettingsService _settings;
    private readonly IDebounceTimer _reloadTimer;
    private readonly FileSystemWatcher? _watcher;
    private DateTime _suppressUntilUtc = DateTime.MinValue;
    private static readonly TimeSpan DebounceWindow = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan SuppressWindow = TimeSpan.FromMilliseconds(500);

    public ServerConfigSnapshot Snapshot { get; } = new();

    /// <summary>
    /// Raised after the debounce fires on an external file change (i.e. not suppressed by
    /// a self-write). Consumers (e.g. the raw-editor VM) can use this to refresh their buffers.
    /// </summary>
    public event Action? RawFilesChanged;

    public ConfigService(SettingsService settings, IDebounceTimer reloadTimer)
    {
        _settings = settings;
        _reloadTimer = reloadTimer;
        _reloadTimer.Elapsed += OnDebounceElapsed;

        ReloadSnapshotFromDisk();

        try
        {
            Directory.CreateDirectory(ConfigDir);
            _watcher = new FileSystemWatcher(ConfigDir, "*.ini")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                EnableRaisingEvents = true,
            };
            _watcher.Changed += OnFileChanged;
            _watcher.Created += OnFileChanged;
            _watcher.Renamed += OnFileChanged;
        }
        catch
        {
            // ConfigDir may not exist before install — that's fine. EnsureIni or first server
            // start will create it, and the next ConfigService instance picks up the watcher.
            _watcher = null;
        }
    }

    private void OnFileChanged(object? sender, FileSystemEventArgs e)
        => _reloadTimer.Schedule(DebounceWindow);

    private void OnDebounceElapsed()
    {
        if (DateTime.UtcNow < _suppressUntilUtc) return;
        ReloadSnapshotFromDisk();
        RawFilesChanged?.Invoke();
    }

    public string ConfigDir => Path.Combine(
        _settings.Current.ServerInstallPath ?? "",
        "ShooterGame", "Saved", "Config", "WindowsServer");

    public string GameUserSettingsPath => Path.Combine(ConfigDir, "GameUserSettings.ini");
    public string GamePath => Path.Combine(ConfigDir, "Game.ini");

    public bool Exists => File.Exists(GameUserSettingsPath);

    public IniFile LoadGameUserSettings()
        => File.Exists(GameUserSettingsPath) ? IniFile.Load(GameUserSettingsPath) : new IniFile();

    public IniFile LoadGame()
        => File.Exists(GamePath) ? IniFile.Load(GamePath) : new IniFile();

    public void SaveGameUserSettings(IniFile file)
    {
        Directory.CreateDirectory(ConfigDir);
        file.Save(GameUserSettingsPath);
    }

    public void SaveGame(IniFile file)
    {
        Directory.CreateDirectory(ConfigDir);
        file.Save(GamePath);
    }

    public string LoadGameUserSettingsRaw()
        => File.Exists(GameUserSettingsPath) ? File.ReadAllText(GameUserSettingsPath) : "";

    public string LoadGameRaw()
        => File.Exists(GamePath) ? File.ReadAllText(GamePath) : "";

    /// <summary>
    /// Overwrites GameUserSettings.ini with the supplied text, then re-parses to refresh Snapshot.
    /// </summary>
    public void SaveGameUserSettingsRaw(string text)
    {
        _suppressUntilUtc = DateTime.UtcNow + SuppressWindow;
        Directory.CreateDirectory(ConfigDir);
        File.WriteAllText(GameUserSettingsPath, text);
        ReloadSnapshotFromDisk();
    }

    public void SaveGameRaw(string text)
    {
        _suppressUntilUtc = DateTime.UtcNow + SuppressWindow;
        Directory.CreateDirectory(ConfigDir);
        File.WriteAllText(GamePath, text);
    }

    /// <summary>
    /// Mutates the 8 ArkManager-owned keys in GameUserSettings.ini and updates Snapshot
    /// in lockstep. Existing unrelated keys (ASA's ~100 defaults, custom edits) are preserved.
    /// </summary>
    public void UpdateBasic(Action<MutableBasic> mutate)
    {
        _suppressUntilUtc = DateTime.UtcNow + SuppressWindow;
        var draft = MutableBasic.FromSnapshot(Snapshot);
        mutate(draft);

        var ini = LoadGameUserSettings();
        var server = ini.GetOrCreateSection("ServerSettings");
        server.SetSingle("ServerPassword", draft.ServerPassword);
        server.SetSingle("ServerAdminPassword", draft.AdminPassword);
        server.SetSingle("SpectatorPassword", draft.SpectatorPassword);
        server.SetSingle("RCONEnabled", draft.RconEnabled ? "True" : "False");
        server.SetSingle("RCONPort", draft.RconPort.ToString());

        var session = ini.GetOrCreateSection("SessionSettings");
        session.SetSingle("SessionName", draft.SessionName);
        session.SetSingle("Port", draft.Port.ToString());
        session.SetSingle("QueryPort", draft.QueryPort.ToString());

        SaveGameUserSettings(ini);

        // Sync Snapshot — INPC raises only for fields that actually changed (ObservableObject behavior).
        Snapshot.SessionName = draft.SessionName;
        Snapshot.Port = draft.Port;
        Snapshot.QueryPort = draft.QueryPort;
        Snapshot.RconPort = draft.RconPort;
        Snapshot.RconEnabled = draft.RconEnabled;
        Snapshot.ServerPassword = draft.ServerPassword;
        Snapshot.AdminPassword = draft.AdminPassword;
        Snapshot.SpectatorPassword = draft.SpectatorPassword;
    }

    /// <summary>
    /// Idempotent: writes a default minimal ini if none exists. Used by InstallViewModel
    /// after a successful steamcmd run, by App startup as a safety net for installs that
    /// pre-date this method, and by ServerManager.StartAsync as a last-resort guarantee.
    /// </summary>
    public void EnsureIni()
    {
        if (File.Exists(GameUserSettingsPath)) return;
        UpdateBasic(_ => { /* defaults already set by MutableBasic.FromSnapshot */ });
    }

    public void WriteActiveMods(IReadOnlyList<string> modIds)
    {
        var ini = LoadGameUserSettings();
        var section = ini.GetOrCreateSection("ModInstaller");
        section.SetSingle("ActiveMods", string.Join(",", modIds));
        SaveGameUserSettings(ini);
    }

    /// <summary>
    /// Parses GameUserSettings.ini into <see cref="Snapshot"/>, raising
    /// PropertyChanged only for fields whose value actually changed.
    /// When the file is absent, Snapshot keeps its current values (defaults on first load).
    /// </summary>
    private void ReloadSnapshotFromDisk()
    {
        if (!File.Exists(GameUserSettingsPath)) return;

        var ini = LoadGameUserSettings();
        var server = ini.TryGetSection("ServerSettings");
        var session = ini.TryGetSection("SessionSettings");

        if (server != null)
        {
            if (server.GetSingle("ServerPassword") is { } sp) Snapshot.ServerPassword = sp;
            if (server.GetSingle("ServerAdminPassword") is { } ap) Snapshot.AdminPassword = ap;
            if (server.GetSingle("SpectatorPassword") is { } spec) Snapshot.SpectatorPassword = spec;
            if (bool.TryParse(server.GetSingle("RCONEnabled"), out var re)) Snapshot.RconEnabled = re;
            if (int.TryParse(server.GetSingle("RCONPort"), out var rp)) Snapshot.RconPort = rp;
        }
        if (session != null)
        {
            if (session.GetSingle("SessionName") is { } sn) Snapshot.SessionName = sn;
            if (int.TryParse(session.GetSingle("Port"), out var p)) Snapshot.Port = p;
            if (int.TryParse(session.GetSingle("QueryPort"), out var qp)) Snapshot.QueryPort = qp;
        }
    }

    public void Dispose()
    {
        // Order matters: unsubscribe handlers and disable the watcher BEFORE disposing the timer,
        // so any FS event in flight stops scheduling new ticks. Then unsubscribe the timer's
        // Elapsed event so any final callback (Timer.Dispose() doesn't wait for in-flight ticks)
        // can't fire into a half-torn-down service.
        if (_watcher != null)
        {
            _watcher.Changed -= OnFileChanged;
            _watcher.Created -= OnFileChanged;
            _watcher.Renamed -= OnFileChanged;
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
        }
        _reloadTimer.Elapsed -= OnDebounceElapsed;
        if (_reloadTimer is IDisposable d) d.Dispose();
    }
}
