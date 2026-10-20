#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// 20261022 カード画像を Default Local Group へ登録する。
/// </summary>
public static class RegisterGd06_20261022Addressables
{
    private const string ImagesFolder = "Assets/Resources_moved/Data/Images";

    private static readonly string[] Addresses =
    {
        "Data/Images/GD06-025_Re-GZ",
        "Data/Images/GD06-118_Infinite Variations",
        "Data/Images/GD06-090_Vidar",
        "Data/Images/GD06-082_Athrun Zala",
        "Data/Images/GD06-002_Infinite Justice Gundam",
        "Data/Images/GD06-095_Kelly Layzner",
        "Data/Images/GD06-128_Gwaden",
        "Data/Images/GD06-099_Chang Wufei",
        "Data/Images/GD06-064_Altron Gundam (EW)",
        "Data/Images/GD06-036_Gundam Vidar",
    };

    [MenuItem("Tools/Cards/Register GD06 20261022 Images to Addressables")]
    public static void Register()
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null || settings.DefaultGroup == null)
        {
            Debug.LogWarning("[RegisterGd06_20261022] Addressables Default Group がありません");
            return;
        }

        int n = 0;
        for (int i = 0; i < Addresses.Length; i++)
        {
            string leaf = Path.GetFileName(Addresses[i]);
            string png = $"{ImagesFolder}/{leaf}.png";
            string jpg = $"{ImagesFolder}/{leaf}.jpg";
            string assetPath = File.Exists(png.Replace('\\', '/')) ? png : jpg;
            if (!File.Exists(assetPath.Replace('\\', '/')))
            {
                Debug.LogWarning("[RegisterGd06_20261022] 画像なし: " + leaf);
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
        Debug.Log("[RegisterGd06_20261022] Addressables 登録: " + n);
    }
}
#endif
