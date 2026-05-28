using ArkManager.Core.Util;

namespace ArkManager.Core.Tests;

public class DisplayFormatTests
{
    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(512L, "512 B")]
    [InlineData(1024L, "1.0 KB")]
    [InlineData(1536L, "1.5 KB")]
    [InlineData(69_273_600L, "66.1 MB")]
    [InlineData(1_288_490_188L, "1.2 GB")]
    public void HumanSize_formats_bytes(long bytes, string expected)
        => Assert.Equal(expected, DisplayFormat.HumanSize(bytes));

    [Fact]
    public void RelativeTime_today_shows_time()
    {
        var now = new DateTime(2026, 5, 28, 23, 40, 0, DateTimeKind.Utc);
        var v   = new DateTime(2026, 5, 28, 23, 28, 0, DateTimeKind.Utc);
        var expectedTime = v.ToLocalTime().ToString("HH:mm");
        Assert.Equal($"today, {expectedTime}", DisplayFormat.RelativeTime(v, now));
    }

    [Fact]
    public void RelativeTime_yesterday_shows_time()
    {
        // noon-to-noon ensures local date diff == 1 in every timezone (-12..+14)
        var now = new DateTime(2026, 5, 28, 12, 0, 0, DateTimeKind.Utc);
        var v   = new DateTime(2026, 5, 27, 12, 0, 0, DateTimeKind.Utc);
        var expectedTime = v.ToLocalTime().ToString("HH:mm");
        Assert.Equal($"yesterday, {expectedTime}", DisplayFormat.RelativeTime(v, now));
    }

    [Fact]
    public void RelativeTime_older_shows_days_ago()
    {
        var now = new DateTime(2026, 5, 28, 12, 0, 0, DateTimeKind.Utc);
        var v   = new DateTime(2026, 5, 25, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal("3 days ago", DisplayFormat.RelativeTime(v, now));
    }
}
