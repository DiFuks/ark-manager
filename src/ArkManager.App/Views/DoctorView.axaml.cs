using ArkManager.App.Services;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ArkManager.App.Views;

public partial class DoctorView : UserControl
{
    public DoctorView()
    {
        AvaloniaXamlLoader.Load(this);
        if (this.FindControl<TextBox>("LogBox") is { } box) LogAutoScroll.Attach(box);
    }
}
