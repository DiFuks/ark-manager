using ArkManager.App.Services;
using ArkManager.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace ArkManager.App.Views;

public partial class RconView : UserControl
{
    public RconView()
    {
        AvaloniaXamlLoader.Load(this);
        if (this.FindControl<TextBox>("LogBox") is { } box) LogAutoScroll.Attach(box);
    }

    private void MessageTextBox_KeyDown(object? sender, KeyEventArgs e){
        if(e.Key != Key.Enter){
            return;
        }

        if(sender is TextBox tb && string.IsNullOrWhiteSpace(tb.Text)){
            return;
        }

        e.Handled = true;
        
        if(DataContext is RconViewModel rm && rm.SendCommand.CanExecute(null)){
            rm.SendCommand.Execute(null);
        }
    }
}
