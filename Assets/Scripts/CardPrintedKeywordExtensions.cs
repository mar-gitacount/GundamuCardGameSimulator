using System.Collections.Generic;

/// <summary>
/// ユニットカードが持つ印刷キーワード（リペア／突破／先制／援護／高機動／制圧／ブロッカー）の検出。
/// </summary>
public static class CardPrintedKeywordExtensions
{
    public struct PrintedKeywords
    {
        public bool HasRepair;
        public int RepairAmount;
        public bool HasBreach;
        public int BreachAmount;
        public bool HasFirstStrike;
        public bool HasSupport;
        public int SupportAp;
        public bool HasHighMobility;
        public bool HasSuppress;
        public int SuppressBreaks;
        public bool HasBlocker;

        public bool HasAny =>
            HasRepair
            || HasBreach
            || HasFirstStrike
            || HasSupport
            || HasHighMobility
            || HasSuppress
            || HasBlocker;
    }

    public static bool HasAnyCopyableKeyword(this CardData card)
    {
        return card != null && card.IsUnitLike() && card.GetPrintedKeywords().HasAny;
    }

    public static PrintedKeywords GetPrintedKeywords(this CardData card)
    {
        PrintedKeywords result = default;
        if (card == null || !card.IsUnitLike())
        {
            return result;
        }

        if (card.isRepair)
        {
            result.HasRepair = true;
            result.RepairAmount = card.repairAmount > 0 ? card.repairAmount : 1;
        }

        int breach = card.GetBreachAmount();
        if (breach > 0)
        {
            result.HasBreach = true;
            result.BreachAmount = breach;
        }

        if (card.IsBlockerUnit())
        {
            result.HasBlocker = true;
        }

        if (card.HasHighMobilityAbility())
        {
            result.HasHighMobility = true;
        }

        ScanTimedEffectsForKeywords(card, ref result);
        return result;
    }

    private static void ScanTimedEffectsForKeywords(CardData card, ref PrintedKeywords result)
    {
        if (card.timedEffects == null)
        {
            return;
        }

        for (int i = 0; i < card.timedEffects.Count; i++)
        {
            TimedEffectData timed = card.timedEffects[i];
            if (timed == null)
            {
                continue;
            }

            string effectsName = timed.effectsName ?? string.Empty;
            bool nameLooksLikeSupport =
                effectsName.IndexOf("Support", System.StringComparison.OrdinalIgnoreCase) >= 0;
            bool resolvedLooksLikeSupport = TryGetSupportApFromResolved(timed, out int supportApFromResolved);
            if (!result.HasSupport
                && timed.timing == EffectTiming.OnMain
                && (nameLooksLikeSupport || resolvedLooksLikeSupport))
            {
                result.HasSupport = true;
                int supportAp = supportApFromResolved > 0
                    ? supportApFromResolved
                    : ParseSupportApFromName(effectsName);
                result.SupportAp = supportAp;
            }

            if (!timed.HasResolvedEffects())
            {
                continue;
            }

            IReadOnlyList<EffectData> resolved = timed.GetResolvedEffects();
            for (int j = 0; j < resolved.Count; j++)
            {
                EffectData effect = resolved[j];
                if (effect == null)
                {
                    continue;
                }

                if (effect.type == EffectType.FirstStrike)
                {
                    result.HasFirstStrike = true;
                }

                if (effect.type == EffectType.HighMobility)
                {
                    result.HasHighMobility = true;
                }

                if (effect.type == EffectType.Suppress)
                {
                    result.HasSuppress = true;
                    int breaks = effect.value > 0 ? effect.value : 2;
                    if (breaks > result.SuppressBreaks)
                    {
                        result.SuppressBreaks = breaks;
                    }
                }

                if (effect.type == EffectType.Breach)
                {
                    int amount = effect.value > 0 ? effect.value : 0;
                    if (amount > 0)
                    {
                        result.HasBreach = true;
                        if (amount > result.BreachAmount)
                        {
                            result.BreachAmount = amount;
                        }
                    }
                }
            }
        }

        if (result.HasSuppress && result.SuppressBreaks <= 0)
        {
            result.SuppressBreaks = 2;
        }

        if (result.HasSupport && result.SupportAp <= 0)
        {
            result.SupportAp = 1;
        }
    }

    /// <summary>《援護N》：自身 REST → 他味方 AP+N（UntilEndOfTurn）。</summary>
    private static bool TryGetSupportApFromResolved(TimedEffectData timed, out int supportAp)
    {
        supportAp = 0;
        IReadOnlyList<EffectData> resolved = timed?.GetResolvedEffects();
        if (resolved == null || resolved.Count < 2)
        {
            return false;
        }

        EffectData rest = resolved[0];
        if (rest == null || rest.type != EffectType.Rest || rest.target != TargetType.Self)
        {
            return false;
        }

        for (int i = 1; i < resolved.Count; i++)
        {
            EffectData buff = resolved[i];
            if (buff == null || buff.type != EffectType.Buff)
            {
                continue;
            }

            if (buff.target != TargetType.AllyOtherUnit && buff.target != TargetType.AllyUnit)
            {
                continue;
            }

            // 援護は AP バフ（statTarget 未指定=AP、または AP/Both）
            if (buff.statTarget != EffectStatTarget.AP
                && buff.statTarget != EffectStatTarget.Both)
            {
                continue;
            }

            int ap = buff.value > 0 ? buff.value : 1;
            if (ap > supportAp)
            {
                supportAp = ap;
            }
        }

        return supportAp > 0;
    }

    private static int ParseSupportApFromName(string effectsName)
    {
        if (string.IsNullOrEmpty(effectsName))
        {
            return 1;
        }

        // Support3_... / Support1_...
        for (int i = 0; i < effectsName.Length - 7; i++)
        {
            if ((effectsName[i] == 'S' || effectsName[i] == 's')
                && effectsName.Length >= i + 8
                && string.Compare(effectsName, i, "Support", 0, 7, System.StringComparison.OrdinalIgnoreCase) == 0
                && char.IsDigit(effectsName[i + 7]))
            {
                return effectsName[i + 7] - '0';
            }
        }

        return 1;
    }
}
