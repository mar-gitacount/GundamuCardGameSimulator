using System.Collections.Generic;

/// <summary>
/// 高機動（ブロック無視）のデータ判定。
/// </summary>
public static class CardHighMobilityExtensions
{
    /// <summary>
    /// カード定義の常時《高機動》（条件なし Permanent）。
    /// 搭乗条件付き（シナジュ等）は CardController 側で評価する。
    /// </summary>
    public static bool HasHighMobilityAbility(this CardData card)
    {
        return HasPermanentHighMobility(card, runtimeUnit: null);
    }

    /// <summary>フィールド上のユニットが高機動を持つか（印刷効果・条件付きマーカー・ターン限定付与）。</summary>
    public static bool HasHighMobilityAbility(this CardController unit)
    {
        if (unit == null)
        {
            return false;
        }

        if (unit.HasHighMobilityUntilEndOfTurnGrant)
        {
            return true;
        }

        if (HasPermanentHighMobility(unit.Data, unit))
        {
            return true;
        }

        CardController pilot = unit.MountedPilot;
        return pilot?.Data != null && HasPermanentHighMobility(pilot.Data, unit);
    }

    private static bool HasPermanentHighMobility(CardData card, CardController runtimeUnit)
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

            if (!TimedBlockHasPermanentHighMobility(timed))
            {
                continue;
            }

            if (timed.HasActivationConditions()
                && !MeetsHighMobilityActivationConditions(timed, runtimeUnit))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool TimedBlockHasPermanentHighMobility(TimedEffectData timed)
    {
        IReadOnlyList<EffectData> resolved = timed.GetResolvedEffects();
        for (int j = 0; j < resolved.Count; j++)
        {
            EffectData effect = resolved[j];
            if (effect != null
                && effect.type == EffectType.HighMobility
                && effect.duration == EffectDuration.Permanent)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 条件付き《高機動》（例: パイロット搭乗中）。
    /// ランタイムユニットが無いときは条件付きブロックを無視する。
    /// </summary>
    private static bool MeetsHighMobilityActivationConditions(TimedEffectData timed, CardController runtimeUnit)
    {
        if (timed == null || !timed.HasActivationConditions())
        {
            return true;
        }

        if (runtimeUnit == null)
        {
            return false;
        }

        EffectActivationContext ctx = new EffectActivationContext(
            BattleGameMain.PlayerType.Player,
            runtimeUnit,
            null,
            null,
            null,
            null,
            isOwnerTurn: true,
            mountHostUnit: runtimeUnit,
            mountedPilot: runtimeUnit.MountedPilot);
        return EffectActivationEvaluator.AreTimedConditionsMet(timed, ctx);
    }
}
