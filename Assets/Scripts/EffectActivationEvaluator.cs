using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// カード効果の発動条件（フィールド走査・Feature 参照）を評価する。
/// </summary>
public sealed class EffectActivationContext
{
    public BattleGameMain.PlayerType OwnerType { get; }
    public CardController SourceCard { get; }
    public IReadOnlyList<CardController> PlayerBattleZone { get; }
    public IReadOnlyList<CardController> EnemyBattleZone { get; }
    public IReadOnlyList<CardController> PlayerHand { get; }
    public IReadOnlyList<CardController> EnemyHand { get; }
    public bool IsOwnerTurn { get; }

    /// <summary>OnPilotMounted 時の搭乗先ユニット（未設定時は Source が Unit ならそれを使用）。OnLink でも同様。</summary>
    public CardController MountHostUnit { get; }

    /// <summary>OnPilotMounted / OnLink 時に載せたパイロット（未設定時は MountHostUnit.MountedPilot）。</summary>
    public CardController MountedPilot { get; }

    /// <summary>同一チェーン内で MillTopToTrash / ExileFromDeck 等が観測したカード。</summary>
    public IReadOnlyList<CardData> ObservedCards { get; }

    public IReadOnlyList<int> OwnerTrashCardIds { get; }
    public IReadOnlyList<int> OpponentTrashCardIds { get; }

    /// <summary>
    /// OnMain 等の同一チェーン内で CompareFieldUnitCount を評価するとき、
    /// 発動開始時点のオーナーバトルゾーン生存ユニット数（-1 なら未固定で都度カウント）。
    /// </summary>
    public int FrozenOwnerBattleAliveUnitCount { get; }

    public bool HasFrozenOwnerBattleAliveUnitCount => FrozenOwnerBattleAliveUnitCount >= 0;

    /// <summary>同一チェーン内で直前までに実ダメージが1以上入ったか。</summary>
    public bool PriorChainDealtDamage { get; }

    public EffectActivationContext(
        BattleGameMain.PlayerType ownerType,
        CardController sourceCard,
        IReadOnlyList<CardController> playerBattleZone,
        IReadOnlyList<CardController> enemyBattleZone,
        IReadOnlyList<CardController> playerHand,
        IReadOnlyList<CardController> enemyHand,
        bool isOwnerTurn,
        CardController mountHostUnit = null,
        CardController mountedPilot = null,
        IReadOnlyList<CardData> observedCards = null,
        IReadOnlyList<int> ownerTrashCardIds = null,
        IReadOnlyList<int> opponentTrashCardIds = null,
        int frozenOwnerBattleAliveUnitCount = -1,
        bool priorChainDealtDamage = false)
    {
        OwnerType = ownerType;
        SourceCard = sourceCard;
        PlayerBattleZone = playerBattleZone ?? System.Array.Empty<CardController>();
        EnemyBattleZone = enemyBattleZone ?? System.Array.Empty<CardController>();
        PlayerHand = playerHand ?? System.Array.Empty<CardController>();
        EnemyHand = enemyHand ?? System.Array.Empty<CardController>();
        IsOwnerTurn = isOwnerTurn;
        MountHostUnit = mountHostUnit;
        MountedPilot = mountedPilot;
        ObservedCards = observedCards ?? System.Array.Empty<CardData>();
        OwnerTrashCardIds = ownerTrashCardIds ?? System.Array.Empty<int>();
        OpponentTrashCardIds = opponentTrashCardIds ?? System.Array.Empty<int>();
        FrozenOwnerBattleAliveUnitCount = frozenOwnerBattleAliveUnitCount;
        PriorChainDealtDamage = priorChainDealtDamage;
    }

    public EffectActivationContext WithFrozenOwnerBattleAliveUnitCount(int count)
    {
        if (count < 0 || count == FrozenOwnerBattleAliveUnitCount)
        {
            return this;
        }

        return new EffectActivationContext(
            OwnerType,
            SourceCard,
            PlayerBattleZone,
            EnemyBattleZone,
            PlayerHand,
            EnemyHand,
            IsOwnerTurn,
            MountHostUnit,
            MountedPilot,
            ObservedCards,
            OwnerTrashCardIds,
            OpponentTrashCardIds,
            count,
            PriorChainDealtDamage);
    }
}

