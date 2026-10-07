using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// GD02-009 等：相手の効果でこのユニットの AP が減少した瞬間に割り込み解決する。
/// 常時パッシブの再適用や自軍デバフでは発火しない。
/// </summary>
public partial class BattleGameMain
{
    private struct PendingApReducedWatch
    {
        public CardController Unit;
        public int OnlineRequestId;
    }

    private readonly List<PendingApReducedWatch> _pendingApReducedWatches = new List<PendingApReducedWatch>();
    private bool _apReducedWatchFlushRunning;
    private bool _resumeRemoteApReducedAfterRemoteApply;
    private bool _apReducedWatchResolving;

    private bool HasPendingLocalApReducedWatch => _pendingApReducedWatches.Count > 0;

    /// <summary>EX 除外監視の後に AP 減少割り込みも消化する（メイン／アクション完了地点）。</summary>
    private IEnumerator FlushPendingExResourceRemovedAndApReducedWatchesCoroutine()
    {
        yield return FlushPendingExResourceRemovedWatchesCoroutine();
        yield return FlushPendingApReducedByOpponentWatchCoroutine();
        yield return FlushPendingEffectDamageTakenFromOpponentWatchCoroutine();
    }

    private void ContinueAfterApReducedByOpponentWatch(Action continuation)
    {
        if ((!HasPendingLocalApReducedWatch && !HasPendingLocalEffectDamageTakenWatch)
            && !_apReducedWatchFlushRunning
            && !_effectDamageTakenWatchFlushRunning)
        {
            continuation?.Invoke();
            return;
        }

        StartCoroutine(CoContinueAfterApReducedByOpponentWatch(continuation));
    }

    private IEnumerator CoContinueAfterApReducedByOpponentWatch(Action continuation)
    {
        yield return FlushPendingApReducedByOpponentWatchCoroutine();
        yield return FlushPendingEffectDamageTakenFromOpponentWatchCoroutine();
        continuation?.Invoke();
    }

    /// <summary>
    /// 相手の Debuff で AP（または Both）が減った対象を監視キューへ載せる。
    /// オンラインで相手所有なら完了待ち ID を返し、Stat 同期に載せる。
    /// </summary>
    private int NotifyApReducedByOpponentIfNeeded(
        CardController sourceCard,
        PlayerType sourceOwner,
        CardController target,
        EffectData effect,
        int signedValue)
    {
        if (ShouldSkipAutomaticEffectsInTestPlay())
        {
            return 0;
        }

        if (effect == null
            || effect.type != EffectType.Debuff
            || signedValue >= 0
            || (effect.statTarget != EffectStatTarget.AP && effect.statTarget != EffectStatTarget.Both))
        {
            return 0;
        }

        if (target == null
            || target.Data == null
            || !target.Data.IsUnitLike()
            || target.CurrentHp <= 0
            || !IsCardOnBattleZone(target))
        {
            return 0;
        }

        PlayerType targetOwner = ResolveCardOwner(target.transform);
        if (targetOwner == sourceOwner)
        {
            return 0;
        }

        if (!UnitHasUnusedApReducedByOpponentWatch(target, targetOwner))
        {
            return 0;
        }

        if (IsOnlineBattle() && !_applyingRemoteBattleAction && targetOwner == PlayerType.Enemy)
        {
            int requestId = AllocateOnlineOnDestroyedRequestId();
            _pendingRemoteOnDestroyedRequestIds.Add(requestId);
            ShowOnlineEffectThinkOverlay();
            Debug.Log(
                $"[ApReducedWatch] wait remote owner request:{requestId} "
                + $"unit:{target.Data.cardName}(id:{target.Data.id})");
            return requestId;
        }

        EnqueueLocalApReducedWatch(target, onlineRequestId: 0);
        return 0;
    }

    private void EnqueueRemoteApReducedWatchFromStatSync(CardController unit, int requestId)
    {
        if (unit == null || unit.Data == null)
        {
            return;
        }

        PlayerType owner = ResolveCardOwner(unit.transform);
        if (owner != PlayerType.Player)
        {
            return;
        }

        if (!UnitHasUnusedApReducedByOpponentWatch(unit, owner))
        {
            if (requestId > 0)
            {
                SendOnlineApReducedWatchComplete(requestId);
            }

            return;
        }

        EnqueueLocalApReducedWatch(unit, requestId);
        if (_applyingRemoteBattleAction)
        {
            _resumeRemoteApReducedAfterRemoteApply = true;
        }
    }

