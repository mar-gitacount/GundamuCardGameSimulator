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
        CardPilotIdRegistry.EnsureLoaded();
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
        productLine = (int)card.productLine,
        boosterSet = (int)card.boosterSet,
        starterSet = (int)card.starterSet,
        eternalBoosterSet = (int)card.eternalBoosterSet,
        sourceTitle = (int)card.sourceTitle,
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
        notUsedOnline = card.notUsedOnline,
        cannotMountPilot = card.cannotMountPilot,
    };
}
CardData ConvertToCardData(CardJson json)
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

    // 画像は Addressables アドレスのみ（Sprite 直参照・Resources.Load しない）
    if (cardDict != null && cardDict.TryGetValue(json.id, out CardData assetCard) && assetCard != null
        && !string.IsNullOrWhiteSpace(assetCard.imageAddress))
    {
        card.imageAddress = assetCard.imageAddress.Trim();
    }
    else if (!string.IsNullOrEmpty(json.imageName))
    {
        card.SetImageAddressFromLeaf(json.imageName.Trim());
    }
    else
    {
        card.imageAddress = string.Empty;
        Debug.LogWarning($"画像名が空のため、カードID {json.id} の imageAddress を設定できませんでした");
    }

    card.imageName = null;
    card.image = null;

    card.version = json.version;
    // card.sourceType = (CardData.CardSourceType)json.sourceType;
    card.sourceType = (CardSourceType)json.sourceType;
    card.productLine = (CardProductLine)json.productLine;
    card.boosterSet = (BoosterProductSet)json.boosterSet;
    card.starterSet = (StarterProductSet)json.starterSet;
    card.eternalBoosterSet = (EternalBoosterProductSet)json.eternalBoosterSet;
    card.sourceTitle = (CardSourceTitle)json.sourceTitle;
    card.color = (CardColor)json.color; // カードの色を追加
    card.type = (Type)json.type;
    card.battleZones = (CardBattleZone)json.battleZones;
    card.isBlocker = json.isBlocker;
    card.isDeployTurnAttack = json.isDeployTurnAttack;
    card.isNotDirectAttack = json.isNotDirectAttack;
    card.isShieldToken = json.isShieldToken;
    card.isRepair = json.isRepair;
    card.repairAmount = json.repairAmount;
    card.notUsedOnline = json.notUsedOnline;
    card.cannotMountPilot = json.cannotMountPilot;
    card.SetFeaturesFromIds(json.featureIds);
    card.SetPilotIdsFromIds(json.pilotIdIds);

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
        return card;
    }
    Debug.LogWarning($"ID {id} のカードが存在しません");
    return null;
}

    /// <summary>
    /// カード名・公式ID（GD01-003 等）・色キーワード（blue / 青 等）で検索する。
    /// 複数トークンは AND（例: "GD01 blue" → GD01 かつ青）。
    /// </summary>
    public List<CardData> SearchCards(string keyword)
    {
        var result = new List<CardData>();
        string raw = keyword ?? string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            foreach (CardData card in cardDict.Values)
            {
                if (card != null)
                {
                    result.Add(card);
                }
            }

            result.Sort((a, b) => a.id.CompareTo(b.id));
            return result;
        }

        ParseSearchKeyword(raw, out List<CardColor> colors, out List<string> textTokens);

        foreach (CardData card in cardDict.Values)
        {
            if (card == null)
            {
                continue;
            }

            if (colors.Count > 0 && !SearchColorsContain(card.color, colors))
            {
                continue;
            }

            if (textTokens.Count == 0)
            {
                result.Add(card);
                continue;
            }

            bool allTokensMatch = true;
            for (int i = 0; i < textTokens.Count; i++)
            {
                if (!CardMatchesTextToken(card, textTokens[i]))
                {
                    allTokensMatch = false;
                    break;
                }
            }

            if (allTokensMatch)
            {
                result.Add(card);
            }
        }

        result.Sort((a, b) => a.id.CompareTo(b.id));
        Debug.Log($"検索結果数: {result.Count} / cardDict:{cardDict.Count}");
        return result;
    }

    public List<CardData> FindByNameContains(string keyword)
    {
        return SearchCards(keyword);
    }

    private static bool SearchColorsContain(CardColor cardColor, List<CardColor> colors)
    {
        for (int i = 0; i < colors.Count; i++)
        {
            if (cardColor == colors[i])
            {
                return true;
            }
        }

        return false;
    }

    private static bool CardMatchesTextToken(CardData card, string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(card.cardName)
            && card.cardName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(card.gcgOfficialId)
            && card.gcgOfficialId.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        return false;
    }

    private static void ParseSearchKeyword(string keyword, out List<CardColor> colors, out List<string> textTokens)
    {
        colors = new List<CardColor>();
        textTokens = new List<string>();
        string[] parts = keyword.Split(
            new[] { ' ', '\u3000', '\t' },
            StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i].Trim();
            if (part.Length == 0)
            {
                continue;
            }

            if (TryParseColorKeyword(part, out CardColor color))
            {
                if (!colors.Contains(color))
                {
                    colors.Add(color);
                }

                continue;
            }

            textTokens.Add(part);
        }
    }

    private static bool TryParseColorKeyword(string token, out CardColor color)
    {
        color = CardColor.Red;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        switch (token.Trim().ToLowerInvariant())
        {
            case "red":
            case "赤":
                color = CardColor.Red;
                return true;
            case "green":
            case "緑":
                color = CardColor.Green;
                return true;
            case "blue":
            case "青":
                color = CardColor.Blue;
                return true;
            case "yellow":
            case "黄":
                color = CardColor.Yellow;
                return true;
            case "white":
            case "白":
                color = CardColor.White;
                return true;
            case "purple":
            case "紫":
                color = CardColor.Purple;
                return true;
            case "colorless":
            case "無色":
                color = CardColor.Colorless;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// 公式 GCG ID が一致するカードをすべて返す（レア違いの同カードが複数ヒットする想定）。
    /// </summary>
    public List<CardData> FindByGcgOfficialId(string gcgOfficialId)
    {
        var result = new List<CardData>();
        if (string.IsNullOrWhiteSpace(gcgOfficialId))
        {
            return result;
        }

        string key = gcgOfficialId.Trim();
        foreach (CardData card in cardDict.Values)
        {
            if (card == null || string.IsNullOrWhiteSpace(card.gcgOfficialId))
            {
                continue;
            }

            if (string.Equals(card.gcgOfficialId.Trim(), key, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(card);
            }
        }

        result.Sort((a, b) => a.id.CompareTo(b.id));
        return result;
    }

    /// <summary>
    /// 公式 GCG ID の部分一致（含有）検索。
    /// </summary>
    public List<CardData> FindByGcgOfficialIdContains(string keyword)
    {
        var result = new List<CardData>();
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return result;
        }

        string key = keyword.Trim();
        foreach (CardData card in cardDict.Values)
        {
            if (card == null || string.IsNullOrWhiteSpace(card.gcgOfficialId))
            {
                continue;
            }

            if (card.gcgOfficialId.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                result.Add(card);
            }
        }

        result.Sort((a, b) => a.id.CompareTo(b.id));
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
