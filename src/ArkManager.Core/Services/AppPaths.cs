using System.Runtime.InteropServices;

namespace ArkManager.Core.Services;

/// <summary>
/// Все пути приложения. По правилам пользователя: user-wide state живёт в одном vendor-каталоге.
/// macOS: ~/Library/Application Support/ArkManager. Linux: $XDG_DATA_HOME/ArkManager.
/// Windows: %LOCALAPPDATA%/ArkManager (а не Roaming — у нас 25GB ASA-сервера).
/// </summary>
public sealed class AppPaths
{
    public string DataDir { get; }
    public string LogsDir { get; }
    public string SettingsFile { get; }
    public string SteamCmdDir { get; }
    public string DefaultBackupsDir { get; }
    public string DefaultServerInstallDir { get; }
    public string ServerRuntimeDir { get; }

    public AppPaths()
    {
        DataDir = ResolveDataDir();
        LogsDir = Path.Combine(DataDir, "logs");
        SettingsFile = Path.Combine(DataDir, "settings.json");
        SteamCmdDir = Path.Combine(DataDir, "steamcmd");
        DefaultBackupsDir = Path.Combine(DataDir, "backups");
        DefaultServerInstallDir = Path.Combine(DataDir, "server");
        ServerRuntimeDir = Path.Combine(DataDir, "server-runtime");

        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(SteamCmdDir);
        Directory.CreateDirectory(DefaultBackupsDir);

        // Legacy cleanup: предыдущие версии держали wineprefix тут.
        // Embedded wine — это «server runtime»; имени wine в UI больше нет.
        var legacy = Path.Combine(DataDir, "wineprefix");
        if (Directory.Exists(legacy))
        {
            try { Directory.Delete(legacy, recursive: true); } catch { /* ignore */ }
        }
    }

    private static string ResolveDataDir()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Application Support", "ArkManager");
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (string.IsNullOrEmpty(xdg))
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                xdg = Path.Combine(home, ".local", "share");
            }
            return Path.Combine(xdg, "ArkManager");
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArkManager");
    }
}
