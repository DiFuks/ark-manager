namespace ArkManager.Core.Util;

/// <summary>Поиск подстроки для find-next в текстовых редакторах (raw ini).</summary>
public static class TextSearch
{
    /// <summary>
    /// Индекс следующего вхождения <paramref name="term"/> в <paramref name="text"/>,
    /// начиная с <paramref name="fromIndex"/>, без учёта регистра. Если после fromIndex
    /// совпадений нет — заворачивается к началу. Возвращает -1, если term пуст или
    /// не встречается вовсе.
    /// </summary>
    public static int NextMatch(string text, string term, int fromIndex)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(term)) return -1;

        var start = Math.Clamp(fromIndex, 0, text.Length);
        var idx = text.IndexOf(term, start, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0) return idx;

        // Заворот к началу.
        return text.IndexOf(term, 0, StringComparison.OrdinalIgnoreCase);
    }
}
