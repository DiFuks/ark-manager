using ArkManager.Core.Util;
using Xunit;

namespace ArkManager.Core.Tests;

public class ProcessTreeStatsTests
{
    // Формат строк = вывод `ps -axo pid=,ppid=,rss=,%cpu=`: pid, ppid, rss(KB), %cpu.
    private const string Ps =
        "  1     0    2000  0.0\n" +
        "100     1  500000 50.0\n" +   // корень
        "200   100  300000 25.0\n" +   // ребёнок 100
        "300   200  100000  5.0\n" +   // внук (ребёнок 200)
        "400     1   10000  1.0\n";    // чужой процесс

    [Fact]
    public void Sum_AggregatesWholeSubtree()
    {
        var st = ProcessTreeStats.Sum(Ps, 100);
        Assert.Equal(900000, st.RssKb);          // 500000+300000+100000
        Assert.Equal(80.0, st.CpuPercent, 3);    // 50+25+5
    }

    [Fact]
    public void Sum_RootNotFound_ReturnsZero()
    {
        var st = ProcessTreeStats.Sum(Ps, 99999);
        Assert.Equal(0, st.RssKb);
        Assert.Equal(0.0, st.CpuPercent, 3);
    }

    [Fact]
    public void Sum_HandlesCommaDecimal()
    {
        // На системах с русской локалью ps печатает %cpu через запятую: "12,5".
        var st = ProcessTreeStats.Sum("100 1 1234 12,5\n", 100);
        Assert.Equal(1234, st.RssKb);
        Assert.Equal(12.5, st.CpuPercent, 3);
    }

    [Fact]
    public void Sum_IgnoresGarbageLines()
    {
        var st = ProcessTreeStats.Sum("header junk\n\n100 1 1234 12.5\n", 100);
        Assert.Equal(1234, st.RssKb);
        Assert.Equal(12.5, st.CpuPercent, 3);
    }
}
