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
    /// パイロットが搭乗ホストへ《ブロッカー》を付与する定義か（パイロットの isBlocker）。
    /// </summary>
    public static bool IsBlockerGrantingPilot(this CardData pilot)
    {
        return pilot != null && pilot.IsPilot() && pilot.isBlocker;
    }

    /// <summary>
    /// ユニット本体または搭乗パイロット由来でブロック可能か（REST は含まない）。
    /// </summary>
    public static bool IsBlockerEligible(this CardController unit, EffectActivationContext ctx)
    {
        if (unit == null || unit.Data == null)
        {
            return false;
        }

        if (unit.Data.IsBlockerEligible(ctx))
        {
            return true;
        }

        return IsPilotGrantedBlockerEligible(unit.MountedPilot != null ? unit.MountedPilot.Data : null, ctx);
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
    /// 搭乗パイロットの isBlocker＋OnEnemyAttack 条件で、ホストがブロッカーを得られるか。
    /// ctx.SourceCard はホストユニットを想定（「このユニットが〔特徴〕」はホスト側で判定）。
    /// </summary>
    public static bool IsPilotGrantedBlockerEligible(CardData pilot, EffectActivationContext hostCtx)
    {
        if (!pilot.IsBlockerGrantingPilot())
        {
            return false;
        }

        return MeetsBlockerActivationConditionsForGrant(pilot, hostCtx);
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

        return MeetsBlockerActivationConditionsForGrant(card, ctx);
    }

    /// <summary>ユニット／パイロット共通の OnEnemyAttack 条件ゲート。</summary>
    private static bool MeetsBlockerActivationConditionsForGrant(CardData card, EffectActivationContext ctx)
    {
        if (card == null)
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
