using System.Runtime.Versioning;
using System.Security.Principal;
using ArkManager.Core.Util;

namespace ArkManager.Core.Services.Firewall;

/// <summary>
/// Windows-only impl. Manipulates inbound rules via <c>netsh advfirewall firewall</c>.
/// Uses three fixed rule names so port changes between Starts simply replace stale rules
/// via the delete-then-add cycle.
/// </summary>
public sealed class WindowsFirewallService : IFirewallService
{
    private static readonly (string Name, string Protocol, Func<(int g, int q, int r), int> Port)[] Rules =
    {
        ("ArkManager: ASA Game",  "UDP", ports => ports.g),
        ("ArkManager: ASA Query", "UDP", ports => ports.q),
        ("ArkManager: ASA RCON",  "TCP", ports => ports.r),
    };

    internal delegate Task<ProcessRunner.RunResult> RunCapture(
        string fileName, IEnumerable<string> args, CancellationToken ct);

    private readonly RunCapture _run;

    public WindowsFirewallService()
        : this((f, a, ct) => ProcessRunner.RunCaptureAsync(f, a, ct: ct)) { }

    internal WindowsFirewallService(RunCapture run) { _run = run; }

    public bool IsSupported => OperatingSystem.IsWindows();

    public bool IsElevated
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return false;
            return CheckElevatedWindows();
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool CheckElevatedWindows()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public event Action<string>? Log;

    public async Task EnsureRulesAsync(int gamePort, int queryPort, int rconPort, CancellationToken ct)
    {
        var ports = (gamePort, queryPort, rconPort);
        foreach (var rule in Rules)
        {
            ct.ThrowIfCancellationRequested();
            var port = rule.Port(ports);

            try
            {
                await _run("netsh", new[] {
                    "advfirewall", "firewall", "delete", "rule",
                    $"name={rule.Name}"
                }, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch { /* silent — delete failures aren't user-actionable */ }

            try
            {
                var res = await _run("netsh", new[] {
                    "advfirewall", "firewall", "add", "rule",
                    $"name={rule.Name}", "dir=in", "action=allow",
                    $"protocol={rule.Protocol}", $"localport={port}"
                }, ct);

                if (res.ExitCode != 0)
                    Log?.Invoke($"failed to add '{rule.Name}' (port {port}): {res.StdErr.Trim()}");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Log?.Invoke($"failed to add '{rule.Name}' (port {port}): {ex.Message}");
            }
        }
    }
}
