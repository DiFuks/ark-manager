using System.Diagnostics;
using System.Runtime.InteropServices;
using ArkManager.Core.Models;
using ArkManager.Core.Util;

namespace ArkManager.Core.Services.Launchers;

/// <summary>
/// Launches ArkAscendedServer.exe natively on Windows — no wine, no WINEPREFIX.
/// Used by DI only when OperatingSystem.IsWindows().
/// </summary>
public sealed class NativeWindowsLauncher : IServerLauncher
{
    public async Task<RunningServer> StartAsync(
        AppSettings settings,
        IReadOnlyList<string> modIds,
        Action<string> onOutput,
        Action<int> onExit,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(settings.ServerInstallPath))
            throw new InvalidOperationException("Server install path is not set.");

        var exe = Path.Combine(
            settings.ServerInstallPath, "ShooterGame", "Binaries", "Win64", "ArkAscendedServer.exe");
        if (!File.Exists(exe))
            throw new InvalidOperationException(
                "ArkAscendedServer.exe not found. Install the server on the Install tab.");

        var args = new List<string>();
        args.AddRange(ServerCommandLine.Build(settings, modIds));

        var workDir = Path.Combine(settings.ServerInstallPath, "ShooterGame", "Binaries", "Win64");

        var tcs = new TaskCompletionSource<RunningServer>();
        _ = Task.Run(async () =>
        {
            try
            {
                var exit = await ProcessRunner.RunStreamingAsync(
                    exe, args,
                    line => onOutput(line),
                    line => onOutput(line),
                    workingDir: workDir,
                    onStarted: p =>
                    {
                        tcs.TrySetResult(new RunningServer(p.Id, DateTime.UtcNow));
                        // ASA opens a Win32 "ARK Survival Ascended Dedicated Server" console
                        // window — under wine we kill the win-mac driver, but natively the
                        // window pops up and steals focus. Run a short hider loop that keeps
                        // ShowWindow(SW_HIDE)ing any top-level window owned by this PID until
                        // the process exits. Cheap (~50ms tick) and covers late-appearing
                        // crash-reporter / loading windows too.
                        _ = Task.Run(() => HideProcessWindowsAsync(p.Id, ct));
                    },
                    ct: ct);
                onExit(exit);
            }
            catch (Exception ex)
            {
                onOutput("[launcher error] " + ex.Message);
                onExit(-1);
                tcs.TrySetException(ex);
            }
        }, CancellationToken.None);

        return await tcs.Task;
    }

    private const int SW_HIDE = 0;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private static async Task HideProcessWindowsAsync(int pid, CancellationToken ct)
    {
        // Tight loop for the first second to catch the splash/console as soon as it appears
        // (otherwise it flashes), then drop to a relaxed tick to keep hiding anything new.
        try
        {
            for (var i = 0; i < 20 && !ct.IsCancellationRequested; i++)
            {
                HideOnce(pid);
                await Task.Delay(50, ct);
            }
            while (!ct.IsCancellationRequested)
            {
                HideOnce(pid);
                if (!IsProcessAlive(pid)) return;
                await Task.Delay(1000, ct);
            }
        }
        catch (OperationCanceledException) { /* server stopping — fine */ }
    }

    private static void HideOnce(int pid)
    {
        try
        {
            EnumWindows((hWnd, _) =>
            {
                GetWindowThreadProcessId(hWnd, out var winPid);
                if (winPid == (uint)pid && IsWindowVisible(hWnd))
                    ShowWindow(hWnd, SW_HIDE);
                return true; // keep enumerating
            }, IntPtr.Zero);
        }
        catch { /* user32 missing on non-Windows host (won't run there) — ignore */ }
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch { return false; }
    }

    public Task StopAsync(int pid, CancellationToken ct = default)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            if (!p.HasExited) p.Kill(entireProcessTree: true);
        }
        catch (ArgumentException) { /* already dead */ }
        return Task.CompletedTask;
    }

    public Task<bool> IsRunningAsync(int pid, CancellationToken ct = default)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return Task.FromResult(!p.HasExited);
        }
        catch { return Task.FromResult(false); }
    }
}
