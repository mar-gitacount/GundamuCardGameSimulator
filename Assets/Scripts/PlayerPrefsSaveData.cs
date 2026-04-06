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
            cardName = card.cardName,
            cost = card.cost,
            level = card.level,
            power = card.power,
            hp = card.hp,
            imageName = card.imageName != null ? card.imageName.name : "",
            version = card.version,
            sourceType = (int)card.sourceType,
            color = (int)card.color // カードの色を追加
        };
    }
    public CardData ConvertToCardData(CardJson json)
    {
        CardData card = ScriptableObject.CreateInstance<CardData>();
        card.id = json.id;
        card.cardName = json.cardName;
        card.cost = json.cost;
        card.level = json.level;
        card.power = json.power;
        card.hp = json.hp;
        if(!string.IsNullOrEmpty(json.imageName))
        {
            card.imageName = Resources.Load<Sprite>($"Data/Cards/{json.imageName}");
        }
        else
        {
            card.imageName = null;
        }
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