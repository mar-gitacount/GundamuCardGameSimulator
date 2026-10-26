#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// 20261025 カード画像を Default Local Group へ登録する。
/// </summary>
public static class RegisterGd06_20261025Addressables
{
    private const string ImagesFolder = "Assets/Resources_moved/Data/Images";

    private static readonly string[] Addresses =
    {
        "Data/Images/GD06-007_Kikeroga (MS Mode) (GQ)",
        "Data/Images/GD06-011_Red Gundam",
        "Data/Images/GD06-012_Z'Gok",
        "Data/Images/GD06-013_Zaku II F2 (EFSF)",
        "Data/Images/GD06-023_Galluss J",
        "Data/Images/GD06-027_Mashymre's Zssa",
        "Data/Images/GD06-028_Gaza D",
        "Data/Images/GD06-031_Zssan",
        "Data/Images/GD06-040_Gundam Mk-II",
        "Data/Images/GD06-047_Windam",
        "Data/Images/GD06-048_Helmwige Reincar",
        "Data/Images/GD06-062_Gyan Hakuji-Packs (GQ)",
        "Data/Images/GD06-061_Dra-C",
        "Data/Images/GD06-063_GFreD",
        "Data/Images/GD06-075_Serpent",
        "Data/Images/GD06-078_WaD",
        "Data/Images/GD06-079_Muttowooo",
        "Data/Images/GD06-102_A Miracle",
        "Data/Images/GD06-126_Skipjack-class Warship",
        "Data/Images/GD06-127_Yomagn'tho",
        "Data/Images/T-030_Gozzo",
        "Data/Images/T-031_Turn X (Arms)",
        "Data/Images/T-032_Turn X (Legs)",
        "Data/Images/T-033_Sazabi",
        "Data/Images/T-034_Nu Gundam",
        "Data/Images/T-035_Gundam Calibarn",
        "Data/Images/T-036_Gundam Aerial Rebuild",
        "Data/Images/T-037_Axis"
    };

    [MenuItem("Tools/Cards/Register GD06 20261025 Images to Addressables")]
    public static void Register()
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null || settings.DefaultGroup == null)
        {
            Debug.LogWarning("[RegisterGd06_20261025] Addressables Default Group がありません");
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
                Debug.LogWarning("[RegisterGd06_20261025] 画像なし: " + leaf);
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
        Debug.Log("[RegisterGd06_20261025] Addressables 登録: " + n);
    }
}
#endif
