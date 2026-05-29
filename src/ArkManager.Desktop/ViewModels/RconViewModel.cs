using ArkManager.Core.Services;
using ArkManager.Core.Services.Rcon;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArkManager.App.ViewModels;

public partial class RconViewModel : ViewModelBase
{
    private const int MaxLogChars = 200_000;

    // The local wine server always listens on 127.0.0.1; the manager has no remote scenarios.
    private const string LocalHost = "127.0.0.1";

    private readonly SettingsService? _settings;
    private readonly ServerManager? _server;
    private RconClient? _client;

    [ObservableProperty] private string _lines = "";
    [ObservableProperty] private string _command = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReconnectCommand))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(StatusDotBrush))]
    private bool _connected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(StatusDotBrush))]
    private string _statusDetail = "";

    public string Endpoint =>
        $"{LocalHost}:{_settings?.Current.LaunchOptions.RconPort ?? 27020}";

    public string StatusText => Connected
        ? $"Connected → {Endpoint}"
        : string.IsNullOrEmpty(StatusDetail) ? $"Disconnected · {Endpoint}" : StatusDetail;

    public Avalonia.Media.IBrush StatusDotBrush => Tok(Connected ? "OkBrush" : "MutedBrush");

    private static Avalonia.Media.IBrush Tok(string key)
    {
        if (Avalonia.Application.Current?.Resources is { } res
            && res.TryGetResource(key, null, out var v) && v is Avalonia.Media.IBrush b)
            return b;
        return Avalonia.Media.Brushes.Gray;
    }

    public bool CanSend => Connected;

    public RconViewModel() { }

    public RconViewModel(SettingsService settings, ServerManager server)
    {
        _settings = settings;
        _server = server;

        // Auto-connect once the world has finished loading (Ready), auto-disconnect when leaving Running.
        // Ready ≠ just Running: the server may be "alive" yet still loading the world — at that point
        // the RCON port is not open. Hook onto Ready so we don't spawn failed connect attempts.
        _server.ReadyChanged += _ => App.UiThread(SyncWithServer);
        _server.StateChanged += _ => App.UiThread(SyncWithServer);
        _settings.Changed += _ => App.UiThread(() =>
        {
            OnPropertyChanged(nameof(Endpoint));
            OnPropertyChanged(nameof(StatusText));
        });

        SyncWithServer();
    }

    private void SyncWithServer()
    {
        if (_server == null) return;
        var shouldBeConnected = _server.State == ServerState.Running && _server.IsReady;
        if (shouldBeConnected && !Connected)
        {
            _ = ConnectAsync();
        }
        else if (!shouldBeConnected)
        {
            // Clear any stale "Error: …" left over from a failed connect attempt — otherwise the
            // status badge stays red after Stop even though we're no longer trying to connect.
            // DisconnectAsync clears StatusDetail too, but it only runs if we WERE connected;
            // a failed-from-the-start session needs an explicit reset.
            if (Connected) _ = DisconnectAsync();
            else if (!string.IsNullOrEmpty(StatusDetail)) StatusDetail = "";
        }
    }

    [RelayCommand(CanExecute = nameof(CanReconnect))]
    public async Task ReconnectAsync()
    {
        if (Connected) await DisconnectAsync();
        await ConnectAsync();
    }

    // Reconnect only makes sense once the server is accepting connections.
    public bool CanReconnect => _server is { State: ServerState.Running, IsReady: true };

    private async Task ConnectAsync()
    {
        if (Connected || _settings == null) return;
        var opts = _settings.Current.LaunchOptions;

        // Pre-flight: ASA never opens the RCON port without an admin password, so a TCP attempt
        // would just produce a system-localised "connection refused" — useless to the user.
        // Stop here with an actionable hint instead.
        if (RconErrors.DescribePrecondition(opts) is string precond)
        {
            StatusDetail = "Error: " + precond;
            Append("[connect skipped] " + precond);
            return;
        }

        var port = opts.RconPort;
        var pass = opts.AdminPassword!;
        _client = new RconClient();
        try
        {
            await _client.ConnectAsync(LocalHost, port, pass);
            Connected = true;
            StatusDetail = "";
            Append($"[connected to {LocalHost}:{port}]");
        }
        catch (Exception ex)
        {
            var msg = RconErrors.DescribeConnectException(ex);
            StatusDetail = "Error: " + msg;
            Append("[connect failed] " + msg);
            await _client.DisposeAsync(); _client = null;
            Connected = false;
        }
    }

    private async Task DisconnectAsync()
    {
        if (_client != null)
        {
            await _client.DisposeAsync();
            _client = null;
        }
        Connected = false;
        StatusDetail = "";
        Append("[disconnected]");
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    public async Task SendAsync()
    {
        if (_client == null || !Connected || string.IsNullOrWhiteSpace(Command)) return;
        var cmd = Command;
        Append("> " + cmd);
        try
        {
            var resp = await _client.SendAsync(cmd);
            if (!string.IsNullOrEmpty(resp))
                foreach (var ln in resp.Replace("\r", "").Split('\n')) Append(ln);
        }
        catch (Exception ex)
        {
            Append("[error] " + RconErrors.DescribeConnectException(ex));
        }
        Command = "";
    }

    [RelayCommand] public void Clear() => Lines = "";
    [RelayCommand] public async Task CopyLog() => await Services.Browse.CopyToClipboardAsync(Lines);

    private void Append(string line)
    {
        Lines += line + Environment.NewLine;
        if (Lines.Length > MaxLogChars)
        {
            var cut = Lines.IndexOf('\n', Lines.Length - MaxLogChars);
            Lines = cut > 0 ? Lines[(cut + 1)..] : Lines[^MaxLogChars..];
        }
    }
}
