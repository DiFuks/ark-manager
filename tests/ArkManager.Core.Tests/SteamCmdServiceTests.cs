using ArkManager.Core.Services.Steam;
using Xunit;

namespace ArkManager.Core.Tests;

public class SteamCmdServiceTests
{
    [Fact]
    public void ParseManifest_ExtractsBuildIdAndLastUpdated()
    {
        var acf = """
        "AppState"
        {
            "appid"		"2430930"
            "name"		"ARK: Survival Ascended Dedicated Server"
            "LastUpdated"		"1779308475"
            "buildid"		"23321173"
            "InstalledDepots"
            {
                "1004" { "manifest" "5612541580377302256" }
            }
        }
        """;
        var v = SteamCmdService.ParseManifest(acf);
        Assert.NotNull(v);
        Assert.Equal("23321173", v!.BuildId);
        Assert.Equal(1779308475L, v.LastUpdated!.Value.ToUnixTimeSeconds());
    }

    [Fact]
    public void ParseManifest_NoBuildId_ReturnsNull()
    {
        Assert.Null(SteamCmdService.ParseManifest("\"AppState\" { \"name\" \"x\" }"));
    }

    [Fact]
    public void ParseLatestBuildId_TakesPublicBranch_NotBeta()
    {
        var output = """
        "branches"
        {
            "public"
            {
                "buildid"		"23321173"
                "timeupdated"	"1779308475"
            }
            "beta"
            {
                "buildid"		"99999999"
            }
        }
        """;
        Assert.Equal("23321173", SteamCmdService.ParseLatestBuildId(output));
    }

    [Fact]
    public void ParseLatestBuildId_NoMatch_ReturnsNull()
    {
        Assert.Null(SteamCmdService.ParseLatestBuildId("garbage output without a buildid"));
    }
}
