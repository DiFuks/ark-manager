using System.Diagnostics;

namespace ArkManager.Core.Util;

/// <summary>
/// Лёгкий wrapper над Process с потоковым stdout/stderr-логом и удобными утилитами.
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
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        if (workingDir != null) psi.WorkingDirectory = workingDir;
        if (env != null) foreach (var kv in env) psi.Environment[kv.Key] = kv.Value;

        using var p = new Process { StartInfo = psi };
        if (!p.Start())
            throw new InvalidOperationException($"Не удалось запустить {fileName}");

        var stdoutTask = p.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        return new RunResult(p.ExitCode, await stdoutTask, await stderrTask);
    }

    /// <summary>
    /// Запуск с потоковым отдаванием каждой строки в коллбек.
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
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        if (workingDir != null) psi.WorkingDirectory = workingDir;
        if (env != null) foreach (var kv in env) psi.Environment[kv.Key] = kv.Value;

        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, e) => { if (e.Data != null) onStdOut(e.Data); };
        p.ErrorDataReceived  += (_, e) => { if (e.Data != null) onStdErr(e.Data); };

        if (!p.Start())
            throw new InvalidOperationException($"Не удалось запустить {fileName}");
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        onStarted?.Invoke(p);

        using (ct.Register(() => { try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { /* ignore */ } }))
        {
            await p.WaitForExitAsync(ct);
        }
        return p.ExitCode;
    }
}
