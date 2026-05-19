using ArkManager.Core.Services.Launchers;
using ArkManager.Core.Services.Steam;
using ArkManager.Core.Util;

namespace ArkManager.Core.Services.Doctor;

public sealed record CheckResult(string Name, bool Ok, string Detail, string? FixHint = null);

/// <summary>Самопроверка готовности окружения к работе.</summary>
public sealed class DoctorService
{
    private readonly SettingsService _settings;
    private readonly SteamCmdService _steam;
    private readonly LauncherFactory _launchers;

    public DoctorService(SettingsService settings, SteamCmdService steam, LauncherFactory launchers)
    {
        _settings = settings;
        _steam = steam;
        _launchers = launchers;
    }

    public async Task<IReadOnlyList<CheckResult>> RunAsync(CancellationToken ct = default)
    {
        var results = new List<CheckResult>();

        // 1. SteamCMD
        if (_steam.IsSteamCmdInstalled())
            results.Add(new("SteamCMD", true, _steam.ResolveSteamCmdBinary()));
        else
            results.Add(new("SteamCMD", false, "Не установлен", "Нажмите «Install SteamCMD» на вкладке Install."));

        // 2. ASA server files
        var serverPath = _settings.Current.ServerInstallPath ?? "";
        var exe = Path.Combine(serverPath, "ShooterGame", "Binaries", "Win64", "ArkAscendedServer.exe");
        results.Add(File.Exists(exe)
            ? new("ASA Dedicated Server", true, exe)
            : new("ASA Dedicated Server", false, "Не установлен в " + serverPath, "Нажмите «Install / Update server»."));

        // 3. Launchers
        foreach (var (mode, launcher) in _launchers.All())
        {
            var probe = await launcher.ProbeAsync(ct);
            results.Add(new($"Runtime: {mode}", probe.Available, probe.DiagnosticMessage ?? ""));
        }

        // 4. Brew
        results.Add(File.Exists("/opt/homebrew/bin/brew") || File.Exists("/usr/local/bin/brew")
            ? new("Homebrew", true, "OK")
            : new("Homebrew", false, "brew не найден", "Установите https://brew.sh"));

        // 5. Свободное место в DataDir
        try
        {
            var di = new DriveInfo(Path.GetPathRoot(serverPath) ?? "/");
            var gb = di.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0;
            results.Add(new("Свободное место", gb >= 20, $"{gb:F1} GB на {di.Name}",
                gb < 20 ? "ASA-сервер занимает ~15-25 GB. Освободите место." : null));
        }
        catch (Exception ex)
        {
            results.Add(new("Свободное место", false, ex.Message));
        }

        return results;
    }

    public async Task<bool> InstallWhiskyViaBrewAsync(Action<string> onOutput, CancellationToken ct = default)
    {
        var brew = File.Exists("/opt/homebrew/bin/brew") ? "/opt/homebrew/bin/brew"
                 : File.Exists("/usr/local/bin/brew") ? "/usr/local/bin/brew"
                 : null;
        if (brew == null)
        {
            onOutput("brew не найден.");
            return false;
        }

        var exit = await ProcessRunner.RunStreamingAsync(
            brew, new[] { "install", "--cask", "whisky" },
            onStdOut: onOutput, onStdErr: onOutput,
            ct: ct);
        return exit == 0;
    }
}
