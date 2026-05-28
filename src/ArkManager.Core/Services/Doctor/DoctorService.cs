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
    private readonly IServerLauncher _launcher;

    public DoctorService(SettingsService settings, SteamCmdService steam, IServerLauncher launcher)
    {
        _settings = settings;
        _steam = steam;
        _launcher = launcher;
    }

    public async Task<IReadOnlyList<CheckResult>> RunAsync(CancellationToken ct = default)
    {
        var results = new List<CheckResult>();

        // 1. SteamCMD
        if (_steam.IsSteamCmdInstalled())
            results.Add(new("SteamCMD", true, _steam.ResolveSteamCmdBinary()));
        else
            results.Add(new("SteamCMD", false, "Not installed", "Click \"Install SteamCMD\" on the Install tab."));

        // 2. ASA server files
        var serverPath = _settings.Current.ServerInstallPath ?? "";
        var exe = Path.Combine(serverPath, "ShooterGame", "Binaries", "Win64", "ArkAscendedServer.exe");
        results.Add(File.Exists(exe)
            ? new("ASA Dedicated Server", true, exe)
            : new("ASA Dedicated Server", false, "Not installed in " + serverPath, "Click \"Install / Update server\"."));

        // 3. Wine runtime
        results.Add(new("Wine", true, "(probe removed — Doctor is being deleted)"));

        // 4. Brew
        results.Add(File.Exists("/opt/homebrew/bin/brew") || File.Exists("/usr/local/bin/brew")
            ? new("Homebrew", true, "OK")
            : new("Homebrew", false, "brew not found", "Install from https://brew.sh"));

        // 5. Свободное место в DataDir
        try
        {
            var di = new DriveInfo(Path.GetPathRoot(serverPath) ?? "/");
            var gb = di.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0;
            results.Add(new("Disk free", gb >= 20, FormattableString.Invariant($"{gb:F1} GB on {di.Name}"),
                gb < 20 ? "ASA server requires ~15-25 GB. Free up disk space." : null));
        }
        catch (Exception ex)
        {
            results.Add(new("Disk free", false, ex.Message));
        }

        return results;
    }

    public async Task<bool> InstallWineViaBrewAsync(Action<string> onOutput, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsMacOS())
        {
            onOutput("Wine installation via brew is only supported on macOS.");
            return false;
        }

        var brew = File.Exists("/opt/homebrew/bin/brew") ? "/opt/homebrew/bin/brew"
                 : File.Exists("/usr/local/bin/brew") ? "/usr/local/bin/brew"
                 : null;
        if (brew == null)
        {
            onOutput("brew not found.");
            return false;
        }

        // Запускаем установку в Terminal.app, а не как child-процесс ArkManager.
        // Причина: gstreamer-runtime (dep wine-stable) — это .pkg-инсталлер с sudo,
        // brew открывает пароль через tty. У нас stdio редиректнут — sudo виснет навсегда.
        // Параллельно скрипт проверяет/ставит Rosetta 2 (wine-stable собран под Intel).
        var script = $"""
            #!/bin/bash
            set -e
            echo "==> ArkManager: installing wine-stable"
            if ! /usr/bin/arch -x86_64 /usr/bin/true >/dev/null 2>&1; then
                echo "Rosetta 2 not installed. Installing..."
                softwareupdate --install-rosetta --agree-to-license
            else
                echo "Rosetta 2 OK"
            fi
            echo "==> brew install --cask wine-stable (sudo required for gstreamer-runtime)"
            "{brew}" install --cask wine-stable
            echo "==> xattr -dr com.apple.quarantine"
            /usr/bin/xattr -dr com.apple.quarantine "/Applications/Wine Stable.app" || true
            echo ""
            echo "==> Done. Return to ArkManager → Doctor → Run checks."
            echo "You may close this window."
            """;

        var scriptPath = Path.Combine(Path.GetTempPath(), "ark-manager-install-wine.sh");
        await File.WriteAllTextAsync(scriptPath, script, ct);
        File.SetUnixFileMode(scriptPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        onOutput("Opening Terminal.app — enter your password there when prompted.");
        onOutput("Script: " + scriptPath);
        onOutput("");
        onOutput("After installation completes, click ↻ Run checks in this tab.");

        var psi = new System.Diagnostics.ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
        psi.ArgumentList.Add("-a");
        psi.ArgumentList.Add("Terminal");
        psi.ArgumentList.Add(scriptPath);
        try
        {
            System.Diagnostics.Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            onOutput("Failed to open Terminal: " + ex.Message);
            onOutput("Run the script manually: bash \"" + scriptPath + "\"");
            return false;
        }
    }
}
