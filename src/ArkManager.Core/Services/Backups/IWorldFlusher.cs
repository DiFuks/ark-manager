namespace ArkManager.Core.Services.Backups;

/// <summary>
/// Flushes the live server world to disk before a snapshot is taken. Implemented by
/// <see cref="ServerManager"/>; injected into <see cref="BackupService"/> so a backup
/// captures the current world rather than just the last server auto-save. Kept as a
/// narrow interface so BackupService doesn't depend on the whole process-lifecycle manager.
/// </summary>
public interface IWorldFlusher
{
    /// <summary>
    /// Issues <c>saveworld</c> via RCON and waits for the disk flush. No-op returning false
    /// when the world isn't ready or RCON/admin-password is unavailable.
    /// </summary>
    Task<bool> TrySaveWorldAsync(CancellationToken ct = default);
}
