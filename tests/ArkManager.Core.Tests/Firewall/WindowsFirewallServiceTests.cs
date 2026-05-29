using ArkManager.Core.Services.Firewall;
using ArkManager.Core.Util;
using Xunit;

namespace ArkManager.Core.Tests.Firewall;

public class WindowsFirewallServiceTests
{
    private sealed class FakeRun
    {
        public readonly List<(string FileName, string[] Args)> Calls = new();
        public readonly List<ProcessRunner.RunResult> ScriptedResults = new();
        private int _i;

        public Task<ProcessRunner.RunResult> Invoke(
            string fileName, IEnumerable<string> args, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Calls.Add((fileName, args.ToArray()));
            var r = _i < ScriptedResults.Count
                ? ScriptedResults[_i++]
                : new ProcessRunner.RunResult(0, "", "");
            return Task.FromResult(r);
        }
    }

    private static WindowsFirewallService Build(FakeRun fake)
        => new(fake.Invoke);

    [Fact]
    public async Task EnsureRulesAsync_InvokesNetshSixTimesWithCorrectArgs()
    {
        var fake = new FakeRun();
        var svc = Build(fake);

        await svc.EnsureRulesAsync(7777, 27015, 27020, CancellationToken.None);

        Assert.Equal(6, fake.Calls.Count);
        foreach (var (fn, _) in fake.Calls) Assert.Equal("netsh", fn);

        Assert.Equal(new[] { "advfirewall", "firewall", "delete", "rule", "name=ArkManager: ASA Game" },
            fake.Calls[0].Args);
        Assert.Equal(new[] { "advfirewall", "firewall", "add", "rule",
            "name=ArkManager: ASA Game", "dir=in", "action=allow",
            "protocol=UDP", "localport=7777" }, fake.Calls[1].Args);

        Assert.Equal(new[] { "advfirewall", "firewall", "delete", "rule", "name=ArkManager: ASA Query" },
            fake.Calls[2].Args);
        Assert.Equal(new[] { "advfirewall", "firewall", "add", "rule",
            "name=ArkManager: ASA Query", "dir=in", "action=allow",
            "protocol=UDP", "localport=27015" }, fake.Calls[3].Args);

        Assert.Equal(new[] { "advfirewall", "firewall", "delete", "rule", "name=ArkManager: ASA RCON" },
            fake.Calls[4].Args);
        Assert.Equal(new[] { "advfirewall", "firewall", "add", "rule",
            "name=ArkManager: ASA RCON", "dir=in", "action=allow",
            "protocol=TCP", "localport=27020" }, fake.Calls[5].Args);
    }

    [Fact]
    public async Task EnsureRulesAsync_DeleteNonZeroExitIsSilent()
    {
        var fake = new FakeRun();
        // delete results (indices 0, 2, 4) fail with exit=1 — normal "rule didn't exist".
        fake.ScriptedResults.Add(new ProcessRunner.RunResult(1, "", "No rules match the specified criteria."));
        fake.ScriptedResults.Add(new ProcessRunner.RunResult(0, "Ok.", ""));
        fake.ScriptedResults.Add(new ProcessRunner.RunResult(1, "", "No rules match the specified criteria."));
        fake.ScriptedResults.Add(new ProcessRunner.RunResult(0, "Ok.", ""));
        fake.ScriptedResults.Add(new ProcessRunner.RunResult(1, "", "No rules match the specified criteria."));
        fake.ScriptedResults.Add(new ProcessRunner.RunResult(0, "Ok.", ""));

        var svc = Build(fake);
        var logged = new List<string>();
        svc.Log += s => logged.Add(s);

        await svc.EnsureRulesAsync(7777, 27015, 27020, CancellationToken.None);

        Assert.Empty(logged);
    }

    [Fact]
    public async Task EnsureRulesAsync_AddFailureLogsAndContinues()
    {
        var fake = new FakeRun();
        // delete OK, add (idx 1) fails for Game rule. Remaining rules still added.
        fake.ScriptedResults.Add(new ProcessRunner.RunResult(0, "Ok.", ""));
        fake.ScriptedResults.Add(new ProcessRunner.RunResult(1, "", "Access is denied."));
        fake.ScriptedResults.Add(new ProcessRunner.RunResult(0, "Ok.", ""));
        fake.ScriptedResults.Add(new ProcessRunner.RunResult(0, "Ok.", ""));
        fake.ScriptedResults.Add(new ProcessRunner.RunResult(0, "Ok.", ""));
        fake.ScriptedResults.Add(new ProcessRunner.RunResult(0, "Ok.", ""));

        var svc = Build(fake);
        var logged = new List<string>();
        svc.Log += s => logged.Add(s);

        await svc.EnsureRulesAsync(7777, 27015, 27020, CancellationToken.None);

        Assert.Single(logged);
        Assert.Contains("ArkManager: ASA Game", logged[0]);
        Assert.Contains("7777", logged[0]);
        Assert.Contains("Access is denied", logged[0]);
        Assert.Equal(6, fake.Calls.Count);
    }

    [Fact]
    public async Task EnsureRulesAsync_CancellationPropagates()
    {
        var fake = new FakeRun();
        var svc = Build(fake);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => svc.EnsureRulesAsync(7777, 27015, 27020, cts.Token));
    }
}
