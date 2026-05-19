using System.Globalization;
using ArkManager.Core.Services.Config;
using Xunit;

namespace ArkManager.Core.Tests;

public class RatesTests
{
    [Fact]
    public void RoundTrip_PreservesValues()
    {
        var ini = new IniFile();
        var r = new Rates
        {
            DifficultyOffset = 0.5,
            XPMultiplier = 2.5,
            HarvestAmountMultiplier = 3,
            DayCycleSpeedScale = 0.75,
        };
        RatesIni.WriteInto(ini, r);
        var serialized = ini.ToString();
        var parsed = IniFile.Parse(serialized);
        var read = RatesIni.ReadFrom(parsed);
        Assert.Equal(0.5, read.DifficultyOffset);
        Assert.Equal(2.5, read.XPMultiplier);
        Assert.Equal(3.0, read.HarvestAmountMultiplier);
        Assert.Equal(0.75, read.DayCycleSpeedScale);
    }

    [Fact]
    public void Write_UsesInvariantCulture()
    {
        // Эмулируем русскую локаль (decimal-comma) — значение должно остаться с точкой.
        var prev = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ru-RU");
            var ini = new IniFile();
            RatesIni.WriteInto(ini, new Rates { XPMultiplier = 1.5 });
            var text = ini.ToString();
            Assert.Contains("XPMultiplier=1.5", text);
        }
        finally
        {
            CultureInfo.CurrentCulture = prev;
        }
    }
}
