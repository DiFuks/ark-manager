using ArkManager.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArkManager.App.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService? _settings;
    private readonly AppPaths? _paths;

    [ObservableProperty] private string _wineBinaryPath = "";
    [ObservableProperty] private string _winePrefixPath = "";
    [ObservableProperty] private string _dataDir = "";
    [ObservableProperty] private bool _autoRestartOnCrash;
    [ObservableProperty] private int _autoRestartDelaySeconds = 10;
    [ObservableProperty] private int _scheduledRestartHours;
    [ObservableProperty] private int _autoBackupIntervalMinutes;
    [ObservableProperty] private bool _autoBackupOnlyWhenRunning = true;
    [ObservableProperty] private string _status = "";

    public SettingsViewModel() { }

    public SettingsViewModel(SettingsService settings, AppPaths paths)
    {
        _settings = settings;
        _paths = paths;
        var c = settings.Current;
        WineBinaryPath = c.WineBinaryPath ?? "";
        WinePrefixPath = c.WinePrefixPath ?? "";
        DataDir = paths.DataDir;
        AutoRestartOnCrash = c.AutoRestartOnCrash;
        AutoRestartDelaySeconds = c.AutoRestartDelaySeconds;
        ScheduledRestartHours = c.ScheduledRestartHours;
        AutoBackupIntervalMinutes = c.AutoBackupIntervalMinutes;
        AutoBackupOnlyWhenRunning = c.AutoBackupOnlyWhenRunning;
    }

    [RelayCommand]
    public void Save()
    {
        if (_settings == null) return;
        _settings.Update(s =>
        {
            s.WineBinaryPath = NullIfEmpty(WineBinaryPath);
            s.WinePrefixPath = NullIfEmpty(WinePrefixPath);
            s.AutoRestartOnCrash = AutoRestartOnCrash;
            s.AutoRestartDelaySeconds = AutoRestartDelaySeconds;
            s.ScheduledRestartHours = ScheduledRestartHours;
            s.AutoBackupIntervalMinutes = AutoBackupIntervalMinutes;
            s.AutoBackupOnlyWhenRunning = AutoBackupOnlyWhenRunning;
        });
        Status = "Saved.";
    }

    [RelayCommand]
    public void OpenDataFolder() => App.OpenInFinder(DataDir);

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

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
