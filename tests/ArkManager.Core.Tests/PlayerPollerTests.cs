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

    // ASA uses Epic Online Services as its identity layer even when players join via Steam — so
    // ListPlayers IDs are always 32-hex EOS Account IDs (e.g. "00025dbef45f4f10a4d9d69b041389f2"),
    // not Steam 17-digit IDs. The old digit-only regex silently dropped every player.
    [Fact]
    public void Parse_EosHexId_IsAccepted()
    {
        var raw = "0. Alpha, 00025dbef45f4f10a4d9d69b041389f2\n1. Beta, ffff112233445566778899aabbccddee";
        var s = PlayerPoller.ParseListPlayers(raw);
        Assert.Equal(2, s.Count);
        Assert.Equal(new[] { "Alpha", "Beta" }, s.Names);
    }

    // Cyrillic / other non-ASCII nicknames are common; ensure the regex handles them once the
    // bytes are decoded as UTF-8 (the RconClient was switched off Encoding.ASCII at the same time).
    [Fact]
    public void Parse_CyrillicName_IsAccepted()
    {
        var raw = "0. Пушистик, 00025dbef45f4f10a4d9d69b041389f2";
        var s = PlayerPoller.ParseListPlayers(raw);
        Assert.Equal(1, s.Count);
        Assert.Equal("Пушистик", s.Names[0]);
    }
}
