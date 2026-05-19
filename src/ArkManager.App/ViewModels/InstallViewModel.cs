using System.Collections.ObjectModel;
using ArkManager.Core.Services;
using ArkManager.Core.Services.Steam;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArkManager.App.ViewModels;

public partial class InstallViewModel : ViewModelBase
{
    private readonly SettingsService? _settings;
    private readonly SteamCmdService? _steam;

    [ObservableProperty] private string _serverInstallPath = "";
    [ObservableProperty] private string _steamCmdState = "";
    [ObservableProperty] private bool _busy;
    public ObservableCollection<string> Log { get; } = new();

    public InstallViewModel() { }

    public InstallViewModel(SettingsService settings, SteamCmdService steam)
    {
        _settings = settings;
        _steam = steam;
        ServerInstallPath = settings.Current.ServerInstallPath ?? "";
        UpdateSteamState();
    }

    [RelayCommand]
    public async Task InstallSteamCmdAsync()
    {
        if (_steam == null) return;
        Busy = true;
        try
        {
            await _steam.InstallSteamCmdAsync(Append);
            UpdateSteamState();
        }
        catch (Exception ex) { Append("[ошибка] " + ex.Message); }
        finally { Busy = false; }
    }

    [RelayCommand]
    public async Task InstallOrUpdateServerAsync()
    {
        if (_steam == null || _settings == null) return;
        Busy = true;
        try
        {
            _settings.Update(s => s.ServerInstallPath = ServerInstallPath);
            await _steam.InstallOrUpdateServerAsync(ServerInstallPath, Append);
            Append("[готово]");
        }
        catch (Exception ex) { Append("[ошибка] " + ex.Message); }
        finally { Busy = false; }
    }

    [RelayCommand]
    public void OpenServerFolder()
    {
        if (string.IsNullOrWhiteSpace(ServerInstallPath)) return;
        Directory.CreateDirectory(ServerInstallPath);
        App.OpenInFinder(ServerInstallPath);
    }

    [RelayCommand]
    public async Task BrowseServerFolderAsync()
    {
        var picked = await Services.Browse.PickFolderAsync("Выбрать папку для ASA сервера", ServerInstallPath);
        if (!string.IsNullOrEmpty(picked)) ServerInstallPath = picked;
    }

    private void UpdateSteamState()
    {
        if (_steam == null) return;
        SteamCmdState = _steam.IsSteamCmdInstalled()
            ? "✅ установлен: " + _steam.ResolveSteamCmdBinary()
            : "❌ не установлен";
    }

    private void Append(string line) => App.UiThread(() =>
    {
        Log.Add(line);
        while (Log.Count > 2000) Log.RemoveAt(0);
    });
}
