#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// feature_master.json から CardFeatureData アセットを一括生成・更新する。
/// </summary>
public static class CardFeatureImportEditor
{
    private const string JsonResourcePath = "Data/Json/feature_master";
    private const string FeatureAssetFolder = "Assets/Resources/Data/Features";

    [MenuItem("Tools/Game/Import Card Features From JSON")]
    public static void ImportFromJson()
    {
        TextAsset jsonAsset = Resources.Load<TextAsset>(JsonResourcePath);
        if (jsonAsset == null)
        {
            Debug.LogError($"[CardFeatureImport] Resources/{JsonResourcePath}.json が見つかりません。");
            return;
        }

        CardFeatureMasterJson master = JsonUtility.FromJson<CardFeatureMasterJson>(jsonAsset.text);
        if (master?.features == null || master.features.Length == 0)
        {
            Debug.LogWarning("[CardFeatureImport] features が空です。");
            return;
        }

        if (!AssetDatabase.IsValidFolder("Assets/Resources/Data/Features"))
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources/Data"))
            {
                AssetDatabase.CreateFolder("Assets/Resources", "Data");
            }

            if (!AssetDatabase.IsValidFolder("Assets/Resources/Data/Features"))
            {
                AssetDatabase.CreateFolder("Assets/Resources/Data", "Features");
            }
        }

        int created = 0;
        int updated = 0;
        for (int i = 0; i < master.features.Length; i++)
        {
            CardFeatureJsonEntry entry = master.features[i];
            if (entry == null || entry.id <= 0)
            {
                continue;
            }

            string safeKey = string.IsNullOrEmpty(entry.featureKey)
                ? $"id_{entry.id}"
                : entry.featureKey.Replace(' ', '_');
            string assetPath = $"{FeatureAssetFolder}/Feature_{entry.id}_{safeKey}.asset";

            CardFeatureData feature = AssetDatabase.LoadAssetAtPath<CardFeatureData>(assetPath);
            if (feature == null)
            {
                feature = ScriptableObject.CreateInstance<CardFeatureData>();
                AssetDatabase.CreateAsset(feature, assetPath);
                created++;
            }
            else
            {
                updated++;
            }

            feature.id = entry.id;
            feature.featureKey = entry.featureKey ?? string.Empty;
            feature.displayName = entry.displayName ?? string.Empty;
            feature.description = entry.description ?? string.Empty;
            EditorUtility.SetDirty(feature);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        CardFeatureRegistry.Reload();
        Debug.Log($"[CardFeatureImport] Done. created:{created} updated:{updated} total:{master.features.Length}");
    }
}
#endif
