using System.Collections.Generic;

/// <summary>
/// 敵攻撃時にこのユニットへ攻撃対象を強制する能力（ForceEnemyAttackTarget）のデータ判定。
/// </summary>
public static class CardForceEnemyAttackExtensions
{
    /// <summary>Permanent ForceEnemyAttackTarget と、ホスト側条件（timed.activationConditions）の組。</summary>
    public readonly struct ForceEnemyAttackAbility
    {
        public readonly EffectData Effect;
        public readonly IList<EffectActivationCondition> HostConditions;

        public ForceEnemyAttackAbility(EffectData effect, IList<EffectActivationCondition> hostConditions)
        {
            Effect = effect;
            HostConditions = hostConditions;
        }
    }

    /// <summary>カード定義の Permanent ForceEnemyAttackTarget を収集。</summary>
    public static void CollectPermanentForceEnemyAttackAbilities(
        CardData card,
        List<ForceEnemyAttackAbility> results)
    {
        if (card == null || results == null || card.timedEffects == null)
        {
            return;
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
                    && effect.type == EffectType.ForceEnemyAttackTarget
                    && effect.duration == EffectDuration.Permanent)
                {
                    results.Add(new ForceEnemyAttackAbility(effect, timed.activationConditions));
                }
            }
        }
    }

    /// <summary>ユニット本体＋搭乗パイロットの ForceEnemyAttackTarget を収集。</summary>
    public static void CollectForceEnemyAttackAbilities(
        CardController unit,
        List<ForceEnemyAttackAbility> results)
    {
        if (unit == null || results == null)
        {
            return;
        }

        if (unit.Data != null)
        {
            CollectPermanentForceEnemyAttackAbilities(unit.Data, results);
        }

        CardController pilot = unit.MountedPilot;
        if (pilot != null && pilot.Data != null)
        {
            CollectPermanentForceEnemyAttackAbilities(pilot.Data, results);
        }
    }

    /// <summary>フィールド上のユニットが ForceEnemyAttackTarget 定義を持つか（条件未評価）。</summary>
    public static bool HasForceEnemyAttackAbility(this CardController unit)
    {
        if (unit == null)
        {
            return false;
        }

        var scratch = new List<ForceEnemyAttackAbility>(2);
        CollectForceEnemyAttackAbilities(unit, scratch);
        return scratch.Count > 0;
    }
}
