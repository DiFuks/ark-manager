using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace ArkManager.Core.Services.Mods;

public sealed record CurseForgeModInfo(string Id, string Name, string? Summary, string? WebsiteUrl);

/// <summary>
/// Резолвер имён модов для ASA — через публичный community-прокси <c>api.cfwidget.com</c>.
/// Прокси сам ходит в CurseForge и кэширует, на нашей стороне ключ не нужен.
/// Тот же подход у ASADedicatedManager и большинства open-source ASA-менеджеров.
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
        // cfwidget возвращает 202 «accepted, scrape in progress», если запрашиваемый
        // проект ещё не был в его кеше — тогда нужно дёрнуть повторно через пару секунд.
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
