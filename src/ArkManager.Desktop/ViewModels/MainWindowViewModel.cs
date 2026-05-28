using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ArkManager.App.ViewModels;

public sealed record NavItem(string Title, Geometry Icon, ViewModelBase ViewModel);

public partial class MainWindowViewModel : ViewModelBase
{
    // Solid glyph paths (mirror Themes/Icons.axaml). 24x24 space.
    private static Geometry G(string path) => Geometry.Parse(path);

    private readonly InstallViewModel _install;
    private readonly NavItem[] _allItems;

    public ObservableCollection<NavItem> NavItems { get; } = new();

    [ObservableProperty] private NavItem _selected = null!;

    public ViewModelBase CurrentPage => Selected.ViewModel;

    partial void OnSelectedChanged(NavItem value)
    {
        OnPropertyChanged(nameof(CurrentPage));
        // The Mods tab has no "Resolve names" button — resolve them ourselves on open.
        // ModsService will skip already-cached IDs, no extra requests will be made.
        if (value.ViewModel is ModsViewModel mods) _ = mods.AutoResolveNamesAsync();
        // On the Config tab ASA may append to / overwrite the ini in the background (server start, manual edits).
        // Refresh the raw buffers and Basic fields from ini on open — no need to press Reload.
        if (value.ViewModel is ConfigViewModel config) config.RefreshFromDisk();
    }

    public MainWindowViewModel(
        InstallViewModel install,
        ConfigViewModel config,
        ModsViewModel mods,
        BackupsViewModel backups,
        ServerViewModel server,
        RconViewModel rcon)
    {
        _install = install;
        _allItems = new[]
        {
            new NavItem("Server",   G("M7 5 L19 12 L7 19 Z"), server),
            new NavItem("RCON",     G("M3 5 H21 V19 H3 Z M6 9 L10 12 L6 15 V13 L8 12 L6 11 Z M12 14 H17 V16 H12 Z"), rcon),
            new NavItem("Install",  G("M11 4 H13 V11 H16 L12 16 L8 11 H11 Z M5 18 H19 V20 H5 Z"), install),
            new NavItem("Config",   G("M3 6 H21 V8 H3 Z M3 11 H21 V13 H3 Z M3 16 H15 V18 H3 Z"), config),
            new NavItem("Mods",     G("M12 3 L20 7 V17 L12 21 L4 17 V7 Z M12 8 L16 10 V14 L12 16 L8 14 V10 Z"), mods),
            new NavItem("Backups",  G("M4 4 H20 V8 H4 Z M5 9 H19 V20 H5 Z M9 12 H15 V14 H9 Z"), backups),
        };

        // While the server isn't installed — the nav shows only Install (everything else is
        // pointless and lures the user into saving/launching into nothing). As soon as InstallViewModel
        // flips IsServerInstalled=true (after a steamcmd install or on loading an existing
        // directory) — the nav expands to the full set, preserving the current tab where possible.
        _install.PropertyChanged += OnInstallPropertyChanged;
        RecomputeNav();

        // Deep-link to a start tab via env (for tests/screenshots; off by default).
        var startTab = Environment.GetEnvironmentVariable("ARKMANAGER_START_TAB");
        if (!string.IsNullOrWhiteSpace(startTab))
        {
            var match = NavItems.FirstOrDefault(n => string.Equals(n.Title, startTab, StringComparison.OrdinalIgnoreCase));
            if (match != null) Selected = match;
        }
    }

    private void OnInstallPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InstallViewModel.IsServerInstalled))
            App.UiThread(RecomputeNav);
    }

    private void RecomputeNav()
    {
        // In-place collection mutation: Clear() would emit CollectionChanged(Reset),
        // the ListBox would lose its visual selection, and CommunityToolkit doesn't fire
        // PropertyChanged on a reference-equal Selected reassignment → selection would not return.
        var target = _allItems
            .Where(i => _install.IsServerInstalled || i.Title == "Install")
            .ToList();

        // Drop entries that shouldn't be there one by one (Remove → CollectionChanged(Remove)).
        for (var i = NavItems.Count - 1; i >= 0; i--)
            if (!target.Contains(NavItems[i])) NavItems.RemoveAt(i);

        // Insert missing entries at the correct position.
        for (var i = 0; i < target.Count; i++)
        {
            if (i >= NavItems.Count) NavItems.Add(target[i]);
            else if (!ReferenceEquals(NavItems[i], target[i])) NavItems.Insert(i, target[i]);
        }

        // If the previous Selected has disappeared from the nav — fall back to the first visible one.
        if (Selected == null || !NavItems.Contains(Selected))
            Selected = NavItems[0];
    }

    // Parameterless constructor exists solely for the XAML designer.
    public MainWindowViewModel() : this(
        new InstallViewModel(),
        new ConfigViewModel(),
        new ModsViewModel(),
        new BackupsViewModel(),
        new ServerViewModel(),
        new RconViewModel())
    {
    }
}
