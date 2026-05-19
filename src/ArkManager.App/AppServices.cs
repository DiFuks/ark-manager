using ArkManager.App.ViewModels;
using ArkManager.Core.Services;
using ArkManager.Core.Services.Backups;
using ArkManager.Core.Services.Config;
using ArkManager.Core.Services.Doctor;
using ArkManager.Core.Services.Launchers;
using ArkManager.Core.Services.Mods;
using ArkManager.Core.Services.Steam;
using Microsoft.Extensions.DependencyInjection;

namespace ArkManager.App;

/// <summary>Сервис-локатор-обёртка над DI. Поднимается один раз в App.OnFrameworkInitializationCompleted.</summary>
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
        sc.AddSingleton<ModsService>();
        sc.AddSingleton<WhiskyLauncher>();
        sc.AddSingleton<LocalWineLauncher>();
        sc.AddSingleton<ParallelsLauncher>();
        sc.AddSingleton<LauncherFactory>();
        sc.AddSingleton<ServerManager>();
        sc.AddSingleton<DoctorService>();

        // ViewModels
        sc.AddSingleton<MainWindowViewModel>();
        sc.AddTransient<DashboardViewModel>();
        sc.AddTransient<InstallViewModel>();
        sc.AddTransient<ConfigViewModel>();
        sc.AddTransient<ModsViewModel>();
        sc.AddTransient<BackupsViewModel>();
        sc.AddTransient<ServerViewModel>();
        sc.AddTransient<SettingsViewModel>();
        sc.AddTransient<DoctorViewModel>();
        sc.AddTransient<RconViewModel>();

        Provider = sc.BuildServiceProvider();
    }

    public static T Get<T>() where T : notnull => Provider.GetRequiredService<T>();
}