public static class EffectActivationEvaluator
{
    public static bool ContainsObservedCardCondition(IList<EffectActivationCondition> conditions)
    {
        if (conditions == null || conditions.Count == 0)
        {
            return false;
        }

        for (int i = 0; i < conditions.Count; i++)
        {
            EffectActivationCondition c = conditions[i];
            if (c != null && (c.checkKind == EffectActivationCheckKind.ObservedCardHasFeature
                || c.checkKind == EffectActivationCheckKind.ObservedCardIsType))
            {
                return true;
            }
        }

        return false;
    }

    public static bool AreAllConditionsMet(IList<EffectActivationCondition> conditions, EffectActivationContext ctx)
    {
        if (conditions == null || conditions.Count == 0)
        {
            return true;
        }

        if (ctx == null)
        {
            return false;
        }

        for (int i = 0; i < conditions.Count; i++)
        {
            EffectActivationCondition c = conditions[i];
            if (c == null)
            {
                continue;
            }

            if (!EvaluateSingle(c, ctx))
            {
                return false;
            }
        }

        return true;
    }

    public static bool AreTimedConditionsMet(TimedEffectData timed, EffectActivationContext ctx)
    {
        if (timed == null || !timed.HasActivationConditions())
        {
            return true;
        }

        return AreAllConditionsMet(timed.activationConditions, ctx);
    }

    private static bool EvaluateSingle(EffectActivationCondition c, EffectActivationContext ctx)
    {
        if (c.turnCheck != EffectTurnCheckKind.Unset)
        {
            bool expectOwnerTurn = c.turnCheck == EffectTurnCheckKind.OwnerTurn;
            if (ctx.IsOwnerTurn != expectOwnerTurn)
            {
                return false;
            }
        }

        // 未指定なら、この条件ブロックはターン条件以外を無視して通す。
        if (c.checkKind == EffectActivationCheckKind.Unset)
        {
            return true;
        }

        if (c.checkKind == EffectActivationCheckKind.MountedPilot)
        {
            return EvaluateMountedPilot(c, ctx);
        }

        if (c.checkKind == EffectActivationCheckKind.SourceUnitStat)
        {
            return EvaluateSourceUnitStat(c, ctx);
        }

        if (c.checkKind == EffectActivationCheckKind.SourceUnitDamaged)
        {
            return EvaluateSourceUnitDamaged(ctx);
        }

        if (c.checkKind == EffectActivationCheckKind.PriorChainDealtDamage)
        {
            return ctx.PriorChainDealtDamage;
        }

        if (c.checkKind == EffectActivationCheckKind.UnitStatOnField)
        {
            if (c.boardSide == EffectBoardSide.Unset)
            {
                return false;
            }

            return EvaluateUnitStatOnField(ResolveZone(ctx, c.boardSide), c);
        }

        if (c.checkKind == EffectActivationCheckKind.ObservedCardHasFeature)
        {
            return EvaluateObservedCardHasFeature(c, ctx);
        }

        if (c.checkKind == EffectActivationCheckKind.ObservedCardIsType)
        {
            return EvaluateObservedCardIsType(c, ctx);
        }

        if (c.checkKind == EffectActivationCheckKind.TrashHasCardType)
        {
            return EvaluateTrashHasCardType(c, ctx);
        }

        if (c.checkKind == EffectActivationCheckKind.TrashHasFeature)
        {
            return EvaluateTrashHasFeature(c, ctx);
        }

        if (c.checkKind == EffectActivationCheckKind.TrashHasCardId)
        {
            return EvaluateTrashHasCardId(c, ctx, expectPresent: true);
        }

        if (c.checkKind == EffectActivationCheckKind.TrashLacksCardId)
        {
            return EvaluateTrashHasCardId(c, ctx, expectPresent: false);
        }

        if (c.checkKind == EffectActivationCheckKind.CompareFieldUnitCount)
        {
            if (c.boardSide == EffectBoardSide.Unset)
            {
                return false;
            }

            int count = ResolveFieldUnitCountForCondition(c, ctx);
            return CompareInts(count, c.unitCountThreshold, c.unitCountCompareOp);
        }

        // boardSide 未指定は「この checkKind のゾーン側判定をスキップ」扱い。
        if (c.boardSide == EffectBoardSide.Unset)
        {
            return true;
        }

        IReadOnlyList<CardController> zone = ResolveZone(ctx, c.boardSide);
        switch (c.checkKind)
        {
            case EffectActivationCheckKind.HasFeature:
                return CountCardsWithFeature(zone, c) >= Mathf.Max(1, c.minimumCount);
            case EffectActivationCheckKind.UnitCountAtLeast:
                return CountAliveUnits(zone) >= Mathf.Max(0, c.minimumCount);
            case EffectActivationCheckKind.UnitLevelOnField:
                return EvaluateUnitLevel(zone, c);
            case EffectActivationCheckKind.CountUnitsAtExactLevel:
            {
                int count = CountUnitsWithExactLevel(zone, c.compareValue);
                return CompareInts(count, c.unitCountThreshold, c.unitCountCompareOp);
            }
            default:
                return false;
        }
    }

