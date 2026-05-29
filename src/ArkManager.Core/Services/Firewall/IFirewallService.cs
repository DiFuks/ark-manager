namespace ArkManager.Core.Services.Firewall;

/// <summary>
/// Manages OS-level inbound firewall rules for ASA server ports. Implementations are
/// per-OS — Windows uses netsh, other platforms get a noop. The UI reads
/// <see cref="IsSupported"/> to hide the feature entirely on non-Windows, and
/// <see cref="IsElevated"/> to disable the checkbox when admin rights are missing.
/// </summary>
public interface IFirewallService
{
    /// <summary>True on Windows. Used by UI to decide visibility of the feature.</summary>
    bool IsSupported { get; }

    /// <summary>True when the current process can modify firewall rules (admin on Windows).</summary>
    bool IsElevated { get; }

    /// <summary>
    /// Idempotent: deletes any rule with our well-known name then adds it back for the
    /// given port. Safe to call across port changes — fixed rule names mean the old
    /// rule is always replaced.
    /// </summary>
    Task EnsureRulesAsync(int gamePort, int queryPort, int rconPort, CancellationToken ct);

    /// <summary>Stream-style log of non-fatal failures (e.g. add-rule exit≠0).</summary>
    event Action<string>? Log;
}
