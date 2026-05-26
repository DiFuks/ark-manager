using System.Globalization;

namespace ArkManager.Core.Util;

/// <summary>
/// Парсинг вывода macOS `footprint &lt;pid&gt;`. Нужен phys_footprint — «настоящая» память
/// процесса (как в Activity Monitor), включая сжатую. Под wine RSS из ps на порядок меньше
/// реального footprint (macOS компрессит память), поэтому для гейджа берём именно footprint.
/// </summary>
public static class MacMemory
{
    /// <summary>phys_footprint в байтах из вывода `footprint`. null — строка не найдена.</summary>
    public static long? PhysFootprintBytes(string footprintOutput)
    {
        foreach (var raw in footprintOutput.Split('\n'))
        {
            var line = raw.Trim();
            // Именно phys_footprint:, не phys_footprint_peak: и не Footprint:.
            if (!line.StartsWith("phys_footprint:", StringComparison.OrdinalIgnoreCase)) continue;

            var rest = line["phys_footprint:".Length..].Trim();          // напр. "10 GB" / "1,5 GB" / "4096 bytes"
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
