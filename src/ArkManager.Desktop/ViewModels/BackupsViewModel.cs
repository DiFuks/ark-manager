using System.Collections.ObjectModel;
using ArkManager.Core.Services.Backups;
using ArkManager.Core.Util;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArkManager.App.ViewModels;

public partial class BackupsViewModel : ViewModelBase
{
    private readonly BackupService? _service;
    private readonly AutoBackupWorker? _auto;

    public ObservableCollection<BackupInfo> Backups { get; } = new();
    [ObservableProperty] private BackupInfo? _selected;
    [ObservableProperty] private string _note = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _autoBackupStatus = "Auto-backup off";
    [ObservableProperty] private string _summary = "";

    public BackupsViewModel() { }

    public BackupsViewModel(BackupService service, AutoBackupWorker auto)
    {
        _service = service;
        _auto = auto;
        Reload();

        _auto.BackupCreated += _ => App.UiThread(() => { Reload(); UpdateAutoStatus(); });
        _auto.Log          += msg => App.UiThread(() => { Status = msg; UpdateAutoStatus(); });

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

    [RelayCommand]
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

    [RelayCommand]
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

    [RelayCommand]
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
        if (Backups.Count > 0) App.OpenInFinder(Path.GetDirectoryName(Backups[0].FilePath)!);
    }
}