    private static bool EvaluateObservedCardHasFeature(EffectActivationCondition c, EffectActivationContext ctx)
    {
        if (c == null || ctx == null)
        {
            return false;
        }

        IReadOnlyList<CardFeatureData> requiredFeatures = c.GetActivationFeatures();
        if (requiredFeatures.Count == 0)
        {
            return false;
        }

        IReadOnlyList<CardData> observed = ctx.ObservedCards;
        if (observed == null || observed.Count == 0)
        {
            return false;
        }

        int need = Mathf.Max(1, c.minimumCount);
        int matched = 0;
        for (int i = 0; i < observed.Count; i++)
        {
            CardData data = observed[i];
            if (data != null && data.HasAnyFeature(requiredFeatures))
            {
                matched++;
            }
        }

        return matched >= need;
    }

    private static bool EvaluateObservedCardIsType(EffectActivationCondition c, EffectActivationContext ctx)
    {
        if (c == null || ctx == null)
        {
            return false;
        }

        IReadOnlyList<CardData> observed = ctx.ObservedCards;
        if (observed == null || observed.Count == 0)
        {
            return false;
        }

        int need = Mathf.Max(1, c.minimumCount);
        int matched = 0;
        for (int i = 0; i < observed.Count; i++)
        {
            CardData data = observed[i];
            if (data != null && data.type == c.observedCardType)
            {
                matched++;
            }
        }

        return matched >= need;
    }

    private static int ResolveTrashConditionCardId(EffectActivationCondition c, EffectActivationContext ctx)
    {
        if (c == null)
        {
            return 0;
        }

        if (c.trashCardId > 0)
        {
            return c.trashCardId;
        }

        if (c.pilotCardId > 0)
        {
            return c.pilotCardId;
        }

        // 未指定時は発動元カード ID（「墓地にこのカードが N 枚」）
        if (ctx?.SourceCard?.Data != null)
        {
            return ctx.SourceCard.Data.id;
        }

        return 0;
    }

    private static bool EvaluateTrashHasCardId(
        EffectActivationCondition c,
        EffectActivationContext ctx,
        bool expectPresent)
    {
        if (c == null || ctx == null)
        {
            return false;
        }

        int cardId = ResolveTrashConditionCardId(c, ctx);
        if (cardId <= 0)
        {
            return false;
        }

        IReadOnlyList<int> trashIds = ResolveTrashZone(ctx, c.boardSide);
        int need = Mathf.Max(1, c.minimumCount);
        if (expectPresent)
        {
            return TrashCardQuery.HasAtLeast(trashIds, cardId, need);
        }

        return TrashCardQuery.HasFewerThan(trashIds, cardId, need);
    }

