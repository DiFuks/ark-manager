using ArkManager.Core.Models;
using ArkManager.Core.Services;
using ArkManager.Core.Services.Firewall;
using Xunit;

namespace ArkManager.Core.Tests.Firewall;

public class ShouldEnsureFirewallRulesTests
{
    private sealed class FakeFirewall : IFirewallService
    {
        public bool IsSupported { get; init; }
        public bool IsElevated  { get; init; }
        public Task EnsureRulesAsync(int g, int q, int r, CancellationToken ct) => Task.CompletedTask;
        public event Action<string>? Log { add { } remove { } }
    }

    [Theory]
    [InlineData(true,  true,  true,  true)]
    [InlineData(true,  true,  false, false)]
    [InlineData(true,  false, true,  false)]
    [InlineData(true,  false, false, false)]
    [InlineData(false, true,  true,  false)]
    [InlineData(false, true,  false, false)]
    [InlineData(false, false, true,  false)]
    [InlineData(false, false, false, false)]
    public void ShouldEnsureFirewallRules_TruthTable(
        bool setting, bool supported, bool elevated, bool expected)
    {
        var s = new AppSettings { ManageFirewallRules = setting };
        var fw = new FakeFirewall { IsSupported = supported, IsElevated = elevated };
        Assert.Equal(expected, ServerManager.ShouldEnsureFirewallRules(s, fw));
    }
}
