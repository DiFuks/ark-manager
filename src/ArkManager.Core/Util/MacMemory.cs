using System.Globalization;

namespace ArkManager.Core.Util;

/// <summary>
/// Parses macOS `footprint &lt;pid&gt;` output. We need phys_footprint — the "real" process
/// memory (as in Activity Monitor), including compressed pages. Under wine, RSS from ps is
/// an order of magnitude lower than the real footprint (macOS compresses memory), so for
/// the gauge we use footprint specifically.
/// </summary>
public static class MacMemory
{
    /// <summary>phys_footprint in bytes from `footprint` output. null — line not found.</summary>
    public static long? PhysFootprintBytes(string footprintOutput)
    {
        foreach (var raw in footprintOutput.Split('\n'))
        {
            var line = raw.Trim();
            // Specifically phys_footprint:, not phys_footprint_peak: and not Footprint:.
            if (!line.StartsWith("phys_footprint:", StringComparison.OrdinalIgnoreCase)) continue;

            var rest = line["phys_footprint:".Length..].Trim();          // e.g. "10 GB" / "1,5 GB" / "4096 bytes"
            var parts = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2) return null;
            if (!double.TryParse(parts[0].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
                return null;

            double mult = parts[1].ToUpperInvariant() switch
            {
                "GB" => 1024d * 1024 * 1024,
                "MB" => 1024d * 1024,
                "KB" => 1024d,
                "BYTES" or "B" => 1d,
                _ => -1,
            };
            if (mult < 0) return null;
            return (long)(num * mult);
        }
        return null;
    }
}
