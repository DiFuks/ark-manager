using Avalonia.Controls;

namespace ArkManager.App.Services;

/// <summary>
/// Привязка к read-only лог-TextBox: при изменении Text прокручивает к концу,
/// если у юзера сейчас нет активного выделения (чтобы не сбивать копирование).
/// </summary>
public static class LogAutoScroll
{
    public static void Attach(TextBox tb)
    {
        tb.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBox.TextProperty) return;
            // Если юзер выделил кусок текста — не дёргаем, иначе видимая область
            // прыгает и сбивает Cmd+C. Если выделения нет — гоним к концу.
            if (tb.SelectionStart != tb.SelectionEnd) return;
            tb.CaretIndex = tb.Text?.Length ?? 0;
        };
    }
}
