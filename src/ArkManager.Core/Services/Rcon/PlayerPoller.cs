using System.Text.RegularExpressions;
using ArkManager.Core.Services.Config;

namespace ArkManager.Core.Services.Rcon;

public sealed record PlayerSample(int Count, IReadOnlyList<string> Names, DateTime SampledUtc, string? Error = null);

/// <summary>
/// Background worker that, while the server is running, calls ListPlayers via local RCON
/// every N seconds and parses the count/names.
/// </summary>
public sealed class PlayerPoller : IAsyncDisposable
{
    private readonly SettingsService _settings;
    private readonly ServerManager _server;
    private readonly ConfigService _config;
    private readonly Action<ServerState> _onState;
    private readonly Action<bool> _onReady;
    private CancellationTokenSource? _cts;
    private Task? _worker;

    public event Action<PlayerSample>? Sampled;

    public PlayerPoller(SettingsService settings, ServerManager server, ConfigService config)
    {
        _settings = settings;
        _server = server;
        _config = config;
        // Gate on Ready, not just Running. Before the world finishes loading ASA hasn't opened
        // the RCON port yet, so polling would immediately fall into the "connection refused" /
        // "RCON disabled" branch and spam a misleading error into PlayersDetail — long before
        // the RCON tab itself even tries to connect (it also waits for Ready). Matching that
        // gating keeps the two views in sync.
        _onState = _ => Sync();
        _onReady = _ => Sync();
        _server.StateChanged += _onState;
        _server.ReadyChanged += _onReady;
    }

    private void Sync()
    {
        if (_server.State == ServerState.Running && _server.IsReady) Start();
        else Stop();
    }

    public void Start()
    {
        Stop();
        _cts = new CancellationTokenSource();
        _worker = Task.Run(() => LoopAsync(_cts.Token));
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        _cts = null;
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        // We're already gated on Ready, so the RCON port is open the moment we enter the loop.
        // No 20-second warmup needed — but give RCON a couple of seconds to stabilise before
        // the first probe, otherwise we sometimes catch the auth handshake mid-flight.
        try { await Task.Delay(TimeSpan.FromSeconds(2), ct); } catch { return; }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var sample = await PollOnceAsync(ct);
                Sampled?.Invoke(sample);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                Sampled?.Invoke(new PlayerSample(0, Array.Empty<string>(), DateTime.UtcNow, ex.Message));
            }
            try { await Task.Delay(TimeSpan.FromSeconds(30), ct); } catch { return; }
        }
    }

    private async Task<PlayerSample> PollOnceAsync(CancellationToken ct)
    {
        var snap = _config.Snapshot;
        // Short, tile-sized hints — the PLAYERS tile uses CharacterEllipsis and chops long
        // text. The verbose precondition lives in the RCON tab status badge; here we just
        // tell the user what's missing.
        if (!snap.RconEnabled)
            return new PlayerSample(0, Array.Empty<string>(), DateTime.UtcNow, "RCON disabled");
        if (string.IsNullOrWhiteSpace(snap.AdminPassword))
            return new PlayerSample(0, Array.Empty<string>(), DateTime.UtcNow, "Set Admin password");

        try
        {
            await using var c = new RconClient();
            await c.ConnectAsync("127.0.0.1", snap.RconPort, snap.AdminPassword, ct);
            var resp = await c.SendAsync("ListPlayers", ct);
            return ParseListPlayers(resp);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // Unwrap into a stable English message — SocketException is OS-localised otherwise.
            return new PlayerSample(0, Array.Empty<string>(), DateTime.UtcNow,
                RconErrors.DescribeConnectException(ex));
        }
    }

    /// <summary>
    /// ASA ListPlayers returns lines like:
    ///   "0. Nickname, 1234567890123456789"   (steam id)
    ///   or "No Players Connected"
    /// </summary>
    public static PlayerSample ParseListPlayers(string raw)
    {
        if (raw.Contains("No Players", StringComparison.OrdinalIgnoreCase))
            return new PlayerSample(0, Array.Empty<string>(), DateTime.UtcNow);

        var names = new List<string>();
        foreach (var line in raw.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // "0. Nickname, steamid"
            var m = Regex.Match(line, @"^\d+\.\s*(?<name>.+?)\s*,\s*\d+\s*$");
            if (m.Success) names.Add(m.Groups["name"].Value);
        }
        return new PlayerSample(names.Count, names, DateTime.UtcNow);
    }

    public async ValueTask DisposeAsync()
    {
        Stop();
        if (_worker != null) { try { await _worker; } catch { } }
        _server.StateChanged -= _onState;
        _server.ReadyChanged -= _onReady;
    }
}
