using System.Collections.Generic;
using UnityEngine;

/// <summary>TestPlay 開始前に UI へ載せるデッキ選択スナップショット。</summary>
public sealed class TestPlayDeckPick
{
    public string StorageKey;
    public string Title;
    public int ThumbnailId;
    public Sprite Thumbnail;
    public string DateLine;
    public int TotalCount;
    public Dictionary<int, int> Cards = new Dictionary<int, int>();

    public static TestPlayDeckPick FromSaveData(DeckSaveData data, DeckStorageEntry entry, string storageKey)
    {
        TestPlayDeckPick pick = new TestPlayDeckPick();
        pick.StorageKey = storageKey ?? entry.StorageKey;
        pick.Title = data != null && !string.IsNullOrEmpty(data.title)
            ? data.title
            : entry.DisplayName;
        pick.Cards = CopyCards(data);
        pick.ThumbnailId = DeckStorageService.ResolveThumbnailId(pick.Cards, data != null ? data.thumbnailId : 0);
        pick.Thumbnail = DeckSettinObject.ResolveDeckCardSprite(pick.ThumbnailId);
        pick.TotalCount = CountCards(pick.Cards);

        System.DateTime stamp = entry.LastWriteTime;
        if (stamp == System.DateTime.MinValue && data != null && data.updatedAtUnix > 0)
        {
            stamp = System.DateTimeOffset.FromUnixTimeSeconds(data.updatedAtUnix).LocalDateTime;
        }

        pick.DateLine = stamp == System.DateTime.MinValue
            ? string.Empty
            : GameLocale.IsEnglish
                ? stamp.ToString("MMM dd, yyyy", System.Globalization.CultureInfo.GetCultureInfo("en-US"))
                : stamp.ToString("yyyy年MM月dd日");
        return pick;
    }

    public static Dictionary<int, int> CopyCards(DeckSaveData data)
    {
        Dictionary<int, int> map = new Dictionary<int, int>();
        if (data == null || data.cards == null)
        {
            return map;
        }

        for (int i = 0; i < data.cards.Count; i++)
        {
            CardSlot slot = data.cards[i];
            if (slot == null || slot.id <= 0 || slot.count <= 0)
            {
                continue;
            }

            map[slot.id] = slot.count;
        }

        return map;
    }

    public static Dictionary<int, int> CopyCards(Dictionary<int, int> source)
    {
        Dictionary<int, int> map = new Dictionary<int, int>();
        if (source == null)
        {
            return map;
        }

        foreach (KeyValuePair<int, int> pair in source)
        {
            if (pair.Key > 0 && pair.Value > 0)
            {
                map[pair.Key] = pair.Value;
            }
        }

        return map;
    }

    public static int CountCards(Dictionary<int, int> cards)
    {
        int total = 0;
        if (cards == null)
        {
            return 0;
        }

        foreach (KeyValuePair<int, int> pair in cards)
        {
            if (pair.Value > 0)
            {
                total += pair.Value;
            }
        }

        return total;
    }
}