    private static bool EvaluateTrashHasCardType(EffectActivationCondition c, EffectActivationContext ctx)
    {
        if (c == null || ctx == null)
        {
            return false;
        }

        IReadOnlyList<int> trashIds = ResolveTrashZone(ctx, c.boardSide);
        int need = Mathf.Max(1, c.minimumCount);
        return TrashCardQuery.HasCardTypeAtLeast(trashIds, c.observedCardType, need);
    }

    private static bool EvaluateTrashHasFeature(EffectActivationCondition c, EffectActivationContext ctx)
    {
        if (c == null || ctx == null)
        {
            return false;
        }

        IReadOnlyList<CardFeatureData> required = c.GetActivationFeatures();
        if (required.Count == 0)
        {
            return false;
        }

        IReadOnlyList<int> trashIds = ResolveTrashZone(ctx, c.boardSide);
        int need = Mathf.Max(1, c.minimumCount);
        return TrashCardQuery.HasAnyFeatureAtLeast(trashIds, required, need);
    }

    private static IReadOnlyList<int> ResolveTrashZone(EffectActivationContext ctx, EffectBoardSide side)
    {
        if (ctx == null)
        {
            return System.Array.Empty<int>();
        }

        bool ownerIsPlayer = ctx.OwnerType == BattleGameMain.PlayerType.Player;
        switch (side)
        {
            case EffectBoardSide.OpponentTrash:
                return ownerIsPlayer ? ctx.OpponentTrashCardIds : ctx.OwnerTrashCardIds;
            case EffectBoardSide.OwnerTrash:
                return ownerIsPlayer ? ctx.OwnerTrashCardIds : ctx.OpponentTrashCardIds;
            case EffectBoardSide.Unset:
            default:
                return ownerIsPlayer ? ctx.OwnerTrashCardIds : ctx.OpponentTrashCardIds;
        }
    }

    private static bool EvaluateSourceUnitStat(EffectActivationCondition c, EffectActivationContext ctx)
    {
        if (c == null || ctx?.SourceCard == null || ctx.SourceCard.Data == null)
        {
            return false;
        }

        int statValue = GetActivationStatValue(ctx.SourceCard, c.activationStatTarget);
        return CompareInts(statValue, c.compareValue, c.compareOp);
    }

    private static CardController ResolveOnAttackConditionUnit(EffectActivationContext ctx)
    {
        if (ctx == null)
        {
            return null;
        }

        if (ctx.MountHostUnit != null && ctx.MountHostUnit.Data != null && ctx.MountHostUnit.Data.IsUnitLike())
        {
            return ctx.MountHostUnit;
        }

        if (ctx.SourceCard != null && ctx.SourceCard.Data != null && ctx.SourceCard.Data.IsUnitLike())
        {
            return ctx.SourceCard;
        }

        return ctx.SourceCard;
    }

    private static bool EvaluateSourceUnitDamaged(EffectActivationContext ctx)
    {
        CardController unit = ResolveOnAttackConditionUnit(ctx);
        if (unit == null || unit.Data == null || !unit.Data.IsUnitLike())
        {
            return false;
        }

        if (unit.CurrentHp <= 0)
        {
            return false;
        }

        return unit.CurrentHp < unit.GetRepairHpCap();
    }

    private static bool EvaluateUnitStatOnField(IReadOnlyList<CardController> zone, EffectActivationCondition c)
    {
        if (c == null || zone == null)
        {
            return false;
        }

        for (int i = 0; i < zone.Count; i++)
        {
            CardController unit = zone[i];
            if (!IsAliveUnit(unit))
            {
                continue;
            }

            int statValue = GetActivationStatValue(unit, c.activationStatTarget);
            if (CompareInts(statValue, c.compareValue, c.compareOp))
            {
                return true;
            }
        }

        return false;
    }

