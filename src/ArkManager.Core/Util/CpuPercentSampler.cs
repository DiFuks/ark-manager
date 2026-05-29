namespace ArkManager.Core.Util;

/// <summary>
/// CPU% as seen by Activity Monitor / Task Manager: dCpuTime / dWallTime / cores * 100.
/// The very first call has no baseline, so it returns 0 — the UI shows "—" until the next tick.
/// Stateful; one instance per monitored PID. Reset() when the PID changes (server restart).
/// </summary>
public sealed class CpuPercentSampler
{
    private readonly int _cores;
    private TimeSpan? _lastTotalProc;
    private DateTime? _lastAt;

    public CpuPercentSampler(int cores)
    {
        _cores = Math.Max(1, cores);
    }

    public double Sample(TimeSpan currentTotalProc, DateTime nowUtc)
    {
        double result = 0;
        if (_lastTotalProc is { } prevProc && _lastAt is { } prevAt)
        {
            var dProc = (currentTotalProc - prevProc).TotalMilliseconds;
            var dWall = (nowUtc - prevAt).TotalMilliseconds;
            if (dWall > 0) result = dProc / dWall / _cores * 100.0;
        }
        _lastTotalProc = currentTotalProc;
        _lastAt = nowUtc;
        return result;
    }

    public void Reset()
    {
        _lastTotalProc = null;
        _lastAt = null;
    }
}
