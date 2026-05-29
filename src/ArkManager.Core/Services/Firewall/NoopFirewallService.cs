namespace ArkManager.Core.Services.Firewall;

/// <summary>No-op implementation used on non-Windows. Constructed by DI when OS != Windows.</summary>
public sealed class NoopFirewallService : IFirewallService
{
    public bool IsSupported => false;
    public bool IsElevated  => false;
    public Task EnsureRulesAsync(int g, int q, int r, CancellationToken ct) => Task.CompletedTask;
    public event Action<string>? Log { add { } remove { } }
}
