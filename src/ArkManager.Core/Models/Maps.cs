namespace ArkManager.Core.Models;

/// <summary>
/// Известные ASA-карты. Список можно дополнять; в UI всегда есть «Custom»
/// (если введена не из списка — отображается как Custom, значение Map берётся как есть).
/// </summary>
public static class Maps
{
    public static readonly IReadOnlyList<MapPreset> Known = new[]
    {
        new MapPreset("The Island",      "TheIsland_WP"),
        new MapPreset("The Center",      "TheCenter_WP"),
        new MapPreset("Scorched Earth",  "ScorchedEarth_WP"),
        new MapPreset("Aberration",      "Aberration_WP"),
        new MapPreset("Extinction",      "Extinction_WP"),
        new MapPreset("Astraeos",        "Astraeos_WP"),
        new MapPreset("Ragnarok",        "Ragnarok_WP"),
    };
}

public sealed record MapPreset(string Title, string Map)
{
    public override string ToString() => $"{Title} ({Map})";
}
