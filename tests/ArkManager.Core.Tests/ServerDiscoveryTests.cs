using ArkManager.Core.Util;
using Xunit;

namespace ArkManager.Core.Tests;

public class ServerDiscoveryTests
{
    // Format = `ps -axww -o pid=,etime=,command=`: pid, etime, full command line.
    private const string Exe =
        "/Users/difuks/Library/Application Support/ArkManager/server/ShooterGame/Binaries/Win64/ArkAscendedServer.exe";

    private const string Ps =
        "  502    01:23 /bin/ps -axww -o pid=,etime=,command=\n" +
        "31714 01:05:09 " + Exe + " TheIsland_WP?listen?Port=7777\n" +
        "  600    10:00 /Applications/Wine Stable.app/Contents/Resources/wine/bin/wineserver\n";

    [Fact]
    public void Find_LocatesServerByExePath_AndUptime()
    {
        var d = ServerDiscovery.Find(Ps, Exe);
        Assert.NotNull(d);
        Assert.Equal(31714, d!.Value.Pid);
        Assert.Equal(new TimeSpan(0, 1, 5, 9), d.Value.Uptime); // 1h05m09s
    }

    [Fact]
    public void Find_ReturnsNull_WhenNoMatch()
        => Assert.Null(ServerDiscovery.Find(Ps, "/some/other/path/ArkAscendedServer.exe"));

    [Theory]
    [InlineData("13:30", 0, 0, 13, 30)]
    [InlineData("01:05:09", 0, 1, 5, 9)]
    [InlineData("2-03:04:05", 2, 3, 4, 5)]
    public void ParseEtime_HandlesAllFormats(string etime, int d, int h, int m, int s)
        => Assert.Equal(new TimeSpan(d, h, m, s), ServerDiscovery.ParseEtime(etime));
}
