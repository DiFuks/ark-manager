using System.Collections.ObjectModel;
using ArkManager.Core.Services;
using ArkManager.Core.Services.Rcon;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArkManager.App.ViewModels;

public partial class RconViewModel : ViewModelBase
{
    private readonly SettingsService? _settings;
    private RconClient? _client;

    public ObservableCollection<string> Lines { get; } = new();
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
            Lines.Add($"[connected to {Host}:{Port}]");
        }
        catch (Exception ex)
        {
            Status = "Ошибка: " + ex.Message;
            Lines.Add("[connect failed] " + ex.Message);
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
        Lines.Add("[disconnected]");
    }

    [RelayCommand]
    public async Task SendAsync()
    {
        if (_client == null || !Connected || string.IsNullOrWhiteSpace(Command)) return;
        var cmd = Command;
        Lines.Add("> " + cmd);
        try
        {
            var resp = await _client.SendAsync(cmd);
            if (!string.IsNullOrEmpty(resp))
                foreach (var ln in resp.Replace("\r", "").Split('\n')) Lines.Add(ln);
        }
        catch (Exception ex)
        {
            Lines.Add("[error] " + ex.Message);
        }
        Command = "";
    }

    [RelayCommand] public void Clear() => Lines.Clear();
    [RelayCommand] public void Saveworld() { Command = "saveworld"; _ = SendAsync(); }
    [RelayCommand] public void DoExit()   { Command = "DoExit";    _ = SendAsync(); }
    [RelayCommand] public void Broadcast(string? msg) { Command = "Broadcast " + (msg ?? "Hello"); _ = SendAsync(); }
}
