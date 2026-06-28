using System.Collections.Generic;

/// <summary>
/// アクティブ（REST でない）敵ユニットを攻撃できる能力のデータ判定。
/// </summary>
public static class CardAttackActiveEnemyExtensions
{
    /// <summary>カード定義に常時（Permanent）のアクティブ攻撃マーカーがあるか。</summary>
    public static bool HasPermanentAttackActiveEnemyAbility(this CardData card)
    {
        if (card == null || card.timedEffects == null)
        {
            return false;
        }

        for (int i = 0; i < card.timedEffects.Count; i++)
        {
            TimedEffectData timed = card.timedEffects[i];
            if (timed == null || !timed.HasResolvedEffects())
            {
                continue;
            }

            IReadOnlyList<EffectData> resolved = timed.GetResolvedEffects();
            for (int j = 0; j < resolved.Count; j++)
            {
                EffectData effect = resolved[j];
                if (effect != null
                    && effect.type == EffectType.AttackActiveEnemyUnit
                    && effect.duration == EffectDuration.Permanent)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>フィールド上のユニット（搭乗パイロット含む）がアクティブ攻撃を持つか。</summary>
    public static bool HasAttackActiveEnemyAbility(this CardController unit)
    {
        if (unit == null)
        {
            return false;
        }

        if (unit.HasAttackActiveEnemyUntilEndOfTurnGrant
            || unit.HasAttackActiveEnemyUntilEndOfBattleGrant)
        {
            return true;
        }

        if (unit.Data != null && unit.Data.HasPermanentAttackActiveEnemyAbility())
        {
            return true;
        }

        CardController pilot = unit.MountedPilot;
        if (pilot == null)
        {
            return false;
        }

        return pilot.HasAttackActiveEnemyUntilEndOfTurnGrant
            || pilot.HasAttackActiveEnemyUntilEndOfBattleGrant
            || (pilot.Data != null && pilot.Data.HasPermanentAttackActiveEnemyAbility());
    }
}
