#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// pilot_master.json から CardPilotIdData アセットを一括生成・更新する。
/// </summary>
public static class CardPilotIdImportEditor
{
    private const string JsonResourcePath = "Data/Json/pilot_master";
    private const string PilotIdAssetFolder = "Assets/Resources/Data/PilotIds";

    [MenuItem("Tools/Game/Import Card Pilot Ids From JSON")]
    public static void ImportFromJson()
    {
        TextAsset jsonAsset = Resources.Load<TextAsset>(JsonResourcePath);
        if (jsonAsset == null)
        {
            Debug.LogError($"[CardPilotIdImport] Resources/{JsonResourcePath}.json が見つかりません。");
            return;
        }

        CardPilotIdMasterJson master = JsonUtility.FromJson<CardPilotIdMasterJson>(jsonAsset.text);
        if (master?.pilots == null || master.pilots.Length == 0)
        {
            Debug.LogWarning("[CardPilotIdImport] pilots が空です。");
            return;
        }

        EnsurePilotIdFolder();

        int created = 0;
        int updated = 0;
        for (int i = 0; i < master.pilots.Length; i++)
        {
            CardPilotIdJsonEntry entry = master.pilots[i];
            if (entry == null || entry.id <= 0)
            {
                continue;
            }

            string safeKey = string.IsNullOrEmpty(entry.pilotKey)
                ? $"id_{entry.id}"
                : entry.pilotKey.Replace(' ', '_');
            string assetPath = $"{PilotIdAssetFolder}/PilotId_{entry.id}_{safeKey}.asset";

            CardPilotIdData pilotId = AssetDatabase.LoadAssetAtPath<CardPilotIdData>(assetPath);
            if (pilotId == null)
            {
                pilotId = ScriptableObject.CreateInstance<CardPilotIdData>();
                AssetDatabase.CreateAsset(pilotId, assetPath);
                created++;
            }
            else
            {
                updated++;
            }

            pilotId.id = entry.id;
            pilotId.pilotKey = entry.pilotKey ?? string.Empty;
            pilotId.displayName = entry.displayName ?? string.Empty;
            pilotId.description = entry.description ?? string.Empty;
            EditorUtility.SetDirty(pilotId);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        CardPilotIdRegistry.Reload();
        Debug.Log($"[CardPilotIdImport] Done. created:{created} updated:{updated} total:{master.pilots.Length}");
    }

    private static void EnsurePilotIdFolder()
    {
        if (AssetDatabase.IsValidFolder(PilotIdAssetFolder))
        {
            return;
        }

        if (!AssetDatabase.IsValidFolder("Assets/Resources/Data"))
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }

            AssetDatabase.CreateFolder("Assets/Resources", "Data");
        }

        if (!AssetDatabase.IsValidFolder(PilotIdAssetFolder))
        {
            AssetDatabase.CreateFolder("Assets/Resources/Data", "PilotIds");
        }
    }
}

/// <summary>
/// PilotId JSON の変更時と Editor 起動時に自動インポートする。
/// </summary>
[InitializeOnLoad]
public sealed class CardPilotIdImportAutoRunner : AssetPostprocessor
{
    private const string JsonAssetPath = "Assets/Resources/Data/Json/pilot_master.json";
    private static bool initialized;

    static CardPilotIdImportAutoRunner()
    {
        EditorApplication.delayCall += RunOnEditorLoad;
    }

    private static void RunOnEditorLoad()
    {
        if (initialized || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        initialized = true;
        CardPilotIdImportEditor.ImportFromJson();
    }

    private static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        for (int i = 0; i < importedAssets.Length; i++)
        {
            if (importedAssets[i] == JsonAssetPath)
            {
                CardPilotIdImportEditor.ImportFromJson();
                return;
            }
        }
    }
}
#endif
