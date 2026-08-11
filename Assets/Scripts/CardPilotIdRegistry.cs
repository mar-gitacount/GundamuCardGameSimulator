using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Resources/Data/PilotIds 以下の <see cref="CardPilotIdData"/> を ID / pilotKey で引くレジストリ。
/// </summary>
public static class CardPilotIdRegistry
{
    public const string DefaultResourcesPath = "Data/PilotIds";

    private static bool loaded;
    private static Dictionary<int, CardPilotIdData> byId = new Dictionary<int, CardPilotIdData>();
    private static Dictionary<string, CardPilotIdData> byKey =
        new Dictionary<string, CardPilotIdData>(StringComparer.OrdinalIgnoreCase);

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

        CardPilotIdData[] all = Resources.LoadAll<CardPilotIdData>(resourcesPath);
        for (int i = 0; i < all.Length; i++)
        {
            Register(all[i]);
        }

        loaded = true;
        Debug.Log($"[CardPilotIdRegistry] Loaded {byId.Count} pilotIds from Resources/{resourcesPath}");
    }

    private static void Register(CardPilotIdData pilotId)
    {
        if (pilotId == null)
        {
            return;
        }

        if (byId.ContainsKey(pilotId.id))
        {
            Debug.LogWarning(
                $"[CardPilotIdRegistry] Duplicate pilotId id {pilotId.id}: '{pilotId.displayName}' vs existing '{byId[pilotId.id].displayName}'");
        }
        else
        {
            byId[pilotId.id] = pilotId;
        }

        if (string.IsNullOrWhiteSpace(pilotId.pilotKey))
        {
            return;
        }

        string key = pilotId.pilotKey.Trim();
        if (byKey.ContainsKey(key))
        {
            Debug.LogWarning(
                $"[CardPilotIdRegistry] Duplicate pilotKey '{key}': id {pilotId.id} vs {byKey[key].id}");
        }
        else
        {
            byKey[key] = pilotId;
        }
    }

    public static CardPilotIdData GetById(int id, string resourcesPath = DefaultResourcesPath)
    {
        EnsureLoaded(resourcesPath);
        return byId.TryGetValue(id, out CardPilotIdData pilotId) ? pilotId : null;
    }

    public static CardPilotIdData GetByKey(string pilotKey, string resourcesPath = DefaultResourcesPath)
    {
        if (string.IsNullOrWhiteSpace(pilotKey))
        {
            return null;
        }

        EnsureLoaded(resourcesPath);
        return byKey.TryGetValue(pilotKey.Trim(), out CardPilotIdData pilotId) ? pilotId : null;
    }

    public static IReadOnlyCollection<CardPilotIdData> GetAll(string resourcesPath = DefaultResourcesPath)
    {
        EnsureLoaded(resourcesPath);
        return byId.Values;
    }

    public static List<CardPilotIdData> ResolveIds(int[] pilotIdIds, string resourcesPath = DefaultResourcesPath)
    {
        List<CardPilotIdData> result = new List<CardPilotIdData>();
        if (pilotIdIds == null || pilotIdIds.Length == 0)
        {
            return result;
        }

        EnsureLoaded(resourcesPath);
        for (int i = 0; i < pilotIdIds.Length; i++)
        {
            CardPilotIdData pilotId = GetById(pilotIdIds[i], resourcesPath);
            if (pilotId != null)
            {
                result.Add(pilotId);
            }
            else
            {
                Debug.LogWarning($"[CardPilotIdRegistry] Unknown pilotId id: {pilotIdIds[i]}");
            }
        }

        return result;
    }

    public static int[] CollectIds(IList<CardPilotIdData> pilotIds)
    {
        if (pilotIds == null || pilotIds.Count == 0)
        {
            return Array.Empty<int>();
        }

        List<int> ids = new List<int>(pilotIds.Count);
        for (int i = 0; i < pilotIds.Count; i++)
        {
            CardPilotIdData pilotId = pilotIds[i];
            if (pilotId == null)
            {
                continue;
            }

            if (!ids.Contains(pilotId.id))
            {
                ids.Add(pilotId.id);
            }
        }

        return ids.ToArray();
    }
}
