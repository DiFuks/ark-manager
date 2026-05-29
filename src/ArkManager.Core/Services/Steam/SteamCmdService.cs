using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using ArkManager.Core.Util;

namespace ArkManager.Core.Services.Steam;

/// <summary>
/// Snapshot of the locally installed ASA server version (from appmanifest_*.acf).
/// </summary>
public sealed record InstalledServerVersion(string BuildId, DateTimeOffset? LastUpdated);

/// <summary>
/// Host OS for the steamcmd bootstrap (used in tests and runtime detection).
/// </summary>
public enum SteamCmdHostOs { MacOS, Linux, Windows }

/// <summary>
/// Installs/updates the ASA Dedicated Server (Steam App ID 2430930) via steamcmd.
/// On macOS the trick is required: +@sSteamCmdForcePlatformType windows (no native build).
/// </summary>
public sealed class SteamCmdService
{
    public const int AsaDedicatedServerAppId = 2430930;
    private const string SteamCmdMacUrl = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd_osx.tar.gz";
    private const string SteamCmdLinuxUrl = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd_linux.tar.gz";
    private const string SteamCmdWindowsUrl = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip";

    internal static SteamCmdHostOs DetectHostOs()
        => OperatingSystem.IsWindows() ? SteamCmdHostOs.Windows
         : OperatingSystem.IsMacOS()   ? SteamCmdHostOs.MacOS
         :                               SteamCmdHostOs.Linux;

    public static string SelectBootstrapUrl(SteamCmdHostOs os) => os switch
    {
        SteamCmdHostOs.MacOS   => SteamCmdMacUrl,
        SteamCmdHostOs.Linux   => SteamCmdLinuxUrl,
        SteamCmdHostOs.Windows => SteamCmdWindowsUrl,
        _ => throw new ArgumentOutOfRangeException(nameof(os)),
    };

    public static IReadOnlyList<string> BuildInstallArgs(string installDir, SteamCmdHostOs os)
    {
        var args = new List<string>();
        // On mac/linux we force steamcmd to download the Windows build (no native ASA build exists).
        // On a Windows host this flag isn't needed and isn't applied.
        if (os != SteamCmdHostOs.Windows)
        {
            args.Add("+@sSteamCmdForcePlatformType");
            args.Add("windows");
        }
        args.AddRange(new[]
        {
            "+force_install_dir", installDir,
            "+login", "anonymous",
            "+app_info_update", "1",
            "+app_update", AsaDedicatedServerAppId.ToString(), "validate",
            "+quit",
        });
        return args;
    }

    /// <summary>
    /// Minimal args used to let steamcmd self-update before the real install.
    /// On Windows, steamcmd's self-update spawns a new process and exits the old one,
    /// and the new process does NOT inherit our original command-line args. Running
    /// app_update on a stale steamcmd therefore loses our args during the relaunch and
    /// the install never happens (see the "Missing configuration" failure mode).
    /// A standalone "+login anonymous +quit" call is harmless if steamcmd is up-to-date.
    /// </summary>
    public static IReadOnlyList<string> BuildWarmupArgs(SteamCmdHostOs os)
    {
        var args = new List<string>();
        if (os != SteamCmdHostOs.Windows)
        {
            args.Add("+@sSteamCmdForcePlatformType");
            args.Add("windows");
        }
        args.AddRange(new[] { "+login", "anonymous", "+quit" });
        return args;
    }

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

        var bundledName = OperatingSystem.IsWindows() ? "steamcmd.exe" : "steamcmd.sh";
        var bundled = Path.Combine(_paths.SteamCmdDir, bundledName);
        if (File.Exists(bundled)) return bundled;

