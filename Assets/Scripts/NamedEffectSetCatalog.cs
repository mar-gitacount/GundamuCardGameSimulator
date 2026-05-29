using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Resources/Data/Json/named_effect_master.json の読み込み（ランタイム・Editor 共通）。
/// </summary>
public static class NamedEffectSetCatalog
{
    public const string DefaultJsonResourcePath = "Data/Json/named_effect_master";

    public static bool TryLoadMaster(
        out NamedEffectSetMasterJson master,
        string jsonResourcePath = DefaultJsonResourcePath)
    {
        master = null;
        TextAsset jsonAsset = Resources.Load<TextAsset>(jsonResourcePath);
        if (jsonAsset == null)
        {
            return false;
        }

        master = JsonUtility.FromJson<NamedEffectSetMasterJson>(jsonAsset.text);
        if (master == null)
        {
            master = new NamedEffectSetMasterJson();
        }

        if (master.effectSets == null)
        {
            master.effectSets = Array.Empty<NamedEffectSetJsonEntry>();
        }

        return true;
    }

    public static List<NamedEffectSetJsonEntry> LoadEntries(string jsonResourcePath = DefaultJsonResourcePath)
    {
        if (!TryLoadMaster(out NamedEffectSetMasterJson master, jsonResourcePath))
        {
            return new List<NamedEffectSetJsonEntry>();
        }

        List<NamedEffectSetJsonEntry> list = new List<NamedEffectSetJsonEntry>(master.effectSets.Length);
        for (int i = 0; i < master.effectSets.Length; i++)
        {
            NamedEffectSetJsonEntry entry = master.effectSets[i];
            if (entry != null && !string.IsNullOrWhiteSpace(entry.effectSetName))
            {
                list.Add(entry);
            }
        }

        list.Sort((a, b) => string.Compare(a.effectSetName, b.effectSetName, StringComparison.OrdinalIgnoreCase));
        return list;
    }

    public static List<string> LoadSortedEffectSetNames(string jsonResourcePath = DefaultJsonResourcePath)
    {
        List<NamedEffectSetJsonEntry> entries = LoadEntries(jsonResourcePath);
        List<string> names = new List<string>(entries.Count);
        for (int i = 0; i < entries.Count; i++)
        {
            names.Add(entries[i].effectSetName.Trim());
        }

        return names;
    }
}
