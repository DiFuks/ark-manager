using ArkManager.Core.Util;

namespace ArkManager.Core.Services;

/// <summary>
/// The single log file for one app run: <c>logs/arkmanager-&lt;date&gt;.log</c>. Everything funnels
/// here — the ASA server console (tagged <c>[server]</c> by ServerManager), install/update output
/// (<c>[steamcmd]</c>), backups (<c>[auto-backup]</c>/<c>[backup]</c>) and uncaught crashes
/// (<c>[FATAL]</c>). One file per session means a user attaches a single file to a bug report.
/// Singleton; old sessions pruned and a soft size cap guards against a runaway server console.
/// </summary>
public sealed class AppLog : IDisposable
{
    private const long MaxBytes = 20 * 1024 * 1024;

    private readonly FileLog _log;

    public AppLog(AppPaths paths) => _log = new FileLog(paths.LogsDir, "arkmanager", keep: 10, maxBytes: MaxBytes);

    public string? Path => _log.Path;

    public void Write(string line) => _log.Write(line);

    public void Dispose() => _log.Dispose();
}
