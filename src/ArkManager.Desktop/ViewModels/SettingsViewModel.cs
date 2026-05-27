using ArkManager.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArkManager.App.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService? _settings;
    private readonly AppPaths? _paths;

    [ObservableProperty] private string _serverInstallPath = "";
    [ObservableProperty] private string _backupsDirectory = "";
    [ObservableProperty] private int _backupRotationKeep = 10;
    [ObservableProperty] private string _wineBinaryPath = "";
    [ObservableProperty] private string _winePrefixPath = "";
    [ObservableProperty] private string _steamCmdPath = "";
    [ObservableProperty] private string _dataDir = "";
    [ObservableProperty] private bool _autoRestartOnCrash;
    [ObservableProperty] private int _autoRestartDelaySeconds = 10;
    [ObservableProperty] private int _scheduledRestartHours;
    [ObservableProperty] private int _autoBackupIntervalMinutes;
    [ObservableProperty] private bool _autoBackupOnlyWhenRunning = true;
    [ObservableProperty] private string _curseForgeApiKey = "";
    [ObservableProperty] private string _status = "";

    public SettingsViewModel() { }

    public SettingsViewModel(SettingsService settings, AppPaths paths)
    {
        _settings = settings;
        _paths = paths;
        var c = settings.Current;
        ServerInstallPath = c.ServerInstallPath ?? "";
        BackupsDirectory = c.BackupsDirectory ?? "";
        BackupRotationKeep = c.BackupRotationKeep;
        WineBinaryPath = c.WineBinaryPath ?? "";
        WinePrefixPath = c.WinePrefixPath ?? "";
        SteamCmdPath = c.SteamCmdPath ?? "";
        DataDir = paths.DataDir;
        AutoRestartOnCrash = c.AutoRestartOnCrash;
        AutoRestartDelaySeconds = c.AutoRestartDelaySeconds;
        ScheduledRestartHours = c.ScheduledRestartHours;
        AutoBackupIntervalMinutes = c.AutoBackupIntervalMinutes;
        AutoBackupOnlyWhenRunning = c.AutoBackupOnlyWhenRunning;
        CurseForgeApiKey = c.CurseForgeApiKey ?? "";
    }

    [RelayCommand]
    public void Save()
    {
        if (_settings == null) return;
        _settings.Update(s =>
        {
            s.ServerInstallPath = NullIfEmpty(ServerInstallPath);
            s.BackupsDirectory = NullIfEmpty(BackupsDirectory);
            s.BackupRotationKeep = BackupRotationKeep;
            s.WineBinaryPath = NullIfEmpty(WineBinaryPath);
            s.WinePrefixPath = NullIfEmpty(WinePrefixPath);
            s.SteamCmdPath = NullIfEmpty(SteamCmdPath);
            s.AutoRestartOnCrash = AutoRestartOnCrash;
            s.AutoRestartDelaySeconds = AutoRestartDelaySeconds;
            s.ScheduledRestartHours = ScheduledRestartHours;
            s.AutoBackupIntervalMinutes = AutoBackupIntervalMinutes;
            s.AutoBackupOnlyWhenRunning = AutoBackupOnlyWhenRunning;
            s.CurseForgeApiKey = NullIfEmpty(CurseForgeApiKey);
        });
        Status = "Saved.";
    }

    [RelayCommand]
    public void OpenDataFolder() => App.OpenInFinder(DataDir);

    [RelayCommand]
    public async Task BrowseServerInstallAsync()
    {
        var p = await Services.Browse.PickFolderAsync("Server folder", ServerInstallPath);
        if (!string.IsNullOrEmpty(p)) ServerInstallPath = p;
    }

    [RelayCommand]
    public async Task BrowseBackupsAsync()
    {
        var p = await Services.Browse.PickFolderAsync("Backups folder", BackupsDirectory);
        if (!string.IsNullOrEmpty(p)) BackupsDirectory = p;
    }

    [RelayCommand]
    public async Task BrowseWineBinaryAsync()
    {
        var p = await Services.Browse.PickFileAsync("wine64 binary", WineBinaryPath);
        if (!string.IsNullOrEmpty(p)) WineBinaryPath = p;
    }

    [RelayCommand]
    public async Task BrowseWinePrefixAsync()
    {
        var p = await Services.Browse.PickFolderAsync("WINEPREFIX", WinePrefixPath);
        if (!string.IsNullOrEmpty(p)) WinePrefixPath = p;
    }

    [RelayCommand]
    public async Task BrowseSteamCmdAsync()
    {
        var p = await Services.Browse.PickFileAsync("steamcmd binary", SteamCmdPath);
        if (!string.IsNullOrEmpty(p)) SteamCmdPath = p;
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