    private static int GetActivationStatValue(CardController card, EffectTargetUnitFilterStat stat)
    {
        if (card == null)
        {
            return 0;
        }

        EffectTargetUnitFilterStat resolved = stat == EffectTargetUnitFilterStat.Unset
            ? EffectTargetUnitFilterStat.AP
            : stat;
        return EffectDataExtensions.GetTargetUnitFilterStatValue(card, resolved);
    }

    private static bool EvaluateMountedPilot(EffectActivationCondition c, EffectActivationContext ctx)
    {
        if (c == null || ctx == null)
        {
            return false;
        }

        int need = Mathf.Max(1, c.minimumCount);
        if (c.boardSide != EffectBoardSide.Unset)
        {
            IReadOnlyList<CardController> zone = ResolveZone(ctx, c.boardSide);
            int matched = CountUnitsWithMatchingMountedPilot(zone, c);
            return matched >= need;
        }

        CardController host = ctx.MountHostUnit;
        if (host == null && ctx.SourceCard != null && ctx.SourceCard.Data != null
            && ctx.SourceCard.Data.IsUnitLike())
        {
            host = ctx.SourceCard;
        }

        if (host == null)
        {
            return false;
        }

        CardController pilot = ctx.MountedPilot ?? host.MountedPilot;
        return PilotMeetsMountedPilotCondition(pilot, c);
    }

    private static int CountUnitsWithMatchingMountedPilot(IReadOnlyList<CardController> zone, EffectActivationCondition c)
    {
        int n = 0;
        for (int i = 0; i < zone.Count; i++)
        {
            CardController unit = zone[i];
            if (!IsAliveUnit(unit))
            {
                continue;
            }

            if (PilotMeetsMountedPilotCondition(unit.MountedPilot, c))
            {
                n++;
            }
        }

        return n;
    }

