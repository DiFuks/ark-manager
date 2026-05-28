using ArkManager.Core.Services;
using ArkManager.Core.Services.Rcon;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArkManager.App.ViewModels;

public partial class RconViewModel : ViewModelBase
{
    private const int MaxLogChars = 200_000;
    private readonly SettingsService? _settings;
    private RconClient? _client;

    [ObservableProperty] private string _lines = "";
    [ObservableProperty] private string _host = "127.0.0.1";
    [ObservableProperty] private int _port = 27020;
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string _command = "";
    [ObservableProperty] private bool _connected;
    [ObservableProperty] private string _status = "Disconnected";

    public RconViewModel() { }

    public RconViewModel(SettingsService settings)
    {
        _settings = settings;
        Port = settings.Current.LaunchOptions.RconPort;
        Password = settings.Current.LaunchOptions.AdminPassword ?? "";
    }

    [RelayCommand]
    public async Task ConnectAsync()
    {
        if (Connected) return;
        _client = new RconClient();
        try
        {
            await _client.ConnectAsync(Host, Port, Password);
            Connected = true;
            Status = $"Connected → {Host}:{Port}";
            Append($"[connected to {Host}:{Port}]");
        }
        catch (Exception ex)
        {
            Status = "Error: " + ex.Message;
            Append("[connect failed] " + ex.Message);
            await _client.DisposeAsync(); _client = null;
            Connected = false;
        }
    }

    [RelayCommand]
    public async Task DisconnectAsync()
    {
        if (_client != null)
        {
            await _client.DisposeAsync();
            _client = null;
        }
        Connected = false;
        Status = "Disconnected";
        Append("[disconnected]");
    }

    [RelayCommand]
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
    [RelayCommand] public void Saveworld() { Command = "saveworld"; _ = SendAsync(); }
    [RelayCommand] public void DoExit()   { Command = "DoExit";    _ = SendAsync(); }
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