        // If PATH contains steamcmd
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathVar.Split(Path.PathSeparator))
        {
            var c = Path.Combine(dir, bundledName);
            if (File.Exists(c)) return c;
            if (!OperatingSystem.IsWindows())
            {
                var bare = Path.Combine(dir, "steamcmd");
                if (File.Exists(bare)) return bare;
            }
        }
        return bundled; // let the caller check existence via File.Exists.
    }

    public bool IsSteamCmdInstalled()
        => File.Exists(ResolveSteamCmdBinary());

    /// <summary>
    /// Downloads and extracts steamcmd into DataDir/steamcmd. Progress isn't tracked — the package is small.
    /// </summary>
    public async Task InstallSteamCmdAsync(Action<string> onLog, CancellationToken ct = default)
    {
        var os = DetectHostOs();
        var url = SelectBootstrapUrl(os);
        onLog("Downloading steamcmd...");
        var ext = os == SteamCmdHostOs.Windows ? ".zip" : ".tar.gz";
        var archive = Path.Combine(_paths.SteamCmdDir, "steamcmd" + ext);

        using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
        await using (var resp = await http.GetStreamAsync(url, ct))
        await using (var fs = File.Create(archive))
        {
            await resp.CopyToAsync(fs, ct);
        }
        onLog("Downloaded. Extracting...");

        if (os == SteamCmdHostOs.Windows)
        {
            ZipFile.ExtractToDirectory(archive, _paths.SteamCmdDir, overwriteFiles: true);
        }
        else
        {
            await using var fs = File.OpenRead(archive);
            await using var gz = new GZipStream(fs, CompressionMode.Decompress);
            await TarFile.ExtractToDirectoryAsync(gz, _paths.SteamCmdDir, overwriteFiles: true, cancellationToken: ct);
        }

        // chmod +x only on Unix — there is no execute bit on Windows.
        if (os != SteamCmdHostOs.Windows)
        {
            var sh = Path.Combine(_paths.SteamCmdDir, "steamcmd.sh");
            if (File.Exists(sh))
            {
                await ProcessRunner.RunCaptureAsync("/bin/chmod", new[] { "+x", sh }, ct: ct);
                foreach (var f in Directory.EnumerateFiles(_paths.SteamCmdDir, "steamcmd", SearchOption.AllDirectories))
                    await ProcessRunner.RunCaptureAsync("/bin/chmod", new[] { "+x", f }, ct: ct);
            }
        }

        try { File.Delete(archive); } catch { /* ignore */ }
        var binary = ResolveSteamCmdBinary();
        onLog("steamcmd ready: " + binary);
    }

    /// <summary>
    /// Runs app_update 2430930 validate. Output is streamed line-by-line to onOutput.
    /// Preceded by a warm-up call so steamcmd can self-update without dropping the
    /// real install's args during the relaunch (see <see cref="BuildWarmupArgs"/>).
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
        var os = DetectHostOs();

        var warmupArgs = BuildWarmupArgs(os);
        onOutput("Warming up steamcmd (allows self-update before the real install)...");
        onOutput($"$ {bin} {string.Join(" ", warmupArgs)}");
        await ProcessRunner.RunStreamingAsync(
            bin, warmupArgs,
            onStdOut: onOutput,
            onStdErr: onOutput,
            ct: ct);

        var args = BuildInstallArgs(installDir, os);
        onOutput($"$ {bin} {string.Join(" ", args)}");
        return await ProcessRunner.RunStreamingAsync(
            bin, args,
            onStdOut: onOutput,
            onStdErr: onOutput,
            ct: ct);
    }

    /// <summary>
    /// Reads the locally installed version from steamapps/appmanifest_2430930.acf.
    /// Returns null if the manifest has not been created yet (server not installed).
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
    /// VDF manifest parser — we only need to extract the top-level "buildid" and "LastUpdated".
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
    /// Asks Steam for the current buildid of the public branch via
    /// steamcmd app_info_print. Does app_info_update 1 to refresh the PICS cache.
    /// Slow (steamcmd itself is slow) — call only on an explicit button press.
    /// </summary>
    public async Task<string?> QueryLatestBuildIdAsync(Action<string>? onLog = null, CancellationToken ct = default)
    {
        if (!IsSteamCmdInstalled())
            throw new InvalidOperationException("steamcmd is not installed.");
        var bin = ResolveSteamCmdBinary();
        var os = DetectHostOs();
        var args = new List<string>();
        if (os != SteamCmdHostOs.Windows)
        {
            args.Add("+@sSteamCmdForcePlatformType");
            args.Add("windows");
        }
        args.AddRange(new[]
        {
            "+login", "anonymous",
            "+app_info_update", "1",
            "+app_info_print", AsaDedicatedServerAppId.ToString(),
            "+quit",
        });

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
    /// Finds the public branch build in app_info_print output.
    /// Format: "branches" { "public" { "buildid" "23321173" ... } ... }.
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
