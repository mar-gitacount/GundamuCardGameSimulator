using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>コスト支払い・1ターン1回の能動効果発動。</summary>
public partial class BattleGameMain
{
    private readonly struct PaidActivationUseKey : System.IEquatable<PaidActivationUseKey>
    {
        public PaidActivationUseKey(PlayerType owner, EntityId cardEntityId, int timedBlockIndex)
        {
            Owner = owner;
            CardEntityId = cardEntityId;
            TimedBlockIndex = timedBlockIndex;
        }

        public PlayerType Owner { get; }
        public EntityId CardEntityId { get; }
        public int TimedBlockIndex { get; }

        public bool Equals(PaidActivationUseKey other)
        {
            return Owner == other.Owner
                && CardEntityId == other.CardEntityId
                && TimedBlockIndex == other.TimedBlockIndex;
        }

        public override bool Equals(object obj)
        {
            return obj is PaidActivationUseKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return System.HashCode.Combine(Owner, CardEntityId, TimedBlockIndex);
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
        if (timed == null || source == null)
        {
            return false;
        }

        IReadOnlyList<EffectData> effects = timed.GetResolvedEffects();
        if (effects.Count == 0)
        {
            return false;
        }

        return IsDeferredOnMainDestroyCost(timed)
            || IsDeferredOnMainRestAllyCost(timed)
            || (side != PlayerType.Enemy && effects[0].type == EffectType.ExileFromTrash);
    }

    /// <summary>
    /// 起動・メインの先頭にある「自身以外の味方1体を選んで破壊」を発動条件として扱う。
    /// 対象を選ぶ前にターン1回を消費せず、実際に選択して破壊する直前に確定する。
    /// </summary>
    private static bool IsDeferredOnMainDestroyCost(TimedEffectData timed)
    {
        IReadOnlyList<EffectData> effects = timed?.GetResolvedEffects();
        if (effects == null || effects.Count == 0)
        {
            return false;
        }

        EffectData first = effects[0];
        return first != null
            && first.type == EffectType.Destroy
            && first.target == TargetType.AllyOtherUnit
            && first.selectionMode.RequiresManualUnitPick();
    }

    /// <summary>
    /// 起動・メインの先頭にある「味方ユニットを選んで REST」を発動コストとして扱う（V2 等）。
    /// 選択確定前にターン1回を消費しない。
    /// </summary>
    private static bool IsDeferredOnMainRestAllyCost(TimedEffectData timed)
    {
        IReadOnlyList<EffectData> effects = timed?.GetResolvedEffects();
        if (effects == null || effects.Count == 0)
        {
            return false;
        }

        EffectData first = effects[0];
        if (first == null
            || first.type != EffectType.Rest
            || !first.selectionMode.RequiresManualUnitPick())
        {
            return false;
        }

        return first.target == TargetType.AllyOtherUnit
            || first.target == TargetType.AllyUnit;
    }

    private bool TryFinalizeOnMainPaidActivation(PaidActivationBlockContext context)
    {
        if (context.Timed == null || context.Source == null)
        {
            return false;
        }

        int cost = GetOnMainActivationCost(context.Source, context.Timed, context.Side);
        int exToUse = 0;
        if (cost > 0)
        {
            Gundam2024RuleScript.PlayerState resourceState = context.Side == PlayerType.Player
                ? gundamRule.Player
                : gundamRule.Enemy;
            exToUse = Gundam2024RuleScript.GetExNeededForCost(resourceState, cost);
        }

        return TryFinalizeOnMainPaidActivation(context, exToUse);
    }

    private bool TryFinalizeOnMainPaidActivation(PaidActivationBlockContext context, int exToUse)
    {
        if (context.Timed == null || context.Source == null)
        {
            return false;
        }

        if (!TryConsumeOnMainActivationCost(context.Side, context.Source, context.Timed, exToUse))
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
            new PaidActivationUseKey(side, card.GetEntityId(), blockIndex));
    }

