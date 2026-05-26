using ArkManager.Core.Models;
using ArkManager.Core.Services;
using Xunit;

namespace ArkManager.Core.Tests;

public class ServerManagerTests
{
    // Перед hard-kill сервер пытается graceful-сейв по RCON (saveworld+DoExit),
    // иначе ASA теряет прогресс с последнего автосейва. Условие — RCON включён
    // и задан admin-пароль (без него RCON-auth невозможна).

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
}
