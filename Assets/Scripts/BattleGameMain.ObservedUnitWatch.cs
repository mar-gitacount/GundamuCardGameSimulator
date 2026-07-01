using System.Collections.Generic;
using UnityEngine;

/// <summary>MarkObservedUnit によるユニット監視と OnObservedUnitTrigger 報酬の解決。</summary>
public partial class BattleGameMain
{
    private sealed class ObservedUnitWatchEntry
    {
        public CardController SourceCard;
        public PlayerType Owner;
        public ObservedUnitTriggerKind TriggerKind;
        public readonly HashSet<int> MarkedInstanceIds = new HashSet<int>();
    }

    private readonly List<ObservedUnitWatchEntry> _observedUnitWatches = new List<ObservedUnitWatchEntry>();

    private void ClearObservedUnitWatches()
    {
        _observedUnitWatches.Clear();
    }

    private void RegisterObservedUnitWatch(
        CardController sourceCard,
        PlayerType ownerType,
        EffectData effect,
        List<CardController> markedUnits)
    {
        if (sourceCard == null || sourceCard.Data == null || effect == null || markedUnits == null || markedUnits.Count == 0)
        {
            return;
        }

        ObservedUnitTriggerKind triggerKind = effect.ResolveObservedUnitTriggerKind();
        ObservedUnitWatchEntry entry = new ObservedUnitWatchEntry
        {
            SourceCard = sourceCard,
            Owner = ownerType,
            TriggerKind = triggerKind,
        };

        for (int i = 0; i < markedUnits.Count; i++)
        {
            CardController unit = markedUnits[i];
            if (unit == null || unit.Data == null || !unit.Data.IsUnitLike())
            {
                continue;
            }

            AssignBattleInstanceIdIfNeeded(unit);
            if (unit.BattleInstanceId > 0)
            {
                entry.MarkedInstanceIds.Add(unit.BattleInstanceId);
            }
        }

        if (entry.MarkedInstanceIds.Count == 0)
        {
            return;
        }

        _observedUnitWatches.Add(entry);
        Debug.Log(
            $"[ObservedUnitWatch] 登録 source:{sourceCard.Data.cardName}(id:{sourceCard.Data.id}) "
            + $"trigger:{triggerKind} units:{entry.MarkedInstanceIds.Count}");
    }

    private void PruneObservedUnitWatchesOnCardRemoved(CardController removedCard)
    {
        if (removedCard == null)
        {
            return;
        }

        for (int i = _observedUnitWatches.Count - 1; i >= 0; i--)
        {
            ObservedUnitWatchEntry entry = _observedUnitWatches[i];
            if (entry.SourceCard == removedCard)
            {
                _observedUnitWatches.RemoveAt(i);
                continue;
            }

            if (removedCard.BattleInstanceId > 0)
            {
                entry.MarkedInstanceIds.Remove(removedCard.BattleInstanceId);
            }

            if (entry.MarkedInstanceIds.Count == 0 || entry.SourceCard == null || entry.SourceCard.Data == null)
            {
                _observedUnitWatches.RemoveAt(i);
            }
        }
    }

    private void TriggerObservedUnitWatchEffects(
        CardController destroyedUnit,
        PlayerType destroyedOwner,
        CardController killer,
        PlayerType killerOwner,
        ObservedUnitTriggerKind triggerKind,
        System.Action onComplete)
    {
        if (killer == null || killer.Data == null || killer.BattleInstanceId <= 0)
        {
            onComplete?.Invoke();
            return;
        }

        List<ObservedUnitWatchEntry> matches = new List<ObservedUnitWatchEntry>();
        for (int i = 0; i < _observedUnitWatches.Count; i++)
        {
            ObservedUnitWatchEntry entry = _observedUnitWatches[i];
            if (entry == null
                || entry.SourceCard == null
                || entry.SourceCard.Data == null
                || entry.TriggerKind != triggerKind
                || !entry.MarkedInstanceIds.Contains(killer.BattleInstanceId))
            {
                continue;
            }

            if (!HasEffectTiming(entry.SourceCard.Data, EffectTiming.OnObservedUnitTrigger))
            {
                continue;
            }

            matches.Add(entry);
        }

        if (matches.Count == 0)
        {
            onComplete?.Invoke();
            return;
        }

        Debug.Log(
            $"[OnObservedUnitTrigger] actor:{killer.Data.cardName}(id:{killer.Data.id}) "
            + $"trigger:{triggerKind} victim:{destroyedUnit?.Data?.cardName} watches:{matches.Count}");
        RunObservedUnitWatchRewardQueue(matches, 0, killerOwner, killer, destroyedUnit, triggerKind, onComplete);
    }

