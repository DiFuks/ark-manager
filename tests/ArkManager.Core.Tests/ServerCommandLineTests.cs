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
    public void Build_RoutesFullLogToStdout()
    {
        // Сервер запускается headless (без winemac.drv → нет окна), поэтому полный UE-лог
        // нужно гнать в stdout, иначе он уходит только в окно/файл и не виден в ArkManager.
        var s = new AppSettings();
        var args = ServerCommandLine.Build(s, Array.Empty<string>());
        Assert.Contains("-stdout", args);
        Assert.Contains("-FullStdOutLogOutput", args);
        Assert.Contains("-unattended", args);
    }

    [Fact]
    public void Build_AddsModsFlag_WhenModsPresent()
    {
        var s = new AppSettings();
        var args = ServerCommandLine.Build(s, new[] { "928597", "929094" });
        Assert.Contains("-mods=928597,929094", args);
    }

    [Fact]
    public void Build_Passwords_NotInUrlQuery()
    {
        // Пароли пишутся ТОЛЬКО в ini (через ConfigService.ApplyLaunchOptionsToIni),
        // в URL не кладём — иначе ASA склеивает хвост строки в значение пароля.
        var s = new AppSettings();
        s.LaunchOptions.ServerPassword = "srv";
        s.LaunchOptions.AdminPassword = "adm";
        s.LaunchOptions.SpectatorPassword = "spec";
        var args = ServerCommandLine.Build(s, Array.Empty<string>());
        Assert.DoesNotContain("ServerPassword", args[0]);
        Assert.DoesNotContain("ServerAdminPassword", args[0]);
        Assert.DoesNotContain("SpectatorPassword", args[0]);
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

    [Fact]
    public void Build_Cluster_AppendsFlags()
    {
        var s = new AppSettings();
        s.LaunchOptions.ClusterId = "my-cluster";
        s.LaunchOptions.ClusterDirOverride = "/tmp/cluster";
        var args = ServerCommandLine.Build(s, Array.Empty<string>());
        Assert.Contains("-ClusterId=my-cluster", args);
        Assert.Contains(args, a => a.StartsWith("-ClusterDirOverride="));
    }

    [Fact]
    public void Build_Rcon_NotInUrlQuery()
    {
        // RCONEnabled/RCONPort идут только в GameUserSettings.ini.
        var s = new AppSettings();
        s.LaunchOptions.RconEnabled = true;
        s.LaunchOptions.RconPort = 27042;
        var args = ServerCommandLine.Build(s, Array.Empty<string>());
        Assert.DoesNotContain("RCONEnabled", args[0]);
        Assert.DoesNotContain("RCONPort", args[0]);
    }

    [Fact]
    public void Build_NoBattlEye_Off_SkipsFlag()
    {
        var s = new AppSettings();
        s.LaunchOptions.NoBattlEye = false;
        var args = ServerCommandLine.Build(s, Array.Empty<string>());
        Assert.DoesNotContain("-NoBattlEye", args);
    }
}
