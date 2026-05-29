using System.Runtime.InteropServices;

namespace ArkManager.Core.Services;

/// <summary>
/// All application paths. Per user rules: user-wide state lives in a single vendor directory.
/// macOS: ~/Library/Application Support/ArkManager. Linux: $XDG_DATA_HOME/ArkManager.
/// Windows: %LOCALAPPDATA%/ArkManager (not Roaming — we have a 25GB ASA server).
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

    public AppPaths(string? dataDirOverride = null)
    {
        DataDir = dataDirOverride ?? ResolveDataDir();
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

        // Legacy cleanup: previous versions kept the wineprefix here.
        // Embedded wine is now the "server runtime"; the wine name is no longer in the UI.
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
