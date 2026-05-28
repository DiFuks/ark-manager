using System.Collections.ObjectModel;
using ArkManager.Core.Services;
using ArkManager.Core.Services.Backups;
using ArkManager.Core.Util;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArkManager.App.ViewModels;

public partial class BackupsViewModel : ViewModelBase
{
    private readonly BackupService? _service;
    private readonly AutoBackupWorker? _auto;
    private readonly ServerManager? _server;
    private readonly SettingsService? _settings;

    public ObservableCollection<BackupInfo> Backups { get; } = new();

    // Restore/Delete only work with a selected snapshot, Create only when not Busy.
    // Without CanExecute the buttons stay active but silently do nothing (the user hit this).
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    private BackupInfo? _selected;

    [ObservableProperty] private string _note = "";
    [ObservableProperty] private string _status = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    private bool _busy;

    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _autoBackupStatus = "Auto-backup off";
    [ObservableProperty] private string _summary = "";

    // Backup storage: path, rotation depth, auto-backup interval. The source of truth moved
    // here from Settings; changing it here writes straight to settings.json (BackupService and
    // AutoBackupWorker read from _settings.Current every tick, no caching).
    [ObservableProperty] private string _backupsDirectory = "";
    [ObservableProperty] private int _backupRotationKeep = 10;
    [ObservableProperty] private int _autoBackupIntervalMinutes;

    public bool HasSelection => Selected != null;
    public bool CanCreate => !Busy;
    public bool CanRestore => Selected != null && !Busy;
    public bool CanDelete => Selected != null && !Busy;

    public BackupsViewModel() { }

    public BackupsViewModel(BackupService service, AutoBackupWorker auto, ServerManager server, SettingsService settings)
    {
        _service = service;
        _auto = auto;
        _server = server;
        _settings = settings;
        BackupsDirectory = settings.Current.BackupsDirectory ?? "";
        BackupRotationKeep = settings.Current.BackupRotationKeep;
        AutoBackupIntervalMinutes = settings.Current.AutoBackupIntervalMinutes;
        Reload();

        _auto.BackupCreated += _ => App.UiThread(() => { Reload(); UpdateAutoStatus(); });
        _auto.Log          += msg => App.UiThread(() => { Status = msg; UpdateAutoStatus(); });
        // Server start/stop toggles the timer between "paused" and ticking — refresh immediately
        // instead of waiting for the 5-second poll.
        _server.StateChanged += _ => App.UiThread(UpdateAutoStatus);
        _settings.Changed += _ => App.UiThread(UpdateAutoStatus);

        _ = Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(5000);
                App.UiThread(UpdateAutoStatus);
            }
        });
        UpdateAutoStatus();
    }

    private void UpdateAutoStatus()
    {
        var interval = _settings?.Current.AutoBackupIntervalMinutes ?? 0;
        if (interval <= 0)
        {
            AutoBackupStatus = "Auto-backup off";
            return;
        }

        // Server not Running → the worker still ticks NextRunUtc but skips the tick:
        // backing up an idle server is pointless. Show an explicit paused state, otherwise
        // a ticking timer is misleading.
        if (_server is { State: not ServerState.Running })
        {
            AutoBackupStatus = "Auto-backup paused (server idle)";
            return;
        }

        if (_auto?.NextRunUtc is { } next)
        {
            var left = next - DateTime.UtcNow;
            AutoBackupStatus = left <= TimeSpan.Zero
                ? "Auto-backup: running…"
                : $"Auto-backup in {(int)left.TotalMinutes:00}:{left.Seconds:00}";
        }
        else AutoBackupStatus = "Auto-backup off";
    }

    [RelayCommand]
    public void Reload()
    {
        if (_service == null) return;
        Backups.Clear();
        foreach (var b in _service.ListBackups()) Backups.Add(b);
        var total = 0L;
        foreach (var b in Backups) total += b.SizeBytes;
        Summary = $"{Backups.Count} snapshots · {DisplayFormat.HumanSize(total)} total";
        Status = "";
    }

    [RelayCommand(CanExecute = nameof(CanCreate))]
    public async Task CreateAsync()
    {
        if (_service == null) return;
        Busy = true;
        var progress = new Progress<double>(p => Progress = p);
        try
        {
            var info = await _service.CreateBackupAsync(Note, progress);
            Status = "Created: " + Path.GetFileName(info.FilePath);
            Reload();
            Note = "";
        }
        catch (Exception ex) { Status = "Error: " + ex.Message; }
        finally { Busy = false; Progress = 0; }
    }

    [RelayCommand(CanExecute = nameof(CanRestore))]
    public async Task RestoreAsync()
    {
        if (_service == null || Selected == null) return;
        Busy = true;
        var progress = new Progress<double>(p => Progress = p);
        try
        {
            await _service.RestoreAsync(Selected.FilePath, wipeFirst: true, progress);
            Status = "Restored from: " + Path.GetFileName(Selected.FilePath);
        }
        catch (Exception ex) { Status = "Error: " + ex.Message; }
        finally { Busy = false; Progress = 0; }
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    public void Delete()
    {
        if (_service == null || Selected == null) return;
        try
        {
            _service.Delete(Selected.FilePath);
            Reload();
        }
        catch (Exception ex) { Status = "Error: " + ex.Message; }
    }

    [RelayCommand]
    public void OpenFolder()
    {
        // If backups already exist — open their folder (this respects a custom path even
        // when it was just entered and Reload has not picked it up yet). Otherwise open
        // the configured BackupsDirectory; if that's empty too, there's nothing to open.
        if (Backups.Count > 0)
            App.OpenInFinder(Path.GetDirectoryName(Backups[0].FilePath)!);
        else if (!string.IsNullOrWhiteSpace(BackupsDirectory))
        {
            Directory.CreateDirectory(BackupsDirectory);
            App.OpenInFinder(BackupsDirectory);
        }
    }

    [RelayCommand]
    public async Task BrowseDirectoryAsync()
    {
        var p = await Services.Browse.PickFolderAsync("Backups folder", BackupsDirectory);
        if (!string.IsNullOrEmpty(p)) BackupsDirectory = p;
    }

    partial void OnBackupsDirectoryChanged(string value)
    {
        _settings?.Update(s => s.BackupsDirectory = string.IsNullOrWhiteSpace(value) ? null : value);
        // The snapshot list lives in this folder — re-read it on change.
        Reload();
    }

    partial void OnBackupRotationKeepChanged(int value)
    {
        _settings?.Update(s => s.BackupRotationKeep = value);
    }

    partial void OnAutoBackupIntervalMinutesChanged(int value)
    {
        _settings?.Update(s => s.AutoBackupIntervalMinutes = value);
        // AutoBackupWorker will pick this up itself via SettingsService.Changed (cancel sleep);
        // the pill above refreshes on the periodic UpdateAutoStatus tick, but kick it now
        // so the user sees the reaction instantly.
        UpdateAutoStatus();
    }
}
