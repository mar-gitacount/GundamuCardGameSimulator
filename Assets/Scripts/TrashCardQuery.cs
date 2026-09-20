using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 墓地（トラッシュ）内のカード探索。効果条件・対象緩和など複数カードから再利用する。
/// </summary>
public static class TrashCardQuery
{
    /// <summary>トラッシュ内の指定カード ID の枚数。</summary>
    public static int CountByCardId(IReadOnlyList<int> trashCardIds, int cardId)
    {
        if (trashCardIds == null || trashCardIds.Count == 0 || cardId <= 0)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < trashCardIds.Count; i++)
        {
            if (trashCardIds[i] == cardId)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>指定 ID が minimumCount 枚以上あるか。</summary>
    public static bool HasAtLeast(IReadOnlyList<int> trashCardIds, int cardId, int minimumCount)
    {
        int need = Mathf.Max(1, minimumCount);
        return CountByCardId(trashCardIds, cardId) >= need;
    }

    /// <summary>指定 ID が minimumCount 枚未満か（0 枚含む）。</summary>
    public static bool HasFewerThan(IReadOnlyList<int> trashCardIds, int cardId, int minimumCount)
    {
        int need = Mathf.Max(1, minimumCount);
        return CountByCardId(trashCardIds, cardId) < need;
    }

    /// <summary>
    /// 指定カード種類の枚数。
    /// Command / Pilot / Unit 指定時は兼用タイプ（CommandPilot / UnitToken）も含める。
    /// </summary>
    public static int CountByCardType(IReadOnlyList<int> trashCardIds, Type cardType)
    {
        if (trashCardIds == null || trashCardIds.Count == 0)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < trashCardIds.Count; i++)
        {
            CardData data = ResolveCardData(trashCardIds[i]);
            if (data == null)
            {
                continue;
            }

            if (cardType == Type.Command)
            {
                if (data.IsCommand())
                {
                    count++;
                }

                continue;
            }

            if (CardTypeExtensions.MatchesTypeFilter(cardType, data.type))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>トラッシュ内のコマンド（Command / CommandPilot）枚数。</summary>
    public static int CountCommands(IReadOnlyList<int> trashCardIds)
    {
        int count = CountByCardType(trashCardIds, Type.Command);
        // トラッシュに ID があるのに 0 件ならキャッシュ再構築して再集計
        if (count == 0 && trashCardIds != null && trashCardIds.Count > 0)
        {
            InvalidateCardDataCache();
            count = CountByCardType(trashCardIds, Type.Command);
        }

        return count;
    }

    /// <summary>トラッシュ ID から CardData を解決（Resources キャッシュ → CardDatabase → DeckSettin）。</summary>
    private static Dictionary<int, CardData> _cardDataByIdCache;

    private static CardData ResolveCardData(int cardId)
    {
        if (cardId <= 0)
        {
            return null;
        }

        EnsureCardDataCache();
        if (_cardDataByIdCache != null && _cardDataByIdCache.TryGetValue(cardId, out CardData cached))
        {
            return cached;
        }

        if (CardDatabase.Instance != null)
        {
            CardData fromDb = CardDatabase.Instance.GetById(cardId);
            if (fromDb != null)
            {
                return fromDb;
            }
        }

        if (DeckSettinObject.Instance != null)
        {
            return DeckSettinObject.Instance.GetCardDataById(cardId);
        }

        return null;
    }

    private static void EnsureCardDataCache()
    {
        // 初回が早すぎて LoadAll が空だった場合に備え、空キャッシュは作り直す
        if (_cardDataByIdCache != null && _cardDataByIdCache.Count > 0)
        {
            return;
        }

        _cardDataByIdCache = new Dictionary<int, CardData>();
        CardData[] all = Resources.LoadAll<CardData>("Data/Cards");
        if (all == null || all.Length == 0)
        {
            return;
        }

        for (int i = 0; i < all.Length; i++)
        {
            CardData data = all[i];
            if (data == null || data.id <= 0 || _cardDataByIdCache.ContainsKey(data.id))
            {
                continue;
            }

            _cardDataByIdCache[data.id] = data;
        }
    }

    /// <summary>キャッシュを破棄（空のまま固まった場合の再構築用）。</summary>
    public static void InvalidateCardDataCache()
    {
        _cardDataByIdCache = null;
    }

    /// <summary>指定種類が minimumCount 枚以上あるか。</summary>
    public static bool HasCardTypeAtLeast(IReadOnlyList<int> trashCardIds, Type cardType, int minimumCount)
    {
        int need = Mathf.Max(1, minimumCount);
        return CountByCardType(trashCardIds, cardType) >= need;
    }

    /// <summary>指定色の枚数。</summary>
    public static int CountByColor(IReadOnlyList<int> trashCardIds, CardColor color)
    {
        if (trashCardIds == null || trashCardIds.Count == 0 || DeckSettinObject.Instance == null)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < trashCardIds.Count; i++)
        {
            CardData data = DeckSettinObject.Instance.GetCardDataById(trashCardIds[i]);
            if (data != null && data.color == color)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>指定色が minimumCount 枚以上あるか。</summary>
    public static bool HasColorAtLeast(IReadOnlyList<int> trashCardIds, CardColor color, int minimumCount)
    {
        int need = Mathf.Max(1, minimumCount);
        return CountByColor(trashCardIds, color) >= need;
    }

    /// <summary>指定 Feature（OR）のいずれかを持つカード枚数。</summary>
    public static int CountByAnyFeature(IReadOnlyList<int> trashCardIds, IReadOnlyList<CardFeatureData> features)
    {
        if (trashCardIds == null || trashCardIds.Count == 0
            || features == null || features.Count == 0
            || DeckSettinObject.Instance == null)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < trashCardIds.Count; i++)
        {
            CardData data = DeckSettinObject.Instance.GetCardDataById(trashCardIds[i]);
            if (data != null && data.HasAnyFeature(features))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>指定 Feature（OR）を持つカードが minimumCount 枚以上あるか。</summary>
    public static bool HasAnyFeatureAtLeast(
        IReadOnlyList<int> trashCardIds,
        IReadOnlyList<CardFeatureData> features,
        int minimumCount)
    {
        int need = Mathf.Max(1, minimumCount);
        return CountByAnyFeature(trashCardIds, features) >= need;
    }

    /// <summary>カード名に指定文字列を含む枚数（部分一致・大小無視）。</summary>
    public static int CountByCardNameContains(IReadOnlyList<int> trashCardIds, string nameContains)
    {
        if (trashCardIds == null || trashCardIds.Count == 0
            || string.IsNullOrWhiteSpace(nameContains)
            || DeckSettinObject.Instance == null)
        {
            return 0;
        }

        string needle = nameContains.Trim();
        int count = 0;
        for (int i = 0; i < trashCardIds.Count; i++)
        {
            CardData data = DeckSettinObject.Instance.GetCardDataById(trashCardIds[i]);
            if (data != null && CardNameContainsMatcher.Matches(data.cardName, needle))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>カード名部分一致が minimumCount 枚以上あるか。</summary>
    public static bool HasCardNameContainsAtLeast(
        IReadOnlyList<int> trashCardIds,
        string nameContains,
        int minimumCount)
    {
        int need = Mathf.Max(1, minimumCount);
        return CountByCardNameContains(trashCardIds, nameContains) >= need;
    }

    /// <summary>
    /// ルール側トラッシュから ID 枚数を数える。
    /// </summary>
    public static int CountByCardId(CardGameRule rule, int cardId)
    {
        if (rule == null)
        {
            return 0;
        }

        return CountByCardId(rule.GetTrashCardIds(), cardId);
    }

    /// <summary>ルール側トラッシュに指定 ID が minimumCount 枚以上あるか。</summary>
    public static bool HasAtLeast(CardGameRule rule, int cardId, int minimumCount)
    {
        return HasAtLeast(rule != null ? rule.GetTrashCardIds() : null, cardId, minimumCount);
    }
}
