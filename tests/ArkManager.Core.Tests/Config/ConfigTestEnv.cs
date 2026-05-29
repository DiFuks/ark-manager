using ArkManager.Core.Models;
using ArkManager.Core.Services;
using ArkManager.Core.Services.Config;

namespace ArkManager.Core.Tests.Config;

/// <summary>
/// Disposable temp-dir harness for ConfigService tests. Creates the
/// ShooterGame/Saved/Config/WindowsServer/ tree, a SettingsService pointing
/// at a temp settings.json, and exposes the paths used by individual tests.
/// </summary>
internal sealed class ConfigTestEnv : IDisposable
{
    public string Root { get; }
    public string ServerInstallPath { get; }
    public string ConfigDir { get; }
    public string GameUserSettingsPath { get; }
    public string GamePath { get; }
    public SettingsService Settings { get; }

    public ConfigTestEnv()
    {
        Root = Path.Combine(Path.GetTempPath(), "ark-manager-tests-" + Guid.NewGuid().ToString("N"));
        ServerInstallPath = Path.Combine(Root, "server");
        ConfigDir = Path.Combine(ServerInstallPath, "ShooterGame", "Saved", "Config", "WindowsServer");
        Directory.CreateDirectory(ConfigDir);
        GameUserSettingsPath = Path.Combine(ConfigDir, "GameUserSettings.ini");
        GamePath = Path.Combine(ConfigDir, "Game.ini");

        var paths = new AppPaths(Root);
        Settings = new SettingsService(paths);
        Settings.Update(s => s.ServerInstallPath = ServerInstallPath);
    }

    public void WriteIni(string content) => File.WriteAllText(GameUserSettingsPath, content);
    public string ReadIni() => File.ReadAllText(GameUserSettingsPath);

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch { /* ignore */ }
    }
}
