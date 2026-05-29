using ArkManager.Core.Models;
using ArkManager.Core.Services;
using Xunit;

namespace ArkManager.Core.Tests;

public class SettingsMigrationTests
{
    private static string MakeTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "ark-mig-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    [Fact]
    public void Migrate_LegacySchema_NoIni_PortsValuesToIni()
    {
        var root = MakeTempRoot();
        var srv = Path.Combine(root, "srv");
        try
        {
            var paths = new AppPaths(root);
            File.WriteAllText(paths.SettingsFile, $$"""
                {
                  "serverInstallPath": "{{srv.Replace("\\", "\\\\")}}",
                  "launchOptions": {
                    "sessionName": "Legacy Server",
                    "port": 7798,
                    "queryPort": 27040,
                    "rconPort": 27077,
                    "rconEnabled": true,
                    "serverPassword": "old-srv",
                    "adminPassword": "old-adm",
                    "spectatorPassword": "old-spec",
                    "map": "TheIsland_WP",
                    "maxPlayers": 25
                  }
                }
                """);

            // Construct service — Load triggers migration.
            var svc = new SettingsService(paths);

            // settings.json is now v2.
            Assert.Equal(2, svc.Current.SchemaVersion);
            Assert.Equal("TheIsland_WP", svc.Current.LaunchOptions.Map);
            Assert.Equal(25, svc.Current.LaunchOptions.MaxPlayers);

            // The ini was created with the legacy values.
            var iniPath = Path.Combine(srv, "ShooterGame", "Saved", "Config", "WindowsServer", "GameUserSettings.ini");
            Assert.True(File.Exists(iniPath));
            var ini = File.ReadAllText(iniPath);
            Assert.Contains("SessionName=Legacy Server", ini);
            Assert.Contains("RCONPort=27077", ini);
            Assert.Contains("ServerAdminPassword=old-adm", ini);

            // Persisted JSON no longer has the 8 fields under launchOptions (we don't deserialize
            // them into the typed model so they're absent on re-serialize).
            var persisted = File.ReadAllText(paths.SettingsFile);
            // After T20 the field also gets removed from ServerLaunchOptions; at THIS task,
            // the field still exists, so the serializer DOES persist sessionName again.
            // We still check that SchemaVersion is bumped to 2 — that's the migration marker.
            Assert.Contains("\"schemaVersion\": 2", persisted);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Migrate_LegacySchema_WithExistingIni_DropsLegacyKeepsIni()
    {
        var root = MakeTempRoot();
        var srv = Path.Combine(root, "srv");
        var cfgDir = Path.Combine(srv, "ShooterGame", "Saved", "Config", "WindowsServer");
        Directory.CreateDirectory(cfgDir);
        var iniPath = Path.Combine(cfgDir, "GameUserSettings.ini");
        File.WriteAllText(iniPath, "[ServerSettings]\nRCONPort=99999\n");
        try
        {
            var paths = new AppPaths(root);
            File.WriteAllText(paths.SettingsFile, $$"""
                {
                  "serverInstallPath": "{{srv.Replace("\\", "\\\\")}}",
                  "launchOptions": {
                    "sessionName": "Stale",
                    "rconPort": 11111
                  }
                }
                """);

            new SettingsService(paths);

            // Ini untouched.
            Assert.Contains("RCONPort=99999", File.ReadAllText(iniPath));
            Assert.DoesNotContain("Stale", File.ReadAllText(iniPath));
        }
        finally { try { Directory.Delete(root, recursive: true); } catch { } }
    }

    [Fact]
    public void Migrate_AlreadyV2_NoOp()
    {
        var root = MakeTempRoot();
        try
        {
            var paths = new AppPaths(root);
            File.WriteAllText(paths.SettingsFile, """
                {
                  "schemaVersion": 2,
                  "serverInstallPath": "/tmp/v2srv",
                  "launchOptions": { "map": "TheIsland_WP", "maxPlayers": 70 }
                }
                """);

            var svc = new SettingsService(paths);

            Assert.Equal(2, svc.Current.SchemaVersion);
            Assert.Equal("TheIsland_WP", svc.Current.LaunchOptions.Map);
            Assert.Equal(70, svc.Current.LaunchOptions.MaxPlayers);
        }
        finally { try { Directory.Delete(root, recursive: true); } catch { } }
    }

    [Fact]
    public void Migrate_FreshInstall_UsesDefaults()
    {
        var root = MakeTempRoot();
        try
        {
            var paths = new AppPaths(root);
            // No file exists yet.
            var svc = new SettingsService(paths);

            Assert.Equal(2, svc.Current.SchemaVersion);
            Assert.True(File.Exists(paths.SettingsFile));
        }
        finally { try { Directory.Delete(root, recursive: true); } catch { } }
    }
}