    private void MarkPaidActivationUsedThisTurn(PlayerType side, CardController card, int blockIndex)
    {
        if (card == null)
        {
            return;
        }

        _paidActivationUsesThisTurn.Add(
            new PaidActivationUseKey(side, card.GetEntityId(), blockIndex));
    }

    /// <summary>
    /// 効果単位の oncePerTurn 用キー（timed ブロック index と衝突しない負値）。
    /// </summary>
    private static int EffectOncePerTurnUseIndex(int effectIndexInChain)
    {
        return -1 - Mathf.Max(0, effectIndexInChain);
    }

    private bool HasUsedEffectOncePerTurnThisTurn(PlayerType side, CardController card, int effectIndexInChain)
    {
        return HasUsedPaidActivationThisTurn(side, card, EffectOncePerTurnUseIndex(effectIndexInChain));
    }

    private void MarkEffectOncePerTurnUsedThisTurn(PlayerType side, CardController card, int effectIndexInChain)
    {
        MarkPaidActivationUsedThisTurn(side, card, EffectOncePerTurnUseIndex(effectIndexInChain));
    }

    /// <summary>
    /// チェーン内の個別効果 oncePerTurn。未使用なら false（実行可）。使用済みなら true（スキップ）。
    /// 条件未達でスキップする場合は呼ばないこと。
    /// </summary>
    private bool ShouldSkipChainedEffectOncePerTurn(
        PlayerType side,
        CardController source,
        EffectData effect,
        int effectIndexInChain)
    {
        if (effect == null || !effect.oncePerTurn)
        {
            return false;
        }

        if (!HasUsedEffectOncePerTurnThisTurn(side, source, effectIndexInChain))
        {
            return false;
        }

        Debug.Log(
            $"[Effect] oncePerTurn 済のためスキップ ({effect.type} cardId:{source?.Data?.id} idx:{effectIndexInChain})");
        return true;
    }

    private void TryMarkChainedEffectOncePerTurn(
        PlayerType side,
        CardController source,
        EffectData effect,
        int effectIndexInChain)
    {
        if (effect == null || !effect.oncePerTurn)
        {
            return;
        }

        MarkEffectOncePerTurnUsedThisTurn(side, source, effectIndexInChain);
    }

    private void ClearPaidActivationUseThisTurn(PlayerType side, CardController card, int blockIndex)
    {
        if (card == null)
        {
            return;
        }

        _paidActivationUsesThisTurn.Remove(
            new PaidActivationUseKey(side, card.GetEntityId(), blockIndex));
    }

    private static bool IsOnMainActivatedFromHand(CardController card, PlayerType ownerType, BattleGameMain host)
    {
        if (card?.Data == null || host == null)
        {
            return false;
        }

        // 場のユニット【メイン】（《援護》等）は、同名カードが手札にあっても手札コストを課さない
        if (card.Data.IsUnitLike() && host.IsCardOnBattleZone(card))
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
        int cost = GetOnMainActivationCost(source, timed, side);
        int exToUse = 0;
        if (cost > 0 && gundamRule != null)
        {
            Gundam2024RuleScript.PlayerState resourceState = side == PlayerType.Player
                ? gundamRule.Player
                : gundamRule.Enemy;
            exToUse = Gundam2024RuleScript.GetExNeededForCost(resourceState, cost);
        }

        return TryConsumeOnMainActivationCost(side, source, timed, exToUse);
    }

    private bool TryConsumeOnMainActivationCost(
        PlayerType side,
        CardController source,
        TimedEffectData timed,
        int exToUse)
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
        int requiredLevel = IsOnMainActivatedFromHand(source, side) ? source.CurrentLevel : -1;
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

        AfterLocalResourceConsumed(ruleSide, exToUse);
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

