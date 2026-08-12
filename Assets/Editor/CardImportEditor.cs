#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// card_master.json から CardData アセットを一括生成・更新する。
/// </summary>
public static class CardImportEditor
{
    private const string JsonResourcePath = "Data/Json/card_master";
    private const string CardAssetFolder = "Assets/Resources/Data/Cards";

    [MenuItem("Tools/Game/Import Cards From JSON")]
    public static void ImportFromJson()
    {
        TextAsset jsonAsset = Resources.Load<TextAsset>(JsonResourcePath);
        if (jsonAsset == null)
        {
            Debug.LogError($"[CardImport] Resources/{JsonResourcePath}.json が見つかりません。");
            return;
        }

        CardImportMasterJson master = JsonUtility.FromJson<CardImportMasterJson>(jsonAsset.text);
        if (master?.cards == null || master.cards.Length == 0)
        {
            Debug.LogWarning("[CardImport] cards が空です。");
            return;
        }

        CardFeatureRegistry.EnsureLoaded();
        EnsureCardFolder();

        int created = 0;
        int updated = 0;
        for (int i = 0; i < master.cards.Length; i++)
        {
            CardImportJsonEntry entry = master.cards[i];
            if (entry == null || entry.id <= 0 || string.IsNullOrWhiteSpace(entry.cardName))
            {
                continue;
            }

            string safeName = entry.cardName.Replace(' ', '_').Replace("'", "");
            string assetPath = $"{CardAssetFolder}/{entry.id}{safeName}.asset";

            CardData card = AssetDatabase.LoadAssetAtPath<CardData>(assetPath);
            if (card == null)
            {
                card = ScriptableObject.CreateInstance<CardData>();
                AssetDatabase.CreateAsset(card, assetPath);
                created++;
            }
            else
            {
                updated++;
            }

            ApplyEntryToCard(card, entry);
            EditorUtility.SetDirty(card);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[CardImport] Done. created:{created} updated:{updated} total:{master.cards.Length}");
    }

    private static void ApplyEntryToCard(CardData card, CardImportJsonEntry entry)
    {
        card.id = entry.id;
        card.gcgOfficialId = entry.gcgOfficialId ?? string.Empty;
        card.cardName = entry.cardName ?? string.Empty;
        card.cost = entry.cost;
        card.level = entry.level;
        card.power = entry.power;
        card.hp = entry.hp;
        card.version = entry.version;
        card.sourceType = (CardSourceType)entry.sourceType;
        card.filterType = FilterType.Version;
        card.color = (CardColor)entry.color;
        card.type = (Type)entry.type;
        card.attackFlg = AttackFlg.False;
        card.isBlocker = entry.isBlocker;
        card.isDeployTurnAttack = entry.isDeployTurnAttack;
        card.isNotDirectAttack = entry.isNotDirectAttack;
        card.isShieldToken = entry.isShieldToken;
        card.isRepair = entry.isRepair;
        card.repairAmount = entry.repairAmount;
        card.notUsedOnline = entry.notUsedOnline;
        card.cannotMountPilot = entry.cannotMountPilot;

        Sprite sprite = LoadCardSprite(entry.imageName);
        if (sprite != null)
        {
            card.imageName = sprite;
        }
        else if (!string.IsNullOrWhiteSpace(entry.imageName))
        {
            Debug.LogWarning($"[CardImport] Sprite not found for id:{entry.id} image:{entry.imageName}");
        }

        card.features = BuildFeatureList(entry.featureIds);
        card.pilotIds = new List<CardPilotIdData>();
        card.link = BuildLinkSlots(entry.linkFeatureIds, entry.linkPilotIdIds);
        card.timedEffects = BuildTimedEffects(entry.timedBlocks);
    }

    private static Sprite LoadCardSprite(string imageName)
    {
        if (string.IsNullOrWhiteSpace(imageName))
        {
            return null;
        }

        Sprite sprite = Resources.Load<Sprite>($"Data/Images/{imageName}");
        if (sprite == null)
        {
            sprite = Resources.Load<Sprite>($"Data/Cards/{imageName}");
        }

        return sprite;
    }

    private static List<CardFeatureData> BuildFeatureList(int[] featureIds)
    {
        List<CardFeatureData> list = new List<CardFeatureData>();
        if (featureIds == null)
        {
            return list;
        }

        for (int i = 0; i < featureIds.Length; i++)
        {
            int id = featureIds[i];
            if (id <= 0)
            {
                continue;
            }

            CardFeatureData feature = CardFeatureRegistry.GetById(id);
            if (feature != null && !list.Contains(feature))
            {
                list.Add(feature);
            }
        }

        return list;
    }

    private static List<UnitLinkPilotSlot> BuildLinkSlots(int[] linkFeatureIds, int[] linkPilotIdIds)
    {
        List<UnitLinkPilotSlot> list = new List<UnitLinkPilotSlot>();

        List<CardFeatureData> features = BuildFeatureList(linkFeatureIds);
        if (features.Count > 0)
        {
            list.Add(new UnitLinkPilotSlot
            {
                pilotCardId = 0,
                linkPilotIds = new List<CardPilotIdData>(),
                pilotFeatures = features,
                pilotFeatureIds = linkFeatureIds
            });
        }

        List<CardPilotIdData> pilotIds = CardPilotIdRegistry.ResolveIds(linkPilotIdIds);
        if (pilotIds.Count > 0)
        {
            list.Add(new UnitLinkPilotSlot
            {
                pilotCardId = 0,
                linkPilotIds = pilotIds,
                linkPilotIdIds = linkPilotIdIds,
                pilotFeatures = new List<CardFeatureData>()
            });
        }

        return list;
    }

    private static List<TimedEffectData> BuildTimedEffects(CardImportTimedBlockJson[] blocks)
    {
        List<TimedEffectData> list = new List<TimedEffectData>();
        if (blocks == null)
        {
            return list;
        }

        for (int i = 0; i < blocks.Length; i++)
        {
            CardImportTimedBlockJson block = blocks[i];
            if (block == null)
            {
                continue;
            }

            TimedEffectData timed = new TimedEffectData
            {
                timing = (EffectTiming)block.timing,
                effectsName = block.effectsName ?? string.Empty,
                effects = new List<EffectData>(),
                activationCost = 0,
                oncePerTurn = block.oncePerTurn,
                observedUnitTriggerKind = ObservedUnitTriggerKind.Unset,
                requireChainObservationContext = false,
                activationConditions = BuildActivationConditions(block)
            };
            list.Add(timed);
        }

        return list;
    }

    private static List<EffectActivationCondition> BuildActivationConditions(CardImportTimedBlockJson block)
    {
        List<EffectActivationCondition> conditions = new List<EffectActivationCondition>();
        if (block.ownerTurn)
        {
            conditions.Add(new EffectActivationCondition
            {
                boardSide = EffectBoardSide.Unset,
                checkKind = EffectActivationCheckKind.Unset,
                turnCheck = EffectTurnCheckKind.OwnerTurn
            });
        }

        if (block.battleDamageDestroy)
        {
            conditions.Add(new EffectActivationCondition
            {
                boardSide = EffectBoardSide.Unset,
                checkKind = EffectActivationCheckKind.DestroyedByBattleDamage,
                turnCheck = EffectTurnCheckKind.Unset
            });
        }

        if (block.observedFeatureIds != null && block.observedFeatureIds.Length > 0)
        {
            List<CardFeatureData> features = BuildFeatureList(block.observedFeatureIds);
            conditions.Add(new EffectActivationCondition
            {
                boardSide = EffectBoardSide.Unset,
                checkKind = EffectActivationCheckKind.ObservedCardHasFeature,
                turnCheck = EffectTurnCheckKind.Unset,
                features = features.ToArray(),
                featureIds = block.observedFeatureIds,
                minimumCount = 1
            });
        }

        return conditions;
    }

    private static void EnsureCardFolder()
    {
        if (AssetDatabase.IsValidFolder(CardAssetFolder))
        {
            return;
        }

        if (!AssetDatabase.IsValidFolder("Assets/Resources/Data"))
        {
            AssetDatabase.CreateFolder("Assets/Resources", "Data");
        }

        if (!AssetDatabase.IsValidFolder(CardAssetFolder))
        {
            AssetDatabase.CreateFolder("Assets/Resources/Data", "Cards");
        }
    }
}

[Serializable]
public class CardImportMasterJson
{
    public CardImportJsonEntry[] cards;
}

[Serializable]
public class CardImportJsonEntry
{
    public int id;
    public string gcgOfficialId;
    public string cardName;
    public int cost;
    public int level;
    public int power;
    public int hp;
    public string imageName;
    public int version;
    public int sourceType;
    public int color;
    public int type;
    public int[] featureIds;
    public bool isBlocker;
    public bool isDeployTurnAttack;
    public bool isNotDirectAttack;
    public bool isShieldToken;
    public bool isRepair;
    public int repairAmount;
    public bool notUsedOnline;
    public bool cannotMountPilot;
    public int[] linkFeatureIds;
    public int[] linkPilotIdIds;
    public CardImportTimedBlockJson[] timedBlocks;
}

[Serializable]
public class CardImportTimedBlockJson
{
    public int timing;
    public string effectsName;
    public bool oncePerTurn;
    public bool ownerTurn;
    public bool battleDamageDestroy;
    public int[] observedFeatureIds;
}

/// <summary>card_master.json 変更時に自動インポート。</summary>
[InitializeOnLoad]
public sealed class CardImportAutoRunner : AssetPostprocessor
{
    private const string JsonAssetPath = "Assets/Resources/Data/Json/card_master.json";

    private static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
        for (int i = 0; i < importedAssets.Length; i++)
        {
            if (importedAssets[i] == JsonAssetPath)
            {
                EditorApplication.delayCall += CardImportEditor.ImportFromJson;
                break;
            }
        }
    }
}

#endif
