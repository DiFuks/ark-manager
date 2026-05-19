using ArkManager.Core.Models;

namespace ArkManager.Core.Services.Launchers;

public sealed class LauncherFactory
{
    private readonly WhiskyLauncher _whisky;
    private readonly LocalWineLauncher _wine;
    private readonly ParallelsLauncher _parallels;

    public LauncherFactory(WhiskyLauncher whisky, LocalWineLauncher wine, ParallelsLauncher parallels)
    {
        _whisky = whisky;
        _wine = wine;
        _parallels = parallels;
    }

    public IServerLauncher Resolve(LaunchMode mode) => mode switch
    {
        LaunchMode.Whisky => _whisky,
        LaunchMode.LocalWine => _wine,
        LaunchMode.Parallels => _parallels,
        _ => _whisky,
    };

    public IEnumerable<(LaunchMode Mode, IServerLauncher Launcher)> All()
    {
        yield return (LaunchMode.Whisky, _whisky);
        yield return (LaunchMode.LocalWine, _wine);
        yield return (LaunchMode.Parallels, _parallels);
    }
}
