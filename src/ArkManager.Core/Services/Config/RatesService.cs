using System.Globalization;

namespace ArkManager.Core.Services.Config;

/// <summary>
/// Типовые рейты ARK. Хранятся в [ServerSettings] (большинство) или
/// корневой секции GameUserSettings.ini (DifficultyOffset/OverrideOfficialDifficulty).
/// </summary>
public sealed class Rates
{
    public double DifficultyOffset { get; set; } = 1.0;
    public double OverrideOfficialDifficulty { get; set; } = 5.0;
    public double TamingSpeedMultiplier { get; set; } = 1.0;
    public double XPMultiplier { get; set; } = 1.0;
    public double HarvestAmountMultiplier { get; set; } = 1.0;
    public double DayCycleSpeedScale { get; set; } = 1.0;
    public double DayTimeSpeedScale { get; set; } = 1.0;
    public double NightTimeSpeedScale { get; set; } = 1.0;
    public double DinoCharacterFoodDrainMultiplier { get; set; } = 1.0;
    public double PlayerCharacterFoodDrainMultiplier { get; set; } = 1.0;
    public double MatingIntervalMultiplier { get; set; } = 1.0;
    public double EggHatchSpeedMultiplier { get; set; } = 1.0;
    public double BabyMatureSpeedMultiplier { get; set; } = 1.0;
}

public static class RatesIni
{
    private static readonly (string Key, Func<Rates, double> Get, Action<Rates, double> Set, string Section)[] Specs =
    {
        ("DifficultyOffset",                  r => r.DifficultyOffset,                  (r,v) => r.DifficultyOffset = v,                  "ServerSettings"),
        ("OverrideOfficialDifficulty",        r => r.OverrideOfficialDifficulty,        (r,v) => r.OverrideOfficialDifficulty = v,        "ServerSettings"),
        ("TamingSpeedMultiplier",             r => r.TamingSpeedMultiplier,             (r,v) => r.TamingSpeedMultiplier = v,             "ServerSettings"),
        ("XPMultiplier",                      r => r.XPMultiplier,                      (r,v) => r.XPMultiplier = v,                      "ServerSettings"),
        ("HarvestAmountMultiplier",           r => r.HarvestAmountMultiplier,           (r,v) => r.HarvestAmountMultiplier = v,           "ServerSettings"),
        ("DayCycleSpeedScale",                r => r.DayCycleSpeedScale,                (r,v) => r.DayCycleSpeedScale = v,                "ServerSettings"),
        ("DayTimeSpeedScale",                 r => r.DayTimeSpeedScale,                 (r,v) => r.DayTimeSpeedScale = v,                 "ServerSettings"),
        ("NightTimeSpeedScale",               r => r.NightTimeSpeedScale,               (r,v) => r.NightTimeSpeedScale = v,               "ServerSettings"),
        ("DinoCharacterFoodDrainMultiplier",  r => r.DinoCharacterFoodDrainMultiplier,  (r,v) => r.DinoCharacterFoodDrainMultiplier = v,  "ServerSettings"),
        ("PlayerCharacterFoodDrainMultiplier",r => r.PlayerCharacterFoodDrainMultiplier,(r,v) => r.PlayerCharacterFoodDrainMultiplier = v,"ServerSettings"),
        ("MatingIntervalMultiplier",          r => r.MatingIntervalMultiplier,          (r,v) => r.MatingIntervalMultiplier = v,          "ServerSettings"),
        ("EggHatchSpeedMultiplier",           r => r.EggHatchSpeedMultiplier,           (r,v) => r.EggHatchSpeedMultiplier = v,           "ServerSettings"),
        ("BabyMatureSpeedMultiplier",         r => r.BabyMatureSpeedMultiplier,         (r,v) => r.BabyMatureSpeedMultiplier = v,         "ServerSettings"),
    };

    public static Rates ReadFrom(IniFile ini)
    {
        var r = new Rates();
        foreach (var spec in Specs)
        {
            var s = ini.TryGetSection(spec.Section);
            if (s == null) continue;
            var raw = s.GetSingle(spec.Key);
            if (raw != null && double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
                spec.Set(r, v);
        }
        return r;
    }

    public static void WriteInto(IniFile ini, Rates r)
    {
        foreach (var spec in Specs)
        {
            var s = ini.GetOrCreateSection(spec.Section);
            var v = spec.Get(r);
            s.SetSingle(spec.Key, v.ToString("0.######", CultureInfo.InvariantCulture));
        }
    }
}