    private void RunObservedUnitWatchRewardQueue(
        List<ObservedUnitWatchEntry> matches,
        int matchIndex,
        PlayerType actingOwner,
        CardController actingUnit,
        CardController triggerContextUnit,
        ObservedUnitTriggerKind triggerKind,
        System.Action onComplete)
    {
        if (matches == null || matchIndex >= matches.Count)
        {
            onComplete?.Invoke();
            return;
        }

        ObservedUnitWatchEntry entry = matches[matchIndex];
        ResolveOnObservedUnitTriggerRewards(
            entry.SourceCard,
            entry.Owner,
            actingUnit,
            triggerContextUnit,
            triggerKind,
            () => RunObservedUnitWatchRewardQueue(
                matches,
                matchIndex + 1,
                actingOwner,
                actingUnit,
                triggerContextUnit,
                triggerKind,
                onComplete));
    }

    private void ResolveOnObservedUnitTriggerRewards(
        CardController sourceCard,
        PlayerType ownerType,
        CardController actingUnit,
        CardController triggerContextUnit,
        ObservedUnitTriggerKind triggerKind,
        System.Action onComplete)
    {
        if (sourceCard == null || sourceCard.Data == null)
        {
            onComplete?.Invoke();
            return;
        }

        EffectActivationContext activationContext =
            BuildObservedUnitTriggerActivationContext(ownerType, sourceCard, actingUnit, triggerContextUnit);
        List<TimedEffectData> blocks = new List<TimedEffectData>();
        for (int i = 0; i < sourceCard.Data.timedEffects.Count; i++)
        {
            TimedEffectData timed = sourceCard.Data.timedEffects[i];
            if (timed == null
                || !timed.IsOnObservedUnitTriggerResolutionBlock()
                || !timed.MatchesObservedUnitTriggerKind(triggerKind))
            {
                continue;
            }

            if (!EffectActivationEvaluator.AreTimedConditionsMet(timed, activationContext))
            {
                continue;
            }

            blocks.Add(timed);
        }

        if (blocks.Count == 0)
        {
            onComplete?.Invoke();
            return;
        }

        RunOnObservedUnitTriggerTimedBlocks(
            sourceCard,
            ownerType,
            actingUnit,
            triggerContextUnit,
            blocks,
            0,
            onComplete);
    }

    private void RunOnObservedUnitTriggerTimedBlocks(
        CardController sourceCard,
        PlayerType ownerType,
        CardController actingUnit,
        CardController triggerContextUnit,
        List<TimedEffectData> blocks,
        int blockIndex,
        System.Action onComplete)
    {
        if (blocks == null || blockIndex >= blocks.Count)
        {
            onComplete?.Invoke();
            return;
        }

        TimedEffectData block = blocks[blockIndex];
        EffectActivationContext activationContext =
            BuildObservedUnitTriggerActivationContext(ownerType, sourceCard, actingUnit, triggerContextUnit);
        if (!CanRunTimedBlockAtChainTime(block, activationContext, "OnObservedUnitTrigger"))
        {
            RunOnObservedUnitTriggerTimedBlocks(
                sourceCard,
                ownerType,
                actingUnit,
                triggerContextUnit,
                blocks,
                blockIndex + 1,
                onComplete);
            return;
        }

        TryExecuteOnObservedUnitTriggerEffectChain(
            sourceCard,
            ownerType,
            actingUnit,
            triggerContextUnit,
            block.GetResolvedEffects(),
            0,
            () => RunOnObservedUnitTriggerTimedBlocks(
                sourceCard,
                ownerType,
                actingUnit,
                triggerContextUnit,
                blocks,
                blockIndex + 1,
                onComplete));
    }

