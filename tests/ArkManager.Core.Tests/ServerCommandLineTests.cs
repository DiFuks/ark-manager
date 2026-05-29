using ArkManager.Core.Models;
using ArkManager.Core.Services.Config;
using ArkManager.Core.Services.Launchers;
using Xunit;

namespace ArkManager.Core.Tests;

public class ServerCommandLineTests
{
    [Fact]
    public void Build_DefaultOptions_HasMapAndCoreFlags()
    {
        var s = new AppSettings();
        var snap = new ServerConfigSnapshot { Port = 7777, QueryPort = 27015 };
        var args = ServerCommandLine.Build(s, snap, Array.Empty<string>());
        Assert.Contains("-server", args);
        Assert.Contains("-log", args);
        Assert.Contains("-NoBattlEye", args);
        var query = args[0];
        Assert.StartsWith("TheIsland_WP?listen?", query);
        Assert.Contains("Port=7777", query);
        Assert.Contains("QueryPort=27015", query);
    }

    [Fact]
    public void Build_RoutesFullLogToStdout()
    {
        // The server runs headless (no winemac.drv → no window), so the full UE log
        // must be routed to stdout — otherwise it only goes to a window/file and is invisible to ArkManager.
        var s = new AppSettings();
        var args = ServerCommandLine.Build(s, DefaultSnapshot(), Array.Empty<string>());
        Assert.Contains("-stdout", args);
        Assert.Contains("-FullStdOutLogOutput", args);
        Assert.Contains("-unattended", args);
    }

    [Fact]
    public void Build_AddsModsFlag_WhenModsPresent()
    {
        var s = new AppSettings();
        var args = ServerCommandLine.Build(s, DefaultSnapshot(), new[] { "928597", "929094" });
        Assert.Contains("-mods=928597,929094", args);
    }

    [Fact]
    public void Build_Passwords_NotInUrlQuery()
    {
        // Passwords are written ONLY to the ini (via ConfigService),
        // never into the URL — otherwise ASA glues the rest of the string into the password value.
        var s = new AppSettings();
        var snap = new ServerConfigSnapshot
        {
            ServerPassword = "srv",
            AdminPassword = "adm",
            SpectatorPassword = "spec",
        };
        var args = ServerCommandLine.Build(s, snap, Array.Empty<string>());
        Assert.DoesNotContain("ServerPassword", args[0]);
        Assert.DoesNotContain("ServerAdminPassword", args[0]);
        Assert.DoesNotContain("SpectatorPassword", args[0]);
    }

    [Fact]
    public void Build_ExtraCli_Tokenized()
    {
        var s = new AppSettings();
        s.LaunchOptions.ExtraCommandLineArgs = "-ForceAllowCaveFlyers -ServerAllowAnsel";
        var args = ServerCommandLine.Build(s, DefaultSnapshot(), Array.Empty<string>());
        Assert.Contains("-ForceAllowCaveFlyers", args);
        Assert.Contains("-ServerAllowAnsel", args);
    }

    [Fact]
    public void Build_Cluster_AppendsFlags()
    {
        var s = new AppSettings();
        s.LaunchOptions.ClusterId = "my-cluster";
        s.LaunchOptions.ClusterDirOverride = "/tmp/cluster";
        var args = ServerCommandLine.Build(s, DefaultSnapshot(), Array.Empty<string>());
        Assert.Contains("-ClusterId=my-cluster", args);
        Assert.Contains(args, a => a.StartsWith("-ClusterDirOverride="));
    }

    [Fact]
    public void Build_Rcon_NotInUrlQuery()
    {
        // RCONEnabled/RCONPort go only into GameUserSettings.ini.
        var s = new AppSettings();
        var snap = new ServerConfigSnapshot { RconEnabled = true, RconPort = 27042 };
        var args = ServerCommandLine.Build(s, snap, Array.Empty<string>());
        Assert.DoesNotContain("RCONEnabled", args[0]);
        Assert.DoesNotContain("RCONPort", args[0]);
    }

    [Fact]
    public void Build_NoBattlEye_Off_SkipsFlag()
    {
        var s = new AppSettings();
        s.LaunchOptions.NoBattlEye = false;
        var args = ServerCommandLine.Build(s, DefaultSnapshot(), Array.Empty<string>());
        Assert.DoesNotContain("-NoBattlEye", args);
    }

    [Fact]
    public void Build_MaxPlayers_NotInUrlQuery()
    {
        var s = new AppSettings { LaunchOptions = new ServerLaunchOptions { MaxPlayers = 42 } };
        var args = ServerCommandLine.Build(s, DefaultSnapshot(), Array.Empty<string>());
        var url = args[0];
        Assert.DoesNotContain("MaxPlayers=", url);
    }

    [Fact]
    public void Build_MaxPlayers_AsWinLiveMaxPlayersFlag()
    {
        var s = new AppSettings { LaunchOptions = new ServerLaunchOptions { MaxPlayers = 42 } };
        var args = ServerCommandLine.Build(s, DefaultSnapshot(), Array.Empty<string>());
        Assert.Contains("-WinLiveMaxPlayers=42", args);
    }

    [Fact]
    public void Build_MaxPlayers_OmittedWhenZero()
    {
        var s = new AppSettings { LaunchOptions = new ServerLaunchOptions { MaxPlayers = 0 } };
        var args = ServerCommandLine.Build(s, DefaultSnapshot(), Array.Empty<string>());
        Assert.DoesNotContain(args, a => a.StartsWith("-WinLiveMaxPlayers="));
    }

    private static ServerConfigSnapshot DefaultSnapshot() => new();
}
