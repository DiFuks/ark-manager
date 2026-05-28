using System.Text.RegularExpressions;

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
    private CancellationTokenSource? _cts;
    private Task? _worker;

    public event Action<PlayerSample>? Sampled;

    public PlayerPoller(SettingsService settings, ServerManager server)
    {
        _settings = settings;
        _server = server;
        _server.StateChanged += OnStateChanged;
    }

    private void OnStateChanged(ServerState s)
    {
        if (s == ServerState.Running) Start();
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
        // Give the server time to warm up before the first poll.
        try { await Task.Delay(TimeSpan.FromSeconds(20), ct); } catch { return; }

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
        var opts = _settings.Current.LaunchOptions;
        if (!opts.RconEnabled || string.IsNullOrEmpty(opts.AdminPassword))
            return new PlayerSample(0, Array.Empty<string>(), DateTime.UtcNow, "RCON disabled or no admin password");

        await using var c = new RconClient();
        await c.ConnectAsync("127.0.0.1", opts.RconPort, opts.AdminPassword!, ct);
        var resp = await c.SendAsync("ListPlayers", ct);
        return ParseListPlayers(resp);
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
        _server.StateChanged -= OnStateChanged;
    }
}
