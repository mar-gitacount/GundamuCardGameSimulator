using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// <see cref="EffectData.value"/> の解決（固定値 / 盤面カウント連動）。
/// 発動条件（activationConditions）とは独立。
/// </summary>
public static class EffectMagnitudeResolver
{
    public static int Resolve(EffectData effect, EffectActivationContext ctx, CardController sourceCard)
    {
        if (effect == null)
        {
            return 0;
        }

        int perUnitOrFixed = Mathf.Max(0, Mathf.Abs(effect.value));
        if (perUnitOrFixed == 0)
        {
            return 0;
        }

        if (effect.valueMode != EffectValueMode.MultiplyByBoardCount)
        {
            return perUnitOrFixed;
        }

        int count = CountForValueScale(effect, ctx, sourceCard);
        int total = perUnitOrFixed * count;
        if (effect.valueScaleMaximum > 0)
        {
            total = Mathf.Min(total, effect.valueScaleMaximum);
        }

        return total;
    }

    public static int CountForValueScale(EffectData effect, EffectActivationContext ctx, CardController sourceCard)
    {
        if (effect == null || ctx == null)
        {
            return 0;
        }

        IReadOnlyList<CardController> zone = ResolveZone(ctx, effect.valueCountBoardSide);
        switch (effect.valueCountKind)
        {
            case EffectValueCountKind.CardsWithFeature:
                return CountCardsWithFeature(zone, effect.valueCountFeature, effect, sourceCard);
            case EffectValueCountKind.UnitsWithLevelAtLeast:
                return CountAliveUnitsWithLevelAtLeast(zone, effect.valueCountMinUnitLevel, effect, sourceCard);
            case EffectValueCountKind.AliveUnits:
            default:
                return CountAliveUnits(zone, effect, sourceCard);
        }
    }

    private static IReadOnlyList<CardController> ResolveZone(EffectActivationContext ctx, EffectBoardSide side)
    {
        bool ownerIsPlayer = ctx.OwnerType == BattleGameMain.PlayerType.Player;
        switch (side)
        {
            case EffectBoardSide.OwnerBattleZone:
                return ownerIsPlayer ? ctx.PlayerBattleZone : ctx.EnemyBattleZone;
            case EffectBoardSide.OpponentBattleZone:
                return ownerIsPlayer ? ctx.EnemyBattleZone : ctx.PlayerBattleZone;
            case EffectBoardSide.OwnerHand:
                return ownerIsPlayer ? ctx.PlayerHand : ctx.EnemyHand;
            case EffectBoardSide.OpponentHand:
                return ownerIsPlayer ? ctx.EnemyHand : ctx.PlayerHand;
            default:
                return System.Array.Empty<CardController>();
        }
    }

    private static int CountAliveUnits(
        IReadOnlyList<CardController> cards,
        EffectData effect,
        CardController sourceCard)
    {
        int n = 0;
        for (int i = 0; i < cards.Count; i++)
        {
            CardController c = cards[i];
            if (!IsAliveUnit(c) || ShouldExcludeSource(c, effect, sourceCard))
            {
                continue;
            }

            n++;
        }

        return n;
    }

    private static int CountAliveUnitsWithLevelAtLeast(
        IReadOnlyList<CardController> cards,
        int minLevel,
        EffectData effect,
        CardController sourceCard)
    {
        int n = 0;
        for (int i = 0; i < cards.Count; i++)
        {
            CardController c = cards[i];
            if (!IsAliveUnit(c) || c.Data == null || ShouldExcludeSource(c, effect, sourceCard))
            {
                continue;
            }

            if (c.Data.level >= minLevel)
            {
                n++;
            }
        }

        return n;
    }

    private static int CountCardsWithFeature(
        IReadOnlyList<CardController> cards,
        CardFeatureData feature,
        EffectData effect,
        CardController sourceCard)
    {
        if (feature == null)
        {
            return 0;
        }

        int n = 0;
        for (int i = 0; i < cards.Count; i++)
        {
            CardController c = cards[i];
            if (c == null || c.Data == null || ShouldExcludeSource(c, effect, sourceCard))
            {
                continue;
            }

            if (c.Data.HasFeature(feature))
            {
                n++;
            }
        }

        return n;
    }

    private static bool ShouldExcludeSource(CardController c, EffectData effect, CardController sourceCard)
    {
        return effect != null
            && effect.valueCountExcludeSource
            && sourceCard != null
            && c == sourceCard;
    }

    private static bool IsAliveUnit(CardController c)
    {
        return c != null
            && c.Data != null
            && c.Data.type == Type.Unit
            && c.CurrentHp > 0;
    }
}
