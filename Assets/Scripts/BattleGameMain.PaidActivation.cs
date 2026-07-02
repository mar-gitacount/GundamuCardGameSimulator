using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>コスト支払い・1ターン1回の能動効果発動。</summary>
public partial class BattleGameMain
{
    private readonly struct PaidActivationUseKey : System.IEquatable<PaidActivationUseKey>
    {
        public PaidActivationUseKey(PlayerType owner, int cardInstanceId, int timedBlockIndex)
        {
            Owner = owner;
            CardInstanceId = cardInstanceId;
            TimedBlockIndex = timedBlockIndex;
        }

        public PlayerType Owner { get; }
        public int CardInstanceId { get; }
        public int TimedBlockIndex { get; }

        public bool Equals(PaidActivationUseKey other)
        {
            return Owner == other.Owner
                && CardInstanceId == other.CardInstanceId
                && TimedBlockIndex == other.TimedBlockIndex;
        }

        public override bool Equals(object obj)
        {
            return obj is PaidActivationUseKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return System.HashCode.Combine(Owner, CardInstanceId, TimedBlockIndex);
        }
    }

    private readonly struct OnMainExecutableBlock
    {
        public OnMainExecutableBlock(TimedEffectData timed, int blockIndex)
        {
            Timed = timed;
            BlockIndex = blockIndex;
        }

        public TimedEffectData Timed { get; }
        public int BlockIndex { get; }
    }

    private PaidActivationBlockContext _activeOnMainPaidBlock;
    private readonly HashSet<PaidActivationUseKey> _paidActivationUsesThisTurn = new HashSet<PaidActivationUseKey>();

    private readonly struct PaidActivationBlockContext
    {
        public PaidActivationBlockContext(
            PlayerType side,
            CardController source,
            TimedEffectData timed,
            int blockIndex)
        {
            Side = side;
            Source = source;
            Timed = timed;
            BlockIndex = blockIndex;
        }

        public PlayerType Side { get; }
        public CardController Source { get; }
        public TimedEffectData Timed { get; }
        public int BlockIndex { get; }
    }

    private bool NeedsDeferredOnMainPayment(TimedEffectData timed, PlayerType side, CardController source)
    {
        if (side == PlayerType.Enemy || timed == null || source == null)
        {
            return false;
        }

        IReadOnlyList<EffectData> effects = timed.GetResolvedEffects();
        return effects.Count > 0 && effects[0].type == EffectType.ExileFromTrash;
    }

    private bool TryFinalizeOnMainPaidActivation(PaidActivationBlockContext context)
    {
        if (context.Timed == null || context.Source == null)
        {
            return false;
        }

        if (!TryConsumeOnMainActivationCost(context.Side, context.Source, context.Timed))
        {
            return false;
        }

        if (context.Timed.oncePerTurn)
        {
            MarkPaidActivationUsedThisTurn(context.Side, context.Source, context.BlockIndex);
        }

        return true;
    }

    private void BeginOnMainPaidBlock(PlayerType side, CardController source, TimedEffectData timed, int blockIndex)
    {
        _activeOnMainPaidBlock = new PaidActivationBlockContext(side, source, timed, blockIndex);
    }

    private void ClearOnMainPaidBlock()
    {
        _activeOnMainPaidBlock = default;
    }

    private bool TryCommitOnMainPaidBlockBeforeExile()
    {
        if (_activeOnMainPaidBlock.Timed == null)
        {
            return true;
        }

        return TryFinalizeOnMainPaidActivation(_activeOnMainPaidBlock);
    }

    private void ClearPaidActivationUsesForSide(PlayerType side)
    {
        List<PaidActivationUseKey> remove = new List<PaidActivationUseKey>();
        foreach (PaidActivationUseKey key in _paidActivationUsesThisTurn)
        {
            if (key.Owner == side)
            {
                remove.Add(key);
            }
        }

        for (int i = 0; i < remove.Count; i++)
        {
            _paidActivationUsesThisTurn.Remove(remove[i]);
        }
    }

    private bool HasUsedPaidActivationThisTurn(PlayerType side, CardController card, int blockIndex)
    {
        if (card == null)
        {
            return false;
        }

        return _paidActivationUsesThisTurn.Contains(
            new PaidActivationUseKey(side, card.GetInstanceID(), blockIndex));
    }

    private void MarkPaidActivationUsedThisTurn(PlayerType side, CardController card, int blockIndex)
    {
        if (card == null)
        {
            return;
        }

        _paidActivationUsesThisTurn.Add(
            new PaidActivationUseKey(side, card.GetInstanceID(), blockIndex));
    }

    private static bool IsOnMainActivatedFromHand(CardController card, PlayerType ownerType, BattleGameMain host)
    {
        if (card?.Data == null || host == null)
        {
            return false;
        }

        List<CardData> hand = ownerType == PlayerType.Player
            ? host.playerHandCards
            : host.enemyHandCards;
        return hand.Contains(card.Data);
    }

    private bool IsOnMainActivatedFromHand(CardController card, PlayerType ownerType)
    {
        return IsOnMainActivatedFromHand(card, ownerType, this);
    }

