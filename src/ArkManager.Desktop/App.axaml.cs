using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ArkManager.App.ViewModels;
using ArkManager.App.Views;
using ArkManager.Core.Services;
using ArkManager.Core.Services.Config;

namespace ArkManager.App;

public partial class App : Application
{
    // Keep signal registrations alive (otherwise GC collects them and the hook does not fire).
    private static PosixSignalRegistration? _sigInt, _sigTerm, _sigQuit;
    private static int _shutdownDone;

    // The single session log (server console + app diagnostics funnel here). Kept alive statically.
    private static AppLog? _appLog;
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        AppServices.Build();

        // Catch crashes of ArkManager itself (not the server) into a file so a user can attach it.
        _appLog = AppServices.Get<AppLog>();
        _appLog.Write($"ArkManager started — {RuntimeInformation.OSDescription} / {RuntimeInformation.OSArchitecture}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            _appLog?.Write("[FATAL] " + ((e.ExceptionObject as Exception)?.ToString() ?? "unknown error"));
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            _appLog?.Write("[unobserved-task] " + e.Exception);
            e.SetObserved();
        };
        Dispatcher.UIThread.UnhandledException += (_, e) => _appLog?.Write("[UI] " + e.Exception);

        // Resolve the singleton worker so it subscribes to StateChanged.
        _ = AppServices.Get<ArkManager.Core.Services.Rcon.PlayerPoller>();
        _ = AppServices.Get<ArkManager.Core.Services.Backups.AutoBackupWorker>();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow { DataContext = AppServices.Get<MainWindowViewModel>() };
            desktop.MainWindow = window;
            Services.Browse.Owner = window;

            // Window close / Quit — shut the server down so it doesn't get orphaned.
            desktop.ShutdownRequested += (_, _) => StopServerOnExit();
        }

        // Server lifecycle notifications (independent of the currently open tab).
        var server = AppServices.Get<ServerManager>();
        var config = AppServices.Get<ConfigService>();

        // Safety-net: ensure the ini files exist for users whose install pre-dates this feature.
        // EnsureIni is idempotent — it is a no-op when the ini already exists.
        var settings = AppServices.Get<SettingsService>();
        if (settings.Current.ServerInstallPath is { } installPath
            && File.Exists(Path.Combine(installPath, "steamapps", "appmanifest_2430930.acf")))
        {
            config.EnsureIni();
        }

        string Name() => config.Snapshot.SessionName;

        // "Green" = world loaded and server accepting players (not merely that the process started).
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
                _ => null, // Running is caught via ReadyChanged; Stopping is transient, skip it
            };
            if (msg != null) Notify("ArkManager", msg);
        };

        // If the previous manager was killed hard (Force Quit/SIGKILL/crash) and the server is still alive —
        // adopt it, so we can show Running and let the user stop it instead of spawning a second one.
        _ = server.AdoptIfRunningAsync();

        // Ctrl+C (SIGINT) and kill (SIGTERM/SIGQUIT) under `dotnet run`: intercept,
        // gracefully shut the server down and exit. Without this the server process outlives the manager.
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
                ctx.Cancel = true;          // cancel the default termination so we can shut the server down first
                StopServerOnExit();
                Environment.Exit(0);
            });
        }
        catch { /* platform without POSIX signals — not critical */ }
    }

    /// <summary>Idempotent: gracefully shuts the server down once with a combined timeout (saveworld + kill).</summary>
    private static void StopServerOnExit()
    {
        if (Interlocked.Exchange(ref _shutdownDone, 1) != 0) return;
        try
        {
            var server = AppServices.Get<ServerManager>();
            // Hop to the thread pool before .Wait(). ShutdownRequested fires on the UI thread,
            // and ShutdownAsync awaits without ConfigureAwait(false) — its continuations would
            // try to resume on the (blocked) UI thread → deadlock, Avalonia force-closes after
            // 45s, and the ASA process survives as an orphan. Running on a pool thread breaks
            // the SyncContext capture so kill actually runs. Hit on Windows where the server is
            // usually in Loading + no admin password → graceful save is skipped and the hard
            // kill is the only thing standing between us and an orphan.
            Task.Run(() => server.ShutdownAsync()).Wait(TimeSpan.FromSeconds(45));
        }
        catch { /* DI not yet built / already stopped — ignore */ }
    }

    public static void UiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }

    /// <summary>Native macOS notification via osascript. No-op on other platforms.</summary>
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
        catch { /* notification — not critical */ }
    }

    public static void OpenInFinder(string path)
    {
        try
        {
            // ArgumentList correctly quotes paths with spaces ("Application Support").
            // Process.Start("open", path) splits on spaces and breaks.
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

