#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// 20261018 ログカード画像を Default Local Group へ登録する。
/// </summary>
public static class RegisterGd06_20261018Addressables
{
    private const string ImagesFolder = "Assets/Resources_moved/Data/Images";

    private static readonly string[] Addresses =
    {
        "Data/Images/GD06-073_Wing Gundam Zero (EW)",
        "Data/Images/GD06-022_Merrybell's Bandit",
        "Data/Images/GD06-087_Merrybell Gadget",
        "Data/Images/GD06-120_Hoping for Peace",
        "Data/Images/GD06-006_GQuuuuuuX (Omega Psycommu)",
        "Data/Images/GD06-083_Challia Bull (GQ)",
    };

    [MenuItem("Tools/Cards/Register GD06 20261018 Images to Addressables")]
    public static void Register()
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null || settings.DefaultGroup == null)
        {
            Debug.LogWarning("[RegisterGd06_20261018] Addressables Default Group がありません");
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
                Debug.LogWarning("[RegisterGd06_20261018] 画像なし: " + leaf);
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
        Debug.Log("[RegisterGd06_20261018] Addressables 登録: " + n);
    }
}
#endif
