using System.Text;

namespace ArkManager.Core.Services.Config;

/// <summary>
/// Простой парсер ini-файлов в стиле UE/ARK.
/// Особенности ARK:
///  - в [/Script/ShooterGame.ShooterGameMode] и [ServerSettings] часто встречаются повторяющиеся ключи
///    (например, OverrideEngramEntries=) — нужно сохранять как многозначные.
///  - комментарии (; ... и # ...) и пустые строки сохраняем для round-trip read-modify-write.
///  - регистр ключей сохраняем как есть; lookup case-insensitive.
/// </summary>
public sealed class IniFile
{
    public List<IniSection> Sections { get; } = new();
    public List<IniLine> LeadingTrivia { get; } = new();

    public IniSection GetOrCreateSection(string name)
    {
        var s = Sections.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        if (s != null) return s;
        s = new IniSection(name);
        Sections.Add(s);
        return s;
    }

    public IniSection? TryGetSection(string name)
        => Sections.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));

    public static IniFile Parse(string text)
    {
        var file = new IniFile();
        IniSection? current = null;
        foreach (var rawLine in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine;
            var trimmed = line.TrimStart();
            if (trimmed.Length == 0 || trimmed.StartsWith(';') || trimmed.StartsWith('#'))
            {
                var trivia = new IniLine(IniLineKind.Trivia, Raw: line);
                if (current == null) file.LeadingTrivia.Add(trivia);
                else current.Lines.Add(trivia);
                continue;
            }
            if (trimmed.StartsWith('['))
            {
                var end = trimmed.IndexOf(']');
                if (end > 0)
                {
                    var name = trimmed.Substring(1, end - 1);
                    current = file.GetOrCreateSection(name);
                    continue;
                }
            }
            var eq = line.IndexOf('=');
            if (eq <= 0)
            {
                var trivia = new IniLine(IniLineKind.Trivia, Raw: line);
                if (current == null) file.LeadingTrivia.Add(trivia);
                else current.Lines.Add(trivia);
                continue;
            }
            var key = line[..eq].Trim();
            var value = line[(eq + 1)..];
            current ??= file.GetOrCreateSection("");
            current.Lines.Add(new IniLine(IniLineKind.Entry, Key: key, Value: value));
        }
        return file;
    }

    public static IniFile Load(string path) => Parse(File.ReadAllText(path));

    public override string ToString()
    {
        var sb = new StringBuilder();
        foreach (var t in LeadingTrivia) sb.AppendLine(t.Raw ?? "");
        foreach (var s in Sections)
        {
            if (!string.IsNullOrEmpty(s.Name))
                sb.Append('[').Append(s.Name).AppendLine("]");
            foreach (var l in s.Lines)
            {
                if (l.Kind == IniLineKind.Trivia) sb.AppendLine(l.Raw ?? "");
                else sb.Append(l.Key).Append('=').AppendLine(l.Value);
            }
        }
        return sb.ToString();
    }

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, ToString());
    }
}

public sealed class IniSection
{
    public string Name { get; }
    public List<IniLine> Lines { get; } = new();

    public IniSection(string name) => Name = name;

    public string? GetSingle(string key)
        => Lines.LastOrDefault(l => l.Kind == IniLineKind.Entry && string.Equals(l.Key, key, StringComparison.OrdinalIgnoreCase))?.Value;

    public IEnumerable<string> GetAll(string key)
        => Lines.Where(l => l.Kind == IniLineKind.Entry && string.Equals(l.Key, key, StringComparison.OrdinalIgnoreCase))
                .Select(l => l.Value!);

    public void SetSingle(string key, string? value)
    {
        var idx = -1;
        for (var i = 0; i < Lines.Count; i++)
        {
            if (Lines[i].Kind == IniLineKind.Entry && string.Equals(Lines[i].Key, key, StringComparison.OrdinalIgnoreCase))
            { idx = i; break; }
        }
        if (value == null)
        {
            if (idx >= 0) Lines.RemoveAt(idx);
            return;
        }
        if (idx < 0)
        {
            Lines.Add(new IniLine(IniLineKind.Entry, Key: key, Value: value));
        }
        else
        {
            Lines[idx] = new IniLine(IniLineKind.Entry, Key: key, Value: value);
        }
    }

    public void RemoveAll(string key)
    {
        Lines.RemoveAll(l => l.Kind == IniLineKind.Entry && string.Equals(l.Key, key, StringComparison.OrdinalIgnoreCase));
    }
}

public enum IniLineKind { Entry, Trivia }

public sealed record IniLine(IniLineKind Kind, string? Key = null, string? Value = null, string? Raw = null);
