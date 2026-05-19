using System.Collections.ObjectModel;
using ArkManager.Core.Services.Backups;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArkManager.App.ViewModels;

public partial class BackupsViewModel : ViewModelBase
{
    private readonly BackupService? _service;

    public ObservableCollection<BackupInfo> Backups { get; } = new();
    [ObservableProperty] private BackupInfo? _selected;
    [ObservableProperty] private string _note = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private double _progress;

    public BackupsViewModel() { }

    public BackupsViewModel(BackupService service)
    {
        _service = service;
        Reload();
    }

    [RelayCommand]
    public void Reload()
    {
        if (_service == null) return;
        Backups.Clear();
        foreach (var b in _service.ListBackups()) Backups.Add(b);
        Status = $"{Backups.Count} бэкап(ов)";
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
            Status = "Создан: " + Path.GetFileName(info.FilePath);
            Reload();
            Note = "";
        }
        catch (Exception ex) { Status = "Ошибка: " + ex.Message; }
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
            Status = "Восстановлен из: " + Path.GetFileName(Selected.FilePath);
        }
        catch (Exception ex) { Status = "Ошибка: " + ex.Message; }
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
        catch (Exception ex) { Status = "Ошибка: " + ex.Message; }
    }

    [RelayCommand]
    public void OpenFolder()
    {
        if (Backups.Count > 0) App.OpenInFinder(Path.GetDirectoryName(Backups[0].FilePath)!);
    }
}
