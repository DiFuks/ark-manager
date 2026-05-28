using System.Collections.ObjectModel;
using ArkManager.Core.Services.Mods;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArkManager.App.ViewModels;

public partial class ModsViewModel : ViewModelBase
{
    private readonly ModsService? _mods;

    public ObservableCollection<ModEntry> Mods { get; } = new();
    [ObservableProperty] private string _newModId = "";

    // Команды ниже работают только с выделенным модом → дизейблятся, когда выделения нет
    // (иначе кнопки выглядят активными, но молча ничего не делают).
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveDownCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenInCurseForgeCommand))]
    private ModEntry? _selected;

    [ObservableProperty] private string _status = "";

    public bool HasSelection => Selected != null;

    public ModsViewModel() { }

    public ModsViewModel(ModsService mods)
    {
        _mods = mods;
        Reload();
    }

    [RelayCommand]
    public void Reload()
    {
        if (_mods == null) return;
        Mods.Clear();
        foreach (var m in _mods.List()) Mods.Add(m);
        Status = $"{Mods.Count} mod(s)";
    }

    [RelayCommand]
    public async Task ResolveNamesAsync()
    {
        if (_mods == null) return;
        Status = "Resolving names via CurseForge...";
        try
        {
            await _mods.ResolveNamesAsync(entry => App.UiThread(() =>
            {
                var idx = Mods.ToList().FindIndex(m => m.Id == entry.Id);
                if (idx >= 0) Mods[idx] = entry;
            }));
            Status = "Names updated.";
        }
        catch (Exception ex) { Status = "Error: " + ex.Message; }
    }

    [RelayCommand]
    public void Add()
    {
        if (_mods == null) return;
        try
        {
            // Принимаем и через запятую, и одиночные.
            var parts = NewModId.Split(new[] { ',', ' ', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            _mods.AddMany(parts);
            NewModId = "";
            Reload();
        }
        catch (Exception ex) { Status = ex.Message; }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    public void Remove()
    {
        if (_mods == null || Selected == null) return;
        _mods.Remove(Selected.Id);
        Reload();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    public void MoveUp()
    {
        if (_mods == null || Selected == null) return;
        var idx = Mods.IndexOf(Selected);
        if (idx <= 0) return;
        var ordered = Mods.Select(m => m.Id).ToList();
        (ordered[idx - 1], ordered[idx]) = (ordered[idx], ordered[idx - 1]);
        _mods.Reorder(ordered);
        Reload();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    public void MoveDown()
    {
        if (_mods == null || Selected == null) return;
        var idx = Mods.IndexOf(Selected);
        if (idx < 0 || idx >= Mods.Count - 1) return;
        var ordered = Mods.Select(m => m.Id).ToList();
        (ordered[idx], ordered[idx + 1]) = (ordered[idx + 1], ordered[idx]);
        _mods.Reorder(ordered);
        Reload();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    public void OpenInCurseForge()
    {
        if (Selected == null) return;
        // Если уже резолвили — открываем конкретную страницу мода (точный URL из API).
        // Если нет — фолбек на поиск по ID, чтобы юзер хотя бы попал в каталог.
        var url = !string.IsNullOrWhiteSpace(Selected.Url)
            ? Selected.Url
            : $"https://www.curseforge.com/ark-survival-ascended/search?q={Selected.Id}";
        App.OpenInBrowser(url);
    }
}
