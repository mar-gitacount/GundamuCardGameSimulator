#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// 20261023 カード画像を Default Local Group へ登録する。
/// </summary>
public static class RegisterGd06_20261023Addressables
{
    private const string ImagesFolder = "Assets/Resources_moved/Data/Images";

    private static readonly string[] Addresses =
    {
        "Data/Images/GD06-096_Xavier Olivette",
        "Data/Images/GD06-024_R-Jarja",
        "Data/Images/GD06-119_Sturm und Drang",
        "Data/Images/GD06-066_Shadow Gundam",
        "Data/Images/GD06-053_Xavier's Gyan Hakuji-Packs (GQ)",
        "Data/Images/GD06-108_On Edge",
    };

    [MenuItem("Tools/Cards/Register GD06 20261023 Images to Addressables")]
    public static void Register()
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null || settings.DefaultGroup == null)
        {
            Debug.LogWarning("[RegisterGd06_20261023] Addressables Default Group がありません");
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
                Debug.LogWarning("[RegisterGd06_20261023] 画像なし: " + leaf);
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
        Debug.Log("[RegisterGd06_20261023] Addressables 登録: " + n);
    }
}
#endif
