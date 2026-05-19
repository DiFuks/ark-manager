using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace ArkManager.Core.Services.Mods;

public sealed record CurseForgeModInfo(string Id, string Name, string? Summary, string? WebsiteUrl);

/// <summary>
/// Минимальный клиент CurseForge Studios API.
/// Документация: https://docs.curseforge.com/
/// ASA — game ID 83374. Мы только читаем /v1/mods/{modId}.
/// </summary>
public sealed class CurseForgeClient
{
    private readonly HttpClient _http;

    public CurseForgeClient()
    {
        _http = new HttpClient { BaseAddress = new Uri("https://api.curseforge.com/"), Timeout = TimeSpan.FromSeconds(15) };
    }

    public async Task<CurseForgeModInfo?> GetModAsync(string id, string apiKey, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"v1/mods/{id}");
        req.Headers.Add("x-api-key", apiKey);
        req.Headers.Add("Accept", "application/json");
        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return null;
        var payload = await resp.Content.ReadFromJsonAsync<Response>(cancellationToken: ct);
        var d = payload?.Data;
        if (d == null) return null;
        return new CurseForgeModInfo(d.Id.ToString(), d.Name ?? "", d.Summary, d.Links?.WebsiteUrl);
    }

    private sealed class Response { [JsonPropertyName("data")] public Data? Data { get; set; } }
    private sealed class Data
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("summary")] public string? Summary { get; set; }
        [JsonPropertyName("links")] public Links? Links { get; set; }
    }
    private sealed class Links { [JsonPropertyName("websiteUrl")] public string? WebsiteUrl { get; set; } }
}
