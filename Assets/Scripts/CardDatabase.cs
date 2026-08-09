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

        CardFeatureRegistry.EnsureLoaded();
        NamedEffectSetRegistry.EnsureLoaded();
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
        color = (int)card.color, // カードの色を追加
        type = (int)card.type,
        featureIds = CardFeatureRegistry.CollectIds(card.features),
        isBlocker = card.isBlocker,
        isDeployTurnAttack = card.isDeployTurnAttack,
        isNotDirectAttack = card.isNotDirectAttack,
        isShieldToken = card.isShieldToken,
        isRepair = card.isRepair,
        repairAmount = card.repairAmount,
        notUsedOnline = card.notUsedOnline,
        cannotMountPilot = card.cannotMountPilot,
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

    // Sprite を Resources から復元（アセット参照がある場合はそれを優先）
    if (cardDict != null && cardDict.TryGetValue(json.id, out CardData assetCard) && assetCard != null
        && assetCard.imageName != null)
    {
        card.imageName = assetCard.imageName;
    }
    else if (!string.IsNullOrEmpty(json.imageName))
    {
        Debug.Log($"画像名が存在するため、カードID {json.id} の画像を読み込もうとしています: {json.imageName}");
        Sprite sprite = Resources.Load<Sprite>($"Data/Images/{json.imageName}");
        if (sprite == null)
        {
            sprite = Resources.Load<Sprite>($"Data/Cards/{json.imageName}");
        }

        card.imageName = sprite;
        if (sprite == null)
        {
            Debug.LogWarning($"カードID {json.id} の Sprite が見つかりません: {json.imageName}");
        }
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
    card.type = (Type)json.type;
    card.isBlocker = json.isBlocker;
    card.isDeployTurnAttack = json.isDeployTurnAttack;
    card.isNotDirectAttack = json.isNotDirectAttack;
    card.isShieldToken = json.isShieldToken;
    card.isRepair = json.isRepair;
    card.repairAmount = json.repairAmount;
    card.notUsedOnline = json.notUsedOnline;
    card.cannotMountPilot = json.cannotMountPilot;
    card.SetFeaturesFromIds(json.featureIds);

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
    {
        Debug.Log($"ID {id} のカードが見つかりました: 名前={card.cardName}, 画像={card.imageName}, version={card.version}");
        return card;
    }
    Debug.LogWarning($"ID {id} のカードが存在しません");
    return null;
}

    public List<CardData> FindByNameContains(string keyword)
    {
        var result = new List<CardData>();
        // JSONから CreateInstance すると imageName(Sprite) が空になる。
        // 検索表示は Resources 上の CardData（Sprite 参照付き）を使う。
        string key = keyword ?? string.Empty;
        foreach (CardData card in cardDict.Values)
        {
            if (card == null || string.IsNullOrEmpty(card.cardName))
            {
                continue;
            }

            if (card.cardName.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                result.Add(card);
                Debug.Log(
                    $"検索ヒット: ID={card.id}, 名前={card.cardName}, 画像={(card.imageName != null ? card.imageName.name : "null")}");
            }
        }

        result.Sort((a, b) => a.id.CompareTo(b.id));
        Debug.Log($"検索結果数: {result.Count} / cardDict:{cardDict.Count}");
        return result;
    }

    public List<CardData> FindByColor(CardColor color)
    {
        var result = new List<CardData>();
        foreach (CardData card in cardDict.Values)
        {
            if (card != null && card.color == color)
            {
                result.Add(card);
            }
        }

        result.Sort((a, b) => a.id.CompareTo(b.id));
        return result;
    }

    public List<CardData> FindByColors(IEnumerable<CardColor> colors)
    {
        var result = new List<CardData>();
        if (colors == null)
        {
            return result;
        }

        HashSet<CardColor> colorSet = new HashSet<CardColor>(colors);
        if (colorSet.Count == 0)
        {
            return result;
        }

        foreach (CardData card in cardDict.Values)
        {
            if (card != null && colorSet.Contains(card.color))
            {
                result.Add(card);
            }
        }

        result.Sort((a, b) =>
        {
            int colorCompare = a.color.CompareTo(b.color);
            return colorCompare != 0 ? colorCompare : a.id.CompareTo(b.id);
        });
        return result;
    }

public List<CardData> IncludedCardsBySet(CardSetData set)
{
    var result = new List<CardData>();
    if (set == null)
    {
        return result;
    }

    foreach (CardData card in cardDict.Values)
    {
        if (card == null)
        {
            continue;
        }

        if (set.version == card.version && set.sourceType == card.sourceType)
        {
            result.Add(card);
            Debug.Log($"カードセット {set.setName} に含まれるカード: ID={card.id}, 名前={card.cardName}");
        }
    }

    result.Sort((a, b) => a.id.CompareTo(b.id));
    return result;
}


CardJson FindByNameLinq(string name)
{
    var master = LoadJson();
    return master.cards.FirstOrDefault(c => c.cardName == name);
}

}
