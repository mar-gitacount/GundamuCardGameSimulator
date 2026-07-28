using System;
using UnityEngine;

/// <summary>
/// 効果適用（ダメージ・バフ・デバフ等）の P2P 同期 payload。
/// </summary>
[Serializable]
public class OnlineBattleUnitEffectChange
{
    public int targetInstanceId;
    /// <summary>送信側視点のバトルゾーン（0=Player, 1=Enemy）。受信側は反転して検索する。</summary>
    public int targetZoneOwnerSide;
    /// <summary>BattleInstanceId が使えない／ずれた時のフォールバック用カード ID。</summary>
    public int targetCardId = -1;
    /// <summary>送信側視点のバトルゾーン内インデックス。-1 は未指定。</summary>
    public int targetZoneIndex = -1;
    /// <summary>Damage / Stat / Rest / Destroy</summary>
    public string changeKind;
    public int hpAfter;
    public int signedStatValue;
    public int statTarget;
    public int duration;
    /// <summary>Buff/Debuff の除去用。付与時に設定し、リモートでも同じ sourceKey で保持する。</summary>
    public string statModifierSourceKey;
    /// <summary>ClearStatGrantsFromSource 用。付与元ユニットの BattleInstanceId。</summary>
    public int grantSourceInstanceId;
    /// <summary>ClearStatGrantsFromSource 用。送信側視点の付与元ゾーン（0=Player, 1=Enemy）。</summary>
    public int grantSourceZoneOwnerSide;
    /// <summary>
    /// 相手所有カードの破壊時効果を相手クライアントで解決するための要求 ID。
    /// 0 は破壊時効果の完了待ちなし。
    /// </summary>
    public int onDestroyedRequestId;
    /// <summary>破壊元ユニットの BattleInstanceId（0=不明）。受信側 OnDestroyed 条件用。</summary>
    public int destroyerInstanceId;
}

/// <summary>
/// OnDestroyedComplete（破壊時効果の解決完了通知）の payload。
/// 所有者側 Look で手札回収した後の山札残数を破壊側ミラーへ反映する。
/// </summary>
[Serializable]
public class OnlineOnDestroyedCompletePayload
{
    public int requestId;
    /// <summary>解決完了時点の所有者（送信側視点 Player）山札残数。-1 は未指定。</summary>
    public int ownerDeckRemainCount = -1;
    /// <summary>OnDestroyed で自身を手札へ戻した場合のカード ID。-1 はなし。</summary>
    public int returnedToHandCardId = -1;

    public static string ToJson(int requestId, int ownerDeckRemainCount, int returnedToHandCardId = -1)
    {
        return JsonUtility.ToJson(new OnlineOnDestroyedCompletePayload
        {
            requestId = requestId,
            ownerDeckRemainCount = ownerDeckRemainCount,
            returnedToHandCardId = returnedToHandCardId
        });
    }

    public static bool TryParse(string raw, out OnlineOnDestroyedCompletePayload payload)
    {
        payload = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        try
        {
            payload = JsonUtility.FromJson<OnlineOnDestroyedCompletePayload>(raw);
            return payload != null && payload.requestId > 0;
        }
        catch
        {
            return false;
        }
    }
}

[Serializable]
public class OnlineBattleEffectSyncPayload
{
    public OnlineBattleUnitEffectChange[] unitChanges;

    public const string ChangeKindDamage = "Damage";
    public const string ChangeKindRepair = "Repair";
    public const string ChangeKindStat = "Stat";
    public const string ChangeKindRest = "Rest";
    public const string ChangeKindActivate = "Activate";
    public const string ChangeKindDestroy = "Destroy";
    public const string ChangeKindBounce = "Bounce";
    public const string ChangeKindReturnToDeckBottom = "ReturnToDeckBottom";
    public const string ChangeKindClearStatGrantsFromSource = "ClearStatGrantsFromSource";
    public const string ChangeKindRefreshOwnerTurnFieldPassives = "RefreshOwnerTurnFieldPassives";

    public static string ToJson(OnlineBattleUnitEffectChange[] changes)
    {
        if (changes == null || changes.Length == 0)
        {
            return null;
        }

        return JsonUtility.ToJson(new OnlineBattleEffectSyncPayload { unitChanges = changes });
    }

    public static bool TryParse(string raw, out OnlineBattleEffectSyncPayload payload)
    {
        payload = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        try
        {
            payload = JsonUtility.FromJson<OnlineBattleEffectSyncPayload>(raw);
            return payload != null && payload.unitChanges != null && payload.unitChanges.Length > 0;
        }
        catch
        {
            return false;
        }
    }
}
