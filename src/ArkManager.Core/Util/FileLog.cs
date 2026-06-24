using System.Text;

namespace ArkManager.Core.Util;

/// <summary>
/// Append-only text log file, one per app run (timestamped name). Thread-safe.
/// On construction it prunes older files sharing the same prefix so logs don't pile up
/// on disk — only the newest <paramref name="keep"/> are kept. Writing never throws into
/// callers: logging is best-effort.
/// </summary>
public sealed class FileLog : IDisposable
{
    private readonly StreamWriter? _writer;
    private readonly object _gate = new();
    private readonly long _maxBytes;
    private long _written;
    private bool _capped;

    public string? Path { get; }

    /// <param name="maxBytes">Soft size cap. Once exceeded, a single notice is written and further
    /// lines are dropped (no mid-file rotation — keeps the format dead simple). 0 = unlimited.</param>
    public FileLog(string dir, string prefix, int keep = 10, long maxBytes = 0)
    {
        _maxBytes = maxBytes;
        try
        {
            Directory.CreateDirectory(dir);
            Prune(dir, prefix, keep);
            // The yyyyMMdd-HHmmss stamp sorts lexicographically = chronologically, which Prune relies on.
            Path = System.IO.Path.Combine(dir, $"{prefix}-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            _writer = new StreamWriter(Path, append: true, Encoding.UTF8) { AutoFlush = true };
        }
        catch
        {
            // A broken log sink must not take the app down — degrade to a no-op.
            _writer = null;
            Path = null;
        }
    }

    public void Write(string line)
    {
        if (_writer == null) return;
        lock (_gate)
        {
            if (_capped) return;
            try
            {
                var text = $"{DateTime.Now:HH:mm:ss} {line}";
                _writer.WriteLine(text);
                _written += text.Length + 1;
                if (_maxBytes > 0 && _written >= _maxBytes)
                {
                    _writer.WriteLine($"{DateTime.Now:HH:mm:ss} [log size cap {_maxBytes / (1024 * 1024)} MB reached — further lines omitted; restart the app for a fresh log]");
                    _capped = true;
                }
            }
            catch { /* ignore — best-effort */ }
        }
    }

    /// <summary>Delete all but the newest <paramref name="keep"/> files matching <c>prefix-*.log</c>.</summary>
    internal static void Prune(string dir, string prefix, int keep)
    {
        string[] files;
        try { files = Directory.GetFiles(dir, $"{prefix}-*.log"); }
        catch { return; }

        foreach (var f in files.OrderByDescending(f => f).Skip(Math.Max(0, keep)))
        {
            try { File.Delete(f); } catch { /* ignore */ }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            try { _writer?.Dispose(); } catch { /* ignore */ }
        }
    }
}
