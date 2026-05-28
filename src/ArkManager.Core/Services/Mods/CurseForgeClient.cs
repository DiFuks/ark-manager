using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace ArkManager.Core.Services.Mods;

public sealed record CurseForgeModInfo(string Id, string Name, string? Summary, string? WebsiteUrl);

/// <summary>
/// Резолвер имён модов CurseForge для ASA. Логика:
/// 1) По умолчанию идём в публичный community-прокси <c>api.cfwidget.com</c> — он не требует
///    ключа и проксирует CurseForge. Аналог того, как ASADedicatedManager на Windows
///    обходится без авторизации.
/// 2) Если cfwidget не отдал данные и юзер вписал в Settings свой CurseForge API key,
///    пробуем официальный <c>api.curseforge.com/v1/mods/{id}</c> с заголовком x-api-key.
/// </summary>
public sealed class CurseForgeClient
{
    private readonly HttpClient _httpCf;
    private readonly HttpClient _httpWidget;

    public CurseForgeClient()
    {
        _httpCf = new HttpClient
        {
            BaseAddress = new Uri("https://api.curseforge.com/"),
            Timeout = TimeSpan.FromSeconds(15),
        };
        _httpWidget = new HttpClient
        {
            BaseAddress = new Uri("https://api.cfwidget.com/"),
            Timeout = TimeSpan.FromSeconds(15),
        };
    }

    public async Task<CurseForgeModInfo?> GetModAsync(string id, string? apiKey, CancellationToken ct = default)
    {
        // Сначала пробуем no-auth путь: cfwidget — community-прокси, его хватает в 99% случаев.
        try
        {
            var widget = await TryCfWidgetAsync(id, ct);
            if (widget != null) return widget;
        }
        catch { /* падает — идём в фолбек */ }

        // Фолбек: официальный CurseForge Studios API. Сюда попадаем только если у юзера
        // явно вписан ключ в Settings и cfwidget не сработал (упал/202/нет проекта).
        if (string.IsNullOrWhiteSpace(apiKey)) return null;
        return await TryOfficialAsync(id, apiKey!, ct);
    }

    private async Task<CurseForgeModInfo?> TryCfWidgetAsync(string id, CancellationToken ct)
    {
        // cfwidget возвращает 202 «accepted, scrape in progress», если запрашиваемый
        // проект ещё не был в его кеше — тогда нужно дёрнуть повторно через пару секунд.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var resp = await _httpWidget.GetAsync(id, ct);
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

    private async Task<CurseForgeModInfo?> TryOfficialAsync(string id, string apiKey, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"v1/mods/{id}");
        req.Headers.Add("x-api-key", apiKey);
        req.Headers.Add("Accept", "application/json");
        using var resp = await _httpCf.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return null;
        var payload = await resp.Content.ReadFromJsonAsync<Response>(cancellationToken: ct);
        var d = payload?.Data;
        if (d == null) return null;
        return new CurseForgeModInfo(d.Id.ToString(), d.Name ?? "", d.Summary, d.Links?.WebsiteUrl);
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