    private static int GetOnMainActivationCost(CardController source, TimedEffectData timed, PlayerType ownerType, BattleGameMain host)
    {
        if (timed != null && timed.activationCost > 0)
        {
            return timed.activationCost;
        }

        if (source != null && IsOnMainActivatedFromHand(source, ownerType, host))
        {
            return source.CurrentCost;
        }

        return 0;
    }

    private int GetOnMainActivationCost(CardController source, TimedEffectData timed, PlayerType ownerType)
    {
        return GetOnMainActivationCost(source, timed, ownerType, this);
    }

    private bool CanAffordOnMainActivation(PlayerType side, CardController source, TimedEffectData timed)
    {
        if (source == null || source.Data == null || gundamRule == null)
        {
            return false;
        }

        if (IsOnMainActivatedFromHand(source, side))
        {
            Gundam2024RuleScript.PlayerState state = side == PlayerType.Player
                ? gundamRule.Player
                : gundamRule.Enemy;
            if (state.TotalLevel < source.CurrentLevel)
            {
                return false;
            }
        }

        int cost = GetOnMainActivationCost(source, timed, side);
        if (cost <= 0)
        {
            return true;
        }

        Gundam2024RuleScript.PlayerState resourceState = side == PlayerType.Player
            ? gundamRule.Player
            : gundamRule.Enemy;
        int requiredLevel = IsOnMainActivatedFromHand(source, side) ? source.CurrentLevel : 0;
        return gundamRule.CanPlayCardWithAnyEx(ToRuleSide(side), requiredLevel, cost);
    }

    private bool TryConsumeOnMainActivationCost(PlayerType side, CardController source, TimedEffectData timed)
    {
        if (source == null || source.Data == null)
        {
            return false;
        }

        int cost = GetOnMainActivationCost(source, timed, side);
        if (cost <= 0)
        {
            return true;
        }

        if (IsResolvingBurstEffect && IsOnMainActivatedFromHand(source, side))
        {
            Debug.LogWarning(
                $"[OnMain] Skipped resource consume during burst (cardId:{source.Data.id}).");
            return false;
        }

        Gundam2024RuleScript.PlayerSide ruleSide = ToRuleSide(side);
        Gundam2024RuleScript.PlayerState resourceState = side == PlayerType.Player
            ? gundamRule.Player
            : gundamRule.Enemy;
        int requiredLevel = IsOnMainActivatedFromHand(source, side) ? source.CurrentLevel : -1;
        int exToUse = Gundam2024RuleScript.GetExNeededForCost(resourceState, cost);
        if (!gundamRule.TryConsumeResource(
                ruleSide,
                cost,
                exToUse,
                source.Data.id,
                requiredLevel))
        {
            Debug.Log("OnMain: リソース不足で実行できません。");
            return false;
        }

        SyncResourceViewsFromRule(ruleSide);
        return true;
    }

    private bool IsOnMainTimedBlockAvailableNow(
        PlayerType side,
        CardController source,
        TimedEffectData timed,
        int blockIndex,
        EffectActivationContext activationContext)
    {
        if (timed == null || !timed.HasResolvedEffects())
        {
            return false;
        }

        if (timed.oncePerTurn && HasUsedPaidActivationThisTurn(side, source, blockIndex))
        {
            return false;
        }

        if (!EffectActivationEvaluator.AreTimedConditionsMet(timed, activationContext))
        {
            return false;
        }

        return CanAffordOnMainActivation(side, source, timed);
    }

    private List<OnMainExecutableBlock> CollectExecutableOnMainBlocks(PlayerType side, CardController source)
    {
        List<OnMainExecutableBlock> blocks = new List<OnMainExecutableBlock>();
        if (source == null || source.Data == null || source.Data.timedEffects == null)
        {
            return blocks;
        }

        EffectActivationContext activationContext = BuildActivationContext(side, source);
        for (int i = 0; i < source.Data.timedEffects.Count; i++)
        {
            TimedEffectData timed = source.Data.timedEffects[i];
            if (timed == null || timed.timing != EffectTiming.OnMain)
            {
                continue;
            }

            if (!IsOnMainTimedBlockAvailableNow(side, source, timed, i, activationContext))
            {
                continue;
            }

            blocks.Add(new OnMainExecutableBlock(timed, i));
        }

        return blocks;
    }

    private List<EffectData> BuildOnMainExecutableEffects(PlayerType side, CardController source)
    {
        List<EffectData> list = new List<EffectData>();
        List<OnMainExecutableBlock> blocks = CollectExecutableOnMainBlocks(side, source);
        for (int i = 0; i < blocks.Count; i++)
        {
            IReadOnlyList<EffectData> resolved = blocks[i].Timed.GetResolvedEffects();
            for (int j = 0; j < resolved.Count; j++)
            {
                if (resolved[j] != null)
                {
                    list.Add(resolved[j]);
                }
            }
        }

        return list;
    }

    private string FormatOnMainActivationButtonLabel(CardController source, PlayerType ownerType)
    {
        List<OnMainExecutableBlock> blocks = CollectExecutableOnMainBlocks(ownerType, source);
        if (blocks.Count == 0)
        {
            return "効果発動";
        }

        int cost = GetOnMainActivationCost(source, blocks[0].Timed, ownerType);
        if (cost > 0)
        {
            return $"効果発動 ({cost})";
        }

        return "メイン効果を発動";
    }
}
