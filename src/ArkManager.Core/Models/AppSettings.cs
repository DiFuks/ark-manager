using System.Text.Json.Serialization;

namespace ArkManager.Core.Models;

public sealed class AppSettings
{
    /// <summary>Where the installed ASA Dedicated Server lives (directory containing ArkAscendedServer.exe).</summary>
    public string? ServerInstallPath { get; set; }

    /// <summary>Path to the steamcmd binary. If null — use the bundled one downloaded to DataDir/steamcmd.</summary>
    public string? SteamCmdPath { get; set; }

    /// <summary>Directory for backup archives.</summary>
    public string? BackupsDirectory { get; set; }

    /// <summary>How many most recent backups to keep (0 = no rotation).</summary>
    public int BackupRotationKeep { get; set; } = 10;

    /// <summary>Launch arguments. Map+options go first; -mods / -NoBattlEye are appended automatically.</summary>
    public ServerLaunchOptions LaunchOptions { get; set; } = new();

    /// <summary>List of server profiles (multi-instance) — for future expansion. Only Default is used for now.</summary>
    [JsonPropertyName("profiles")]
    public List<ServerProfile> Profiles { get; set; } = new();

    /// <summary>Auto-backup every N minutes (0 = off). Rotation — via BackupRotationKeep.</summary>
    /// <remarks>Tick is always skipped when the server is not Running: idle snapshots are pointless.</remarks>
    public int AutoBackupIntervalMinutes { get; set; } = 0;

    /// <summary>
    /// Open Windows Firewall inbound rules for the configured game/query/RCON ports on
    /// each Start (Windows only, requires admin). When off — no firewall changes.
    /// </summary>
    public bool ManageFirewallRules { get; set; } = false;
}

public sealed class ServerLaunchOptions
{
    public string Map { get; set; } = "TheIsland_WP";
    public string SessionName { get; set; } = "My ASA Server";
    public int Port { get; set; } = 7777;
    public int QueryPort { get; set; } = 27015;
    public int RconPort { get; set; } = 27020;
    public bool RconEnabled { get; set; } = true;
    public string? ServerPassword { get; set; }
    public string? AdminPassword { get; set; }
    public string? SpectatorPassword { get; set; }
    public int MaxPlayers { get; set; } = 70;
    public bool NoBattlEye { get; set; } = true;
    public bool AutoManagedMods { get; set; } = true;
    /// <summary>Cluster ID — servers sharing the same ID form a cluster (creature/item transfers).</summary>
    public string? ClusterId { get; set; }
    /// <summary>Directory for cluster data. If set — appends -ClusterDirOverride=...</summary>
    public string? ClusterDirOverride { get; set; }
    /// <summary>Additional "bare" CLI flags, e.g. "-ForceAllowCaveFlyers -ServerAllowAnsel".</summary>
    public string ExtraCommandLineArgs { get; set; } = "";
    /// <summary>Additional QueryString parameters after Map, separated by ?.</summary>
    public string ExtraQueryString { get; set; } = "";
}

public sealed class ServerProfile
{
    public string Name { get; set; } = "Default";
    public ServerLaunchOptions Options { get; set; } = new();
    public List<string> ModIds { get; set; } = new();
}
