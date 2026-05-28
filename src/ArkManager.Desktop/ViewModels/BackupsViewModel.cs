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

    // Restore/Delete работают только с выделенным снэпшотом, Create — только когда не Busy.
    // Без CanExecute кнопки активны но молча ничего не делают (юзер ловил).
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

    // Бэкап-сторадж: путь и глубина ротации. Источник истины переехал сюда из Settings;
    // меняется здесь — сразу пишем в settings.json (BackupService читает из _settings.Current
    // на каждом тике, кэша нет).
    [ObservableProperty] private string _backupsDirectory = "";
    [ObservableProperty] private int _backupRotationKeep = 10;

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
        Reload();

        _auto.BackupCreated += _ => App.UiThread(() => { Reload(); UpdateAutoStatus(); });
        _auto.Log          += msg => App.UiThread(() => { Status = msg; UpdateAutoStatus(); });
        // Старт/стоп сервера переключает таймер между «paused» и тикающим — обновляем сразу,
        // не дожидаясь 5-секундного poll'а.
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

        // OnlyWhenRunning + сервер не Running → воркер всё равно крутит NextRunUtc, но создавать
        // снэпшот не будет: показывать тикающий таймер для никогда-не-сработающего тика
        // вводит юзера в заблуждение. Показываем явный paused.
        var onlyWhenRunning = _settings?.Current.AutoBackupOnlyWhenRunning ?? true;
        if (onlyWhenRunning && _server is { State: not ServerState.Running })
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
        // Если бэкапы уже есть — открываем их папку (учитывает кастомный path даже если
        // он только что введён, но Reload ещё не успел подтянуть). Если нет — открываем
        // настроенную BackupsDirectory, иначе и открывать нечего.
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
        // Список снэпшотов хранится в этой папке — перечитываем при смене.
        Reload();
    }

    partial void OnBackupRotationKeepChanged(int value)
    {
        _settings?.Update(s => s.BackupRotationKeep = value);
    }
}
