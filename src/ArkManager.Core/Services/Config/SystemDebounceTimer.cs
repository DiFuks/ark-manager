namespace ArkManager.Core.Services.Config;

internal sealed class SystemDebounceTimer : IDebounceTimer, IDisposable
{
    private readonly System.Threading.Timer _timer;

    public SystemDebounceTimer()
    {
        _timer = new System.Threading.Timer(_ => Elapsed?.Invoke(), null,
            System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
    }

    public event Action? Elapsed;

    public void Schedule(TimeSpan delay)
        => _timer.Change(delay, System.Threading.Timeout.InfiniteTimeSpan);

    public void Dispose() => _timer.Dispose();
}
