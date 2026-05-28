namespace ArkManager.Core.Util;

/// <summary>An already-running server we found (for "adoption" after a crash / Force Quit of the manager).</summary>
public readonly record struct DiscoveredServer(int Pid, TimeSpan Uptime);

/// <summary>
/// Locates a running ArkAscendedServer.exe in the output of `ps -axww -o pid=,etime=,command=`
/// by the full exe path. UI-agnostic and testable without real processes.
/// </summary>
public static class ServerDiscovery
{
    public static DiscoveredServer? Find(string psOutput, string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return null;

        foreach (var raw in psOutput.Split('\n'))
        {
            var line = raw.TrimStart();
            // pid <sp> etime <sp> command...
            var sp1 = line.IndexOf(' ');
            if (sp1 <= 0) continue;
            if (!int.TryParse(line[..sp1], out var pid)) continue;

            var rest = line[(sp1 + 1)..].TrimStart();
            var sp2 = rest.IndexOf(' ');
            if (sp2 <= 0) continue;
            var etime = rest[..sp2];
            var command = rest[(sp2 + 1)..];

            if (command.Contains(exePath, StringComparison.OrdinalIgnoreCase))
                return new DiscoveredServer(pid, ParseEtime(etime));
        }
        return null;
    }

    /// <summary>etime from ps: [[DD-]HH:]MM:SS.</summary>
    internal static TimeSpan ParseEtime(string s)
    {
        s = s.Trim();
        var days = 0;
        var dash = s.IndexOf('-');
        if (dash >= 0)
        {
            int.TryParse(s[..dash], out days);
            s = s[(dash + 1)..];
        }

        var parts = s.Split(':');
        int h = 0, m = 0, sec = 0;
        if (parts.Length == 3)
        {
            int.TryParse(parts[0], out h);
            int.TryParse(parts[1], out m);
            int.TryParse(parts[2], out sec);
        }
        else if (parts.Length == 2)
        {
            int.TryParse(parts[0], out m);
            int.TryParse(parts[1], out sec);
        }
        else return TimeSpan.Zero;

        return new TimeSpan(days, h, m, sec);
    }
}
