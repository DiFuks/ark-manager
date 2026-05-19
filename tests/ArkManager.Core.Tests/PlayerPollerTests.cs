using ArkManager.Core.Services.Rcon;
using Xunit;

namespace ArkManager.Core.Tests;

public class PlayerPollerTests
{
    [Fact]
    public void Parse_NoPlayers_ReturnsZero()
    {
        var s = PlayerPoller.ParseListPlayers("No Players Connected");
        Assert.Equal(0, s.Count);
        Assert.Empty(s.Names);
    }

    [Fact]
    public void Parse_TwoPlayers_ReturnsBoth()
    {
        var raw = "0. AlphaWolf, 76561198000000001\n1. BetaCat, 76561198000000002";
        var s = PlayerPoller.ParseListPlayers(raw);
        Assert.Equal(2, s.Count);
        Assert.Equal(new[] { "AlphaWolf", "BetaCat" }, s.Names);
    }

    [Fact]
    public void Parse_IgnoresNonMatchingLines()
    {
        var raw = "garbage\n0. Solo, 12345";
        var s = PlayerPoller.ParseListPlayers(raw);
        Assert.Equal(1, s.Count);
        Assert.Equal("Solo", s.Names[0]);
    }
}
