using System.Globalization;

namespace ArkManager.Core.Util;

/// <summary>Pure formatters for the UI: human-readable size/time. No UI dependencies.</summary>
public static class DisplayFormat
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB" };

    public static string HumanSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double v = bytes;
        var u = 0;
        while (v >= 1024 && u < Units.Length - 1) { v /= 1024; u++; }
        return string.Create(CultureInfo.InvariantCulture, $"{v:0.0} {Units[u]}");
    }

    /// <summary>"today, 23:28" / "yesterday, 22:55" / "3 days ago". Local time for display.</summary>
    public static string RelativeTime(DateTime valueUtc, DateTime nowUtc)
    {
        var local = valueUtc.ToLocalTime();
        var today = nowUtc.ToLocalTime().Date;
        var day = local.Date;
        if (day == today) return $"today, {local:HH:mm}";
        if (day == today.AddDays(-1)) return $"yesterday, {local:HH:mm}";
        var days = (today - day).Days;
        return $"{days} days ago";
    }
}
