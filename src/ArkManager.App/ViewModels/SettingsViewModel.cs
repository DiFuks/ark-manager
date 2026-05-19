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

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
