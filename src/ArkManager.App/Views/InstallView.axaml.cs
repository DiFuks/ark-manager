using ArkManager.App.Services;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ArkManager.App.Views;

public partial class InstallView : UserControl
{
    public InstallView()
    {
        AvaloniaXamlLoader.Load(this);
        if (this.FindControl<TextBox>("LogBox") is { } box) LogAutoScroll.Attach(box);
    }
}
