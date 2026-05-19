using ArkManager.Core.Models;
using ArkManager.Core.Util;

namespace ArkManager.Core.Services.Launchers;

/// <summary>
/// Fallback: брю-установленный wine (brew install --cask wine-stable / gptk).
/// Использует WINEPREFIX из настроек или ~/.wine.
/// </summary>
public sealed class LocalWineLauncher : WineLauncherBase
{
    public override async Task<LauncherStatus> ProbeAsync(CancellationToken ct = default)
    {
        var candidates = new[]
        {
            "/usr/local/bin/wine64",
            "/opt/homebrew/bin/wine64",
            "/usr/local/bin/wine",
            "/opt/homebrew/bin/wine",
        };

        var found = candidates.FirstOrDefault(File.Exists);
        if (found == null)
            return new LauncherStatus(false, "Локальный wine не найден. Установите brew-пакет (например, gcenx/wine).");
        try
        {
            var r = await ProcessRunner.RunCaptureAsync(found, new[] { "--version" }, ct: ct);
            return new LauncherStatus(r.ExitCode == 0, r.StdOut.Trim() + (r.StdErr.Length > 0 ? " | " + r.StdErr.Trim() : ""));
        }
        catch (Exception ex)
        {
            return new LauncherStatus(false, ex.Message);
        }
    }

    protected override string GetWineBinary(AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.WineBinaryPath) && File.Exists(settings.WineBinaryPath))
            return settings.WineBinaryPath;
        var candidates = new[]
        {
            "/opt/homebrew/bin/wine64",
            "/usr/local/bin/wine64",
            "/opt/homebrew/bin/wine",
            "/usr/local/bin/wine",
        };
        return candidates.FirstOrDefault(File.Exists)
               ?? throw new InvalidOperationException("Wine не найден.");
    }

    protected override string? GetWinePrefix(AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.WhiskyBottlePath) && Directory.Exists(settings.WhiskyBottlePath))
            return settings.WhiskyBottlePath;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".wine");
    }
}
