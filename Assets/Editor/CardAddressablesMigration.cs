#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// カード画像を Resources 直参照から Addressables（Local・オンデマンド）へ移す。
/// メニュー: Gundam / Addressables / カード画像をオンデマンド化
/// </summary>
public static class CardAddressablesMigration
{
    private const string SourceImagesFolder = "Assets/Resources/Data/Images";
    private const string AltMovedImagesFolder = "Assets/Resources_moved/Data/Images";
    private const string DestRootFolder = "Assets/AddressableData";
    private const string DestImagesFolder = "Assets/AddressableData/Images";
    private const string AddressPrefix = "Data/Images/";

    [MenuItem("Gundam/Addressables/CardDataのSprite参照だけ解除（imageAddress設定）")]
    public static void ClearCardDataSpriteRefsOnly()
    {
        if (!EditorUtility.DisplayDialog(
                "CardData 参照解除",
                "全 CardData の imageName/image を外し、imageAddress のみにします。\n画像ファイルの移動はしません。",
                "実行",
                "キャンセル"))
        {
            return;
        }

        int cardsUpdated = ClearAllCardDataSpriteReferences();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("完了", $"CardData 更新: {cardsUpdated}", "OK");
    }

    [MenuItem("Gundam/Addressables/カード画像をオンデマンド化（移動・登録・参照解除）")]
    public static void MigrateCardImagesToAddressables()
    {
        if (!EditorUtility.DisplayDialog(
                "カード画像のオンデマンド化",
                "1) Images を Resources 外へ移動\n" +
                "2) Addressables（Local）に登録\n" +
                "3) 全 CardData の Sprite 直参照を外し imageAddress のみにする\n\n" +
                "実行しますか？（事前にプロジェクトを保存・バックアップ推奨）",
                "実行",
                "キャンセル"))
        {
            return;
        }

        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogError("[CardAddressablesMigration] Addressables Settings がありません。Window → Addressables → Groups で作成してください。");
            return;
        }

        AddressableAssetGroup group = settings.DefaultGroup;
        if (group == null)
        {
            Debug.LogError("[CardAddressablesMigration] Default Local Group がありません。");
            return;
        }

        EnsureFolder("Assets", "AddressableData");
        EnsureFolder(DestRootFolder, "Images");

        int moved = 0;
        int addressed = 0;
        StringBuilder log = new StringBuilder();

        // 既に Resources_moved へ移している場合も登録対象にする
        string[] scanFolders =
        {
            SourceImagesFolder,
            AltMovedImagesFolder,
            DestImagesFolder
        };

        try
        {
            AssetDatabase.StartAssetEditing();

            for (int f = 0; f < scanFolders.Length; f++)
            {
                string folder = scanFolders[f];
                if (!AssetDatabase.IsValidFolder(folder))
                {
                    continue;
                }

                string[] guids = AssetDatabase.FindAssets(string.Empty, new[] { folder });
                for (int i = 0; i < guids.Length; i++)
                {
                    string srcPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (string.IsNullOrEmpty(srcPath) || AssetDatabase.IsValidFolder(srcPath))
                    {
                        continue;
                    }

                    string ext = Path.GetExtension(srcPath);
                    if (!IsImageExtension(ext))
                    {
                        continue;
                    }

                    string fileName = Path.GetFileName(srcPath);
                    string leaf = Path.GetFileNameWithoutExtension(srcPath);
                    string workingPath = srcPath;

                    // Resources 配下だけ Dest へ移動（Resources_moved はそのまま）
                    if (srcPath.StartsWith(SourceImagesFolder + "/", System.StringComparison.Ordinal))
                    {
                        string dstPath = $"{DestImagesFolder}/{fileName}";
                        if (srcPath != dstPath)
                        {
                            if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(dstPath)))
                            {
                                workingPath = dstPath;
                            }
                            else
                            {
                                string moveError = AssetDatabase.MoveAsset(srcPath, dstPath);
                                if (!string.IsNullOrEmpty(moveError))
                                {
                                    log.AppendLine($"Move failed: {srcPath} → {dstPath} ({moveError})");
                                    continue;
                                }

                                workingPath = dstPath;
                                moved++;
                            }
                        }
                    }

                    string guid = AssetDatabase.AssetPathToGUID(workingPath);
                    if (string.IsNullOrEmpty(guid))
                    {
                        continue;
                    }

                    AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, group, false, false);
                    if (entry != null)
                    {
                        entry.SetAddress(AddressPrefix + leaf, false);
                        addressed++;
                    }
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        int cardsUpdated = ClearAllCardDataSpriteReferences();

        AssetDatabase.SaveAssets();
        EditorUtility.SetDirty(settings);
        AssetDatabase.Refresh();

        Debug.Log(
            $"[CardAddressablesMigration] done. moved={moved} addressed≈{addressed} cardsUpdated={cardsUpdated}\n{log}");
        EditorUtility.DisplayDialog(
            "完了",
            $"移動: {moved}\nAddressable 登録: {addressed}\nCardData 更新: {cardsUpdated}\n\n" +
            "Play Mode Script は Use Asset Database のままで Editor 確認できます。\n" +
            "Android ビルド前に Addressables → Build → New Build を実行してください。",
            "OK");
    }

    private static int ClearAllCardDataSpriteReferences()
    {
        int cardsUpdated = 0;
        string[] cardGuids = AssetDatabase.FindAssets("t:CardData");
        for (int i = 0; i < cardGuids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(cardGuids[i]);
            CardData card = AssetDatabase.LoadAssetAtPath<CardData>(path);
            if (card == null)
            {
                continue;
            }

            string leaf = ResolveLeafName(card);
            if (string.IsNullOrEmpty(leaf))
            {
                if (!string.IsNullOrWhiteSpace(card.imageAddress) && card.imageName == null && card.image == null)
                {
                    continue;
                }

                Debug.LogWarning($"[CardAddressablesMigration] No image leaf: {path} (id={card.id})");
                continue;
            }

            Undo.RecordObject(card, "Migrate card image to Addressables");
            card.imageAddress = AddressPrefix + leaf;
            card.imageName = null;
            card.image = null;
            EditorUtility.SetDirty(card);
            cardsUpdated++;
        }

        return cardsUpdated;
    }

    private static string ResolveLeafName(CardData card)
    {
        if (card == null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(card.imageAddress))
        {
            string addr = card.imageAddress.Trim();
            if (addr.StartsWith(AddressPrefix))
            {
                return addr.Substring(AddressPrefix.Length);
            }

            return Path.GetFileNameWithoutExtension(addr);
        }

        if (card.imageName != null)
        {
            return StripMultipleSuffix(card.imageName.name);
        }

        if (card.image != null)
        {
            return StripMultipleSuffix(card.image.name);
        }

        return null;
    }

    private static string StripMultipleSuffix(string leaf)
    {
        if (string.IsNullOrEmpty(leaf))
        {
            return leaf;
        }

        if (leaf.EndsWith("_0") && leaf.Length > 2)
        {
            return leaf.Substring(0, leaf.Length - 2);
        }

        return leaf;
    }

    private static bool IsImageExtension(string ext)
    {
        if (string.IsNullOrEmpty(ext))
        {
            return false;
        }

        ext = ext.ToLowerInvariant();
        return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".webp" || ext == ".tga";
    }

    private static void EnsureFolder(string parent, string name)
    {
        string path = $"{parent}/{name}";
        if (!AssetDatabase.IsValidFolder(path))
        {
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
#endif
