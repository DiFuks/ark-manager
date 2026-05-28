using ArkManager.Core.Services.Steam;
using Xunit;

namespace ArkManager.Core.Tests;

public class SteamCmdBootstrapTests
{
    [Fact]
    public void Mac_returns_osx_tarball_url()
    {
        var url = SteamCmdService.SelectBootstrapUrl(SteamCmdHostOs.MacOS);
        Assert.Equal("https://steamcdn-a.akamaihd.net/client/installer/steamcmd_osx.tar.gz", url);
    }

    [Fact]
    public void Linux_returns_linux_tarball_url()
    {
        var url = SteamCmdService.SelectBootstrapUrl(SteamCmdHostOs.Linux);
        Assert.Equal("https://steamcdn-a.akamaihd.net/client/installer/steamcmd_linux.tar.gz", url);
    }

    [Fact]
    public void Windows_returns_windows_zip_url()
    {
        var url = SteamCmdService.SelectBootstrapUrl(SteamCmdHostOs.Windows);
        Assert.Equal("https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip", url);
    }

    [Fact]
    public void ForcePlatformWindows_arg_omitted_on_windows_host()
    {
        var args = SteamCmdService.BuildInstallArgs("/path", SteamCmdHostOs.Windows);
        Assert.DoesNotContain("+@sSteamCmdForcePlatformType", args);
    }

    [Fact]
    public void ForcePlatformWindows_arg_present_on_mac_and_linux()
    {
        var macArgs = SteamCmdService.BuildInstallArgs("/path", SteamCmdHostOs.MacOS);
        var linuxArgs = SteamCmdService.BuildInstallArgs("/path", SteamCmdHostOs.Linux);
        Assert.Contains("+@sSteamCmdForcePlatformType", macArgs);
        Assert.Contains("windows", macArgs);
        Assert.Contains("+@sSteamCmdForcePlatformType", linuxArgs);
        Assert.Contains("windows", linuxArgs);
    }
}
