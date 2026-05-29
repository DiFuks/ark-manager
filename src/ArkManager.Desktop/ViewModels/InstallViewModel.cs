using ArkManager.Core.Services;
using ArkManager.Core.Services.Config;
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
    private readonly ConfigService? _config;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallOrUpdateServerCommand))]
    private string _serverInstallPath = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallSteamCmdCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallOrUpdateServerCommand))]
    private bool _busy;

    [ObservableProperty] private bool _checking;
    [ObservableProperty] private string _log = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SteamCmdStatusText))]
    [NotifyPropertyChangedFor(nameof(SteamCmdStatusBrush))]
    [NotifyPropertyChangedFor(nameof(SteamCmdActionLabel))]
    [NotifyCanExecuteChangedFor(nameof(InstallOrUpdateServerCommand))]
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ServerStatusText))]
    [NotifyPropertyChangedFor(nameof(ServerStatusBrush))]
    private string _latestBuild = "—";

    public string SteamCmdStatusText => IsSteamCmdInstalled ? "Installed" : "Not installed";
    public IBrush SteamCmdStatusBrush => Tok(IsSteamCmdInstalled ? "OkBrush" : "DangerBrush");
    public string SteamCmdActionLabel => IsSteamCmdInstalled ? "Reinstall SteamCMD" : "Install SteamCMD";

    public string ServerPrimaryActionLabel => IsServerInstalled ? "Update server" : "Install server";

    // Server status text. Version comparison and display use the Steam buildid
    // (a number like 23321173). A "human" game version (v40.55) is unavailable
    // on the latest side, so we use a single format on both sides.
    public string ServerStatusText
    {
        get
        {
            if (!IsServerInstalled) return "Not installed";
            if (LatestBuild is "—") return $"Installed · build {InstalledBuild}";
            if (LatestBuild is "failed to parse") return $"Installed · build {InstalledBuild} (update check failed)";
            return string.Equals(InstalledBuild, LatestBuild, StringComparison.Ordinal)
                ? $"Up to date · build {InstalledBuild}"
                : $"Update available · build {InstalledBuild} → build {LatestBuild}";
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

    public InstallViewModel(SettingsService settings, SteamCmdService steam, ConfigService config)
    {
        _settings = settings;
        _steam = steam;
        _config = config;
        ServerInstallPath = settings.Current.ServerInstallPath ?? "";
        UpdateSteamState();
        RefreshInstalledVersion();

        // Auto-check for the latest version at startup — in the background. Kicked off on the UI thread,
        // the heavy steamcmd I/O is moved off-thread inside QueryLatestBuildIdAsync,
        // the continuation returns to the UI via the captured SyncContext.
        if (IsServerInstalled) _ = CheckForUpdatesAsync();
    }

    private bool CanInstallSteamCmd() => !Busy;

    [RelayCommand(CanExecute = nameof(CanInstallSteamCmd))]
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

    // Install/Update of the server requires:
    //  - not being busy already;
    //  - steamcmd present (otherwise ProcessRunner will throw);
    //  - a non-empty install path (Directory.CreateDirectory("") crashes).
    private bool CanInstallOrUpdateServer()
        => !Busy && IsSteamCmdInstalled && !string.IsNullOrWhiteSpace(ServerInstallPath);

    [RelayCommand(CanExecute = nameof(CanInstallOrUpdateServer))]
    public async Task InstallOrUpdateServerAsync()
    {
        if (_steam == null || _settings == null) return;
        Busy = true;
        try
        {
            _settings.Update(s => s.ServerInstallPath = ServerInstallPath);

            // Update scenario: if we already have a fresh check result (from auto-start or
            // the manual button) — trust it, don't run steamcmd again. Re-check only
            // when LatestBuild has not been confirmed yet (null/failed).
            if (IsServerInstalled)
            {
                if (LatestBuild is "—" or "failed to parse")
                {
                    Append("Checking for updates...");
                    var latest = await _steam.QueryLatestBuildIdAsync(Append);
                    LatestBuild = latest ?? "failed to parse";
                }

                if (LatestBuild is not ("—" or "failed to parse")
                    && string.Equals(LatestBuild, InstalledBuild, StringComparison.Ordinal))
                {
                    Append($"[ok] Already on the latest build ({InstalledBuild}).");
                    return;
                }
            }

            await _steam.InstallOrUpdateServerAsync(ServerInstallPath, Append);
            Append("[done]");
            RefreshInstalledVersion();
            _config?.EnsureIni();
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
        // The source of truth for ServerInstallPath moved to this tab (the field in Settings was dropped).
        // Save on every change so it isn't lost when switching tabs.
        _settings?.Update(s => s.ServerInstallPath = string.IsNullOrWhiteSpace(value) ? null : value);
        RefreshInstalledVersion();
        // After a path change the latest-check against the previous path is no longer relevant.
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
            return;
        }
        IsServerInstalled = true;
        InstalledBuild = v.BuildId;
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
        // Prevent the string from growing forever — trim the "head" at a line boundary.
        if (Log.Length > MaxLogChars)
        {
            var cut = Log.IndexOf('\n', Log.Length - MaxLogChars);
            Log = cut > 0 ? Log[(cut + 1)..] : Log[^MaxLogChars..];
        }
    });
}
