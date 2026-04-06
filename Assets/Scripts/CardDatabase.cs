using UnityEngine;      
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
public class CardDatabase : MonoBehaviour
{
    public static CardDatabase Instance;

    [SerializeField] private List<CardData> cardList;

    private Dictionary<int, CardData> cardDict;

    void Awake()
    {
        Instance = this;
        Debug.Log("CardDatabase Awake: インスタンスが作成されました");

        cardDict = new Dictionary<int, CardData>();
        LoadAllCards();
        // foreach (var card in cardList)
        // {
        //     Debug.Log($"カード登録: ID={card.id}, 名前={card.cardName}");
        //     cardDict[card.id] = card;
        // }
    }
    // public void LoadAllCards()
    // {
    //     // ここでカードデータをResourcesフォルダなどから読み込む処理を実装可能
    //     CardData[] cards = Resources.LoadAll<CardData>("Data/Cards");
    //     foreach (var card in cards)
    //     {
    //         Debug.Log($"カード読み込み: ID={card.id}, 名前={card.cardName}");
    //         cardDict[card.id] = card;
    //     }
    // }

    public void AddJsonCard(CardData newCard)
    {
        var list = new List<CardData>();
        var dict = new Dictionary<int, CardData>();

        
        if (!cardDict.ContainsKey(newCard.id))
        {
            cardDict[newCard.id] = newCard;
            Debug.Log($"カード追加: ID={newCard.id}, 名前={newCard.cardName}");
        }
        else
        {
            Debug.LogWarning($"カードID {newCard.id} は既に存在しています。");
        }
    }



    // 以下を必要に応じてトレンドのカードデータを取得する。
    public List<CardData> GetAllCards()
    {
        return new List<CardData>(cardDict.Values);
    }

    public int LoadCardsCount()
    {
        return cardDict.Count;
    }

    public CardData GetById(int id)
    {
        Debug.Log("GetById: " + id);
        return cardDict.TryGetValue(id, out var card) ? card : null;
    }
    // 新規のカードデータのid一覧を取得するメソッド実際には上記のGetByIdを使う




    CardJson ConvertToJson(CardData card)
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
CardData ConvertToCardData(CardJson json)
{
    CardData card = ScriptableObject.CreateInstance<CardData>();

    card.id = json.id;
    card.cardName = json.cardName;
    card.cost = json.cost;
    card.level = json.level;
    card.power = json.power;
    card.hp = json.hp;

    // Sprite を Resources から復元
    if (!string.IsNullOrEmpty(json.imageName))
    {
        Debug.Log($"画像名が存在するため、カードID {json.id} の画像を読み込もうとしています: {json.imageName}");
        // card.imageName = json.imageName;
        card.imageName = Resources.Load<Sprite>($"Data/Cards/{json.imageName}");
    }
    else
    {
        Debug.LogWarning($"画像名が空のため、カードID {json.id} の画像を読み込めませんでした");
        card.imageName = null;
    }

    card.version = json.version;
    // card.sourceType = (CardData.CardSourceType)json.sourceType;
    card.sourceType = (CardSourceType)json.sourceType;
    card.color = (CardColor)json.color; // カードの色を追加


    return card;
}




CardMasterJson LoadOrCreateJson(string path)
{
    if (File.Exists(path))
    {
        string json = File.ReadAllText(path);
        return JsonUtility.FromJson<CardMasterJson>(json);
    }

    return new CardMasterJson();
}

void SaveJson(string path, CardMasterJson master)
{
    string json = JsonUtility.ToJson(master, true);
    File.WriteAllText(path, json);
}



public void LoadAllCards()
{
    Debug.Log("カードデータをロード中...");
    // JSONファイルのパス
    string path = Path.Combine(
        Application.persistentDataPath,
        "card_master.json"
    );

    // JSONを読み込む（なければ新規）
    CardMasterJson master = LoadOrCreateJson(path);

    // 既存JSONのID一覧（重複防止）
    HashSet<int> existingIds = new HashSet<int>();
    foreach (var c in master.cards)
    {
        existingIds.Add(c.id);
    }

    // ResourcesからCardDataを読み込む
    CardData[] cards = Resources.LoadAll<CardData>("Data/Cards");

    foreach (var card in cards)
    {
        Debug.Log($"カード読み込み: ID={card.id}, 名前={card.cardName}, 画像={card.imageName}, version={card.version}");

        // Runtime用Dictionary
        cardDict[card.id] = card;

        // JSONに未登録なら追加
        if (!existingIds.Contains(card.id))
        {
            master.cards.Add(ConvertToJson(card));
        }
    }

    // JSONに保存
    SaveJson(path, master);
}
CardMasterJson LoadJson()
{
    string path = Path.Combine(
        Application.persistentDataPath,
        "card_master.json"
    );

    if (!File.Exists(path))
    {
        Debug.LogError("JSONファイルが存在しません");
        return null;
    }

    string json = File.ReadAllText(path);
    return JsonUtility.FromJson<CardMasterJson>(json);
}

CardJson FindByName(string name)
{
    var master = LoadJson();
    if (master == null) return null;

    foreach (var card in master.cards)
    {
        if (card.cardName == name)
        {
             
            return card;
        }
    }
    return null;
}
public CardData FindById(int id)
{
    if (cardDict.TryGetValue(id, out var card))
        Debug.Log($"ID {id} のカードが見つかりました: 名前={card.cardName}, 画像={card.imageName}, version={card.version}");
        return card;
    Debug.LogWarning($"ID {id} のカードが存在しません");
    return null;
}

public List<CardData> FindByNameContains(string keyword)
{
    var result = new List<CardData>();
    var master = LoadJson();
    if (master == null) return result;

    foreach (var card in master.cards)
    {
        if (card.cardName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            CardData convertedCard = ConvertToCardData(card);
            result.Add(convertedCard);
            // cardDict[card.id] = convertedCard;
            Debug.Log($"色: {card.color}, $コンバート後カード検索: ID={card.id}, 名前={card.cardName}, 画像={card.imageName}, version={card.version},sourceType={card.sourceType}");
        }
    }
    Debug.Log($"検索後の全カードデータの数（検索関数内）: {cardDict.Count}");
    return result;
}

public List<CardData> IncludedCardsBySet(CardSetData set)
{
    var result = new List<CardData>();
    var master = LoadJson();
    if (master == null) return result;

    foreach (var card in master.cards)
    {
        // データをCardDataに変換してからセットの条件と比較する
        CardData convertedCard = ConvertToCardData(card);
        if (set.version == convertedCard.version && set.sourceType == convertedCard.sourceType)
        {
           
            result.Add(convertedCard);
            Debug.Log($"カードセット {set.setName} に含まれるカード: ID={card.id}, 名前={card.cardName}");
        }
    }
    return result;
}


CardJson FindByNameLinq(string name)
{
    var master = LoadJson();
    return master.cards.FirstOrDefault(c => c.cardName == name);
}

}
