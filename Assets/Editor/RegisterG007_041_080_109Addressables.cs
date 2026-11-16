#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// GD07-041 / GD07-080 / GD07-109 のカード画像を Default Local Group へ登録する。
/// </summary>
public static class RegisterG007_041_080_109Addressables
{
    private const string ImagesFolder = "Assets/Resources_moved/Data/Images";

    private static readonly string[] Addresses =
    {
        "Data/Images/G007-041_Bolt Gundam",
        "Data/Images/G007-080_Dragon Gundam",
        "Data/Images/G007-109_Power of the People"
    };

    [MenuItem("Tools/Cards/Register G007-041 080 109 Images to Addressables")]
    public static void Register()
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null || settings.DefaultGroup == null)
        {
            Debug.LogWarning("[RegisterG007_041_080_109] Addressables Default Group がありません");
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
                Debug.LogWarning("[RegisterG007_041_080_109] 画像なし: " + leaf);
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
        Debug.Log("[RegisterG007_041_080_109] Addressables 登録: " + n);
    }
}
#endif
