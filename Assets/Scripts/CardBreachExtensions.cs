using System.Collections.Generic;

/// <summary>突破（Breach）のデータ判定。複数ある場合は value を合算する。</summary>
public static class CardBreachExtensions
{
    /// <summary>カード定義の Breach 量（effects / effectsName 合算。0 なら無し）。</summary>
    public static int GetBreachAmount(this CardData card)
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

    /// <summary>ユニット本体＋搭乗パイロットの Breach 合算。</summary>
    public static int GetBreachAmount(this CardController unit)
    {
        if (unit == null)
        {
            return 0;
        }

        int total = unit.Data != null ? unit.Data.GetBreachAmount() : 0;
        if (unit.MountedPilot != null && unit.MountedPilot.Data != null)
        {
            total += unit.MountedPilot.Data.GetBreachAmount();
        }

        return total;
    }
}
