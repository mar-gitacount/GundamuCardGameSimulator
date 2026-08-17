#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// リリース用：カード画像を端末ローカルのまま、Android の容量・メモリを抑える。
/// メニュー: Gundam / Addressables / カード画像を Android 向けに圧縮
/// </summary>
public static class CardImageReleaseOptimize
{
    private const int AndroidMaxTextureSize = 1024;
    private static readonly string[] ImageFolders =
    {
        "Assets/Resources_moved/Data/Images",
        "Assets/AddressableData/Images",
        "Assets/Resources/Data/Images",
    };

    [MenuItem("Gundam/Addressables/カード画像を Android 向けに圧縮（ASTC・最大1024）")]
    public static void OptimizeCardImagesForAndroidRelease()
    {
        if (!EditorUtility.DisplayDialog(
                "カード画像の Android 圧縮",
                "カード画像の Android 設定を次にします。\n\n" +
                "・最大サイズ 1024\n" +
                "・ASTC 6x6（端末内バンドルのまま）\n" +
                "・Sprite Mode を Single\n\n" +
                "Editor / PC 用の解像度は変えません。\n" +
                "実行後は Addressables を Android 向けに Rebuild してください。",
                "実行",
                "キャンセル"))
        {
            return;
        }

        int scanned = 0;
        int changed = 0;
        try
        {
            AssetDatabase.StartAssetEditing();
            for (int f = 0; f < ImageFolders.Length; f++)
            {
                string folder = ImageFolders[f];
                if (!AssetDatabase.IsValidFolder(folder))
                {
                    continue;
                }

                string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folder });
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (importer == null)
                    {
                        continue;
                    }

                    scanned++;
                    if (ApplyAndroidReleaseSettings(importer))
                    {
                        importer.SaveAndReimport();
                        changed++;
                    }
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog(
            "完了",
            $"走査: {scanned} / 変更: {changed}\n\n次に Window → Asset Management → Addressables → Groups で Android の New Build を実行してください。",
            "OK");
    }

    private static bool ApplyAndroidReleaseSettings(TextureImporter importer)
    {
        bool dirty = false;
        if (importer.textureType != TextureImporterType.Sprite)
        {
            importer.textureType = TextureImporterType.Sprite;
            dirty = true;
        }

        if (importer.spriteImportMode != SpriteImportMode.Single)
        {
            importer.spriteImportMode = SpriteImportMode.Single;
            dirty = true;
        }

        if (importer.mipmapEnabled)
        {
            importer.mipmapEnabled = false;
            dirty = true;
        }

        TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
        bool androidDirty = !android.overridden
            || android.maxTextureSize != AndroidMaxTextureSize
            || android.format != TextureImporterFormat.ASTC_6x6;
        if (androidDirty)
        {
            android.overridden = true;
            android.maxTextureSize = AndroidMaxTextureSize;
            android.format = TextureImporterFormat.ASTC_6x6;
            android.textureCompression = TextureImporterCompression.Compressed;
            android.compressionQuality = 50;
            importer.SetPlatformTextureSettings(android);
            dirty = true;
        }

        return dirty;
    }
}
#endif
