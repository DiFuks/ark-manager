namespace ArkManager.Core.Models;

/// <summary>
/// Known ASA maps. The list can be extended; the UI always has a "Custom" entry
/// (if the value isn't in the list it shows as Custom and Map is taken as-is).
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
