using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace ArkManager.App.Behaviors;

// Attached behavior: pressing Enter in a TextBox runs the bound command (the
// same one its Send/Add button uses). Lives here instead of per-view code-behind
// so the two single-line input boxes (RCON, Mods) don't each copy a KeyDown handler.
public static class SubmitOnEnter
{
    public static readonly AttachedProperty<ICommand?> CommandProperty =
        AvaloniaProperty.RegisterAttached<TextBox, ICommand?>("Command", typeof(SubmitOnEnter));

    public static void SetCommand(TextBox target, ICommand? value) => target.SetValue(CommandProperty, value);

    public static ICommand? GetCommand(TextBox target) => target.GetValue(CommandProperty);

    static SubmitOnEnter()
    {
        CommandProperty.Changed.AddClassHandler<TextBox>((tb, e) =>
        {
            tb.KeyDown -= OnKeyDown;
            if (e.NewValue is ICommand)
                tb.KeyDown += OnKeyDown;
        });
    }

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox tb || string.IsNullOrWhiteSpace(tb.Text))
            return;

        var command = GetCommand(tb);
        if (command is null || !command.CanExecute(null))
            return;

        e.Handled = true;
        command.Execute(null);
    }
}
