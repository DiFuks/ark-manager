namespace ArkManager.Core.Services.Backups;

/// <summary>
/// Background worker for automatic backups with a configurable interval.
/// Subscribes to SettingsService.Changed — when the interval changes
/// the current sleep is cancelled immediately and the new interval applies at once.
/// When the server isn't running the tick is always skipped: backups only make sense
/// when the world is actively changing (Saved/* doesn't grow on an idle server).
/// </summary>
public sealed class AutoBackupWorker : IDisposable
{
    private readonly SettingsService _settings;
    private readonly ServerManager _server;
    private readonly BackupService _backups;
    private readonly CancellationTokenSource _shutdown = new();

    // We only cancel the sleep, not the whole work. Each iteration creates a new linked token.
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
        // Cancel the current sleep so the new values apply immediately.
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
                await SleepAsync(TimeSpan.FromMinutes(1)); // poll settings once a minute
                continue;
            }

            var interval = TimeSpan.FromMinutes(minutes);
            var anchor = _lastRunUtc == DateTime.MinValue ? DateTime.UtcNow : _lastRunUtc;
            NextRunUtc = anchor + interval;
            var delay = NextRunUtc.Value - DateTime.UtcNow;
            if (delay > TimeSpan.Zero) await SleepAsync(delay);

            if (_shutdown.IsCancellationRequested) return;

            // Re-check settings after waking (they might have changed).
            if (_settings.Current.AutoBackupIntervalMinutes <= 0) continue;

            if (_server.State != ServerState.Running)
            {
                Log?.Invoke("[auto-backup] skipped — server not running");
                _lastRunUtc = DateTime.UtcNow;
                continue;
            }

            try
            {
                Log?.Invoke("[auto-backup] creating snapshot...");
                var info = await _backups.CreateBackupAsync(note: BackupService.AutoNote, progress: null, _shutdown.Token);
                BackupCreated?.Invoke(info);
                Log?.Invoke($"[auto-backup] done: {Path.GetFileName(info.FilePath)}");
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                BackupFailed?.Invoke(ex);
                Log?.Invoke("[auto-backup] error: " + ex.Message);
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
        catch (OperationCanceledException) { /* settings changed or shutdown — that's fine */ }
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
