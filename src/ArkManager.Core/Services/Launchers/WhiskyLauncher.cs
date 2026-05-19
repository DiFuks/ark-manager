using ArkManager.Core.Models;
using ArkManager.Core.Util;

namespace ArkManager.Core.Services.Launchers;

/// <summary>
/// Whisky хранит боттлы в ~/Library/Containers/com.isaacmarovitz.Whisky/Bottles/ (новая версия)
/// либо ~/Library/Application Support/com.isaacmarovitz.Whisky/Bottles/ (старая).
/// Wine-бинарь — Whisky.app/Contents/Resources/Libraries/Wine/bin/wine64.
/// </summary>
public sealed class WhiskyLauncher : WineLauncherBase
{
    public const string WhiskyAppPath = "/Applications/Whisky.app";
    public const string WhiskyWine = "/Applications/Whisky.app/Contents/Resources/Libraries/Wine/bin/wine64";

    public override async Task<LauncherStatus> ProbeAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(WhiskyAppPath))
            return new LauncherStatus(false, "Whisky не установлен. Поставьте: brew install --cask whisky");
        if (!File.Exists(WhiskyWine))
            return new LauncherStatus(false, "Whisky установлен, но wine64 не найден по пути " + WhiskyWine);

        try
        {
            var r = await ProcessRunner.RunCaptureAsync(WhiskyWine, new[] { "--version" }, ct: ct);
            if (r.ExitCode != 0)
                return new LauncherStatus(false, "wine64 не запускается: " + r.StdErr);
            return new LauncherStatus(true, r.StdOut.Trim());
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
        return WhiskyWine;
    }

    protected override string? GetWinePrefix(AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.WhiskyBottlePath) && Directory.Exists(settings.WhiskyBottlePath))
            return settings.WhiskyBottlePath;

        // Авто-детект первого попавшегося боттла.
        foreach (var root in EnumerateBottleRoots())
        {
            if (!Directory.Exists(root)) continue;
            var first = Directory.EnumerateDirectories(root).FirstOrDefault();
            if (first != null) return first;
        }
        return null;
    }

    public static IEnumerable<string> EnumerateBottleRoots()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(home, "Library", "Containers", "com.isaacmarovitz.Whisky", "Bottles");
        yield return Path.Combine(home, "Library", "Application Support", "com.isaacmarovitz.Whisky", "Bottles");
    }
}
