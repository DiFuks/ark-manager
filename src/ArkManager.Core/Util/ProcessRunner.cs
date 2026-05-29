using System.Diagnostics;
using System.Text;

namespace ArkManager.Core.Util;

/// <summary>
/// Lightweight wrapper around Process with streaming stdout/stderr log and convenience utilities.
/// </summary>
public sealed class ProcessRunner
{
    public sealed record RunResult(int ExitCode, string StdOut, string StdErr);

    public static async Task<RunResult> RunCaptureAsync(
        string fileName,
        IEnumerable<string> args,
        string? workingDir = null,
        IReadOnlyDictionary<string, string>? env = null,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        if (workingDir != null) psi.WorkingDirectory = workingDir;
        if (env != null) foreach (var kv in env) psi.Environment[kv.Key] = kv.Value;

        using var p = new Process { StartInfo = psi };
        if (!p.Start())
            throw new InvalidOperationException($"Failed to start {fileName}");

        var stdoutTask = p.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        return new RunResult(p.ExitCode, await stdoutTask, await stderrTask);
    }

    /// <summary>
    /// Runs the process, streaming each "line" to a callback. A line ends on either
    /// LF or CR — so steamcmd's in-place `\r`-only progress updates also surface
    /// in the UI, not only the rare terminator newlines.
    /// </summary>
    public static async Task<int> RunStreamingAsync(
        string fileName,
        IEnumerable<string> args,
        Action<string> onStdOut,
        Action<string> onStdErr,
        string? workingDir = null,
        IReadOnlyDictionary<string, string>? env = null,
        Action<Process>? onStarted = null,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        if (workingDir != null) psi.WorkingDirectory = workingDir;
        if (env != null) foreach (var kv in env) psi.Environment[kv.Key] = kv.Value;

        using var p = new Process { StartInfo = psi };
        if (!p.Start())
            throw new InvalidOperationException($"Failed to start {fileName}");
        onStarted?.Invoke(p);

        var stdoutTask = DrainAsync(p.StandardOutput, onStdOut, ct);
        var stderrTask = DrainAsync(p.StandardError, onStdErr, ct);

        using (ct.Register(() => { try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { /* ignore */ } }))
        {
            await p.WaitForExitAsync(ct);
        }
        // Drain the rest of the pipes before returning — otherwise the tail of
        // the output races the caller's RefreshInstalledVersion() check.
        try { await Task.WhenAll(stdoutTask, stderrTask); } catch { /* ignore */ }
        return p.ExitCode;
    }

    private static async Task DrainAsync(StreamReader reader, Action<string> cb, CancellationToken ct)
    {
        var splitter = new LineSplitter();
        var buf = new char[1024];
        try
        {
            while (true)
            {
                int n;
                try { n = await reader.ReadAsync(buf.AsMemory(), ct); }
                catch (OperationCanceledException) { break; }
                if (n == 0) break;
                foreach (var line in splitter.Feed(buf.AsSpan(0, n))) cb(line);
            }
        }
        catch { /* swallow — pipe closed mid-read on process exit */ }
        var tail = splitter.Flush();
        if (tail != null) cb(tail);
    }
}

/// <summary>
/// Splits a stream of characters into "lines", treating either CR or LF as a
/// terminator. Empty segments are suppressed, so CRLF emits one line. Stateful
/// across <see cref="Feed"/> calls — the unterminated tail is held until the
/// next chunk or <see cref="Flush"/>.
/// </summary>
internal sealed class LineSplitter
{
    private readonly StringBuilder _buf = new();

    public IReadOnlyList<string> Feed(ReadOnlySpan<char> chunk)
    {
        List<string>? lines = null;
        foreach (var c in chunk)
        {
            if (c == '\r' || c == '\n')
            {
                if (_buf.Length > 0)
                {
                    (lines ??= new List<string>()).Add(_buf.ToString());
                    _buf.Clear();
                }
            }
            else
            {
                _buf.Append(c);
            }
        }
        return (IReadOnlyList<string>?)lines ?? Array.Empty<string>();
    }

    public string? Flush()
    {
        if (_buf.Length == 0) return null;
        var s = _buf.ToString();
        _buf.Clear();
        return s;
    }
}
