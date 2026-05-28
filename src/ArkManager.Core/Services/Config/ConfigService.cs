using ArkManager.Core.Models;

namespace ArkManager.Core.Services.Config;

/// <summary>
/// Reads/writes GameUserSettings.ini and Game.ini.
/// Folder: <ServerInstallPath>/ShooterGame/Saved/Config/WindowsServer/.
/// </summary>
public sealed class ConfigService
{
    private readonly SettingsService _settings;

    public ConfigService(SettingsService settings) => _settings = settings;

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
    /// Applies the main settings from ServerLaunchOptions into [ServerSettings] and [SessionSettings] of GameUserSettings.ini.
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

    /// <summary>Rewrites the ActiveMods= list in [ModInstaller] (new ASA format via automanagedmods).</summary>
    public void WriteActiveMods(IReadOnlyList<string> modIds)
    {
        var ini = LoadGameUserSettings();
        var section = ini.GetOrCreateSection("ModInstaller");
        section.SetSingle("ActiveMods", string.Join(",", modIds));
        SaveGameUserSettings(ini);
    }
}
