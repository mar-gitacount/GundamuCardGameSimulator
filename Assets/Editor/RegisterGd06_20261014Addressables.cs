#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// 20261014 ログカード画像を Default Local Group へ登録する。
/// </summary>
public static class RegisterGd06_20261014Addressables
{
    private const string ImagesFolder = "Assets/Resources_moved/Data/Images";

    private static readonly string[] Addresses =
    {
        "Data/Images/GD06-043_Julieta's Reginlaze",
        "Data/Images/GD06-038_Gundam Kimaris Vidar",
        "Data/Images/GD06-074_Flat",
        "Data/Images/GD06-081_Amate Yuzuriha (Machu)",
        "Data/Images/GD06-003_GGumega Psycommu Released",
        "Data/Images/GD06-098_Duo Maxwell",
        "Data/Images/GD06-072_Gundam Deathscythe Hell (EW)",
    };

    [MenuItem("Tools/Cards/Register GD06 20261014 Images to Addressables")]
    public static void Register()
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null || settings.DefaultGroup == null)
        {
            Debug.LogWarning("[RegisterGd06_20261014] Addressables Default Group がありません");
            return;
        }

        int n = 0;
        for (int i = 0; i < Addresses.Length; i++)
        {
            string leaf = Path.GetFileName(Addresses[i]);
            string assetPath = $"{ImagesFolder}/{leaf}.jpg";
            if (!File.Exists(assetPath.Replace('\\', '/')))
            {
                Debug.LogWarning("[RegisterGd06_20261014] 画像なし: " + assetPath);
                continue;
            }

            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(guid))
            {
                continue;
            }

            AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, settings.DefaultGroup, false, false);
            if (entry != null)
            {
                entry.SetAddress(Addresses[i], false);
                n++;
            }
        }

        EditorUtility.SetDirty(settings);
        EditorUtility.SetDirty(settings.DefaultGroup);
        AssetDatabase.SaveAssets();
        Debug.Log("[RegisterGd06_20261014] Addressables 登録: " + n);
    }
}
#endif