    private void EnqueueLocalApReducedWatch(CardController unit, int onlineRequestId)
    {
        if (unit == null)
        {
            return;
        }

        for (int i = 0; i < _pendingApReducedWatches.Count; i++)
        {
            if (_pendingApReducedWatches[i].Unit == unit)
            {
                PendingApReducedWatch merged = _pendingApReducedWatches[i];
                if (merged.OnlineRequestId <= 0 && onlineRequestId > 0)
                {
                    merged.OnlineRequestId = onlineRequestId;
                    _pendingApReducedWatches[i] = merged;
                }

                return;
            }
        }

        _pendingApReducedWatches.Add(new PendingApReducedWatch
        {
            Unit = unit,
            OnlineRequestId = onlineRequestId
        });
        Debug.Log(
            $"[ApReducedWatch] queued {unit.Data?.cardName}(id:{unit.Data?.id}) request:{onlineRequestId}");
    }

    private void ResumeDeferredApReducedWatchIfNeeded()
    {
        if (!_resumeRemoteApReducedAfterRemoteApply)
        {
            return;
        }

        _resumeRemoteApReducedAfterRemoteApply = false;
        if (HasPendingLocalApReducedWatch)
        {
            StartCoroutine(FlushPendingApReducedByOpponentWatchCoroutine());
        }
    }

    private IEnumerator FlushPendingApReducedByOpponentWatchCoroutine()
    {
        if (_apReducedWatchFlushRunning)
        {
            yield return new WaitUntil(() => !_apReducedWatchFlushRunning);
            yield break;
        }

        if (_pendingApReducedWatches.Count == 0)
        {
            yield break;
        }

        _apReducedWatchFlushRunning = true;
        _apReducedWatchResolving = true;
        try
        {
            yield return null;

            while (_pendingApReducedWatches.Count > 0)
            {
                PendingApReducedWatch pending = _pendingApReducedWatches[0];
                _pendingApReducedWatches.RemoveAt(0);

                bool finished = false;
                ResolveApReducedByOpponentWatch(
                    pending.Unit,
                    pending.OnlineRequestId,
                    () => finished = true);
                yield return new WaitUntil(() => finished);
            }
        }
        finally
        {
            _apReducedWatchResolving = false;
            _apReducedWatchFlushRunning = false;
        }
    }

    private void ResolveApReducedByOpponentWatch(
        CardController unit,
        int onlineRequestId,
        Action onComplete)
    {
        void Finish()
        {
            if (onlineRequestId > 0)
            {
                SendOnlineApReducedWatchComplete(onlineRequestId);
            }

            onComplete?.Invoke();
        }

        if (unit == null
            || unit.Data == null
            || unit.CurrentHp <= 0
            || !IsCardOnBattleZone(unit))
        {
            Finish();
            return;
        }

        PlayerType ownerType = ResolveCardOwner(unit.transform);
        List<TimedEffectData> blocks = new List<TimedEffectData>();
        CollectApReducedByOpponentBlocks(unit, ownerType, blocks);
        if (blocks.Count == 0)
        {
            Finish();
            return;
        }

        Debug.Log(
            $"[ApReducedWatch] resolve {unit.Data.cardName}(id:{unit.Data.id}) blocks:{blocks.Count}");
        RunApReducedByOpponentBlocks(unit, ownerType, blocks, 0, Finish);
    }

