using ArkManager.App.ViewModels;
using ArkManager.Core.Services;
using ArkManager.Core.Services.Backups;
using ArkManager.Core.Services.Config;
using ArkManager.Core.Services.Launchers;
using ArkManager.Core.Services.Mods;
using ArkManager.Core.Services.Rcon;
using ArkManager.Core.Services.Steam;
using Microsoft.Extensions.DependencyInjection;

namespace ArkManager.App;

/// <summary>Service-locator wrapper over DI. Bootstrapped once in App.OnFrameworkInitializationCompleted.</summary>
public static class AppServices
{
    public static IServiceProvider Provider { get; private set; } = default!;

    public static void Build()
    {
        var sc = new ServiceCollection();

        // Core singletons
        sc.AddSingleton<AppPaths>();
        sc.AddSingleton<SettingsService>();
        sc.AddSingleton<SteamCmdService>();
        sc.AddSingleton<ConfigService>();
        sc.AddSingleton<BackupService>();
        sc.AddSingleton<AutoBackupWorker>();
        sc.AddSingleton<CurseForgeClient>();
        sc.AddSingleton<ModsService>();
        if (OperatingSystem.IsWindows())
            sc.AddSingleton<IServerLauncher, NativeWindowsLauncher>();
        else
            sc.AddSingleton<IServerLauncher, BundledWineLauncher>();
        sc.AddSingleton<ServerManager>();
        sc.AddSingleton<PlayerPoller>();

        // ViewModels
        sc.AddSingleton<MainWindowViewModel>();
        sc.AddTransient<InstallViewModel>();
        sc.AddTransient<ConfigViewModel>();
        sc.AddTransient<ModsViewModel>();
        sc.AddTransient<BackupsViewModel>();
        sc.AddTransient<ServerViewModel>();
        sc.AddTransient<RconViewModel>();

        Provider = sc.BuildServiceProvider();
    }

    public static T Get<T>() where T : notnull => Provider.GetRequiredService<T>();
}
