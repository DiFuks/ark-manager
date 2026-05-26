using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ArkManager.App.ViewModels;

public sealed record NavItem(string Title, string Icon, ViewModelBase ViewModel);

public partial class MainWindowViewModel : ViewModelBase
{
    public ObservableCollection<NavItem> NavItems { get; }

    [ObservableProperty] private NavItem _selected = null!;

    public ViewModelBase CurrentPage => Selected.ViewModel;

    partial void OnSelectedChanged(NavItem value) => OnPropertyChanged(nameof(CurrentPage));

    public MainWindowViewModel(
        InstallViewModel install,
        ConfigViewModel config,
        ModsViewModel mods,
        BackupsViewModel backups,
        ServerViewModel server,
        RconViewModel rcon,
        SettingsViewModel settings,
        DoctorViewModel doctor)
    {
        NavItems = new ObservableCollection<NavItem>
        {
            new("Server",      "▶️", server),
            new("RCON",        "🛰️", rcon),
            new("Install",     "⬇️", install),
            new("Config",      "⚙️", config),
            new("Mods",        "🧩", mods),
            new("Backups",     "💾", backups),
            new("Doctor",      "🩺", doctor),
            new("Settings",    "🔧", settings),
        };
        _selected = NavItems[0];
    }

    // Параметрless конструктор нужен только для XAML-дизайнера.
    public MainWindowViewModel() : this(
        new InstallViewModel(),
        new ConfigViewModel(),
        new ModsViewModel(),
        new BackupsViewModel(),
        new ServerViewModel(),
        new RconViewModel(),
        new SettingsViewModel(),
        new DoctorViewModel())
    {
    }
}
