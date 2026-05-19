using System.Diagnostics;
using ArkManager.Core.Models;
using ArkManager.Core.Util;

namespace ArkManager.Core.Services.Launchers;

/// <summary>
/// Запуск сервера внутри Parallels VM через prlctl exec.
/// Требования: гостевая Windows VM, prl-tools, расшаренная папка (share home или mapped),
/// чтобы путь к ArkAscendedServer.exe был доступен в гостевой системе.
///
/// settings.ServerInstallPath здесь — путь на хосте, а в гостевой системе он маппится через
/// Z:\Mac\Home\... (если включён shared home). Чтобы не угадывать, мы транслируем хост-путь
/// в гостевой по простой схеме: подменяем $HOME на Z:\Mac\Home. Если этого недостаточно —
/// пользователь укажет переменную окружения ARK_GUEST_PATH_PREFIX.
/// </summary>
public sealed class ParallelsLauncher : IServerLauncher
{
    private const string Prlctl = "/usr/local/bin/prlctl";

    public async Task<LauncherStatus> ProbeAsync(CancellationToken ct = default)
    {
        if (!File.Exists(Prlctl))
            return new LauncherStatus(false, "prlctl не найден (Parallels Desktop не установлен или нет CLI).");
        try
        {
            var r = await ProcessRunner.RunCaptureAsync(Prlctl, new[] { "list", "--all" }, ct: ct);
            if (r.ExitCode != 0)
                return new LauncherStatus(false, r.StdErr.Trim());
            return new LauncherStatus(true, r.StdOut.Trim().Split('\n').FirstOrDefault() ?? "OK");
        }
        catch (Exception ex)
        {
            return new LauncherStatus(false, ex.Message);
        }
    }

    public async Task<RunningServer> StartAsync(
        AppSettings settings,
        IReadOnlyList<string> modIds,
        Action<string> onOutput,
        Action<int> onExit,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(settings.ParallelsVmName))
            throw new InvalidOperationException("ParallelsVmName не задан в настройках.");
        if (string.IsNullOrWhiteSpace(settings.ServerInstallPath))
            throw new InvalidOperationException("ServerInstallPath не задан.");

        var guestPath = TranslateToGuestPath(settings.ServerInstallPath);
        var exe = guestPath + @"\ShooterGame\Binaries\Win64\ArkAscendedServer.exe";
        var args = ServerCommandLine.Build(settings, modIds);

        // prlctl exec <vm> cmd /c "<exe> <args...>"
        var cmd = $"\"{exe}\" " + string.Join(" ", args.Select(QuoteIfNeeded));
        var prlArgs = new List<string> { "exec", settings.ParallelsVmName!, "cmd", "/c", cmd };

        Process? started = null;
        var tcs = new TaskCompletionSource<RunningServer>();
        _ = Task.Run(async () =>
        {
            try
            {
                var exit = await ProcessRunner.RunStreamingAsync(
                    Prlctl, prlArgs,
                    line => onOutput(line),
                    line => onOutput(line),
                    onStarted: p =>
                    {
                        started = p;
                        tcs.TrySetResult(new RunningServer(p.Id, DateTime.UtcNow));
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

    public Task StopAsync(int pid, CancellationToken ct = default)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            if (!p.HasExited) p.Kill(entireProcessTree: true);
        }
        catch (ArgumentException) { /* gone */ }
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

    private static string TranslateToGuestPath(string hostPath)
    {
        var prefix = Environment.GetEnvironmentVariable("ARK_GUEST_PATH_PREFIX");
        if (!string.IsNullOrWhiteSpace(prefix))
        {
            // Pure override — берём имя последней папки от ServerInstallPath.
            return prefix.TrimEnd('\\') + "\\" + Path.GetFileName(hostPath.TrimEnd('/'));
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (hostPath.StartsWith(home, StringComparison.Ordinal))
        {
            var rel = hostPath[home.Length..].TrimStart('/').Replace('/', '\\');
            return @"\\Mac\Home\" + rel;
        }

        // По умолчанию Parallels монтирует / как \\Mac\AllFiles.
        return @"\\Mac\AllFiles\" + hostPath.TrimStart('/').Replace('/', '\\');
    }

    private static string QuoteIfNeeded(string s)
        => s.Contains(' ') ? "\"" + s.Replace("\"", "\\\"") + "\"" : s;
}
