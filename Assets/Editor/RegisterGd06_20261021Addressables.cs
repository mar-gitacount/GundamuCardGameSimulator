#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// 20261021（ログファイル 20262021.txt）カード画像を Default Local Group へ登録する。
/// </summary>
public static class RegisterGd06_20261021Addressables
{
    private const string ImagesFolder = "Assets/Resources_moved/Data/Images";

    private static readonly string[] Addresses =
    {
        "Data/Images/GD06-105_Brace Under Pressure",
        "Data/Images/GD06-058_Blast Impulse Gundam",
        "Data/Images/GD06-016_Dom Trooper",
        "Data/Images/GD06-129_5th Luna",
        "Data/Images/GD06-035_Zeta Gundam",
        "Data/Images/GD06-071_Shining Gundam (Meikyo Shisui)",
    };

    [MenuItem("Tools/Cards/Register GD06 20261021 Images to Addressables")]
    public static void Register()
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null || settings.DefaultGroup == null)
        {
            Debug.LogWarning("[RegisterGd06_20261021] Addressables Default Group がありません");
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
                Debug.LogWarning("[RegisterGd06_20261021] 画像なし: " + leaf);
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
        Debug.Log("[RegisterGd06_20261021] Addressables 登録: " + n);
    }
}
#endif
