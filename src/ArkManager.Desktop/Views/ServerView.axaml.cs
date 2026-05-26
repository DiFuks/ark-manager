using ArkManager.App.Services;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ArkManager.App.Views;

public partial class ServerView : UserControl
{
    public ServerView()
    {
        AvaloniaXamlLoader.Load(this);
        if (this.FindControl<TextBox>("LogBox") is { } box) LogAutoScroll.Attach(box);
    }
}
