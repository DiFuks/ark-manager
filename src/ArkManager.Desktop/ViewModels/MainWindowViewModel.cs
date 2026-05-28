using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ArkManager.App.ViewModels;

public sealed record NavItem(string Title, Geometry Icon, ViewModelBase ViewModel);

public partial class MainWindowViewModel : ViewModelBase
{
    // Solid glyph paths (mirror Themes/Icons.axaml). 24x24 space.
    private static Geometry G(string path) => Geometry.Parse(path);

    public ObservableCollection<NavItem> NavItems { get; }

    [ObservableProperty] private NavItem _selected = null!;

    public ViewModelBase CurrentPage => Selected.ViewModel;

    partial void OnSelectedChanged(NavItem value)
    {
        OnPropertyChanged(nameof(CurrentPage));
        // На Mods-табе нет кнопки «Resolve names» — резолвим имена сами при открытии.
        // Уже закэшированные ID ModsService пропустит, лишних запросов не будет.
        if (value.ViewModel is ModsViewModel mods) _ = mods.AutoResolveNamesAsync();
    }

    public MainWindowViewModel(
        InstallViewModel install,
        ConfigViewModel config,
        ModsViewModel mods,
        BackupsViewModel backups,
        ServerViewModel server,
        RconViewModel rcon,
        DoctorViewModel doctor)
    {
        NavItems = new ObservableCollection<NavItem>
        {
            new("Server",   G("M7 5 L19 12 L7 19 Z"), server),
            new("RCON",     G("M3 5 H21 V19 H3 Z M6 9 L10 12 L6 15 V13 L8 12 L6 11 Z M12 14 H17 V16 H12 Z"), rcon),
            new("Install",  G("M11 4 H13 V11 H16 L12 16 L8 11 H11 Z M5 18 H19 V20 H5 Z"), install),
            new("Config",   G("M3 6 H21 V8 H3 Z M3 11 H21 V13 H3 Z M3 16 H15 V18 H3 Z"), config),
            new("Mods",     G("M12 3 L20 7 V17 L12 21 L4 17 V7 Z M12 8 L16 10 V14 L12 16 L8 14 V10 Z"), mods),
            new("Backups",  G("M4 4 H20 V8 H4 Z M5 9 H19 V20 H5 Z M9 12 H15 V14 H9 Z"), backups),
            new("Doctor",   G("M10 3 H14 V9 H20 V13 H14 V21 H10 V13 H4 V9 H10 Z"), doctor),
        };
        _selected = NavItems[0];

        // Deep-link на стартовый таб через env (для тестов/скриншотов; по умолчанию выключено).
        var startTab = Environment.GetEnvironmentVariable("ARKMANAGER_START_TAB");
        if (!string.IsNullOrWhiteSpace(startTab))
        {
            var match = NavItems.FirstOrDefault(n => string.Equals(n.Title, startTab, StringComparison.OrdinalIgnoreCase));
            if (match != null) _selected = match;
        }
    }

    // Параметрless конструктор нужен только для XAML-дизайнера.
    public MainWindowViewModel() : this(
        new InstallViewModel(),
        new ConfigViewModel(),
        new ModsViewModel(),
        new BackupsViewModel(),
        new ServerViewModel(),
        new RconViewModel(),
        new DoctorViewModel())
    {
    }
}
