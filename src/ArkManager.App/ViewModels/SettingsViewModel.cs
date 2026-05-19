using ArkManager.Core.Models;
using ArkManager.Core.Services;
using ArkManager.Core.Services.Launchers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArkManager.App.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService? _settings;
    private readonly AppPaths? _paths;

    public IReadOnlyList<LaunchMode> AllLaunchModes { get; } =
        new[] { LaunchMode.Whisky, LaunchMode.LocalWine, LaunchMode.Parallels };

    [ObservableProperty] private LaunchMode _launchMode = LaunchMode.Whisky;
    [ObservableProperty] private string _serverInstallPath = "";
    [ObservableProperty] private string _backupsDirectory = "";
    [ObservableProperty] private int _backupRotationKeep = 10;
    [ObservableProperty] private string _whiskyBottlePath = "";
    [ObservableProperty] private string _wineBinaryPath = "";
    [ObservableProperty] private string _parallelsVmName = "";
    [ObservableProperty] private string _steamCmdPath = "";
    [ObservableProperty] private string _dataDir = "";
    [ObservableProperty] private bool _autoRestartOnCrash;
    [ObservableProperty] private int _autoRestartDelaySeconds = 10;
    [ObservableProperty] private int _scheduledRestartHours;
    [ObservableProperty] private string _status = "";

    public SettingsViewModel() { }

    public SettingsViewModel(SettingsService settings, AppPaths paths)
    {
        _settings = settings;
        _paths = paths;
        var c = settings.Current;
        LaunchMode = c.LaunchMode;
        ServerInstallPath = c.ServerInstallPath ?? "";
        BackupsDirectory = c.BackupsDirectory ?? "";
        BackupRotationKeep = c.BackupRotationKeep;
        WhiskyBottlePath = c.WhiskyBottlePath ?? "";
        WineBinaryPath = c.WineBinaryPath ?? "";
        ParallelsVmName = c.ParallelsVmName ?? "";
        SteamCmdPath = c.SteamCmdPath ?? "";
        DataDir = paths.DataDir;
        AutoRestartOnCrash = c.AutoRestartOnCrash;
        AutoRestartDelaySeconds = c.AutoRestartDelaySeconds;
        ScheduledRestartHours = c.ScheduledRestartHours;
    }

    [RelayCommand]
    public void Save()
    {
        if (_settings == null) return;
        _settings.Update(s =>
        {
            s.LaunchMode = LaunchMode;
            s.ServerInstallPath = NullIfEmpty(ServerInstallPath);
            s.BackupsDirectory = NullIfEmpty(BackupsDirectory);
            s.BackupRotationKeep = BackupRotationKeep;
            s.WhiskyBottlePath = NullIfEmpty(WhiskyBottlePath);
            s.WineBinaryPath = NullIfEmpty(WineBinaryPath);
            s.ParallelsVmName = NullIfEmpty(ParallelsVmName);
            s.SteamCmdPath = NullIfEmpty(SteamCmdPath);
            s.AutoRestartOnCrash = AutoRestartOnCrash;
            s.AutoRestartDelaySeconds = AutoRestartDelaySeconds;
            s.ScheduledRestartHours = ScheduledRestartHours;
        });
        Status = "Сохранено.";
    }

    [RelayCommand]
    public void AutodetectWhiskyBottle()
    {
        foreach (var root in WhiskyLauncher.EnumerateBottleRoots())
        {
            if (!Directory.Exists(root)) continue;
            var first = Directory.EnumerateDirectories(root).FirstOrDefault();
            if (first != null) { WhiskyBottlePath = first; Status = "Найден боттл: " + first; return; }
        }
        Status = "Whisky-боттлы не найдены.";
    }

    [RelayCommand]
    public void OpenDataFolder() => App.OpenInFinder(DataDir);

    [RelayCommand]
    public async Task BrowseServerInstallAsync()
    {
        var p = await Services.Browse.PickFolderAsync("Папка сервера", ServerInstallPath);
        if (!string.IsNullOrEmpty(p)) ServerInstallPath = p;
    }

    [RelayCommand]
    public async Task BrowseBackupsAsync()
    {
        var p = await Services.Browse.PickFolderAsync("Папка для бэкапов", BackupsDirectory);
        if (!string.IsNullOrEmpty(p)) BackupsDirectory = p;
    }

    [RelayCommand]
    public async Task BrowseWhiskyBottleAsync()
    {
        var p = await Services.Browse.PickFolderAsync("Whisky bottle (wineprefix)", WhiskyBottlePath);
        if (!string.IsNullOrEmpty(p)) WhiskyBottlePath = p;
    }

    [RelayCommand]
    public async Task BrowseWineBinaryAsync()
    {
        var p = await Services.Browse.PickFileAsync("wine64 binary", WineBinaryPath);
        if (!string.IsNullOrEmpty(p)) WineBinaryPath = p;
    }

    [RelayCommand]
    public async Task BrowseSteamCmdAsync()
    {
        var p = await Services.Browse.PickFileAsync("steamcmd binary", SteamCmdPath);
        if (!string.IsNullOrEmpty(p)) SteamCmdPath = p;
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