    private static bool PilotMeetsMountedPilotCondition(CardController pilot, EffectActivationCondition c)
    {
        if (pilot == null || pilot.Data == null || pilot.Data.type != Type.Pilot)
        {
            return false;
        }

        CardData data = pilot.Data;
        if (c.pilotCardId > 0 && data.id != c.pilotCardId)
        {
            return false;
        }

        IReadOnlyList<CardFeatureData> requiredFeatures = c.GetActivationFeatures();
        if (requiredFeatures.Count > 0 && !data.HasAnyFeature(requiredFeatures))
        {
            return false;
        }

        int levelThreshold = ResolveMountedPilotLevelThreshold(c);
        if (levelThreshold > 0)
        {
            if (!CompareInts(pilot.CurrentLevel, levelThreshold, c.compareOp))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>MountedPilot のレベル閾値。pilotLevelThreshold 優先、未設定時は compareValue。</summary>
    private static int ResolveMountedPilotLevelThreshold(EffectActivationCondition c)
    {
        if (c == null)
        {
            return 0;
        }

        if (c.pilotLevelThreshold > 0)
        {
            return c.pilotLevelThreshold;
        }

        return c.compareValue;
    }

    private static bool EvaluateUnitLevel(IReadOnlyList<CardController> zone, EffectActivationCondition c)
    {
        List<int> levels = CollectAliveUnitLevels(zone);
        if (levels.Count == 0)
        {
            return CompareInts(0, c.compareValue, c.compareOp);
        }

        switch (c.levelAggregate)
        {
            case EffectLevelAggregate.MaxLevel:
                return CompareInts(MaxInt(levels), c.compareValue, c.compareOp);
            case EffectLevelAggregate.MinLevel:
                return CompareInts(MinInt(levels), c.compareValue, c.compareOp);
            case EffectLevelAggregate.SumLevel:
            {
                int sum = 0;
                for (int i = 0; i < levels.Count; i++)
                {
                    sum += levels[i];
                }

                return CompareInts(sum, c.compareValue, c.compareOp);
            }
            case EffectLevelAggregate.CountUnitsWithLevelAtLeast:
            {
                int need = Mathf.Max(1, c.minimumCount);
                int thr = c.compareValue;
                int cnt = 0;
                for (int i = 0; i < levels.Count; i++)
                {
                    if (levels[i] >= thr)
                    {
                        cnt++;
                    }
                }

                return cnt >= need;
            }
            case EffectLevelAggregate.AnyUnitLevelCompare:
            {
                for (int i = 0; i < levels.Count; i++)
                {
                    if (CompareInts(levels[i], c.compareValue, c.compareOp))
                    {
                        return true;
                    }
                }

                return false;
            }
            default:
                return false;
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

    private static int CountUnitsWithExactLevel(IReadOnlyList<CardController> cards, int exactLevel)
    {
        int n = 0;
        for (int i = 0; i < cards.Count; i++)
        {
            CardController c = cards[i];
            if (!IsAliveUnit(c) || c.Data == null)
            {
                continue;
            }

            if (c.Data.level == exactLevel)
            {
                n++;
            }
        }

        return n;
    }

    public static int CountAliveUnitsInZone(IReadOnlyList<CardController> cards)
    {
        return CountAliveUnits(cards);
    }

    private static int ResolveFieldUnitCountForCondition(EffectActivationCondition c, EffectActivationContext ctx)
    {
        if (c == null || ctx == null)
        {
            return 0;
        }

        if (c.boardSide == EffectBoardSide.OwnerBattleZone && ctx.HasFrozenOwnerBattleAliveUnitCount)
        {
            return ctx.FrozenOwnerBattleAliveUnitCount;
        }

        return CountAliveUnits(ResolveZone(ctx, c.boardSide));
    }

    private static int CountAliveUnits(IReadOnlyList<CardController> cards)
    {
        int n = 0;
        for (int i = 0; i < cards.Count; i++)
        {
            CardController c = cards[i];
            if (IsAliveUnit(c))
            {
                n++;
            }
        }

        return n;
    }

    private static List<int> CollectAliveUnitLevels(IReadOnlyList<CardController> cards)
    {
        List<int> levels = new List<int>();
        for (int i = 0; i < cards.Count; i++)
        {
            CardController c = cards[i];
            if (!IsAliveUnit(c) || c.Data == null)
            {
                continue;
            }

            levels.Add(c.Data.level);
        }

        return levels;
    }

    private static bool IsAliveUnit(CardController c)
    {
        return c != null
            && c.Data != null
            && c.Data.IsUnitLike()
            && c.CurrentHp > 0;
    }

    private static int CountCardsWithFeature(IReadOnlyList<CardController> cards, EffectActivationCondition c)
    {
        IReadOnlyList<CardFeatureData> requiredFeatures = c.GetActivationFeatures();
        if (requiredFeatures.Count == 0)
        {
            return 0;
        }

        int n = 0;
        for (int i = 0; i < cards.Count; i++)
        {
            CardController card = cards[i];
            if (card?.Data == null)
            {
                continue;
            }

            if (card.Data.HasAnyFeature(requiredFeatures))
            {
                n++;
            }
        }

        return n;
    }

    private static int MaxInt(List<int> values)
    {
        int m = int.MinValue;
        for (int i = 0; i < values.Count; i++)
        {
            if (values[i] > m)
            {
                m = values[i];
            }
        }

        return m;
    }

    private static int MinInt(List<int> values)
    {
        int m = int.MaxValue;
        for (int i = 0; i < values.Count; i++)
        {
            if (values[i] < m)
            {
                m = values[i];
            }
        }

        return m;
    }

    private static bool CompareInts(int value, int threshold, EffectCompareOperator op)
    {
        switch (op)
        {
            case EffectCompareOperator.GreaterOrEqual:
                return value >= threshold;
            case EffectCompareOperator.Greater:
                return value > threshold;
            case EffectCompareOperator.Equal:
                return value == threshold;
            case EffectCompareOperator.LessOrEqual:
                return value <= threshold;
            case EffectCompareOperator.Less:
                return value < threshold;
            default:
                return false;
        }
    }
}
