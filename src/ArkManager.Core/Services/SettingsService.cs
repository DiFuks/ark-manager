using System.Text.Json;
using ArkManager.Core.Models;

namespace ArkManager.Core.Services;

public sealed class SettingsService
{
    private readonly AppPaths _paths;
    private readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public AppSettings Current { get; private set; } = new();

    public event Action<AppSettings>? Changed;

    public SettingsService(AppPaths paths)
    {
        _paths = paths;
        Load();
    }

    public void Load()
    {
        if (!File.Exists(_paths.SettingsFile))
        {
            Current = Defaults();
            Save();
            return;
        }

        try
        {
            using var fs = File.OpenRead(_paths.SettingsFile);
            var loaded = JsonSerializer.Deserialize<AppSettings>(fs, _json);
            Current = loaded ?? Defaults();
        }
        catch
        {
            // Корраптнутый settings — не валим приложение, пересохраняем дефолт.
            var bak = _paths.SettingsFile + ".broken-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            try { File.Copy(_paths.SettingsFile, bak, overwrite: true); } catch { /* ignore */ }
            Current = Defaults();
            Save();
        }
    }

    private AppSettings Defaults()
    {
        var s = new AppSettings
        {
            ServerInstallPath = _paths.DefaultServerInstallDir,
            BackupsDirectory = _paths.DefaultBackupsDir,
            WinePrefixPath = _paths.DefaultWinePrefixDir,
        };
        s.Profiles.Add(new ServerProfile { Name = "Default", Options = s.LaunchOptions });
        return s;
    }

    public void Save()
    {
        var tmp = _paths.SettingsFile + ".tmp";
        using (var fs = File.Create(tmp))
        {
            JsonSerializer.Serialize(fs, Current, _json);
        }
        File.Move(tmp, _paths.SettingsFile, overwrite: true);
        Changed?.Invoke(Current);
    }

    public void Update(Action<AppSettings> mutate)
    {
        mutate(Current);
        Save();
    }
}
