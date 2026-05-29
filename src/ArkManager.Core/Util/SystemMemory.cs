using System.Runtime.InteropServices;

namespace ArkManager.Core.Util;

/// <summary>
/// Total physical RAM lookup. Used to render "X% (Y GB)" alongside RSS.
/// macOS: sysctl hw.memsize (kept in ServerViewModel — already covered there).
/// Linux: /proc/meminfo.
/// Windows: GlobalMemoryStatusEx.
/// </summary>
public static class SystemMemory
{
    /// <summary>Returns total physical RAM in bytes for the current OS, or null if unavailable.</summary>
    public static long? GetTotalRamBytes()
    {
        if (OperatingSystem.IsWindows()) return WindowsTotalRam();
        if (OperatingSystem.IsLinux()) return LinuxTotalRam();
        return null;
    }

    /// <summary>Pure parser for /proc/meminfo — extracted so we can unit-test without a real fs.</summary>
    public static long? ParseLinuxMemTotalKb(string memInfoContents)
    {
        foreach (var raw in memInfoContents.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (!line.StartsWith("MemTotal:", StringComparison.Ordinal)) continue;
            var rest = line.AsSpan("MemTotal:".Length).Trim();
            // "16384000 kB" — take the first whitespace-separated token.
            var sp = rest.IndexOfAny(' ', '\t');
            var num = sp < 0 ? rest : rest[..sp];
            if (long.TryParse(num, out var kb)) return kb;
            return null;
        }
        return null;
    }

    private static long? LinuxTotalRam()
    {
        try { return ParseLinuxMemTotalKb(File.ReadAllText("/proc/meminfo")) is long kb ? kb * 1024 : null; }
        catch { return null; }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    private static long? WindowsTotalRam()
    {
        try
        {
            var ms = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            return GlobalMemoryStatusEx(ref ms) ? (long)ms.ullTotalPhys : null;
        }
        catch { return null; }
    }
}
