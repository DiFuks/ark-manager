using ArkManager.Core.Util;
using Xunit;

namespace ArkManager.Core.Tests;

public class MacMemoryTests
{
    private const long Gb = 1024L * 1024 * 1024;
    private const long Mb = 1024L * 1024;

    [Fact]
    public void PhysFootprint_ParsesGb_AndIgnoresPeak()
    {
        // Реальный вывод `footprint <pid>`: берём phys_footprint, НЕ phys_footprint_peak.
        var s = "wine [31714]: 64-bit (translated)    Footprint: 10 GB (4096 bytes per page)\n" +
                "    phys_footprint: 10 GB\n" +
                "    phys_footprint_peak: 12 GB\n";
        Assert.Equal(10 * Gb, MacMemory.PhysFootprintBytes(s));
    }

    [Fact]
    public void PhysFootprint_ParsesMb()
        => Assert.Equal(562 * Mb, MacMemory.PhysFootprintBytes("    phys_footprint: 562 MB\n"));

    [Fact]
    public void PhysFootprint_ParsesFractional_CommaOrDot()
    {
        Assert.Equal((long)(1.5 * Gb), MacMemory.PhysFootprintBytes("    phys_footprint: 1.5 GB"));
        Assert.Equal((long)(1.5 * Gb), MacMemory.PhysFootprintBytes("    phys_footprint: 1,5 GB"));
    }

    [Fact]
    public void PhysFootprint_ParsesBytes()
        => Assert.Equal(4096, MacMemory.PhysFootprintBytes("    phys_footprint: 4096 bytes"));

    [Fact]
    public void PhysFootprint_ReturnsNull_WhenAbsent()
        => Assert.Null(MacMemory.PhysFootprintBytes("some other output\nFootprint: 10 GB\n"));
}
