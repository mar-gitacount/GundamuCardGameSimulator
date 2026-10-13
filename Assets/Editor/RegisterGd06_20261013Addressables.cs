#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// 20261013 ログカード画像を Default Local Group へ登録する。
/// </summary>
public static class RegisterGd06_20261013Addressables
{
    private const string ImagesFolder = "Assets/Resources_moved/Data/Images";

    private static readonly string[] Addresses =
    {
        "Data/Images/GD06-060_Zaku II F2 Type",
        "Data/Images/GD06-056_Xamel",
        "Data/Images/GD06-104_Steady Support",
        "Data/Images/GD06-015_Waldfeld's Aile Gundam",
        "Data/Images/GD06-107_Wheel Spoke Formation",
        "Data/Images/GD06-026_Stero's Mahiroo",
    };

    [MenuItem("Tools/Cards/Register GD06 20261013 Images to Addressables")]
    public static void Register()
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null || settings.DefaultGroup == null)
        {
            Debug.LogWarning("[RegisterGd06_20261013] Addressables Default Group がありません");
            return;
        }

        int n = 0;
        for (int i = 0; i < Addresses.Length; i++)
        {
            string leaf = Path.GetFileName(Addresses[i]);
            string assetPath = $"{ImagesFolder}/{leaf}.jpg";
            if (!File.Exists(assetPath.Replace('\\', '/')))
            {
                Debug.LogWarning("[RegisterGd06_20261013] 画像なし: " + assetPath);
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
        Debug.Log("[RegisterGd06_20261013] Addressables 登録: " + n);
    }
}
#endif
