using Avalonia.Controls;

namespace ArkManager.App.Services;

/// <summary>
/// Attach to a read-only log TextBox: on Text change, scrolls to the end
/// if the user has no active selection (so we don't break a copy in progress).
/// </summary>
public static class LogAutoScroll
{
    public static void Attach(TextBox tb)
    {
        tb.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBox.TextProperty) return;
            // If the user has highlighted a piece of text — don't budge, otherwise the visible
            // area jumps and breaks Cmd+C. If there's no selection — scroll to the end.
            if (tb.SelectionStart != tb.SelectionEnd) return;
            tb.CaretIndex = tb.Text?.Length ?? 0;
        };
    }
}
