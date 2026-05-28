using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ArkManager.App.ViewModels;
using ArkManager.App.Views;
using ArkManager.Core.Services;

namespace ArkManager.App;

public partial class App : Application
{
    // Держим регистрации сигналов живыми (иначе соберёт GC и хук не сработает).
    private static PosixSignalRegistration? _sigInt, _sigTerm, _sigQuit;
    private static int _shutdownDone;
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

            // Закрытие окна / Quit — гасим сервер, чтобы он не остался осиротевшим.
            desktop.ShutdownRequested += (_, _) => StopServerOnExit();
        }

        // Уведомления о жизненном цикле сервера (независимо от открытой вкладки).
        var server = AppServices.Get<ServerManager>();
        var settings = AppServices.Get<SettingsService>();
        string Name() => settings.Current.LaunchOptions.SessionName;

        // «Зелёный» = мир загружен и сервер принимает игроков (а не просто стартовал процесс).
        server.ReadyChanged += ready =>
        {
            if (ready) Notify("ArkManager", $"Server \"{Name()}\" is up and accepting players");
        };
        server.StateChanged += state =>
        {
            var msg = state switch
            {
                ServerState.Starting => $"Server \"{Name()}\" is starting…",
                ServerState.Stopped  => $"Server \"{Name()}\" stopped",
                ServerState.Crashed  => $"Server \"{Name()}\" crashed",
                _ => null, // Running ловим через ReadyChanged; Stopping — транзитный, пропускаем
            };
            if (msg != null) Notify("ArkManager", msg);
        };

        // Если прошлый менеджер убили жёстко (Force Quit/SIGKILL/краш) и сервер остался жив —
        // подхватываем его, чтобы показать Running и дать остановить, а не плодить второй.
        _ = server.AdoptIfRunningAsync();

        // Ctrl+C (SIGINT) и kill (SIGTERM/SIGQUIT) в режиме `dotnet run`: перехватываем,
        // gracefully гасим сервер и выходим. Без этого процесс сервера переживает менеджер.
        RegisterShutdownSignal(PosixSignal.SIGINT, ref _sigInt);
        RegisterShutdownSignal(PosixSignal.SIGTERM, ref _sigTerm);
        RegisterShutdownSignal(PosixSignal.SIGQUIT, ref _sigQuit);

        base.OnFrameworkInitializationCompleted();
    }

    private static void RegisterShutdownSignal(PosixSignal sig, ref PosixSignalRegistration? slot)
    {
        try
        {
            slot = PosixSignalRegistration.Create(sig, ctx =>
            {
                ctx.Cancel = true;          // отменяем дефолтное завершение, чтобы успеть погасить сервер
                StopServerOnExit();
                Environment.Exit(0);
            });
        }
        catch { /* платформа без POSIX-сигналов — не критично */ }
    }

    /// <summary>Идемпотентно: один раз gracefully гасит сервер с общим таймаутом (saveworld + kill).</summary>
    private static void StopServerOnExit()
    {
        if (Interlocked.Exchange(ref _shutdownDone, 1) != 0) return;
        try
        {
            var server = AppServices.Get<ServerManager>();
            // Блокируемся, но ограниченно — иначе процесс выйдет раньше, чем сервер убит.
            server.ShutdownAsync().Wait(TimeSpan.FromSeconds(45));
        }
        catch { /* DI ещё не поднят / уже остановлен — игнор */ }
    }

    public static void UiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }

    /// <summary>Нативное уведомление macOS через osascript. На других платформах — no-op.</summary>
    public static void Notify(string title, string body)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return;
        try
        {
            static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
            var psi = new ProcessStartInfo { FileName = "osascript", UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add("-e");
            psi.ArgumentList.Add($"display notification \"{Esc(body)}\" with title \"{Esc(title)}\"");
            Process.Start(psi);
        }
        catch { /* уведомление — не критично */ }
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

