using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ArkManager.App.ViewModels;
using ArkManager.App.Views;

namespace ArkManager.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        AppServices.Build();
        // Запускаем синглтон-воркер, чтобы он подписался на StateChanged.
        _ = AppServices.Get<ArkManager.Core.Services.Rcon.PlayerPoller>();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow { DataContext = AppServices.Get<MainWindowViewModel>() };
            desktop.MainWindow = window;
            Services.Browse.Owner = window;
        }

        base.OnFrameworkInitializationCompleted();
    }

    public static void UiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }

    public static void OpenInFinder(string path)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                Process.Start("open", path);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Process.Start("explorer.exe", path);
            else
                Process.Start("xdg-open", path);
        }
        catch { /* ignore */ }
    }

    public static void OpenInBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    Process.Start("open", url);
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    Process.Start(new ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = true });
                else
                    Process.Start("xdg-open", url);
            }
            catch { /* ignore */ }
        }
    }
}

