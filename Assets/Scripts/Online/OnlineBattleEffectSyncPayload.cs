using System;
using UnityEngine;

/// <summary>
/// 効果適用（ダメージ・バフ・デバフ等）の P2P 同期 payload。
/// </summary>
[Serializable]
public class OnlineBattleUnitEffectChange
{
    public int targetInstanceId;
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
    /// <summary>攻撃フロー終了時の UntilEndOfBattle 等の一括解除（全盤面ユニット）。</summary>
    public const string ChangeKindClearTimedStatModifiersByDuration = "ClearTimedStatModifiersByDuration";

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
