using System.Text.Json;
using System.Text.Json.Nodes;
using ArkManager.Core.Models;
using ArkManager.Core.Services.Config;

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
            var rawJson = File.ReadAllText(_paths.SettingsFile);
            var rawNode = JsonNode.Parse(rawJson);
            var loaded = JsonSerializer.Deserialize<AppSettings>(rawJson, _json) ?? Defaults();

            // v1 files don't have a schemaVersion key; typed default (2) would otherwise mask it.
            var isLegacy = rawNode is JsonObject obj && obj["schemaVersion"] is null;
            if (isLegacy)
                MigrateV1ToV2(loaded, rawNode);

            loaded.SchemaVersion = 2;
            Current = loaded;
            Save();
        }
        catch
        {
            // Corrupted settings — don't crash the app, rewrite defaults.
            var bak = _paths.SettingsFile + ".broken-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            try { File.Copy(_paths.SettingsFile, bak, overwrite: true); } catch { /* ignore */ }
            Current = Defaults();
            Save();
        }
    }

    private void MigrateV1ToV2(AppSettings loaded, JsonNode? rawNode)
    {
        var lo = rawNode?["launchOptions"] as JsonObject;
        if (lo is null) return;

        string? Str(string key) => lo[key]?.GetValue<string>();
        int? Int(string key) => lo[key]?.GetValue<int>();
        bool? Bool(string key) => lo[key]?.GetValue<bool>();

        var configDir = Path.Combine(
            loaded.ServerInstallPath ?? "",
            "ShooterGame", "Saved", "Config", "WindowsServer");
        var iniPath = Path.Combine(configDir, "GameUserSettings.ini");

        if (File.Exists(iniPath))
            return;     // ini is the truth; legacy values are discarded silently.

        Directory.CreateDirectory(configDir);

        var ini = new IniFile();
        var server = ini.GetOrCreateSection("ServerSettings");
        server.SetSingle("ServerPassword", Str("serverPassword") ?? "");
        server.SetSingle("ServerAdminPassword", Str("adminPassword") ?? "");
        server.SetSingle("SpectatorPassword", Str("spectatorPassword") ?? "");
        server.SetSingle("RCONEnabled", (Bool("rconEnabled") ?? true) ? "True" : "False");
        server.SetSingle("RCONPort", (Int("rconPort") ?? 27020).ToString());

        var session = ini.GetOrCreateSection("SessionSettings");
        session.SetSingle("SessionName", Str("sessionName") ?? "My ASA Server");
        session.SetSingle("Port", (Int("port") ?? 7777).ToString());
        session.SetSingle("QueryPort", (Int("queryPort") ?? 27015).ToString());

        ini.Save(iniPath);
    }

    private AppSettings Defaults()
    {
        var s = new AppSettings
        {
            ServerInstallPath = _paths.DefaultServerInstallDir,
            BackupsDirectory = _paths.DefaultBackupsDir,
            SchemaVersion = 2,
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