        // 配備前破壊の履歴があれば、判定直前にアーム（ResetRuntime 後でも復旧）
        TryArmCardFromOwnEffectDestroyHistory(source, side);

        if (timed.oncePerTurn && HasUsedPaidActivationThisTurn(side, source, blockIndex))
        {
            return false;
        }

        if (!EffectActivationEvaluator.AreTimedConditionsMet(timed, activationContext))
        {
            return false;
        }

        if (IsDeferredOnMainDestroyCost(timed))
        {
            EffectData destroyCost = timed.GetResolvedEffects()[0];
            if (ResolveSelectableEffectTargets(source, side, destroyCost).Count == 0)
            {
                return false;
            }
        }

        if (IsDeferredOnMainRestAllyCost(timed))
        {
            EffectData restCost = timed.GetResolvedEffects()[0];
            int need = restCost != null ? restCost.GetSelectMinCount() : 1;
            if (ResolveSelectableEffectTargets(source, side, restCost).Count < need)
            {
                return false;
            }
        }

        if (TimedStartsWithRestSelf(timed) && source.IsRestState)
        {
            return false;
        }

        if (!HasOnMainDeployZoneCandidatesIfRequired(side, timed))
        {
            return false;
        }

        if (!HasMeaningfulOnMainEffectsIncludingChooseOne(side, source, timed))
        {
            return false;
        }

        return CanAffordOnMainActivation(side, source, timed);
    }

    /// <summary>OnMain の先頭が「自身を REST」のとき true（Axis 等）。</summary>
    private static bool TimedStartsWithRestSelf(TimedEffectData timed)
    {
        IReadOnlyList<EffectData> effects = timed?.GetResolvedEffects();
        if (effects == null || effects.Count == 0)
        {
            return false;
        }

        EffectData first = effects[0];
        return first != null
            && first.type == EffectType.Rest
            && first.target == TargetType.Self;
    }

    /// <summary>手札／トラッシュ配備（選択必須）を含む OnMain は、候補が1枚以上あるときだけ発動可。</summary>
    private bool HasOnMainDeployZoneCandidatesIfRequired(PlayerType side, TimedEffectData timed)
    {
        IReadOnlyList<EffectData> effects = timed?.GetResolvedEffects();
        if (effects == null)
        {
            return true;
        }

        bool hasHandDeploySelect = false;
        bool hasTrashDeploySelect = false;
        for (int i = 0; i < effects.Count; i++)
        {
            EffectData effect = effects[i];
            if (effect == null || effect.type != EffectType.DeployUnit)
            {
                continue;
            }

            if (effect.deployUnitSource == DeployUnitSource.Hand)
            {
                if (!effect.RequiresDeployUnitZoneSelection())
                {
                    continue;
                }

                hasHandDeploySelect = true;
                if (CollectHandDeployCandidates(side, effect).Count > 0)
                {
                    return true;
                }
            }
            else if (effect.deployUnitSource == DeployUnitSource.Trash)
            {
                hasTrashDeploySelect = true;
                CardGameRule trashRule = ResolveTrashRuleForEffect(side, effect);
                if (trashRule != null)
                {
                    List<TrashExileCandidate> trashCandidates = CollectTrashDeployCandidates(trashRule, effect);
                    if (effect.deployUnitPayCost)
                    {
                        trashCandidates = FilterAffordableTrashDeployCandidates(side, trashCandidates);
                    }

                    if (trashCandidates.Count > 0)
                    {
                        return true;
                    }
                }
            }
        }

        return !hasHandDeploySelect && !hasTrashDeploySelect;
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
            return GameLocale.T("効果発動", "Activate effect");
        }

        int cost = GetOnMainActivationCost(source, blocks[0].Timed, ownerType);
        if (cost > 0)
        {
            return GameLocale.T($"効果発動 ({cost})", $"Activate effect ({cost})");
        }

        return GameLocale.T("メイン効果を発動", "Activate Main effect");
    }
}
