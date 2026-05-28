using ArkManager.Core.Services;
using ArkManager.Core.Services.Steam;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArkManager.App.ViewModels;

public partial class InstallViewModel : ViewModelBase
{
    private const int MaxLogChars = 200_000;
    private readonly SettingsService? _settings;
    private readonly SteamCmdService? _steam;

    [ObservableProperty] private string _serverInstallPath = "";
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private bool _checking;
    [ObservableProperty] private string _log = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SteamCmdStatusText))]
    [NotifyPropertyChangedFor(nameof(SteamCmdStatusBrush))]
    [NotifyPropertyChangedFor(nameof(SteamCmdActionLabel))]
    private bool _isSteamCmdInstalled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ServerStatusText))]
    [NotifyPropertyChangedFor(nameof(ServerStatusBrush))]
    [NotifyPropertyChangedFor(nameof(ServerPrimaryActionLabel))]
    [NotifyCanExecuteChangedFor(nameof(CheckForUpdatesCommand))]
    private bool _isServerInstalled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ServerStatusText))]
    [NotifyPropertyChangedFor(nameof(ServerStatusBrush))]
    private string _installedBuild = "—";

    [ObservableProperty] private string _installedAt = "—";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ServerStatusText))]
    [NotifyPropertyChangedFor(nameof(ServerStatusBrush))]
    private string _latestBuild = "—";

    public string SteamCmdStatusText => IsSteamCmdInstalled ? "Installed" : "Not installed";
    public IBrush SteamCmdStatusBrush => Tok(IsSteamCmdInstalled ? "OkBrush" : "DangerBrush");
    public string SteamCmdActionLabel => IsSteamCmdInstalled ? "Reinstall SteamCMD" : "Install SteamCMD";

    public string ServerPrimaryActionLabel => IsServerInstalled ? "Update server" : "Install server";

    // Текст статуса сервера: «Not installed» → «Installed · build X» (не чекали) →
    // «Up to date · build X» / «Update available · X → Y».
    public string ServerStatusText
    {
        get
        {
            if (!IsServerInstalled) return "Not installed";
            if (LatestBuild is "—") return $"Installed · build {InstalledBuild}";
            if (LatestBuild is "failed to parse") return $"Installed · build {InstalledBuild} (update check failed)";
            return string.Equals(InstalledBuild, LatestBuild, StringComparison.Ordinal)
                ? $"Up to date · build {InstalledBuild}"
                : $"Update available · {InstalledBuild} → {LatestBuild}";
        }
    }

    public IBrush ServerStatusBrush
    {
        get
        {
            if (!IsServerInstalled) return Tok("DangerBrush");
            if (LatestBuild is "—" or "failed to parse") return Tok("MutedBrush");
            return string.Equals(InstalledBuild, LatestBuild, StringComparison.Ordinal)
                ? Tok("OkBrush")
                : Tok("WarnBrush");
        }
    }

    private static IBrush Tok(string key)
    {
        if (Avalonia.Application.Current?.Resources is { } res
            && res.TryGetResource(key, null, out var v) && v is IBrush b)
            return b;
        return Brushes.Gray;
    }

    public InstallViewModel() { }

    public InstallViewModel(SettingsService settings, SteamCmdService steam)
    {
        _settings = settings;
        _steam = steam;
        ServerInstallPath = settings.Current.ServerInstallPath ?? "";
        UpdateSteamState();
        RefreshInstalledVersion();

        // Авточек последней версии при старте — фоном. Запускается на UI-потоке,
        // тяжёлый I/O steamcmd уходит off-thread внутри QueryLatestBuildIdAsync,
        // continuation возвращается на UI через захваченный SyncContext.
        if (IsServerInstalled) _ = CheckForUpdatesAsync();
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
        catch (Exception ex) { Append("[error] " + ex.Message); }
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

            // Update-сценарий: сначала чекаем, нужно ли вообще обновляться,
            // чтобы не гонять steamcmd 5+ минут впустую.
            if (IsServerInstalled)
            {
                Append("Checking for updates...");
                var latest = await _steam.QueryLatestBuildIdAsync(Append);
                LatestBuild = latest ?? "failed to parse";
                if (latest != null && string.Equals(latest, InstalledBuild, StringComparison.Ordinal))
                {
                    Append($"[ok] Already on the latest build ({InstalledBuild}).");
                    return;
                }
            }

            await _steam.InstallOrUpdateServerAsync(ServerInstallPath, Append);
            Append("[done]");
            RefreshInstalledVersion();
        }
        catch (Exception ex) { Append("[error] " + ex.Message); }
        finally { Busy = false; }
    }

    [RelayCommand(CanExecute = nameof(IsServerInstalled))]
    public async Task CheckForUpdatesAsync()
    {
        if (_steam == null || !IsServerInstalled) return;
        Checking = true;
        try
        {
            RefreshInstalledVersion();
            var latest = await _steam.QueryLatestBuildIdAsync(Append);
            LatestBuild = latest ?? "failed to parse";
        }
        catch (Exception ex)
        {
            Append("[error] " + ex.Message);
            LatestBuild = "failed to parse";
        }
        finally { Checking = false; }
    }

    partial void OnServerInstallPathChanged(string value)
    {
        // Источник истины для ServerInstallPath переехал на этот таб (поле в Settings убрали).
        // Сохраняем сразу на изменение, чтобы не терять при переключении вкладок.
        _settings?.Update(s => s.ServerInstallPath = string.IsNullOrWhiteSpace(value) ? null : value);
        RefreshInstalledVersion();
        // После смены пути latest-чек по предыдущему пути уже не релевантен.
        LatestBuild = "—";
    }

    private void RefreshInstalledVersion()
    {
        if (_steam == null) return;
        var v = _steam.ReadInstalledVersion(ServerInstallPath);
        if (v == null)
        {
            IsServerInstalled = false;
            InstalledBuild = "—";
            InstalledAt = "—";
            return;
        }
        IsServerInstalled = true;
        InstalledBuild = v.BuildId;
        InstalledAt = v.LastUpdated?.LocalDateTime.ToString("yyyy-MM-dd HH:mm") ?? "—";
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
        var picked = await Services.Browse.PickFolderAsync("Select ASA server folder", ServerInstallPath);
        if (!string.IsNullOrEmpty(picked)) ServerInstallPath = picked;
    }

    private void UpdateSteamState()
    {
        if (_steam == null) return;
        IsSteamCmdInstalled = _steam.IsSteamCmdInstalled();
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
