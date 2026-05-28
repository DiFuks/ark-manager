using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace ArkManager.Core.Services.Mods;

public sealed record CurseForgeModInfo(string Id, string Name, string? Summary, string? WebsiteUrl);

/// <summary>
/// ASA mod-name resolver — via the public community proxy <c>api.cfwidget.com</c>.
/// The proxy itself hits CurseForge and caches; no key is needed on our side.
/// Same approach used by ASADedicatedManager and most open-source ASA managers.
/// </summary>
public sealed class CurseForgeClient
{
    private readonly HttpClient _http;

    public CurseForgeClient()
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri("https://api.cfwidget.com/"),
            Timeout = TimeSpan.FromSeconds(15),
        };
    }

    public async Task<CurseForgeModInfo?> GetModAsync(string id, CancellationToken ct = default)
    {
        // cfwidget returns 202 "accepted, scrape in progress" if the requested project
        // wasn't in its cache yet — in that case we need to hit it again in a couple of seconds.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var resp = await _http.GetAsync(id, ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.Accepted)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
                continue;
            }
            if (!resp.IsSuccessStatusCode) return null;
            var data = await resp.Content.ReadFromJsonAsync<WidgetData>(cancellationToken: ct);
            if (data == null || string.IsNullOrWhiteSpace(data.Title)) return null;
            return new CurseForgeModInfo(id, data.Title!, data.Summary, data.Urls?.Curseforge);
        }
        return null;
    }

    private sealed class WidgetData
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("summary")] public string? Summary { get; set; }
        [JsonPropertyName("urls")] public WidgetUrls? Urls { get; set; }
    }
    private sealed class WidgetUrls
    {
        [JsonPropertyName("curseforge")] public string? Curseforge { get; set; }
    }
}
