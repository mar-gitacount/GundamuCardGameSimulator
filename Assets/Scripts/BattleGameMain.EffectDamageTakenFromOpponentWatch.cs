using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// GD02-010 等：相手の効果ダメージでこのユニットの HP が減ったときに割り込み解決する。
/// 戦闘ダメージ・自軍効果・無効化で 0 になった場合は発火しない。
/// </summary>
public partial class BattleGameMain
{
    private struct PendingEffectDamageTakenWatch
    {
        public CardController Unit;
        public PlayerType OwnerType;
        public int OnlineRequestId;
    }

    private readonly List<PendingEffectDamageTakenWatch> _pendingEffectDamageTakenWatches =
        new List<PendingEffectDamageTakenWatch>();
    private bool _effectDamageTakenWatchFlushRunning;
    private bool _resumeRemoteEffectDamageTakenAfterRemoteApply;
    private bool _effectDamageTakenWatchResolving;

    private bool HasPendingLocalEffectDamageTakenWatch => _pendingEffectDamageTakenWatches.Count > 0;

    /// <summary>
    /// 相手の効果ダメージで実 HP が減った対象を監視キューへ載せる。
    /// オンラインで相手所有なら完了待ち ID を返す。
    /// </summary>
    private int NotifyOpponentEffectDamageTakenIfNeeded(
        CardController sourceCard,
        PlayerType sourceOwner,
        CardController target,
        int hpBefore)
    {
        if (ShouldSkipAutomaticEffectsInTestPlay())
        {
            return 0;
        }

        if (target == null
            || target.Data == null
            || !target.Data.IsUnitLike()
            || target.CurrentHp >= hpBefore)
        {
            return 0;
        }

        PlayerType targetOwner = ResolveCardOwner(target.transform);
        if (targetOwner == sourceOwner)
        {
            return 0;
        }

        if (!UnitHasUnusedEffectDamageTakenFromOpponentWatch(target, targetOwner))
        {
            return 0;
        }

        if (IsOnlineBattle() && !_applyingRemoteBattleAction && targetOwner == PlayerType.Enemy)
        {
            int requestId = AllocateOnlineOnDestroyedRequestId();
            _pendingRemoteOnDestroyedRequestIds.Add(requestId);
            ShowOnlineEffectThinkOverlay();
            Debug.Log(
                $"[EffectDmgTakenWatch] wait remote owner request:{requestId} "
                + $"unit:{target.Data.cardName}(id:{target.Data.id})");
            return requestId;
        }

        EnqueueLocalEffectDamageTakenWatch(target, targetOwner, onlineRequestId: 0);
        return 0;
    }

    private void EnqueueRemoteEffectDamageTakenWatchFromDamageSync(CardController unit, int requestId)
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

        if (!UnitHasUnusedEffectDamageTakenFromOpponentWatch(unit, owner))
        {
            if (requestId > 0)
            {
                SendOnlineEffectDamageTakenWatchComplete(requestId);
            }

            return;
        }

