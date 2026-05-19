using ArkManager.Core.Services.Config;
using Xunit;

namespace ArkManager.Core.Tests;

public class IniFileTests
{
    [Fact]
    public void RoundTrip_PreservesCommentsAndOrder()
    {
        var src = "; header\n[A]\nfoo=1\n; mid\nbar=hello\n[B]\nx=y\n";
        var ini = IniFile.Parse(src);
        var back = ini.ToString();
        Assert.Contains("; header", back);
        Assert.Contains("; mid", back);
        Assert.Contains("[A]", back);
        Assert.Contains("foo=1", back);
        Assert.Contains("bar=hello", back);
        Assert.Contains("[B]", back);
        Assert.Contains("x=y", back);
    }

    [Fact]
    public void SetSingle_AddsOrReplaces()
    {
        var ini = IniFile.Parse("[A]\nfoo=1\n");
        var a = ini.TryGetSection("A")!;
        a.SetSingle("foo", "2");
        a.SetSingle("bar", "10");
        Assert.Equal("2", a.GetSingle("foo"));
        Assert.Equal("10", a.GetSingle("bar"));
    }

    [Fact]
    public void GetAll_ReturnsAllDuplicates()
    {
        var ini = IniFile.Parse("[A]\nfoo=1\nfoo=2\nfoo=3\n");
        var a = ini.TryGetSection("A")!;
        Assert.Equal(new[] { "1", "2", "3" }, a.GetAll("foo"));
        // GetSingle берёт последнее объявление.
        Assert.Equal("3", a.GetSingle("foo"));
    }

    [Fact]
    public void GetOrCreateSection_IsIdempotent()
    {
        var ini = new IniFile();
        var a1 = ini.GetOrCreateSection("ServerSettings");
        var a2 = ini.GetOrCreateSection("serversettings");
        Assert.Same(a1, a2);
    }
}
