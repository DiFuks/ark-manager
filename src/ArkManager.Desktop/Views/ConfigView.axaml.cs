using System.Linq;
using ArkManager.Core.Util;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace ArkManager.App.Views;

public partial class ConfigView : UserControl
{
    public ConfigView() { AvaloniaXamlLoader.Load(this); }

    private void OnFindNext(object? sender, RoutedEventArgs e) => FindInBar(sender as Control);

    // Enter in the search field = Find next.
    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) FindInBar(sender as Control);
    }

    // The controls live inside lazily-realised TabItems (own namescope), so root-level x:Name
    // fields are null here. We locate search/editor/info by structure from the clicked element:
    // find-bar — StackPanel [search, button, info]; editor — the only TextBox that is a direct
    // child of the parent Grid (the search box lives inside the find-bar StackPanel, not the
    // Grid). We do NOT pin it to a row index — the "file changed on disk" ribbon shifts the
    // editor's row, and a hard-coded row is exactly what silently broke find earlier.
    private static void FindInBar(Control? origin)
    {
        if (origin?.Parent is not Panel bar || bar.Parent is not Grid grid) return;
        var search = bar.Children.OfType<TextBox>().FirstOrDefault();
        var info = bar.Children.OfType<TextBlock>().FirstOrDefault();
        var editor = grid.Children.OfType<TextBox>().FirstOrDefault();
        if (search != null && editor != null) FindNext(editor, search, info);
    }

    private static void FindNext(TextBox editor, TextBox search, TextBlock? info)
    {
        var term = search.Text ?? "";
        if (term.Length == 0) { if (info != null) info.Text = ""; return; }

        var text = editor.Text ?? "";
        // Continue after the current selection/caret, wrapping back to the start.
        var from = System.Math.Max(editor.SelectionStart, editor.SelectionEnd);
        var idx = TextSearch.NextMatch(text, term, from);
        if (idx < 0) { if (info != null) info.Text = "no matches"; return; }

        editor.Focus();
        editor.SelectionStart = idx;
        editor.SelectionEnd = idx + term.Length;
        editor.CaretIndex = idx + term.Length; // scrolls to the match
        if (info != null) info.Text = "";
    }
}
