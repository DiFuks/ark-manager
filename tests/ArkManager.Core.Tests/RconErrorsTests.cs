using System.Net.Sockets;
using ArkManager.Core.Services.Config;
using ArkManager.Core.Services.Rcon;
using Xunit;

namespace ArkManager.Core.Tests;

public class RconErrorsTests
{
    [Fact]
    public void Precondition_RconDisabled_ReturnsHint()
    {
        var snap = new ServerConfigSnapshot { RconEnabled = false, AdminPassword = "x" };
        var msg = RconErrors.DescribePrecondition(snap);
        Assert.NotNull(msg);
        Assert.Contains("disabled", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Precondition_EmptyAdminPassword_PointsAtConfigTab(string? pw)
    {
        var snap = new ServerConfigSnapshot { RconEnabled = true, AdminPassword = pw ?? "" };
        var msg = RconErrors.DescribePrecondition(snap);
        Assert.NotNull(msg);
        Assert.Contains("ServerAdminPassword", msg);
        Assert.Contains("Config", msg);
    }

    [Fact]
    public void Precondition_Ok_ReturnsNull()
    {
        var snap = new ServerConfigSnapshot { RconEnabled = true, AdminPassword = "secret" };
        Assert.Null(RconErrors.DescribePrecondition(snap));
    }

    // SocketException.Message comes from FormatMessage and is localised by the OS, so on a
    // Russian Windows the user gets Cyrillic. Mapping codes ourselves keeps the UI predictable
    // AND lets us replace the cryptic "connection refused" with the actionable hint about
    // ServerAdminPassword (99% of the time the cause).
    [Fact]
    public void DescribeSocketError_ConnectionRefused_MentionsAdminPassword()
    {
        var msg = RconErrors.DescribeSocketError(SocketError.ConnectionRefused);
        Assert.Contains("refused", msg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ServerAdminPassword", msg);
    }

    [Fact]
    public void DescribeSocketError_TimedOut_Mentioned()
    {
        Assert.Contains("timed out", RconErrors.DescribeSocketError(SocketError.TimedOut),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DescribeSocketError_UnknownCode_FallsBackToCodeName()
    {
        var msg = RconErrors.DescribeSocketError(SocketError.InvalidArgument);
        Assert.Contains("InvalidArgument", msg);
    }

    [Fact]
    public void DescribeConnectException_SocketException_UsesCodeMapping()
    {
        var ex = new SocketException((int)SocketError.ConnectionRefused);
        Assert.Contains("refused", RconErrors.DescribeConnectException(ex),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DescribeConnectException_InvalidOperation_PassesMessageThrough()
    {
        var ex = new InvalidOperationException("RCON: wrong password.");
        Assert.Equal("RCON: wrong password.", RconErrors.DescribeConnectException(ex));
    }
}
