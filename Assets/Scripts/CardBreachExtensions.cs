using System.Collections.Generic;

/// <summary>突破（Breach）のデータ判定。複数ある場合は value を合算する。</summary>
public static class CardBreachExtensions
{
    /// <summary>カード定義の Breach 量（条件なしマーカーのみ。0 なら無し）。</summary>
    public static int GetBreachAmount(this CardData card)
    {
        return SumBreachFromCardData(card, runtimeUnit: null);
    }

    /// <summary>ユニット本体＋搭乗パイロットの Breach 合算（条件付き突破は実効 AP 等で判定）。</summary>
    public static int GetBreachAmount(this CardController unit)
    {
        if (unit == null)
        {
            return 0;
        }

        int total = SumBreachFromCardData(unit.Data, unit);
        if (unit.MountedPilot != null && unit.MountedPilot.Data != null)
        {
            total += SumBreachFromCardData(unit.MountedPilot.Data, unit);
        }

        if (unit.HasBreachUntilEndOfTurnGrant)
        {
            total += unit.BreachUntilEndOfTurnAmount;
        }

        if (unit.HasBreachUntilEndOfBattleGrant)
        {
            total += unit.BreachUntilEndOfBattleAmount;
        }

        return total;
    }

    private static int SumBreachFromCardData(CardData card, CardController runtimeUnit)
    {
        if (card == null || card.timedEffects == null)
        {
            return 0;
        }

        int total = 0;
        for (int i = 0; i < card.timedEffects.Count; i++)
        {
            TimedEffectData timed = card.timedEffects[i];
            if (timed == null || !timed.HasResolvedEffects())
            {
                continue;
            }

            if (timed.HasActivationConditions()
                && !MeetsBreachActivationConditions(timed, runtimeUnit))
            {
                continue;
            }

            IReadOnlyList<EffectData> resolved = timed.GetResolvedEffects();
            for (int j = 0; j < resolved.Count; j++)
            {
                EffectData effect = resolved[j];
                if (effect == null || effect.type != EffectType.Breach)
                {
                    continue;
                }

                int amount = effect.value > 0 ? effect.value : 0;
                if (amount > 0)
                {
                    total += amount;
                }
            }
        }

        return total;
    }

    /// <summary>
    /// 条件付き突破（例: AP5以上の間 Breach3）。
    /// ランタイムユニットが無いときは条件付きブロックを無視する。
    /// </summary>
    private static bool MeetsBreachActivationConditions(TimedEffectData timed, CardController runtimeUnit)
    {
        if (timed == null || !timed.HasActivationConditions())
        {
            return true;
        }

        if (runtimeUnit == null)
        {
            return false;
        }

        IReadOnlyList<EffectActivationCondition> conditions = timed.activationConditions;
        for (int i = 0; i < conditions.Count; i++)
        {
            EffectActivationCondition c = conditions[i];
            if (c == null || c.checkKind == EffectActivationCheckKind.Unset)
            {
                continue;
            }

            if (c.checkKind != EffectActivationCheckKind.SourceUnitStat)
            {
                return false;
            }

            EffectTargetUnitFilterStat stat = c.activationStatTarget == EffectTargetUnitFilterStat.Unset
                ? EffectTargetUnitFilterStat.AP
                : c.activationStatTarget;
            int statValue = EffectDataExtensions.GetTargetUnitFilterStatValue(runtimeUnit, stat);
            if (!EffectCompareHelper.Compare(statValue, c.compareValue, c.compareOp))
            {
                return false;
            }
        }

        return true;
    }
}
