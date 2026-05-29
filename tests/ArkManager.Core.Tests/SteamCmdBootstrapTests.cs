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

    [Fact]
    public void Warmup_args_on_windows_only_login_and_quit()
    {
        var args = SteamCmdService.BuildWarmupArgs(SteamCmdHostOs.Windows);
        Assert.Equal(new[] { "+login", "anonymous", "+quit" }, args);
    }

    [Fact]
    public void Warmup_args_on_nonwindows_include_force_platform()
    {
        var args = SteamCmdService.BuildWarmupArgs(SteamCmdHostOs.MacOS);
        Assert.Equal(
            new[] { "+@sSteamCmdForcePlatformType", "windows", "+login", "anonymous", "+quit" },
            args);
    }

    [Fact]
    public void Warmup_args_never_include_app_update()
    {
        // Whole point of the warmup is to NOT carry app_update — otherwise the
        // self-update relaunch would silently drop it (see SteamCmdService docs).
        foreach (var os in new[] { SteamCmdHostOs.Windows, SteamCmdHostOs.MacOS, SteamCmdHostOs.Linux })
        {
            var args = SteamCmdService.BuildWarmupArgs(os);
            Assert.DoesNotContain("+app_update", args);
            Assert.DoesNotContain("+app_info_update", args);
            Assert.DoesNotContain("+force_install_dir", args);
        }
    }
}
