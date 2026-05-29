namespace ArkManager.Core.Services.Config;

/// <summary>
/// Mutation surface for <c>ConfigService.UpdateBasic</c>. Callers receive an
/// instance prefilled from the current Snapshot, mutate the fields they want to change,
/// and on return the service writes the affected keys to GameUserSettings.ini.
/// </summary>
public sealed class MutableBasic
{
    public string SessionName { get; set; } = "My ASA Server";
    public int Port { get; set; } = 7777;
    public int QueryPort { get; set; } = 27015;
    public int RconPort { get; set; } = 27020;
    public bool RconEnabled { get; set; } = true;
    public string ServerPassword { get; set; } = "";
    public string AdminPassword { get; set; } = "";
    public string SpectatorPassword { get; set; } = "";

    internal static MutableBasic FromSnapshot(ServerConfigSnapshot s) => new()
    {
        SessionName = s.SessionName,
        Port = s.Port,
        QueryPort = s.QueryPort,
        RconPort = s.RconPort,
        RconEnabled = s.RconEnabled,
        ServerPassword = s.ServerPassword,
        AdminPassword = s.AdminPassword,
        SpectatorPassword = s.SpectatorPassword,
    };
}
