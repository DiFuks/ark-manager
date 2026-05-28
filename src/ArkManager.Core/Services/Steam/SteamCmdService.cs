using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using ArkManager.Core.Util;

namespace ArkManager.Core.Services.Steam;

/// <summary>
/// Слепок локально установленной версии ASA сервера (из appmanifest_*.acf).
/// </summary>
public sealed record InstalledServerVersion(string BuildId, DateTimeOffset? LastUpdated);

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
        onLog("Downloading steamcmd...");
        var url = OperatingSystem.IsMacOS() ? SteamCmdMacUrl : SteamCmdLinuxUrl;
        var tarGzPath = Path.Combine(_paths.SteamCmdDir, "steamcmd.tar.gz");

        using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
        await using (var resp = await http.GetStreamAsync(url, ct))
        await using (var fs = File.Create(tarGzPath))
        {
            await resp.CopyToAsync(fs, ct);
        }
        onLog("Downloaded. Extracting...");

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
        onLog("steamcmd ready: " + sh);
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
            throw new InvalidOperationException("steamcmd is not installed. Run Install SteamCMD first.");

        Directory.CreateDirectory(installDir);
        var bin = ResolveSteamCmdBinary();

        var args = new List<string>
        {
            // Под mac/linux — заставляем качать Windows-сборку.
            "+@sSteamCmdForcePlatformType", "windows",
            "+force_install_dir", installDir,
            "+login", "anonymous",
            // Без этого SteamCMD на маке с принудительной windows-платформой падает
            // с «Failed to install app '2430930' (Missing configuration)» — манифест
            // не подтягивается, кэш пустой. app_info_update 1 форсит refresh.
            "+app_info_update", "1",
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

    /// <summary>
    /// Читает «человеческую» версию игры (что-то вроде "40.55") из PE-ресурса
    /// VS_VERSIONINFO у ArkAscendedServer.exe. Это то, что юзер видит в самой игре,
    /// в отличие от Steam buildid (большое число вроде 23321173). exe не запускается,
    /// .NET просто парсит ресурсную секцию PE — работает и на macOS.
    /// Возвращает null, если файла нет либо версия не заполнена/тривиальная.
    /// </summary>
    public string? ReadInstalledFileVersion(string installDir)
    {
        if (string.IsNullOrWhiteSpace(installDir)) return null;
        var exe = Path.Combine(installDir, "ShooterGame", "Binaries", "Win64", "ArkAscendedServer.exe");
        if (!File.Exists(exe)) return null;
        try
        {
            var vi = System.Diagnostics.FileVersionInfo.GetVersionInfo(exe);
            var v = vi.FileVersion?.Trim();
            if (string.IsNullOrEmpty(v)) return null;
            if (v is "0.0.0.0" or "1.0.0.0") return null;
            return v;
        }
        catch { return null; }
    }

    /// <summary>
    /// Читает локально установленную версию из steamapps/appmanifest_2430930.acf.
    /// Возвращает null, если манифест ещё не создан (сервер не установлен).
    /// </summary>
    public InstalledServerVersion? ReadInstalledVersion(string installDir)
    {
        if (string.IsNullOrWhiteSpace(installDir)) return null;
        var manifest = Path.Combine(installDir, "steamapps", $"appmanifest_{AsaDedicatedServerAppId}.acf");
        if (!File.Exists(manifest)) return null;
        string text;
        try { text = File.ReadAllText(manifest); }
        catch { return null; }
        return ParseManifest(text);
    }

    /// <summary>
    /// Парсер VDF-манифеста — нам достаточно вытащить top-level "buildid" и "LastUpdated".
    /// </summary>
    internal static InstalledServerVersion? ParseManifest(string text)
    {
        var buildId = Regex.Match(text, "\"buildid\"\\s+\"(\\d+)\"", RegexOptions.IgnoreCase).Groups[1].Value;
        if (string.IsNullOrEmpty(buildId)) return null;
        var lastUpd = Regex.Match(text, "\"LastUpdated\"\\s+\"(\\d+)\"", RegexOptions.IgnoreCase).Groups[1].Value;
        DateTimeOffset? when = long.TryParse(lastUpd, out var ts) && ts > 0
            ? DateTimeOffset.FromUnixTimeSeconds(ts)
            : null;
        return new InstalledServerVersion(buildId, when);
    }

    /// <summary>
    /// Спрашивает у Steam актуальный buildid для public-ветки через
    /// steamcmd app_info_print. Делает app_info_update 1 для свежего PICS-кэша.
    /// Медленно (steamcmd сам по себе медленный) — вызывать только по явной кнопке.
    /// </summary>
    public async Task<string?> QueryLatestBuildIdAsync(Action<string>? onLog = null, CancellationToken ct = default)
    {
        if (!IsSteamCmdInstalled())
            throw new InvalidOperationException("steamcmd is not installed.");
        var bin = ResolveSteamCmdBinary();
        var args = new[]
        {
            "+@sSteamCmdForcePlatformType", "windows",
            "+login", "anonymous",
            "+app_info_update", "1",
            "+app_info_print", AsaDedicatedServerAppId.ToString(),
            "+quit",
        };

        var buf = new StringBuilder();
        onLog?.Invoke($"$ {bin} +app_info_print {AsaDedicatedServerAppId}");
        await ProcessRunner.RunStreamingAsync(
            bin, args,
            onStdOut: line => { buf.AppendLine(line); onLog?.Invoke(line); },
            onStdErr: line => onLog?.Invoke(line),
            ct: ct);
        return ParseLatestBuildId(buf.ToString());
    }

    /// <summary>
    /// Ищет в выводе app_info_print билд public-ветки.
    /// Формат: "branches" { "public" { "buildid" "23321173" ... } ... }.
    /// </summary>
    internal static string? ParseLatestBuildId(string output)
    {
        var m = Regex.Match(
            output,
            "\"public\"\\s*\\{[^}]*?\"buildid\"\\s+\"(\\d+)\"",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return m.Success ? m.Groups[1].Value : null;
    }
}
