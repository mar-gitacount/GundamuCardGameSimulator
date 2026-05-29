#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// named_effect_master.json から NamedEffectSetData アセットを同期し、ランタイムレジストリを再読込する。
/// </summary>
public static class NamedEffectSetImportEditor
{
    private const string JsonResourcePath = "Data/Json/named_effect_master";
    private const string EffectSetAssetFolder = "Assets/Resources/Data/NamedEffectSets";

    [MenuItem("Tools/Game/Import Named Effect Sets From JSON")]
    public static void ImportFromJson()
    {
        TextAsset jsonAsset = Resources.Load<TextAsset>(JsonResourcePath);
        if (jsonAsset == null)
        {
            Debug.LogError($"[NamedEffectSetImport] Resources/{JsonResourcePath}.json が見つかりません。");
            return;
        }

        NamedEffectSetMasterJson master = JsonUtility.FromJson<NamedEffectSetMasterJson>(jsonAsset.text);
        if (master?.effectSets == null || master.effectSets.Length == 0)
        {
            Debug.LogWarning("[NamedEffectSetImport] effectSets が空です。");
            return;
        }

        EnsureAssetFolder();

        int created = 0;
        int updated = 0;
        for (int i = 0; i < master.effectSets.Length; i++)
        {
            NamedEffectSetJsonEntry entry = master.effectSets[i];
            if (entry == null || string.IsNullOrWhiteSpace(entry.effectSetName))
            {
                continue;
            }

            string safeName = entry.effectSetName.Trim().Replace(' ', '_');
            string assetPath = $"{EffectSetAssetFolder}/EffectSet_{safeName}.asset";

            NamedEffectSetData set = AssetDatabase.LoadAssetAtPath<NamedEffectSetData>(assetPath);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<NamedEffectSetData>();
                AssetDatabase.CreateAsset(set, assetPath);
                created++;
            }
            else
            {
                updated++;
            }

            set.effectSetName = entry.effectSetName.Trim();
            set.displayName = entry.displayName ?? string.Empty;
            set.effects.Clear();
            if (entry.effects != null)
            {
                for (int e = 0; e < entry.effects.Length; e++)
                {
                    if (entry.effects[e] != null)
                    {
                        set.effects.Add(entry.effects[e]);
                    }
                }
            }

            EditorUtility.SetDirty(set);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        NamedEffectSetRegistry.Reload();
        Debug.Log($"[NamedEffectSetImport] Done. created:{created} updated:{updated} total:{master.effectSets.Length}");
    }

    [MenuItem("Tools/Game/Reload Named Effect Sets (JSON)")]
    public static void ReloadRegistry()
    {
        NamedEffectSetRegistry.Reload();
        Debug.Log("[NamedEffectSetImport] NamedEffectSetRegistry reloaded from JSON.");
    }

    private static void EnsureAssetFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources/Data/NamedEffectSets"))
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources/Data"))
            {
                AssetDatabase.CreateFolder("Assets/Resources", "Data");
            }

            if (!AssetDatabase.IsValidFolder("Assets/Resources/Data/NamedEffectSets"))
            {
                AssetDatabase.CreateFolder("Assets/Resources/Data", "NamedEffectSets");
            }
        }
    }
}
#endif
