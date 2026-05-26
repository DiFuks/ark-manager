namespace ArkManager.Core.Services.Backups;

/// <summary>
/// Фоновый воркер автоматических бэкапов с настраиваемым интервалом.
/// Подписывается на SettingsService.Changed — при смене интервала
/// текущий sleep немедленно прерывается, новый интервал применяется сразу.
/// Опция OnlyWhenRunning гарантирует, что не плодим одинаковые снимки
/// при остановленном сервере (Saved не меняется → бесполезная нагрузка).
/// </summary>
public sealed class AutoBackupWorker : IDisposable
{
    private readonly SettingsService _settings;
    private readonly ServerManager _server;
    private readonly BackupService _backups;
    private readonly CancellationTokenSource _shutdown = new();

    // Кенселим только sleep, не всю работу. На каждой итерации создаём новый linked token.
    private CancellationTokenSource? _sleepCts;
    private readonly object _sleepLock = new();

    private DateTime _lastRunUtc = DateTime.MinValue;
    private bool _disposed;

    public DateTime? NextRunUtc { get; private set; }

    public event Action<BackupInfo>? BackupCreated;
    public event Action<Exception>? BackupFailed;
    public event Action<string>? Log;

    public AutoBackupWorker(SettingsService settings, ServerManager server, BackupService backups)
    {
        _settings = settings;
        _server = server;
        _backups = backups;
        _settings.Changed += OnSettingsChanged;
        _ = Task.Run(LoopAsync);
    }

    private void OnSettingsChanged(Models.AppSettings _)
    {
        // Прервать текущий sleep, чтобы новые значения применились немедленно.
        lock (_sleepLock) { _sleepCts?.Cancel(); }
    }

    private async Task LoopAsync()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            var minutes = Math.Max(0, _settings.Current.AutoBackupIntervalMinutes);
            if (minutes <= 0)
            {
                NextRunUtc = null;
                await SleepAsync(TimeSpan.FromMinutes(1)); // опрашиваем настройки раз в минуту
                continue;
            }

            var interval = TimeSpan.FromMinutes(minutes);
            var anchor = _lastRunUtc == DateTime.MinValue ? DateTime.UtcNow : _lastRunUtc;
            NextRunUtc = anchor + interval;
            var delay = NextRunUtc.Value - DateTime.UtcNow;
            if (delay > TimeSpan.Zero) await SleepAsync(delay);

            if (_shutdown.IsCancellationRequested) return;

            // Re-check settings после пробуждения (могли поменяться).
            if (_settings.Current.AutoBackupIntervalMinutes <= 0) continue;

            if (_settings.Current.AutoBackupOnlyWhenRunning && _server.State != ServerState.Running)
            {
                Log?.Invoke("[auto-backup] пропуск — сервер не запущен");
                _lastRunUtc = DateTime.UtcNow;
                continue;
            }

            try
            {
                Log?.Invoke("[auto-backup] создаю снимок...");
                var info = await _backups.CreateBackupAsync(note: "auto", progress: null, _shutdown.Token);
                BackupCreated?.Invoke(info);
                Log?.Invoke($"[auto-backup] готово: {Path.GetFileName(info.FilePath)}");
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                BackupFailed?.Invoke(ex);
                Log?.Invoke("[auto-backup] ошибка: " + ex.Message);
            }
            finally
            {
                _lastRunUtc = DateTime.UtcNow;
            }
        }
    }

    private async Task SleepAsync(TimeSpan duration)
    {
        CancellationTokenSource cts;
        lock (_sleepLock)
        {
            cts = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
            _sleepCts = cts;
        }
        try { await Task.Delay(duration, cts.Token); }
        catch (OperationCanceledException) { /* settings changed или shutdown — нормально */ }
        finally
        {
            lock (_sleepLock)
            {
                if (ReferenceEquals(_sleepCts, cts)) _sleepCts = null;
            }
            cts.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _settings.Changed -= OnSettingsChanged;
        _shutdown.Cancel();
        lock (_sleepLock) { _sleepCts?.Cancel(); }
    }
}
