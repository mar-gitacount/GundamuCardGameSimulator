#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// ST04 ユニットトークン T-008 / T-009 / T-010 の CardData 作成と画像 Addressables 登録。
/// </summary>
public static class CreateSt04StrikeUnitTokens
{
    private const string CardsFolder = "Assets/Resources/Data/Cards";
    private const string ImagesFolder = "Assets/Resources/Data/Images";
    private const string EarthAllianceFeatureGuid = "f1d0c509706cbef4bac52a766c1afbfa";

    private struct TokenSpec
    {
        public string officialId;
        public string cardName;
        public string fileName;
        public string imageLeaf;
        public int id;
        public int ap;
        public int hp;
        public int cardNumber;
    }

    [MenuItem("Tools/Cards/Create ST04 Strike Unit Tokens (T-008〜T-010)")]
    public static void CreateOrUpdate()
    {
        var specs = new[]
        {
            new TokenSpec
            {
                officialId = "T-008",
                cardName = "Aile Strike Gundam",
                fileName = "T-008 Aile Strike Gundam.asset",
                imageLeaf = "T-008",
                id = 1000545,
                ap = 3,
                hp = 3,
                cardNumber = 8
            },
            new TokenSpec
            {
                officialId = "T-009",
                cardName = "Launcher Strike Gundam",
                fileName = "T-009 Launcher Strike Gundam.asset",
                imageLeaf = "T-009",
                id = 1000546,
                ap = 2,
                hp = 4,
                cardNumber = 9
            },
            new TokenSpec
            {
                officialId = "T-010",
                cardName = "Sword Strike Gundam",
                fileName = "T-010 Sword Strike Gundam.asset",
                imageLeaf = "T-010",
                id = 1000547,
                ap = 4,
                hp = 2,
                cardNumber = 10
            }
        };

        CardFeatureData earthAlliance = AssetDatabase.LoadAssetAtPath<CardFeatureData>(
            AssetDatabase.GUIDToAssetPath(EarthAllianceFeatureGuid));

        int created = 0;
        int updated = 0;
        var missingImages = new System.Collections.Generic.List<string>();

        AssetDatabase.StartAssetEditing();
        try
        {
            for (int i = 0; i < specs.Length; i++)
            {
                TokenSpec spec = specs[i];
                string assetPath = $"{CardsFolder}/{spec.fileName}";
                CardData card = AssetDatabase.LoadAssetAtPath<CardData>(assetPath);
                bool isNew = card == null;
                if (isNew)
                {
                    card = ScriptableObject.CreateInstance<CardData>();
                }

                ApplyTokenFields(card, spec, earthAlliance);

                string imagePath = $"{ImagesFolder}/{spec.imageLeaf}.png";
                if (!File.Exists(imagePath.Replace('\\', '/')))
                {
                    missingImages.Add(spec.officialId + " → " + imagePath);
                }
                else
                {
                    EnsureImageAddressable(imagePath, "Data/Images/" + spec.imageLeaf);
                }

                if (isNew)
                {
                    AssetDatabase.CreateAsset(card, assetPath);
                    created++;
                }
                else
                {
                    EditorUtility.SetDirty(card);
                    updated++;
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        string msg =
            $"作成: {created} / 更新: {updated}\n" +
            "T-008 Aile Strike (3/3)\n" +
            "T-009 Launcher Strike (2/4)\n" +
            "T-010 Sword Strike (4/2)\n" +
            "UnitToken / Blocker / Earth Alliance / SEED / ST04";
        if (missingImages.Count > 0)
        {
            msg += "\n\n画像なし:\n - " + string.Join("\n - ", missingImages);
        }

        Debug.Log("[CreateSt04StrikeUnitTokens] " + msg);
        EditorUtility.DisplayDialog("ST04 Unit Tokens", msg, "OK");
    }

    private static void ApplyTokenFields(CardData card, TokenSpec spec, CardFeatureData earthAlliance)
    {
        card.id = spec.id;
        card.gcgOfficialId = spec.officialId;
        card.cardName = spec.cardName;
        card.cost = 0;
        card.level = 0;
        card.power = spec.ap;
        card.hp = spec.hp;
        card.imageName = null;
        card.image = null;
        card.SetImageAddressFromLeaf(spec.imageLeaf);
        card.productLine = CardProductLine.Starter;
        card.boosterSet = BoosterProductSet.None;
        card.starterSet = StarterProductSet.SeedStrike;
        card.eternalBoosterSet = EternalBoosterProductSet.None;
        card.SyncProductFieldsFromLine();
        card.sourceTitle = CardSourceTitle.GundamSeed;
        card.filterType = FilterType.Version;
        card.color = CardColor.Colorless;
        card.type = Type.UnitToken;
        card.battleZones = 0;
        card.attackFlg = AttackFlg.False;
        card.timedEffects = new System.Collections.Generic.List<TimedEffectData>();
        card.features = new System.Collections.Generic.List<CardFeatureData>();
        if (earthAlliance != null)
        {
            card.features.Add(earthAlliance);
        }

        card.pilotIds = new System.Collections.Generic.List<CardPilotIdData>();
        card.link = new System.Collections.Generic.List<UnitLinkPilotSlot>();
        card.isBlocker = true;
        card.isDeployTurnAttack = false;
        card.isNotDirectAttack = false;
        card.isShieldToken = false;
        card.isRepair = false;
        card.repairAmount = 0;
        card.notUsedOnline = false;
        card.cannotMountPilot = true;
        card.gcgId = new GcgIdParts
        {
            setKind = GcgOfficialSetKind.Token,
            setNumber = 0,
            cardNumber = spec.cardNumber
        };
    }

    private static void EnsureImageAddressable(string assetPath, string address)
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogWarning("[CreateSt04StrikeUnitTokens] Addressables Settings がありません: " + assetPath);
            return;
        }

        AddressableAssetGroup group = settings.DefaultGroup;
        if (group == null)
        {
            Debug.LogWarning("[CreateSt04StrikeUnitTokens] Default Group がありません");
            return;
        }

        string guid = AssetDatabase.AssetPathToGUID(assetPath);
        if (string.IsNullOrEmpty(guid))
        {
            return;
        }

        AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, group, false, false);
        if (entry != null)
        {
            entry.SetAddress(address, false);
            EditorUtility.SetDirty(settings);
            EditorUtility.SetDirty(group);
        }
    }
}
#endif
