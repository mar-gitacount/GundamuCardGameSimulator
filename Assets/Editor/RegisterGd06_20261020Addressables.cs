#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// 20261020 ログカード画像を Default Local Group へ登録する。
/// </summary>
public static class RegisterGd06_20261020Addressables
{
    private const string ImagesFolder = "Assets/Resources_moved/Data/Images";

    private static readonly string[] Addresses =
    {
        "Data/Images/GD06-005_Akatsuki (Shiranui)",
        "Data/Images/GD06-046_Hyaku-Shiki",
        "Data/Images/GD06-049_Gundam GP02A",
        "Data/Images/GD06-093_Anavel Gato",
        "Data/Images/GD06-113_Unsanctioned Ordnance",
        "Data/Images/GD06-130_Soleil",
    };

    [MenuItem("Tools/Cards/Register GD06 20261020 Images to Addressables")]
    public static void Register()
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null || settings.DefaultGroup == null)
        {
            Debug.LogWarning("[RegisterGd06_20261020] Addressables Default Group がありません");
            return;
        }

        int n = 0;
        for (int i = 0; i < Addresses.Length; i++)
        {
            string leaf = Path.GetFileName(Addresses[i]);
            string jpg = $"{ImagesFolder}/{leaf}.jpg";
            string png = $"{ImagesFolder}/{leaf}.png";
            string assetPath = File.Exists(jpg.Replace('\\', '/')) ? jpg : png;
            if (!File.Exists(assetPath.Replace('\\', '/')))
            {
                Debug.LogWarning("[RegisterGd06_20261020] 画像なし: " + leaf);
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
        Debug.Log("[RegisterGd06_20261020] Addressables 登録: " + n);
    }
}
#endif
