using System.Collections.ObjectModel;
using ArkManager.Core.Services.Doctor;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ArkManager.App.ViewModels;

public partial class DoctorViewModel : ViewModelBase
{
    private readonly DoctorService? _service;

    public ObservableCollection<CheckResult> Results { get; } = new();
    public ObservableCollection<string> InstallLog { get; } = new();
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private string _summary = "";

    public DoctorViewModel() { }

    public DoctorViewModel(DoctorService service)
    {
        _service = service;
        _ = RunAsync();
    }

    [RelayCommand]
    public async Task RunAsync()
    {
        if (_service == null) return;
        Busy = true;
        Results.Clear();
        try
        {
            var r = await _service.RunAsync();
            foreach (var c in r) Results.Add(c);
            var ok = r.Count(c => c.Ok);
            Summary = $"{ok}/{r.Count} OK";
        }
        finally { Busy = false; }
    }

    [RelayCommand]
    public async Task InstallWhiskyAsync()
    {
        if (_service == null) return;
        Busy = true;
        try
        {
            InstallLog.Clear();
            var ok = await _service.InstallWhiskyViaBrewAsync(line => App.UiThread(() => InstallLog.Add(line)));
            App.UiThread(() => InstallLog.Add(ok ? "[ok]" : "[fail]"));
            await RunAsync();
        }
        finally { Busy = false; }
    }
}
