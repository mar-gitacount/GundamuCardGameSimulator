using System.Collections.Generic;

/// <summary>
/// 敵ユニットがこのユニットをアタック先に選べない能力（CannotBeChosenAsAttackTarget）のデータ判定。
/// </summary>
public static class CardCannotBeChosenAsAttackExtensions
{
    /// <summary>Permanent CannotBeChosenAsAttackTarget と、ホスト側条件（timed.activationConditions）の組。</summary>
    public readonly struct CannotBeChosenAsAttackAbility
    {
        public readonly EffectData Effect;
        public readonly IList<EffectActivationCondition> HostConditions;

        public CannotBeChosenAsAttackAbility(EffectData effect, IList<EffectActivationCondition> hostConditions)
        {
            Effect = effect;
            HostConditions = hostConditions;
        }
    }

    /// <summary>カード定義の Permanent CannotBeChosenAsAttackTarget を収集。</summary>
    public static void CollectPermanentCannotBeChosenAsAttackAbilities(
        CardData card,
        List<CannotBeChosenAsAttackAbility> results)
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
                    && effect.type == EffectType.CannotBeChosenAsAttackTarget
                    && effect.duration == EffectDuration.Permanent)
                {
                    results.Add(new CannotBeChosenAsAttackAbility(effect, timed.activationConditions));
                }
            }
        }
    }

    /// <summary>ユニット本体＋搭乗パイロットの CannotBeChosenAsAttackTarget を収集。</summary>
    public static void CollectCannotBeChosenAsAttackAbilities(
        CardController unit,
        List<CannotBeChosenAsAttackAbility> results)
    {
        if (unit == null || results == null)
        {
            return;
        }

        if (unit.Data != null)
        {
            CollectPermanentCannotBeChosenAsAttackAbilities(unit.Data, results);
        }

        CardController pilot = unit.MountedPilot;
        if (pilot != null && pilot.Data != null)
        {
            CollectPermanentCannotBeChosenAsAttackAbilities(pilot.Data, results);
        }
    }
}
