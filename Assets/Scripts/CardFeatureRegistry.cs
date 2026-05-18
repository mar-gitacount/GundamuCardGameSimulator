using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Resources/Data/Features 以下の <see cref="CardFeatureData"/> を ID / featureKey で引くレジストリ。
/// </summary>
public static class CardFeatureRegistry
{
    public const string DefaultResourcesPath = "Data/Features";

    private static bool loaded;
    private static Dictionary<int, CardFeatureData> byId = new Dictionary<int, CardFeatureData>();
    private static Dictionary<string, CardFeatureData> byKey = new Dictionary<string, CardFeatureData>(StringComparer.OrdinalIgnoreCase);

    public static void Reload(string resourcesPath = DefaultResourcesPath)
    {
        loaded = false;
        byId.Clear();
        byKey.Clear();
        EnsureLoaded(resourcesPath);
    }

    public static void EnsureLoaded(string resourcesPath = DefaultResourcesPath)
    {
        if (loaded)
        {
            return;
        }

        CardFeatureData[] all = Resources.LoadAll<CardFeatureData>(resourcesPath);
        for (int i = 0; i < all.Length; i++)
        {
            Register(all[i]);
        }

        loaded = true;
        Debug.Log($"[CardFeatureRegistry] Loaded {byId.Count} features from Resources/{resourcesPath}");
    }

    private static void Register(CardFeatureData feature)
    {
        if (feature == null)
        {
            return;
        }

        if (byId.ContainsKey(feature.id))
        {
            Debug.LogWarning(
                $"[CardFeatureRegistry] Duplicate feature id {feature.id}: '{feature.displayName}' vs existing '{byId[feature.id].displayName}'");
        }
        else
        {
            byId[feature.id] = feature;
        }

        if (string.IsNullOrWhiteSpace(feature.featureKey))
        {
            return;
        }

        string key = feature.featureKey.Trim();
        if (byKey.ContainsKey(key))
        {
            Debug.LogWarning(
                $"[CardFeatureRegistry] Duplicate featureKey '{key}': id {feature.id} vs {byKey[key].id}");
        }
        else
        {
            byKey[key] = feature;
        }
    }

    public static CardFeatureData GetById(int id, string resourcesPath = DefaultResourcesPath)
    {
        EnsureLoaded(resourcesPath);
        return byId.TryGetValue(id, out CardFeatureData feature) ? feature : null;
    }

    public static CardFeatureData GetByKey(string featureKey, string resourcesPath = DefaultResourcesPath)
    {
        if (string.IsNullOrWhiteSpace(featureKey))
        {
            return null;
        }

        EnsureLoaded(resourcesPath);
        return byKey.TryGetValue(featureKey.Trim(), out CardFeatureData feature) ? feature : null;
    }

    public static IReadOnlyCollection<CardFeatureData> GetAll(string resourcesPath = DefaultResourcesPath)
    {
        EnsureLoaded(resourcesPath);
        return byId.Values;
    }

    public static List<CardFeatureData> ResolveIds(int[] featureIds, string resourcesPath = DefaultResourcesPath)
    {
        List<CardFeatureData> result = new List<CardFeatureData>();
        if (featureIds == null || featureIds.Length == 0)
        {
            return result;
        }

        EnsureLoaded(resourcesPath);
        for (int i = 0; i < featureIds.Length; i++)
        {
            CardFeatureData feature = GetById(featureIds[i], resourcesPath);
            if (feature != null)
            {
                result.Add(feature);
            }
            else
            {
                Debug.LogWarning($"[CardFeatureRegistry] Unknown feature id: {featureIds[i]}");
            }
        }

        return result;
    }

    public static int[] CollectIds(IList<CardFeatureData> features)
    {
        if (features == null || features.Count == 0)
        {
            return Array.Empty<int>();
        }

        List<int> ids = new List<int>(features.Count);
        for (int i = 0; i < features.Count; i++)
        {
            CardFeatureData feature = features[i];
            if (feature == null)
            {
                continue;
            }

            if (!ids.Contains(feature.id))
            {
                ids.Add(feature.id);
            }
        }

        return ids.ToArray();
    }
}
