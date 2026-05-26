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

    // «Зелёный» индикатор готовности = строка лога, которую ASA печатает, когда мир загружен
    // и сервер начал принимать подключения. До неё процесс жив, но это ещё «жёлтая» загрузка.
    [Theory]
    [InlineData("[2026.05.26-22.43.10:834][232]Server has completed startup and is now advertising for join. (2.07GB Mem)", true)]
    [InlineData("Server has completed startup and is now ADVERTISING FOR JOIN.", true)]
    [InlineData("[2026.05.26-22.42.50:723][  2]Server: \"x\" has successfully started!", false)]
    [InlineData("LogMemory: Platform Memory Stats for WindowsServer", false)]
    public void IsServerReadyLine_DetectsAdvertisingMarker(string line, bool expected)
        => Assert.Equal(expected, ServerManager.IsServerReadyLine(line));
}
