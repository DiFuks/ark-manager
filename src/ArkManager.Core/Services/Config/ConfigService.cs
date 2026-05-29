using ArkManager.Core.Models;

namespace ArkManager.Core.Services.Config;

/// <summary>
/// Reads/writes GameUserSettings.ini and Game.ini. Publishes a reactive
/// <see cref="Snapshot"/> of the 8 server-knob fields ArkManager owns;
/// FileSystemWatcher (added later) keeps it in sync with external changes.
/// </summary>
public sealed class ConfigService : IDisposable
{
    private readonly SettingsService _settings;
    private readonly IDebounceTimer _reloadTimer;

    public ServerConfigSnapshot Snapshot { get; } = new();

    public ConfigService(SettingsService settings, IDebounceTimer reloadTimer)
    {
        _settings = settings;
        _reloadTimer = reloadTimer;
        ReloadSnapshotFromDisk();
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

    /// <summary>
    /// Applies the main settings from ServerLaunchOptions into [ServerSettings] and [SessionSettings].
    /// (Legacy; kept until consumer migration is complete. Will be removed in a later task.)
    /// </summary>
    public void ApplyLaunchOptionsToIni(ServerLaunchOptions o)
    {
        var ini = LoadGameUserSettings();
        var server = ini.GetOrCreateSection("ServerSettings");
        server.SetSingle("ServerPassword", o.ServerPassword ?? "");
        server.SetSingle("ServerAdminPassword", o.AdminPassword ?? "");
        server.SetSingle("SpectatorPassword", o.SpectatorPassword ?? "");
        server.SetSingle("RCONEnabled", o.RconEnabled ? "True" : "False");
        server.SetSingle("RCONPort", o.RconPort.ToString());

        var session = ini.GetOrCreateSection("SessionSettings");
        session.SetSingle("SessionName", o.SessionName);
        session.SetSingle("Port", o.Port.ToString());
        session.SetSingle("QueryPort", o.QueryPort.ToString());

        var general = ini.GetOrCreateSection("/Script/Engine.GameSession");
        general.SetSingle("MaxPlayers", o.MaxPlayers.ToString());

        SaveGameUserSettings(ini);
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

    public void Dispose() { /* FileSystemWatcher added in a later task */ }
}
