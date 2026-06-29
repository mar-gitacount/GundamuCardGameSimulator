using System;
using UnityEngine;

/// <summary>
/// トラッシュ／除外ゾーン／山札残数の P2P 同期 payload。
/// zoneOwnerSide は送信側クライアント上のゾーン（0=Player, 1=Enemy）。
/// 受信側では相手視点に反転して適用する。
/// </summary>
[Serializable]
public class OnlineBattleZoneSyncPayload
{
    public string mutation;
    /// <summary>送信側の PlayerType（0=Player, 1=Enemy）。</summary>
    public int zoneOwnerSide;
    public int[] cardIds;
    /// <summary>山札操作時の残枚数。-1 のとき変更なし。</summary>
    public int deckRemainCount;

    public const string DeckToTrash = "DeckToTrash";
    public const string DeckToExile = "DeckToExile";
    public const string TrashToExile = "TrashToExile";
    public const string AddTrash = "AddTrash";

    public static string ToJson(
        string mutation,
        int zoneOwnerSide,
        int[] cardIds,
        int deckRemainCount = -1)
    {
        if (string.IsNullOrWhiteSpace(mutation))
        {
            return null;
        }

        return JsonUtility.ToJson(new OnlineBattleZoneSyncPayload
        {
            mutation = mutation,
            zoneOwnerSide = zoneOwnerSide,
            cardIds = cardIds,
            deckRemainCount = deckRemainCount
        });
    }

    public static string ToJsonSingleCard(string mutation, int zoneOwnerSide, int cardId)
    {
        return ToJson(mutation, zoneOwnerSide, new[] { cardId });
    }

    public static bool TryParse(string raw, out OnlineBattleZoneSyncPayload payload)
    {
        payload = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        try
        {
            payload = JsonUtility.FromJson<OnlineBattleZoneSyncPayload>(raw);
            return payload != null && !string.IsNullOrWhiteSpace(payload.mutation);
        }
        catch
        {
            return false;
        }
    }
}
