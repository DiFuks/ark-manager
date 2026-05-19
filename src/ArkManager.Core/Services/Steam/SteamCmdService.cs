using System.Formats.Tar;
using System.IO.Compression;
using ArkManager.Core.Util;

namespace ArkManager.Core.Services.Steam;

/// <summary>
/// Установка/обновление ASA Dedicated Server (Steam App ID 2430930) через steamcmd.
/// Под macOS требуется trick: +@sSteamCmdForcePlatformType windows (нет native билда).
/// </summary>
public sealed class SteamCmdService
{
    public const int AsaDedicatedServerAppId = 2430930;
    private const string SteamCmdMacUrl = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd_osx.tar.gz";
    private const string SteamCmdLinuxUrl = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd_linux.tar.gz";

    private readonly AppPaths _paths;
    private readonly SettingsService _settings;

    public SteamCmdService(AppPaths paths, SettingsService settings)
    {
        _paths = paths;
        _settings = settings;
    }

    public string ResolveSteamCmdBinary()
    {
        if (!string.IsNullOrWhiteSpace(_settings.Current.SteamCmdPath) && File.Exists(_settings.Current.SteamCmdPath))
            return _settings.Current.SteamCmdPath;

        var bundled = Path.Combine(_paths.SteamCmdDir, "steamcmd.sh");
        if (File.Exists(bundled)) return bundled;

        // Если в PATH есть steamcmd
        var p = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in p.Split(Path.PathSeparator))
        {
            var c = Path.Combine(dir, "steamcmd");
            if (File.Exists(c)) return c;
        }
        return bundled; // пусть зовущий проверит наличие через File.Exists.
    }

    public bool IsSteamCmdInstalled()
        => File.Exists(ResolveSteamCmdBinary());

    /// <summary>
    /// Скачивает и распаковывает steamcmd в DataDir/steamcmd. Прогресс не считается — пакет маленький.
    /// </summary>
    public async Task InstallSteamCmdAsync(Action<string> onLog, CancellationToken ct = default)
    {
        onLog("Скачиваю steamcmd...");
        var url = OperatingSystem.IsMacOS() ? SteamCmdMacUrl : SteamCmdLinuxUrl;
        var tarGzPath = Path.Combine(_paths.SteamCmdDir, "steamcmd.tar.gz");

        using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
        await using (var resp = await http.GetStreamAsync(url, ct))
        await using (var fs = File.Create(tarGzPath))
        {
            await resp.CopyToAsync(fs, ct);
        }
        onLog("Скачано. Распаковываю...");

        await using (var fs = File.OpenRead(tarGzPath))
        await using (var gz = new GZipStream(fs, CompressionMode.Decompress))
        {
            await TarFile.ExtractToDirectoryAsync(gz, _paths.SteamCmdDir, overwriteFiles: true, cancellationToken: ct);
        }

        // chmod +x для steamcmd.sh
        var sh = Path.Combine(_paths.SteamCmdDir, "steamcmd.sh");
        if (File.Exists(sh))
        {
            await ProcessRunner.RunCaptureAsync("/bin/chmod", new[] { "+x", sh }, ct: ct);
            // ещё бинарник steamcmd внутри (бывает называется steamcmd или steamcmd.exe)
            foreach (var f in Directory.EnumerateFiles(_paths.SteamCmdDir, "steamcmd", SearchOption.AllDirectories))
                await ProcessRunner.RunCaptureAsync("/bin/chmod", new[] { "+x", f }, ct: ct);
        }

        try { File.Delete(tarGzPath); } catch { /* ignore */ }
        onLog("steamcmd готов: " + sh);
    }

    /// <summary>
    /// Запускает app_update 2430930 validate. Поток вывода — построчно в onOutput.
    /// </summary>
    public async Task<int> InstallOrUpdateServerAsync(
        string installDir,
        Action<string> onOutput,
        CancellationToken ct = default)
    {
        if (!IsSteamCmdInstalled())
            throw new InvalidOperationException("steamcmd не установлен. Запустите Install SteamCMD сначала.");

        Directory.CreateDirectory(installDir);
        var bin = ResolveSteamCmdBinary();

        var args = new List<string>
        {
            // Под mac/linux — заставляем качать Windows-сборку.
            "+@sSteamCmdForcePlatformType", "windows",
            "+force_install_dir", installDir,
            "+login", "anonymous",
            "+app_update", AsaDedicatedServerAppId.ToString(), "validate",
            "+quit",
        };

        onOutput($"$ {bin} {string.Join(" ", args)}");
        return await ProcessRunner.RunStreamingAsync(
            bin, args,
            onStdOut: onOutput,
            onStdErr: onOutput,
            ct: ct);
    }
}