    private bool UnitHasUnusedApReducedByOpponentWatch(CardController unit, PlayerType ownerType)
    {
        if (unit?.Data?.timedEffects == null)
        {
            return false;
        }

        for (int i = 0; i < unit.Data.timedEffects.Count; i++)
        {
            TimedEffectData timed = unit.Data.timedEffects[i];
            if (timed == null || !timed.IsOnApReducedByOpponentEffectResolutionBlock())
            {
                continue;
            }

            if (timed.oncePerTurn && HasUsedPaidActivationThisTurn(ownerType, unit, i))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private void CollectApReducedByOpponentBlocks(
        CardController unit,
        PlayerType ownerType,
        List<TimedEffectData> blocks)
    {
        if (unit?.Data?.timedEffects == null || blocks == null)
        {
            return;
        }

        EffectActivationContext ctx = BuildActivationContext(ownerType, unit);
        for (int i = 0; i < unit.Data.timedEffects.Count; i++)
        {
            TimedEffectData timed = unit.Data.timedEffects[i];
            if (timed == null || !timed.IsOnApReducedByOpponentEffectResolutionBlock())
            {
                continue;
            }

            if (timed.oncePerTurn && HasUsedPaidActivationThisTurn(ownerType, unit, i))
            {
                continue;
            }

            if (!EffectActivationEvaluator.AreTimedConditionsMet(timed, ctx))
            {
                continue;
            }

            blocks.Add(timed);
        }
    }

    private void RunApReducedByOpponentBlocks(
        CardController unit,
        PlayerType ownerType,
        List<TimedEffectData> blocks,
        int index,
        Action onComplete)
    {
        if (blocks == null || index >= blocks.Count)
        {
            onComplete?.Invoke();
            return;
        }

        TimedEffectData timed = blocks[index];
        if (timed == null)
        {
            RunApReducedByOpponentBlocks(unit, ownerType, blocks, index + 1, onComplete);
            return;
        }

        int timedIndex = unit.Data != null ? unit.Data.timedEffects.IndexOf(timed) : -1;
        if (timed.oncePerTurn && timedIndex >= 0)
        {
            MarkPaidActivationUsedThisTurn(ownerType, unit, timedIndex);
        }

        IReadOnlyList<EffectData> effects = timed.GetResolvedEffects();
        RunApReducedByOpponentEffectChain(
            unit,
            ownerType,
            effects,
            0,
            () => RunApReducedByOpponentBlocks(unit, ownerType, blocks, index + 1, onComplete));
    }

    private void RunApReducedByOpponentEffectChain(
        CardController sourceCard,
        PlayerType ownerType,
        IReadOnlyList<EffectData> effects,
        int effectIndex,
        Action onComplete)
    {
        if (effects == null || effectIndex >= effects.Count)
        {
            onComplete?.Invoke();
            return;
        }

        EffectData effect = effects[effectIndex];
        if (effect == null)
        {
            RunApReducedByOpponentEffectChain(sourceCard, ownerType, effects, effectIndex + 1, onComplete);
            return;
        }

        EffectActivationContext activationContext = BuildActivationContext(ownerType, sourceCard);
        if (!ShouldApplyChainedEffect(effect, activationContext, "OnApReducedByOpponentEffect"))
        {
            RunApReducedByOpponentEffectChain(sourceCard, ownerType, effects, effectIndex + 1, onComplete);
            return;
        }

        Action next = () => RunApReducedByOpponentEffectChain(
            sourceCard,
            ownerType,
            effects,
            effectIndex + 1,
            onComplete);

        if (EffectRequiresManualUnitSelection(effect))
        {
            CardController attackingUnit = attackFlowAttackerUnit ?? pendingOnAttackEffectResolvedAttacker;
            TryExecuteManualUnitSelectionEffect(
                sourceCard,
                ownerType,
                effect,
                attackingUnit,
                next,
                onSkipped: next);
            return;
        }

        ApplyEffectRespectingLookAsync(sourceCard, ownerType, effect, next);
    }

    private void SendOnlineApReducedWatchComplete(int requestId)
    {
        if (requestId <= 0 || !IsOnlineBattle())
        {
            return;
        }

        int ownerDeckRemain = cardGameRule != null ? cardGameRule.GetRemainingCount() : -1;
        SendOnlineBattleMessage(EosOnlineBattleMessage.CreateOnDestroyedComplete(
            OnlineOnDestroyedCompletePayload.ToJson(requestId, ownerDeckRemain)));
        Debug.Log($"[ApReducedWatch][Online] complete sent request:{requestId}");
    }
}
