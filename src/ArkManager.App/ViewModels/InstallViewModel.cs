using ArkManager.Core.Services;
using ArkManager.Core.Services.Steam;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArkManager.App.ViewModels;

public partial class InstallViewModel : ViewModelBase
{
    private const int MaxLogChars = 200_000;
    private readonly SettingsService? _settings;
    private readonly SteamCmdService? _steam;

    [ObservableProperty] private string _serverInstallPath = "";
    [ObservableProperty] private string _steamCmdState = "";
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private string _log = "";

    [ObservableProperty] private string _installedBuild = "—";
    [ObservableProperty] private string _installedAt = "—";
    [ObservableProperty] private string _latestBuild = "—";
    [ObservableProperty] private string _updateStatus = "не проверялось";

    public InstallViewModel() { }

    public InstallViewModel(SettingsService settings, SteamCmdService steam)
    {
        _settings = settings;
        _steam = steam;
        ServerInstallPath = settings.Current.ServerInstallPath ?? "";
        UpdateSteamState();
        RefreshInstalledVersion();
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
            RefreshInstalledVersion();
            RecomputeUpdateStatus();
        }
        catch (Exception ex) { Append("[ошибка] " + ex.Message); }
        finally { Busy = false; }
    }

    [RelayCommand]
    public async Task CheckForUpdatesAsync()
    {
        if (_steam == null) return;
        Busy = true;
        UpdateStatus = "проверяю...";
        try
        {
            RefreshInstalledVersion();
            var latest = await _steam.QueryLatestBuildIdAsync(Append);
            LatestBuild = latest ?? "не удалось распарсить";
            RecomputeUpdateStatus();
        }
        catch (Exception ex)
        {
            Append("[ошибка] " + ex.Message);
            UpdateStatus = "ошибка проверки";
        }
        finally { Busy = false; }
    }

    partial void OnServerInstallPathChanged(string value)
    {
        RefreshInstalledVersion();
        RecomputeUpdateStatus();
    }

    private void RefreshInstalledVersion()
    {
        if (_steam == null) return;
        var v = _steam.ReadInstalledVersion(ServerInstallPath);
        if (v == null)
        {
            InstalledBuild = "—";
            InstalledAt = "—";
            return;
        }
        InstalledBuild = v.BuildId;
        InstalledAt = v.LastUpdated?.LocalDateTime.ToString("yyyy-MM-dd HH:mm") ?? "—";
    }

    private void RecomputeUpdateStatus()
    {
        if (InstalledBuild is "—" || LatestBuild is "—" or "не удалось распарсить")
        {
            if (LatestBuild is "не удалось распарсить") UpdateStatus = "не удалось распарсить ответ steamcmd";
            else if (InstalledBuild is "—") UpdateStatus = "сервер не установлен";
            else UpdateStatus = "нажмите Check для проверки";
            return;
        }
        UpdateStatus = string.Equals(InstalledBuild, LatestBuild, StringComparison.Ordinal)
            ? "✅ актуальная версия"
            : $"⚠️ доступно обновление (latest {LatestBuild})";
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

    [RelayCommand]
    public async Task CopyLog() => await Services.Browse.CopyToClipboardAsync(Log);

    [RelayCommand]
    public void ClearLog() => Log = "";

    private void Append(string line) => App.UiThread(() =>
    {
        Log += line + Environment.NewLine;
        // Не даём строке расти бесконечно — режем «голову» по границе строки.
        if (Log.Length > MaxLogChars)
        {
            var cut = Log.IndexOf('\n', Log.Length - MaxLogChars);
            Log = cut > 0 ? Log[(cut + 1)..] : Log[^MaxLogChars..];
        }
    });
}
