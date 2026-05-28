using System.Globalization;

namespace ArkManager.Core.Util;

/// <summary>Aggregated resources of a process subtree (for the wine server: exe + wineserver + helpers).</summary>
public readonly record struct TreeStats(long RssKb, double CpuPercent);

/// <summary>
/// Parses output of `ps -axo pid=,ppid=,rss=,%cpu=` and sums RSS/%CPU over the subtree
/// rooted at the given PID (inclusive). UI-agnostic and testable without real processes.
/// </summary>
public static class ProcessTreeStats
{
    private readonly record struct Row(int Ppid, long RssKb, double Cpu);

    public static TreeStats Sum(string psOutput, int rootPid)
    {
        // pid -> (ppid, rss, cpu)
        var rows = new Dictionary<int, Row>();
        // ppid -> list of children
        var children = new Dictionary<int, List<int>>();

        foreach (var raw in psOutput.Split('\n'))
        {
            var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 4) continue;
            if (!int.TryParse(parts[0], out var pid)) continue;
            if (!int.TryParse(parts[1], out var ppid)) continue;
            if (!long.TryParse(parts[2], out var rss)) continue;
            // On a Russian locale ps prints %cpu with a comma — normalize to a dot.
            if (!double.TryParse(parts[3].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var cpu)) continue;

            rows[pid] = new Row(ppid, rss, cpu);
            (children.TryGetValue(ppid, out var list) ? list : children[ppid] = new List<int>()).Add(pid);
        }

        if (!rows.ContainsKey(rootPid)) return new TreeStats(0, 0);

        long totalRss = 0;
        double totalCpu = 0;
        var stack = new Stack<int>();
        stack.Push(rootPid);
        var seen = new HashSet<int>();
        while (stack.Count > 0)
        {
            var pid = stack.Pop();
            if (!seen.Add(pid)) continue;          // guard against cycles
            if (!rows.TryGetValue(pid, out var r)) continue;
            totalRss += r.RssKb;
            totalCpu += r.Cpu;
            if (children.TryGetValue(pid, out var kids))
                foreach (var k in kids) stack.Push(k);
        }

        return new TreeStats(totalRss, totalCpu);
    }
}
