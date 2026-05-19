using ArkManager.Core.Models;
using ArkManager.Core.Services.Config;

namespace ArkManager.Core.Services.Mods;

public sealed record ModEntry(string Id, string? DisplayName = null, string? Note = null);

/// <summary>
/// Управление модами для ASA. ASA использует свой каталог модов (CurseForge-based, через automanagedmods).
/// Мы храним список ID в settings.json (по профилю) и зеркалим его в GameUserSettings.ini → ActiveMods.
/// При запуске сервера тот же список передаётся как -mods=id1,id2,...
/// </summary>
public sealed class ModsService
{
    private readonly SettingsService _settings;
    private readonly ConfigService _config;
    private readonly CurseForgeClient _cf;
    private readonly Dictionary<string, ModEntry> _resolvedCache = new();

    public ModsService(SettingsService settings, ConfigService config, CurseForgeClient cf)
    {
        _settings = settings;
        _config = config;
        _cf = cf;
    }

    private ServerProfile DefaultProfile
    {
        get
        {
            var p = _settings.Current.Profiles.FirstOrDefault();
            if (p != null) return p;
            p = new ServerProfile { Name = "Default", Options = _settings.Current.LaunchOptions };
            _settings.Current.Profiles.Add(p);
            return p;
        }
    }

    public IReadOnlyList<ModEntry> List()
        => DefaultProfile.ModIds
            .Select(id => _resolvedCache.TryGetValue(id, out var e) ? e : new ModEntry(id))
            .ToList();

    public async Task ResolveNamesAsync(Action<ModEntry> onUpdate, CancellationToken ct = default)
    {
        var key = _settings.Current.CurseForgeApiKey;
        if (string.IsNullOrWhiteSpace(key)) return;
        foreach (var id in DefaultProfile.ModIds.ToArray())
        {
            ct.ThrowIfCancellationRequested();
            if (_resolvedCache.ContainsKey(id)) continue;
            try
            {
                var info = await _cf.GetModAsync(id, key, ct);
                var entry = info != null
                    ? new ModEntry(id, info.Name, info.Summary)
                    : new ModEntry(id, "(не найдено в CurseForge)");
                _resolvedCache[id] = entry;
                onUpdate(entry);
            }
            catch (Exception ex)
            {
                onUpdate(new ModEntry(id, "(ошибка резолва)", ex.Message));
            }
        }
    }

    public void Add(string id)
    {
        id = id.Trim();
        if (string.IsNullOrEmpty(id)) return;
        if (!id.All(char.IsDigit)) throw new ArgumentException("Mod ID должен быть числом (CurseForge ID), а не '" + id + "'.");
        if (DefaultProfile.ModIds.Contains(id)) return;
        DefaultProfile.ModIds.Add(id);
        Persist();
    }

    public void AddMany(IEnumerable<string> ids)
    {
        foreach (var id in ids)
        {
            var t = id.Trim();
            if (string.IsNullOrEmpty(t)) continue;
            if (!t.All(char.IsDigit)) continue;
            if (!DefaultProfile.ModIds.Contains(t)) DefaultProfile.ModIds.Add(t);
        }
        Persist();
    }

    public void Remove(string id)
    {
        if (DefaultProfile.ModIds.Remove(id))
            Persist();
    }

    public void Reorder(IReadOnlyList<string> orderedIds)
    {
        DefaultProfile.ModIds.Clear();
        DefaultProfile.ModIds.AddRange(orderedIds);
        Persist();
    }

    public IReadOnlyList<string> Ids() => DefaultProfile.ModIds.ToList();

    private void Persist()
    {
        _settings.Save();
        // Зеркалим в ini, если сервер уже установлен и существует папка конфигов.
        try
        {
            if (Directory.Exists(_config.ConfigDir) || File.Exists(_config.GameUserSettingsPath))
                _config.WriteActiveMods(DefaultProfile.ModIds);
        }
        catch { /* конфиг может ещё не существовать — это ок */ }
    }
}
