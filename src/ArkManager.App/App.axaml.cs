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
        _ = AppServices.Get<ArkManager.Core.Services.Backups.AutoBackupWorker>();

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
            // ArgumentList корректно квотит путь с пробелами ("Application Support").
            // Process.Start("open", path) сплитит по пробелам и ломается.
            var psi = new ProcessStartInfo
            {
                FileName = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "open"
                         : RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "explorer.exe"
                         : "xdg-open",
                UseShellExecute = false,
            };
            psi.ArgumentList.Add(path);
            Process.Start(psi);
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
                var psi = new ProcessStartInfo
                {
                    FileName = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "open"
                             : RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "cmd"
                             : "xdg-open",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    psi.ArgumentList.Add("/c");
                    psi.ArgumentList.Add("start");
                    psi.ArgumentList.Add(url);
                }
                else
                {
                    psi.ArgumentList.Add(url);
                }
                Process.Start(psi);
            }
            catch { /* ignore */ }
        }
    }
}

