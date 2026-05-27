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

    // Enter в поле поиска = Find next.
    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) FindInBar(sender as Control);
    }

    // Контролы лежат внутри лениво-реализуемых TabItem (свой namescope), поэтому x:Name-поля
    // корня тут null. Находим search/editor/info по структуре от нажатого элемента:
    // find-bar — StackPanel [search, button, info]; редактор — TextBox в Row 1 родительского Grid.
    private static void FindInBar(Control? origin)
    {
        if (origin?.Parent is not Panel bar || bar.Parent is not Grid grid) return;
        var search = bar.Children.OfType<TextBox>().FirstOrDefault();
        var info = bar.Children.OfType<TextBlock>().FirstOrDefault();
        var editor = grid.Children.OfType<TextBox>().FirstOrDefault(c => Grid.GetRow(c) == 1);
        if (search != null && editor != null) FindNext(editor, search, info);
    }

    private static void FindNext(TextBox editor, TextBox search, TextBlock? info)
    {
        var term = search.Text ?? "";
        if (term.Length == 0) { if (info != null) info.Text = ""; return; }

        var text = editor.Text ?? "";
        // Продолжаем после текущего выделения/каретки, с заворотом к началу.
        var from = System.Math.Max(editor.SelectionStart, editor.SelectionEnd);
        var idx = TextSearch.NextMatch(text, term, from);
        if (idx < 0) { if (info != null) info.Text = "no matches"; return; }

        editor.Focus();
        editor.SelectionStart = idx;
        editor.SelectionEnd = idx + term.Length;
        editor.CaretIndex = idx + term.Length; // прокручивает к совпадению
        if (info != null) info.Text = "";
    }
}
