namespace ArkManager.Core.Util;

/// <summary>Substring search for find-next in text editors (raw ini).</summary>
public static class TextSearch
{
    /// <summary>
    /// Index of the next occurrence of <paramref name="term"/> in <paramref name="text"/>,
    /// starting at <paramref name="fromIndex"/>, case-insensitive. If there are no matches
    /// after fromIndex, wraps to the beginning. Returns -1 if term is empty or does not
    /// occur at all.
    /// </summary>
    public static int NextMatch(string text, string term, int fromIndex)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(term)) return -1;

        var start = Math.Clamp(fromIndex, 0, text.Length);
        var idx = text.IndexOf(term, start, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0) return idx;

        // Wrap to the beginning.
        return text.IndexOf(term, 0, StringComparison.OrdinalIgnoreCase);
    }
}
