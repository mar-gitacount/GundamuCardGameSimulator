using System.Collections.Generic;

/// <summary>
/// ブロッカー（敵攻撃の身代わり）のデータ判定。
/// </summary>
public static class CardBlockerExtensions
{
    /// <summary>ユニットとしてブロッカー能力を持つか（<see cref="CardData.isBlocker"/>）。</summary>
    public static bool IsBlockerUnit(this CardData card)
    {
        return card != null && card.IsUnitLike() && card.isBlocker;
    }

    /// <summary>
    /// 敵攻撃時にブロック可能か（ブロッカーデータ＋OnEnemyAttack の発動条件。REST は含まない）。
    /// </summary>
    public static bool IsBlockerEligible(this CardData card, EffectActivationContext ctx)
    {
        if (card == null)
        {
            return false;
        }

        if (card.IsBlockerUnit())
        {
            return MeetsBlockerActivationConditions(card, ctx);
        }

        return HasLegacyBlockRedirectOnEnemyAttack(card, ctx);
    }

    /// <summary>
    /// ブロッカーカードの OnEnemyAttack に発動条件がある場合のみ AND 判定。条件ブロックが無ければ常に true。
    /// </summary>
    public static bool MeetsBlockerActivationConditions(CardData card, EffectActivationContext ctx)
    {
        if (card == null || !card.IsBlockerUnit())
        {
            return false;
        }

        if (card.timedEffects == null || card.timedEffects.Count == 0)
        {
            return true;
        }

        bool anyConditionGate = false;
        for (int i = 0; i < card.timedEffects.Count; i++)
        {
            TimedEffectData timed = card.timedEffects[i];
            if (timed == null || timed.timing != EffectTiming.OnEnemyAttack || !timed.HasActivationConditions())
            {
                continue;
            }

            anyConditionGate = true;
            if (!EffectActivationEvaluator.AreTimedConditionsMet(timed, ctx))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>旧データ互換: OnEnemyAttack + BlockRedirect 効果。</summary>
    public static bool HasLegacyBlockRedirectOnEnemyAttack(CardData data, EffectActivationContext ctx)
    {
        if (data == null || data.timedEffects == null)
        {
            return false;
        }

        for (int i = 0; i < data.timedEffects.Count; i++)
        {
            TimedEffectData timed = data.timedEffects[i];
            if (timed == null || timed.timing != EffectTiming.OnEnemyAttack || !timed.HasResolvedEffects())
            {
                continue;
            }

            if (ctx != null && !EffectActivationEvaluator.AreTimedConditionsMet(timed, ctx))
            {
                continue;
            }

            IReadOnlyList<EffectData> resolvedEffects = timed.GetResolvedEffects();
            for (int j = 0; j < resolvedEffects.Count; j++)
            {
                EffectData effect = resolvedEffects[j];
                if (effect != null && effect.type == EffectType.BlockRedirect)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
