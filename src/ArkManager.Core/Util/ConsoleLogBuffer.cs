using System.Text;

namespace ArkManager.Core.Util;

/// <summary>
/// Thread-safe, batched text buffer behind the console views. Producers (process stdout/stderr
/// callbacks, RCON responses) call <see cref="Append"/> from any thread — cheap, it only locks a
/// small pending builder. A UI timer calls <see cref="Flush"/> a few times a second to fold the
/// pending lines into the displayed text in ONE shot.
///
/// This is the fix for the "huge logs froze the UI" case: previously each line did
/// <c>Log += line</c> (an O(n) copy of the whole buffer) plus a full TextBox re-render, on the UI
/// thread, once per line. A crash-looping server spewing hundreds of lines/second saturated the
/// dispatcher. Batching collapses a flood into one re-render per tick; the smaller cap keeps each
/// re-render cheap.
///
/// <see cref="Flush"/>/<see cref="Clear"/> mutate the displayed text and must be called from the
/// UI thread (the timer tick / a command). <see cref="Append"/> is safe from any thread.
/// </summary>
public sealed class ConsoleLogBuffer
{
    private readonly int _maxChars;
    private readonly StringBuilder _pending = new();
    private readonly object _lock = new();
    private string _text = "";

    public ConsoleLogBuffer(int maxChars) => _maxChars = maxChars;

    /// <summary>The text as of the last <see cref="Flush"/>. Read on the UI thread.</summary>
    public string Text => _text;

    /// <summary>Queue a line for the next flush. Safe to call from any thread.</summary>
    public void Append(string line)
    {
        lock (_lock) _pending.Append(line).Append('\n');
    }

    /// <summary>
    /// Fold pending lines into the displayed text, trimming the head (whole lines) past the cap.
    /// Returns the new text, or <c>null</c> when nothing was pending (so the caller can skip the
    /// property assignment / re-render entirely).
    /// </summary>
    public string? Flush()
    {
        string pending;
        lock (_lock)
        {
            if (_pending.Length == 0) return null;
            pending = _pending.ToString();
            _pending.Clear();
        }

        var combined = _text + pending;
        if (combined.Length > _maxChars)
        {
            // Cut at the first line boundary at/after the overflow point so we never leave a
            // partial leading line. Fall back to a hard cut if a single line exceeds the cap.
            var cut = combined.IndexOf('\n', combined.Length - _maxChars);
            combined = cut >= 0 ? combined[(cut + 1)..] : combined[^_maxChars..];
        }

        _text = combined;
        return _text;
    }

    /// <summary>Drop everything (pending + displayed). Returns the new (empty) text.</summary>
    public string Clear()
    {
        lock (_lock) _pending.Clear();
        _text = "";
        return _text;
    }
}