        EnqueueLocalEffectDamageTakenWatch(unit, owner, requestId);
        if (_applyingRemoteBattleAction)
        {
            _resumeRemoteEffectDamageTakenAfterRemoteApply = true;
        }
    }

    private void EnqueueLocalEffectDamageTakenWatch(CardController unit, PlayerType ownerType, int onlineRequestId)
    {
        if (unit == null)
        {
            return;
        }

        for (int i = 0; i < _pendingEffectDamageTakenWatches.Count; i++)
        {
            if (_pendingEffectDamageTakenWatches[i].Unit == unit)
            {
                PendingEffectDamageTakenWatch merged = _pendingEffectDamageTakenWatches[i];
                if (merged.OnlineRequestId <= 0 && onlineRequestId > 0)
                {
                    merged.OnlineRequestId = onlineRequestId;
                    _pendingEffectDamageTakenWatches[i] = merged;
                }

                return;
            }
        }

        _pendingEffectDamageTakenWatches.Add(new PendingEffectDamageTakenWatch
        {
            Unit = unit,
            OwnerType = ownerType,
            OnlineRequestId = onlineRequestId
        });
        Debug.Log(
            $"[EffectDmgTakenWatch] queued {unit.Data?.cardName}(id:{unit.Data?.id}) request:{onlineRequestId}");
    }

    private void ResumeDeferredEffectDamageTakenWatchIfNeeded()
    {
        if (!_resumeRemoteEffectDamageTakenAfterRemoteApply)
        {
            return;
        }

        _resumeRemoteEffectDamageTakenAfterRemoteApply = false;
        if (HasPendingLocalEffectDamageTakenWatch)
        {
            StartCoroutine(FlushPendingEffectDamageTakenFromOpponentWatchCoroutine());
        }
    }

    private IEnumerator FlushPendingEffectDamageTakenFromOpponentWatchCoroutine()
    {
        if (_effectDamageTakenWatchFlushRunning)
        {
            yield return new WaitUntil(() => !_effectDamageTakenWatchFlushRunning);
            yield break;
        }

        if (_pendingEffectDamageTakenWatches.Count == 0)
        {
            yield break;
        }

        _effectDamageTakenWatchFlushRunning = true;
        _effectDamageTakenWatchResolving = true;
        try
        {
            yield return null;

            while (_pendingEffectDamageTakenWatches.Count > 0)
            {
                PendingEffectDamageTakenWatch pending = _pendingEffectDamageTakenWatches[0];
                _pendingEffectDamageTakenWatches.RemoveAt(0);

                bool finished = false;
                ResolveEffectDamageTakenFromOpponentWatch(
                    pending.Unit,
                    pending.OwnerType,
                    pending.OnlineRequestId,
                    () => finished = true);
                yield return new WaitUntil(() => finished);
            }
        }
        finally
        {
            _effectDamageTakenWatchResolving = false;
            _effectDamageTakenWatchFlushRunning = false;
        }
    }

    private void ResolveEffectDamageTakenFromOpponentWatch(
        CardController unit,
        PlayerType ownerType,
        int onlineRequestId,
        Action onComplete)
    {
        void Finish()
        {
            if (onlineRequestId > 0)
            {
                SendOnlineEffectDamageTakenWatchComplete(onlineRequestId);
            }

            onComplete?.Invoke();
        }

        if (unit == null || unit.Data == null)
        {
            Finish();
            return;
        }

        List<TimedEffectData> blocks = new List<TimedEffectData>();
        CollectEffectDamageTakenFromOpponentBlocks(unit, ownerType, blocks);
        if (blocks.Count == 0)
        {
            Finish();
            return;
        }

        Debug.Log(
            $"[EffectDmgTakenWatch] resolve {unit.Data.cardName}(id:{unit.Data.id}) blocks:{blocks.Count}");
        RunEffectDamageTakenFromOpponentBlocks(unit, ownerType, blocks, 0, Finish);
    }

    private bool UnitHasUnusedEffectDamageTakenFromOpponentWatch(CardController unit, PlayerType ownerType)
    {
        if (unit?.Data?.timedEffects == null)
        {
            return false;
        }

        for (int i = 0; i < unit.Data.timedEffects.Count; i++)
        {
            TimedEffectData timed = unit.Data.timedEffects[i];
            if (timed == null || !timed.IsOnEffectDamageTakenFromOpponentResolutionBlock())
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

    private void CollectEffectDamageTakenFromOpponentBlocks(
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
            if (timed == null || !timed.IsOnEffectDamageTakenFromOpponentResolutionBlock())
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

    private void RunEffectDamageTakenFromOpponentBlocks(
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
            RunEffectDamageTakenFromOpponentBlocks(unit, ownerType, blocks, index + 1, onComplete);
            return;
        }

        int timedIndex = unit.Data != null ? unit.Data.timedEffects.IndexOf(timed) : -1;
        if (timed.oncePerTurn && timedIndex >= 0)
        {
            MarkPaidActivationUsedThisTurn(ownerType, unit, timedIndex);
        }

        IReadOnlyList<EffectData> effects = timed.GetResolvedEffects();
        RunEffectDamageTakenFromOpponentEffectChain(
            unit,
            ownerType,
            effects,
            0,
            () => RunEffectDamageTakenFromOpponentBlocks(unit, ownerType, blocks, index + 1, onComplete));
    }

    private void RunEffectDamageTakenFromOpponentEffectChain(
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
            RunEffectDamageTakenFromOpponentEffectChain(
                sourceCard, ownerType, effects, effectIndex + 1, onComplete);
            return;
        }

        EffectActivationContext activationContext = BuildActivationContext(ownerType, sourceCard);
        if (!ShouldApplyChainedEffect(effect, activationContext, "OnEffectDamageTakenFromOpponent"))
        {
            RunEffectDamageTakenFromOpponentEffectChain(
                sourceCard, ownerType, effects, effectIndex + 1, onComplete);
            return;
        }

        ApplyEffectRespectingLookAsync(
            sourceCard,
            ownerType,
            effect,
            () => RunEffectDamageTakenFromOpponentEffectChain(
                sourceCard, ownerType, effects, effectIndex + 1, onComplete));
    }

    private void SendOnlineEffectDamageTakenWatchComplete(int requestId)
    {
        if (requestId <= 0 || !IsOnlineBattle())
        {
            return;
        }

        int ownerDeckRemain = cardGameRule != null ? cardGameRule.GetRemainingCount() : -1;
        SendOnlineBattleMessage(EosOnlineBattleMessage.CreateOnDestroyedComplete(
            OnlineOnDestroyedCompletePayload.ToJson(requestId, ownerDeckRemain)));
        Debug.Log($"[EffectDmgTakenWatch][Online] complete sent request:{requestId}");
    }
}
