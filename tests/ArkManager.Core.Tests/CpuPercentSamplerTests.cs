using ArkManager.Core.Util;
using Xunit;

namespace ArkManager.Core.Tests;

public class CpuPercentSamplerTests
{
    // Without a previous sample we can't compute a delta — first tick must be 0, not NaN/garbage.
    [Fact]
    public void Sample_FirstCall_ReturnsZero()
    {
        var s = new CpuPercentSampler(cores: 4);
        var p = s.Sample(TimeSpan.FromMilliseconds(500), DateTime.UtcNow);
        Assert.Equal(0, p);
    }

    // 200 ms of CPU time accumulated over 1000 ms of wall clock on 4 cores =
    // 200 / 1000 / 4 * 100 = 5%.
    [Fact]
    public void Sample_DeltaOverWall_NormalisedByCores()
    {
        var s = new CpuPercentSampler(cores: 4);
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        s.Sample(TimeSpan.Zero, t0);
        var p = s.Sample(TimeSpan.FromMilliseconds(200), t0.AddMilliseconds(1000));
        Assert.Equal(5.0, p, 3);
    }

    // Full pegging on a single-core box: 1000 ms cpu / 1000 ms wall / 1 core = 100%.
    [Fact]
    public void Sample_FullyPegged_OneCore_Returns100()
    {
        var s = new CpuPercentSampler(cores: 1);
        var t0 = DateTime.UtcNow;
        s.Sample(TimeSpan.Zero, t0);
        var p = s.Sample(TimeSpan.FromMilliseconds(1000), t0.AddMilliseconds(1000));
        Assert.Equal(100.0, p, 3);
    }

    // Reset wipes the previous sample — next call must again return 0 (no delta available).
    [Fact]
    public void Reset_ForgetsPreviousSample()
    {
        var s = new CpuPercentSampler(cores: 4);
        var t0 = DateTime.UtcNow;
        s.Sample(TimeSpan.Zero, t0);
        s.Reset();
        var p = s.Sample(TimeSpan.FromMilliseconds(200), t0.AddMilliseconds(1000));
        Assert.Equal(0, p);
    }

    // Wall delta of 0 means we somehow got two samples at the same instant —
    // dividing would produce ∞/NaN. Treat as "no data" → 0.
    [Fact]
    public void Sample_ZeroWallDelta_ReturnsZero()
    {
        var s = new CpuPercentSampler(cores: 4);
        var t0 = DateTime.UtcNow;
        s.Sample(TimeSpan.Zero, t0);
        var p = s.Sample(TimeSpan.FromMilliseconds(100), t0);
        Assert.Equal(0, p);
    }
}
