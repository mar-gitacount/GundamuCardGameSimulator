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
}

[Serializable]
public class OnlineBattleEffectSyncPayload
{
    public OnlineBattleUnitEffectChange[] unitChanges;

    public const string ChangeKindDamage = "Damage";
    public const string ChangeKindStat = "Stat";
    public const string ChangeKindRest = "Rest";
    public const string ChangeKindDestroy = "Destroy";

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
