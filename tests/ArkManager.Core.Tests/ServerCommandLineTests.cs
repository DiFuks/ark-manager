using ArkManager.Core.Models;
using ArkManager.Core.Services.Launchers;
using Xunit;

namespace ArkManager.Core.Tests;

public class ServerCommandLineTests
{
    [Fact]
    public void Build_DefaultOptions_HasMapAndCoreFlags()
    {
        var s = new AppSettings();
        var args = ServerCommandLine.Build(s, Array.Empty<string>());
        Assert.Contains("-server", args);
        Assert.Contains("-log", args);
        Assert.Contains("-NoBattlEye", args);
        var query = args[0];
        Assert.StartsWith("TheIsland_WP?listen?", query);
        Assert.Contains("Port=7777", query);
        Assert.Contains("QueryPort=27015", query);
    }

    [Fact]
    public void Build_AddsModsFlag_WhenModsPresent()
    {
        var s = new AppSettings();
        var args = ServerCommandLine.Build(s, new[] { "928597", "929094" });
        Assert.Contains("-mods=928597,929094", args);
    }

    [Fact]
    public void Build_ServerPassword_IsIncludedInQuery()
    {
        var s = new AppSettings();
        s.LaunchOptions.ServerPassword = "secret";
        var args = ServerCommandLine.Build(s, Array.Empty<string>());
        Assert.Contains("ServerPassword=secret", args[0]);
    }

    [Fact]
    public void Build_ExtraCli_Tokenized()
    {
        var s = new AppSettings();
        s.LaunchOptions.ExtraCommandLineArgs = "-ForceAllowCaveFlyers -ServerAllowAnsel";
        var args = ServerCommandLine.Build(s, Array.Empty<string>());
        Assert.Contains("-ForceAllowCaveFlyers", args);
        Assert.Contains("-ServerAllowAnsel", args);
    }
}
