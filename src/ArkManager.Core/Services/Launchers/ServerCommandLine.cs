using System.Text;
using ArkManager.Core.Models;

namespace ArkManager.Core.Services.Launchers;

/// <summary>
/// Формирует CLI ASA-сервера: первый аргумент — quoted map?key=value?... строка,
/// дальше идут «голые» флаги: -server -log -port=N -QueryPort=M -mods=... -NoBattlEye и т.д.
/// </summary>
public static class ServerCommandLine
{
    public static IReadOnlyList<string> Build(AppSettings settings, IReadOnlyList<string> modIds)
    {
        var o = settings.LaunchOptions;
        var queryParts = new List<string>
        {
            o.Map,
            "listen",
            $"SessionName={Escape(o.SessionName)}",
            $"Port={o.Port}",
            $"QueryPort={o.QueryPort}",
            $"MaxPlayers={o.MaxPlayers}",
        };
        if (!string.IsNullOrEmpty(o.ServerPassword))
            queryParts.Add($"ServerPassword={Escape(o.ServerPassword)}");
        if (!string.IsNullOrEmpty(o.AdminPassword))
            queryParts.Add($"ServerAdminPassword={Escape(o.AdminPassword)}");
        if (!string.IsNullOrEmpty(o.SpectatorPassword))
            queryParts.Add($"SpectatorPassword={Escape(o.SpectatorPassword)}");
        if (o.RconEnabled)
        {
            queryParts.Add($"RCONEnabled=True");
            queryParts.Add($"RCONPort={o.RconPort}");
        }

        if (!string.IsNullOrWhiteSpace(o.ExtraQueryString))
        {
            foreach (var kv in o.ExtraQueryString.Split('?', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                queryParts.Add(kv);
        }

        var queryString = string.Join("?", queryParts);

        var list = new List<string> { queryString, "-server", "-log" };

        if (modIds.Count > 0)
            list.Add("-mods=" + string.Join(",", modIds));

        if (o.AutoManagedMods)
            list.Add("-automanagedmods");

        if (o.NoBattlEye)
            list.Add("-NoBattlEye");

        if (!string.IsNullOrWhiteSpace(o.ExtraCommandLineArgs))
        {
            foreach (var arg in Tokenize(o.ExtraCommandLineArgs))
                list.Add(arg);
        }

        return list;
    }

    private static string Escape(string value)
    {
        // В ASA query-string специальные символы — '?' и пробел. Простейшая защита: заменяем пробелы на _ и режем '?'.
        return value.Replace("?", "").Trim();
    }

    /// <summary>Самый базовый shell-like tokenize: кавычки сохраняют пробелы.</summary>
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
