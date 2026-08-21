using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using UnityEngine;
// using Unity.Services.CloudSave;
// using Unity.Services.Authentication;
// using Unity.Services.Core;
using System.Threading.Tasks;
using System;
using System.IO;
public class PlayerPrefsSaveData : ISaveData
{
    public CardData JsonSaveToLocal(CardData data,int slot = 0)
    {
        Debug.Log($"JsonSaveToLocal called with CardData: ID={data.id}, Name={data.cardName}, Slot={slot}");
        return data;
    }
    public CardJson ConvertToJson(CardData card)
    {
        return new CardJson
        {
            id = card.id,
            gcgOfficialId = card.gcgOfficialId,
            gcgSetKind = card.gcgId != null ? (int)card.gcgId.setKind : 0,
            gcgSetNumber = card.gcgId != null ? card.gcgId.setNumber : 0,
            gcgCardNumber = card.gcgId != null ? card.gcgId.cardNumber : 0,
            cardName = card.cardName,
            cost = card.cost,
            level = card.level,
            power = card.power,
            hp = card.hp,
            imageName = card.GetImageLeafNameForJson(),
            version = card.version,
            sourceType = (int)card.sourceType,
            color = (int)card.color, // カードの色を追加
            type = (int)card.type,
            battleZones = (int)card.battleZones,
            featureIds = CardFeatureRegistry.CollectIds(card.features),
            pilotIdIds = CardPilotIdRegistry.CollectIds(card.pilotIds),
            isBlocker = card.isBlocker,
            isDeployTurnAttack = card.isDeployTurnAttack,
            isNotDirectAttack = card.isNotDirectAttack,
            isShieldToken = card.isShieldToken,
            isRepair = card.isRepair,
            repairAmount = card.repairAmount,
        };
    }
    public CardData ConvertToCardData(CardJson json)
    {
        CardData card = ScriptableObject.CreateInstance<CardData>();
        card.id = json.id;
        card.gcgOfficialId = json.gcgOfficialId;
        if (card.gcgId == null)
        {
            card.gcgId = new GcgIdParts();
        }

        card.gcgId.setKind = (GcgOfficialSetKind)json.gcgSetKind;
        card.gcgId.setNumber = json.gcgSetNumber;
        card.gcgId.cardNumber = json.gcgCardNumber;
        card.SyncGcgOfficialIdFromParts();
        card.cardName = json.cardName;
        card.cost = json.cost;
        card.level = json.level;
        card.power = json.power;
        card.hp = json.hp;
        if (!string.IsNullOrEmpty(json.imageName))
        {
            card.SetImageAddressFromLeaf(json.imageName);
        }
        else
        {
            card.imageAddress = string.Empty;
            card.imageName = null;
            card.image = null;
        }

        card.isBlocker = json.isBlocker;
        card.isDeployTurnAttack = json.isDeployTurnAttack;
        card.isNotDirectAttack = json.isNotDirectAttack;
        card.isShieldToken = json.isShieldToken;
        card.isRepair = json.isRepair;
        card.repairAmount = json.repairAmount;
        card.type = (Type)json.type;
        card.battleZones = (CardBattleZone)json.battleZones;
        card.SetFeaturesFromIds(json.featureIds);
        card.SetPilotIdsFromIds(json.pilotIdIds);
        return card;
    }
    
    public CardMasterJson LoadOrCreateJson(string path)
    {
        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            return JsonUtility.FromJson<CardMasterJson>(json);
        }
        else
        {
            return new CardMasterJson();
        }
    }

    public void SaveJson(string path , CardMasterJson master)
    {
        // ここでは仮に空のCardMasterJsonを保存する例を示します。
       
        string json = JsonUtility.ToJson(master, true);
        File.WriteAllText(path, json);
    }
}