using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// named_effect_master.json から effectSetName で共有効果を引くレジストリ。
/// </summary>
public static class NamedEffectSetRegistry
{
    private static bool loaded;
    private static Dictionary<string, NamedEffectSetEntry> byName =
        new Dictionary<string, NamedEffectSetEntry>(StringComparer.OrdinalIgnoreCase);

    public static void Reload(string jsonResourcePath = NamedEffectSetCatalog.DefaultJsonResourcePath)
    {
        loaded = false;
        byName.Clear();
        EnsureLoaded(jsonResourcePath);
    }

    public static void EnsureLoaded(string jsonResourcePath = NamedEffectSetCatalog.DefaultJsonResourcePath)
    {
        if (loaded)
        {
            return;
        }

        byName.Clear();
        if (!NamedEffectSetCatalog.TryLoadMaster(out NamedEffectSetMasterJson master, jsonResourcePath))
        {
            Debug.LogWarning(
                $"[NamedEffectSetRegistry] Resources/{jsonResourcePath}.json が見つかりません。");
            loaded = true;
            return;
        }

        for (int i = 0; i < master.effectSets.Length; i++)
        {
            RegisterFromJson(master.effectSets[i]);
        }

        loaded = true;
        Debug.Log($"[NamedEffectSetRegistry] Loaded {byName.Count} effect sets from Resources/{jsonResourcePath}.json");
    }

    private static void RegisterFromJson(NamedEffectSetJsonEntry entry)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.effectSetName))
        {
            return;
        }

        string key = entry.effectSetName.Trim();
        List<EffectData> effects = new List<EffectData>();
        if (entry.effects != null)
        {
            for (int i = 0; i < entry.effects.Length; i++)
            {
                if (entry.effects[i] != null)
                {
                    effects.Add(entry.effects[i]);
                }
            }
        }

        if (byName.ContainsKey(key))
        {
            Debug.LogWarning(
                $"[NamedEffectSetRegistry] Duplicate effectSetName '{key}': '{entry.displayName}' vs '{byName[key].DisplayName}'");
        }

        byName[key] = new NamedEffectSetEntry(key, entry.displayName, effects);
    }

    public static NamedEffectSetEntry GetEntry(string effectSetName)
    {
        if (string.IsNullOrWhiteSpace(effectSetName))
        {
            return null;
        }

        EnsureLoaded();
        return byName.TryGetValue(effectSetName.Trim(), out NamedEffectSetEntry entry) ? entry : null;
    }

    public static IReadOnlyList<EffectData> GetEffects(string effectSetName)
    {
        NamedEffectSetEntry entry = GetEntry(effectSetName);
        return entry != null ? entry.Effects : Array.Empty<EffectData>();
    }

    public static IReadOnlyCollection<NamedEffectSetEntry> GetAll()
    {
        EnsureLoaded();
        return byName.Values;
    }
}