    private void TryExecuteOnObservedUnitTriggerEffectChain(
        CardController sourceCard,
        PlayerType ownerType,
        CardController actingUnit,
        CardController triggerContextUnit,
        IReadOnlyList<EffectData> effects,
        int index,
        System.Action onDone)
    {
        if (effects == null || index >= effects.Count)
        {
            onDone?.Invoke();
            return;
        }

        EffectData effect = effects[index];
        if (effect == null)
        {
            TryExecuteOnObservedUnitTriggerEffectChain(
                sourceCard, ownerType, actingUnit, triggerContextUnit, effects, index + 1, onDone);
            return;
        }

        if (effect.type == EffectType.MarkObservedUnit)
        {
            TryExecuteOnObservedUnitTriggerEffectChain(
                sourceCard, ownerType, actingUnit, triggerContextUnit, effects, index + 1, onDone);
            return;
        }

        EffectActivationContext activationContext =
            BuildObservedUnitTriggerActivationContext(ownerType, sourceCard, actingUnit, triggerContextUnit);
        if (!ShouldApplyChainedEffect(effect, activationContext, "OnObservedUnitTrigger"))
        {
            TryExecuteOnObservedUnitTriggerEffectChain(
                sourceCard, ownerType, actingUnit, triggerContextUnit, effects, index + 1, onDone);
            return;
        }

        if (TryExecutePriorChainPickedTargetEffect(
            sourceCard,
            ownerType,
            effect,
            () => TryExecuteOnObservedUnitTriggerEffectChain(
                sourceCard, ownerType, actingUnit, triggerContextUnit, effects, index + 1, onDone)))
        {
            return;
        }

        if (effect.type == EffectType.DeployUnit && effect.RequiresDeployUnitZoneSelection())
        {
            ApplyDeployUnitEffect(
                sourceCard,
                ownerType,
                effect,
                () => TryExecuteOnObservedUnitTriggerEffectChain(
                    sourceCard, ownerType, actingUnit, triggerContextUnit, effects, index + 1, onDone));
            return;
        }

        if (EffectRequiresManualHandSelection(effect))
        {
            TryExecuteManualHandSelectionEffect(
                sourceCard,
                ownerType,
                effect,
                () => TryExecuteOnObservedUnitTriggerEffectChain(
                    sourceCard, ownerType, actingUnit, triggerContextUnit, effects, index + 1, onDone));
            return;
        }

        if (EffectRequiresManualUnitSelection(effect))
        {
            TryExecuteManualUnitSelectionEffect(
                sourceCard,
                ownerType,
                effect,
                null,
                () => TryExecuteOnObservedUnitTriggerEffectChain(
                    sourceCard, ownerType, actingUnit, triggerContextUnit, effects, index + 1, onDone));
            return;
        }

        if (IsFieldWideUnitDamageEffect(effect))
        {
            TryApplyFieldWideDamageWithPreviewAsync(
                sourceCard,
                ownerType,
                effect,
                () => TryExecuteOnObservedUnitTriggerEffectChain(
                    sourceCard, ownerType, actingUnit, triggerContextUnit, effects, index + 1, onDone));
            return;
        }

        ApplyEffectRespectingLookAsync(
            sourceCard,
            ownerType,
            effect,
            () => TryExecuteOnObservedUnitTriggerEffectChain(
                sourceCard, ownerType, actingUnit, triggerContextUnit, effects, index + 1, onDone));
    }

    private EffectActivationContext BuildObservedUnitTriggerActivationContext(
        PlayerType ownerType,
        CardController sourceCard,
        CardController actingUnit,
        CardController triggerContextUnit)
    {
        EffectActivationContext baseContext = BuildActivationContext(ownerType, sourceCard);
        CardController mountHost = actingUnit != null ? actingUnit : triggerContextUnit;
        return new EffectActivationContext(
            baseContext.OwnerType,
            baseContext.SourceCard,
            baseContext.PlayerBattleZone,
            baseContext.EnemyBattleZone,
            baseContext.PlayerHand,
            baseContext.EnemyHand,
            baseContext.IsOwnerTurn,
            mountHost,
            mountHost != null ? mountHost.MountedPilot : null,
            baseContext.ObservedCards,
            baseContext.OwnerTrashCardIds,
            baseContext.OpponentTrashCardIds,
            baseContext.FrozenOwnerBattleAliveUnitCount,
            baseContext.PriorChainDealtDamage);
    }
}
