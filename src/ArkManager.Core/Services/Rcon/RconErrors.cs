using System.Net.Sockets;
using ArkManager.Core.Models;
using ArkManager.Core.Services.Config;

namespace ArkManager.Core.Services.Rcon;

/// <summary>
/// Produces stable, English RCON error messages. SocketException.Message comes from FormatMessage
/// and is localised by the OS — on a Russian Windows the user gets Cyrillic, on a German one
/// German, etc. Mapping SocketErrorCode ourselves keeps the UI predictable AND lets us replace the
/// cryptic "connection refused" with the actionable hint that 99% of the time the cause is an
/// empty ServerAdminPassword (ASA never opens the RCON port without one).
/// </summary>
public static class RconErrors
{
    /// <summary>Pre-flight check before attempting a TCP connect. null = ok to try.</summary>
    public static string? DescribePrecondition(ServerLaunchOptions o)
    {
        if (!o.RconEnabled)
            return "RCON is disabled — enable it on the Config tab.";
        if (string.IsNullOrWhiteSpace(o.AdminPassword))
            return "ServerAdminPassword is empty. Set it on the Config tab — " +
                   "ASA does not open the RCON port without an admin password.";
        return null;
    }

    /// <summary>Pre-flight check using the Snapshot. null = ok to try.</summary>
    public static string? DescribePrecondition(ServerConfigSnapshot snap)
    {
        if (!snap.RconEnabled)
            return "RCON is disabled — enable it on the Config tab.";
        if (string.IsNullOrWhiteSpace(snap.AdminPassword))
            return "ServerAdminPassword is empty. Set it on the Config tab — " +
                   "ASA does not open the RCON port without an admin password.";
        return null;
    }

    /// <summary>Maps a SocketError code into a stable English message (no OS localisation).</summary>
    public static string DescribeSocketError(SocketError code) => code switch
    {
        SocketError.ConnectionRefused =>
            "Connection refused — the server is not listening on the RCON port. " +
            "Most often this means ServerAdminPassword is empty (ASA refuses to open the RCON port " +
            "without one), or the server is still loading.",
        SocketError.TimedOut =>
            "Connection timed out — the server may still be loading, or a firewall is blocking the port.",
        SocketError.HostUnreachable or SocketError.NetworkUnreachable =>
            "RCON host is unreachable.",
        SocketError.HostNotFound =>
            "RCON host not found.",
        SocketError.AccessDenied =>
            "Access denied opening the RCON connection (firewall or security software).",
        SocketError.ConnectionReset =>
            "Connection reset by the server (it closed the socket — likely still loading).",
        _ => $"RCON connection failed (socket error: {code}).",
    };

    /// <summary>Convenience: maps any exception thrown by RconClient.ConnectAsync/SendAsync.</summary>
    public static string DescribeConnectException(Exception ex) => ex switch
    {
        SocketException se => DescribeSocketError(se.SocketErrorCode),
        // RconClient throws InvalidOperationException with already-English messages
        // ("RCON: wrong password.", "Not connected.", etc.). Pass them through.
        InvalidOperationException ioe => ioe.Message,
        _ => ex.Message,
    };
}
