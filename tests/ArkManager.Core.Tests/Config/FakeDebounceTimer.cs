using ArkManager.Core.Services.Config;

namespace ArkManager.Core.Tests.Config;

internal sealed class FakeDebounceTimer : IDebounceTimer
{
    public event Action? Elapsed;
    public TimeSpan? LastDelay { get; private set; }
    public int ScheduleCount { get; private set; }

    public void Schedule(TimeSpan delay)
    {
        LastDelay = delay;
        ScheduleCount++;
    }

    public void Tick()
    {
        LastDelay = null;
        Elapsed?.Invoke();
    }
}
