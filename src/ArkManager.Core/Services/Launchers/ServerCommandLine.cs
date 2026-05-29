using System.Text;
using ArkManager.Core.Models;
using ArkManager.Core.Services.Config;

namespace ArkManager.Core.Services.Launchers;

/// <summary>
/// Builds the ASA server CLI: first argument is a quoted map?key=value?... string,
/// followed by "bare" flags: -server -log -port=N -QueryPort=M -mods=... -NoBattlEye etc.
/// </summary>
public static class ServerCommandLine
{
    public static IReadOnlyList<string> Build(AppSettings settings, ServerConfigSnapshot snapshot, IReadOnlyList<string> modIds)
    {
        var o = settings.LaunchOptions;
        // Passwords and RCON are NOT put into the URL query. Reason: with multiple parameters
        // after ServerAdminPassword= the ASA URL parser may glue the tail of the string into
        // the password value and save it that way to GameUserSettings.ini — RCON auth then
        // breaks. These keys are written to ini via ConfigService.ApplyLaunchOptionsToIni,
        // and that's where the server reads them from.
        var queryParts = new List<string>
        {
            o.Map,
            "listen",
            $"SessionName={Escape(snapshot.SessionName)}",
            $"Port={snapshot.Port}",
            $"QueryPort={snapshot.QueryPort}",
        };

        if (!string.IsNullOrWhiteSpace(o.ExtraQueryString))
        {
            foreach (var kv in o.ExtraQueryString.Split('?', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                queryParts.Add(kv);
        }

        var queryString = string.Join("?", queryParts);

        // Server runs headless (winemac.drv disabled in BundledWineLauncher → no window).
        // -stdout -FullStdOutLogOutput pipe the FULL UE log to stdout (otherwise it only
        // goes to the window/ShooterGame.log and isn't visible in ArkManager). -unattended suppresses dialogs.
        var list = new List<string> { queryString, "-server", "-log", "-stdout", "-FullStdOutLogOutput", "-unattended" };

        // ASA quirk: ?MaxPlayers= in URL and MaxPlayers= in [/Script/Engine.GameSession] are ignored.
        // Only -WinLiveMaxPlayers=N actually changes the player cap. Verified empirically 2026-05-29.
        if (o.MaxPlayers > 0)
            list.Add($"-WinLiveMaxPlayers={o.MaxPlayers}");

        if (modIds.Count > 0)
            list.Add("-mods=" + string.Join(",", modIds));

        if (o.AutoManagedMods)
            list.Add("-automanagedmods");

        if (o.NoBattlEye)
            list.Add("-NoBattlEye");

        if (!string.IsNullOrWhiteSpace(o.ClusterId))
            list.Add("-ClusterId=" + o.ClusterId);
        if (!string.IsNullOrWhiteSpace(o.ClusterDirOverride))
            list.Add("-ClusterDirOverride=\"" + o.ClusterDirOverride + "\"");

        if (!string.IsNullOrWhiteSpace(o.ExtraCommandLineArgs))
        {
            foreach (var arg in Tokenize(o.ExtraCommandLineArgs))
                list.Add(arg);
        }

        return list;
    }

    private static string Escape(string value)
    {
        // In ASA query strings the special characters are '?' and space. Simplest guard: replace spaces with _ and strip '?'.
        return value.Replace("?", "").Trim();
    }

    /// <summary>Most basic shell-like tokenize: quotes preserve spaces.</summary>
    private static IEnumerable<string> Tokenize(string s)
    {
        var sb = new StringBuilder();
        var inQuote = false;
        foreach (var ch in s)
        {
            if (ch == '"') { inQuote = !inQuote; continue; }
            if (!inQuote && char.IsWhiteSpace(ch))
            {
                if (sb.Length > 0) { yield return sb.ToString(); sb.Clear(); }
                continue;
            }
            sb.Append(ch);
        }
        if (sb.Length > 0) yield return sb.ToString();
    }
}
