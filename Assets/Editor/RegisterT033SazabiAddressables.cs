#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>T-033 Sazabi 画像を Default Local Group へ登録する。</summary>
[InitializeOnLoad]
public static class RegisterT033SazabiAddressables
{
    private const string AssetPathPng = "Assets/Resources_moved/Data/Images/T-033_Sazabi.png";
    private const string Address = "Data/Images/T-033_Sazabi";

    static RegisterT033SazabiAddressables()
    {
        EditorApplication.delayCall += () =>
        {
            if (SessionState.GetBool("T033AddrOnce", false))
            {
                return;
            }

            SessionState.SetBool("T033AddrOnce", true);
            Register();
        };
    }

    [MenuItem("Tools/Cards/Register T-033 Sazabi to Addressables")]
    public static void Register()
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null || settings.DefaultGroup == null)
        {
            Debug.LogWarning("[T-033] Addressables Default Group がありません");
            return;
        }

        if (!File.Exists(AssetPathPng))
        {
            Debug.LogWarning("[T-033] 画像なし: " + AssetPathPng);
            return;
        }

        string guid = AssetDatabase.AssetPathToGUID(AssetPathPng);
        if (string.IsNullOrEmpty(guid))
        {
            AssetDatabase.ImportAsset(AssetPathPng, ImportAssetOptions.ForceUpdate);
            guid = AssetDatabase.AssetPathToGUID(AssetPathPng);
        }

        if (string.IsNullOrEmpty(guid))
        {
            Debug.LogWarning("[T-033] GUID を取得できません: " + AssetPathPng);
            return;
        }

        AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, settings.DefaultGroup, false, false);
        if (entry == null)
        {
            Debug.LogWarning("[T-033] CreateOrMoveEntry 失敗 guid:" + guid);
            return;
        }

        entry.SetAddress(Address, false);
        entry.SetLabel("default", true, true, false);
        EditorUtility.SetDirty(settings);
        EditorUtility.SetDirty(settings.DefaultGroup);
        AssetDatabase.SaveAssets();
        Debug.Log($"[T-033] Addressables 登録完了 address:{Address} guid:{guid} path:{AssetPathPng}");
    }
}
#endif
