using ArkManager.Core.Services;
using ArkManager.Core.Services.Rcon;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArkManager.App.ViewModels;

public partial class RconViewModel : ViewModelBase
{
    private const int MaxLogChars = 200_000;

    // Локальный wine-сервер всегда слушает 127.0.0.1; remote-сценариев у менеджера нет.
    private const string LocalHost = "127.0.0.1";

    private readonly SettingsService? _settings;
    private readonly ServerManager? _server;
    private RconClient? _client;

    [ObservableProperty] private string _lines = "";
    [ObservableProperty] private string _command = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveworldCommand))]
    [NotifyCanExecuteChangedFor(nameof(DoExitCommand))]
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

        // Auto-connect когда мир догрузился (Ready), auto-disconnect при выходе из Running.
        // Ready ≠ просто Running: сервер может быть «жив» но ещё грузить мир — RCON-порт
        // в этот момент ещё не открыт. Привязываемся к Ready, чтобы не плодить failed connect.
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
        if (shouldBeConnected && !Connected) _ = ConnectAsync();
        else if (!shouldBeConnected && Connected) _ = DisconnectAsync();
    }

    [RelayCommand(CanExecute = nameof(CanReconnect))]
    public async Task ReconnectAsync()
    {
        if (Connected) await DisconnectAsync();
        await ConnectAsync();
    }

    // Reconnect имеет смысл только когда сервер уже принимает подключения.
    public bool CanReconnect => _server is { State: ServerState.Running, IsReady: true };

    private async Task ConnectAsync()
    {
        if (Connected || _settings == null) return;
        var port = _settings.Current.LaunchOptions.RconPort;
        var pass = _settings.Current.LaunchOptions.AdminPassword ?? "";
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
            StatusDetail = "Error: " + ex.Message;
            Append("[connect failed] " + ex.Message);
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
            Append("[error] " + ex.Message);
        }
        Command = "";
    }

    [RelayCommand] public void Clear() => Lines = "";
    [RelayCommand] public async Task CopyLog() => await Services.Browse.CopyToClipboardAsync(Lines);

    [RelayCommand(CanExecute = nameof(CanSend))]
    public void Saveworld() { Command = "saveworld"; _ = SendAsync(); }

    [RelayCommand(CanExecute = nameof(CanSend))]
    public void DoExit()   { Command = "DoExit";    _ = SendAsync(); }

    [RelayCommand] public void Broadcast(string? msg) { Command = "Broadcast " + (msg ?? "Hello"); _ = SendAsync(); }

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
