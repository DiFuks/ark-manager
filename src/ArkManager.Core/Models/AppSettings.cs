using System.Text.Json.Serialization;

namespace ArkManager.Core.Models;

public enum LaunchMode
{
    Whisky,
    LocalWine,
    Parallels,
}

public sealed class AppSettings
{
    /// <summary>Где лежит установленный ASA Dedicated Server (директория с ArkAscendedServer.exe).</summary>
    public string? ServerInstallPath { get; set; }

    /// <summary>Путь к steamcmd-бинарю. Если null — пользоваться встроенным, скачанным в DataDir/steamcmd.</summary>
    public string? SteamCmdPath { get; set; }

    /// <summary>Каталог для backup-архивов.</summary>
    public string? BackupsDirectory { get; set; }

    /// <summary>Сколько последних бэкапов хранить (0 = без ротации).</summary>
    public int BackupRotationKeep { get; set; } = 10;

    public LaunchMode LaunchMode { get; set; } = LaunchMode.Whisky;

    /// <summary>Whisky: путь к боттлу (директория с wineprefix). Если null — авто-детект.</summary>
    public string? WhiskyBottlePath { get; set; }

    /// <summary>Путь до бинарника wine (для LocalWine или для оверрайда whisky-bundled wine).</summary>
    public string? WineBinaryPath { get; set; }

    /// <summary>Имя Parallels VM, в которой запускается сервер.</summary>
    public string? ParallelsVmName { get; set; }

    /// <summary>Аргументы запуска. Map+опции попадают в начало; -mods / -NoBattlEye добавляются автоматически.</summary>
    public ServerLaunchOptions LaunchOptions { get; set; } = new();

    /// <summary>Список профилей серверов (мульти-инстанс) — для будущего расширения. Пока используется только Default.</summary>
    [JsonPropertyName("profiles")]
    public List<ServerProfile> Profiles { get; set; } = new();

    /// <summary>CurseForge Studios API key. Когда задан — Mods-страница резолвит ID→имя через api.curseforge.com.</summary>
    public string? CurseForgeApiKey { get; set; }

    /// <summary>Авто-рестарт при ненулевом коде выхода / краше.</summary>
    public bool AutoRestartOnCrash { get; set; } = false;

    /// <summary>Пауза между авто-рестартами в секундах (back-off для первого, дальше тот же).</summary>
    public int AutoRestartDelaySeconds { get; set; } = 10;

    /// <summary>Периодический рестарт каждые N часов (0 = выкл).</summary>
    public int ScheduledRestartHours { get; set; } = 0;
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
    /// <summary>Cluster ID — серверы с одинаковым ID образуют кластер (трансфер существ/предметов).</summary>
    public string? ClusterId { get; set; }
    /// <summary>Папка для кластер-данных. Если задана — добавляется -ClusterDirOverride=...</summary>
    public string? ClusterDirOverride { get; set; }
    /// <summary>Дополнительные «голые» CLI-флаги, например "-ForceAllowCaveFlyers -ServerAllowAnsel".</summary>
    public string ExtraCommandLineArgs { get; set; } = "";
    /// <summary>Дополнительные QueryString-параметры после Map, разделённые ?.</summary>
    public string ExtraQueryString { get; set; } = "";
}

public sealed class ServerProfile
{
    public string Name { get; set; } = "Default";
    public ServerLaunchOptions Options { get; set; } = new();
    public List<string> ModIds { get; set; } = new();
}
