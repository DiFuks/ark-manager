using ArkManager.Core.Util;
using Xunit;

namespace ArkManager.Core.Tests;

public class SystemMemoryTests
{
    // /proc/meminfo on Linux looks like a list of "Key: value unit" lines. We only need MemTotal.
    [Fact]
    public void ParseLinuxMemTotalKb_RealisticFile_ReturnsKilobytes()
    {
        var raw = """
            MemTotal:       16384000 kB
            MemFree:         1234567 kB
            MemAvailable:    8000000 kB
            Buffers:           12345 kB
            """;
        Assert.Equal(16_384_000L, SystemMemory.ParseLinuxMemTotalKb(raw));
    }

    [Fact]
    public void ParseLinuxMemTotalKb_MissingLine_ReturnsNull()
    {
        var raw = "MemFree: 100 kB\nBuffers: 200 kB";
        Assert.Null(SystemMemory.ParseLinuxMemTotalKb(raw));
    }

    // Some distros vary spacing — must be tolerant to multiple spaces and tabs.
    [Fact]
    public void ParseLinuxMemTotalKb_TolerantToWhitespace()
    {
        var raw = "MemTotal:\t\t  8192 kB";
        Assert.Equal(8192L, SystemMemory.ParseLinuxMemTotalKb(raw));
    }
}
