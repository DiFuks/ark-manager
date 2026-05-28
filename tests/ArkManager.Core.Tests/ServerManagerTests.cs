using ArkManager.Core.Models;
using ArkManager.Core.Services;
using Xunit;

namespace ArkManager.Core.Tests;

public class ServerManagerTests
{
    // Before a hard-kill the server attempts a graceful save via RCON (saveworld+DoExit),
    // otherwise ASA loses progress since the last autosave. Conditions: RCON is enabled
    // and an admin password is set (without it RCON auth is impossible).

    [Fact]
    public void ShouldAttemptGracefulSave_True_WhenRconEnabledAndPasswordSet()
    {
        var o = new ServerLaunchOptions { RconEnabled = true, AdminPassword = "secret" };
        Assert.True(ServerManager.ShouldAttemptGracefulSave(o));
    }

    [Fact]
    public void ShouldAttemptGracefulSave_False_WhenRconDisabled()
    {
        var o = new ServerLaunchOptions { RconEnabled = false, AdminPassword = "secret" };
        Assert.False(ServerManager.ShouldAttemptGracefulSave(o));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ShouldAttemptGracefulSave_False_WhenNoAdminPassword(string? password)
    {
        var o = new ServerLaunchOptions { RconEnabled = true, AdminPassword = password };
        Assert.False(ServerManager.ShouldAttemptGracefulSave(o));
    }

    // The "green" readiness indicator = the log line ASA prints once the world is loaded
    // and the server starts accepting connections. Before it the process is alive but still in "yellow" loading.
    [Theory]
    [InlineData("[2026.05.26-22.43.10:834][232]Server has completed startup and is now advertising for join. (2.07GB Mem)", true)]
    [InlineData("Server has completed startup and is now ADVERTISING FOR JOIN.", true)]
    [InlineData("[2026.05.26-22.42.50:723][  2]Server: \"x\" has successfully started!", false)]
    [InlineData("LogMemory: Platform Memory Stats for WindowsServer", false)]
    public void IsServerReadyLine_DetectsAdvertisingMarker(string line, bool expected)
        => Assert.Equal(expected, ServerManager.IsServerReadyLine(line));
}
