using ArkManager.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace ArkManager.App.Views;

public partial class ModsView : UserControl
{
    public ModsView() { AvaloniaXamlLoader.Load(this); }

    private void MessageTextBox_KeyDown(object? sender, KeyEventArgs e){
        if(e.Key != Key.Enter){
            return;
        }

        if(sender is TextBox tb && string.IsNullOrWhiteSpace(tb.Text)){
            return;
        }

        e.Handled = true;
        
        if(DataContext is ModsViewModel mv && mv.AddCommand.CanExecute(null)){
            mv.AddCommand.Execute(null);
        }
    }
}
