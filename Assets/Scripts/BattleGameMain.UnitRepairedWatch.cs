using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ユニットが実際に HP 回復したときの監視（例: GD02-085 フォウ・ムラサメ）。
/// 回復したユニット本体と搭乗パイロットの OnUnitRepaired を解決する。
/// </summary>
public partial class BattleGameMain
{
    private struct UnitRepairedWatchBlock
    {
        public CardController EffectSource;
        public TimedEffectData Timed;
        public int BlockIndex;
    }

    /// <summary>実際に HP が増えたときだけ呼ぶ（0 回復は呼ばない）。</summary>
    private void NotifyUnitRepaired(CardController repairedUnit, Action onComplete = null)
    {
        if (ShouldSkipAutomaticEffectsInTestPlay())
        {
            onComplete?.Invoke();
            return;
        }

        if (repairedUnit == null || repairedUnit.Data == null || !repairedUnit.Data.IsUnitLike())
        {
            onComplete?.Invoke();
            return;
        }

        PlayerType ownerType = ResolveCardOwner(repairedUnit.transform);
        List<UnitRepairedWatchBlock> blocks = new List<UnitRepairedWatchBlock>();

        EffectActivationContext unitContext = BuildUnitRepairedActivationContext(
            ownerType,
            repairedUnit,
            repairedUnit);
        CollectUnitRepairedBlocks(repairedUnit, ownerType, unitContext, blocks);

        CardController pilot = repairedUnit.MountedPilot;
        if (pilot != null && pilot.Data != null)
        {
            EffectActivationContext pilotContext = BuildUnitRepairedActivationContext(
                ownerType,
                pilot,
                repairedUnit);
            CollectUnitRepairedBlocks(pilot, ownerType, pilotContext, blocks);
        }

        if (blocks.Count == 0)
        {
            onComplete?.Invoke();
            return;
        }

        Debug.Log(
            $"[OnUnitRepaired] unit:{repairedUnit.Data.cardName}(id:{repairedUnit.Data.id}) "
            + $"blocks:{blocks.Count} owner:{ownerType}");

        RunUnitRepairedWatchTimedBlocks(ownerType, blocks, 0, onComplete);
    }

    private void NotifyUnitRepairedUnits(List<CardController> repairedUnits, Action onComplete = null)
    {
        if (repairedUnits == null || repairedUnits.Count == 0)
        {
            onComplete?.Invoke();
            return;
        }

        RunNotifyUnitRepairedUnits(repairedUnits, 0, onComplete);
    }

    private void RunNotifyUnitRepairedUnits(
        List<CardController> repairedUnits,
        int index,
        Action onComplete)
    {
        if (repairedUnits == null || index >= repairedUnits.Count)
        {
            onComplete?.Invoke();
            return;
        }

        NotifyUnitRepaired(
            repairedUnits[index],
            () => RunNotifyUnitRepairedUnits(repairedUnits, index + 1, onComplete));
    }

    private EffectActivationContext BuildUnitRepairedActivationContext(
        PlayerType ownerType,
        CardController effectSource,
        CardController repairedUnit)
    {
        return new EffectActivationContext(
            ownerType,
            effectSource,
            playerBattleZoneCards,
            enemyBattleZoneCards,
            CollectHandControllers(cardGameRule),
            CollectHandControllers(enemyCardGameRule),
            isOwnerTurn: ownerType == currentPlayerType,
            mountHostUnit: repairedUnit,
            mountedPilot: repairedUnit != null ? repairedUnit.MountedPilot : null,
            observedCards: GetActiveObservedCardsForActivation(),
            ownerTrashCardIds: cardGameRule.GetTrashCardIds(),
            opponentTrashCardIds: enemyCardGameRule.GetTrashCardIds(),
            priorChainDealtDamage: GetEffectChainDealtDamage(),
            ownerActivatedSpecialMoveCommandThisTurn: HasOwnerActivatedSpecialMoveCommandThisTurn(ownerType),
            ownerHasDeployedBase: HasActiveDeployedBaseForRuleSide(ToRuleSide(ownerType)),
            ownerActivatedResourceByEffectThisTurn: HasOwnerActivatedResourceByEffectThisTurn(ownerType));
    }

    private void CollectUnitRepairedBlocks(
        CardController effectSource,
        PlayerType ownerType,
        EffectActivationContext activationContext,
        List<UnitRepairedWatchBlock> blocks)
    {
        if (effectSource?.Data?.timedEffects == null || blocks == null)
        {
            return;
        }

        for (int i = 0; i < effectSource.Data.timedEffects.Count; i++)
        {
            TimedEffectData timed = effectSource.Data.timedEffects[i];
            if (timed == null || !timed.IsOnUnitRepairedResolutionBlock())
            {
                continue;
            }

            if (timed.oncePerTurn && HasUsedPaidActivationThisTurn(ownerType, effectSource, i))
            {
                continue;
            }

            if (!CanRunTimedBlockAtChainTime(timed, activationContext, "OnUnitRepaired"))
            {
                continue;
            }

            blocks.Add(new UnitRepairedWatchBlock
            {
                EffectSource = effectSource,
                Timed = timed,
                BlockIndex = i
            });
        }
    }

    private void RunUnitRepairedWatchTimedBlocks(
        PlayerType ownerType,
        List<UnitRepairedWatchBlock> blocks,
        int index,
        Action onComplete)
    {
        if (blocks == null || index >= blocks.Count)
        {
            onComplete?.Invoke();
            return;
        }

        UnitRepairedWatchBlock block = blocks[index];
        CardController effectSource = block.EffectSource;
        TimedEffectData timed = block.Timed;
        if (timed != null && timed.oncePerTurn && effectSource != null)
        {
            MarkPaidActivationUsedThisTurn(ownerType, effectSource, block.BlockIndex);
        }

        RunUnitRepairedEffectChain(
            effectSource,
            ownerType,
            timed != null ? timed.GetResolvedEffects() : null,
            0,
            () => RunUnitRepairedWatchTimedBlocks(ownerType, blocks, index + 1, onComplete));
    }

    private void RunUnitRepairedEffectChain(
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
            RunUnitRepairedEffectChain(sourceCard, ownerType, effects, effectIndex + 1, onComplete);
            return;
        }

        EffectActivationContext activationContext = BuildActivationContext(ownerType, sourceCard);
        if (!ShouldApplyChainedEffect(effect, activationContext, "OnUnitRepaired"))
        {
            RunUnitRepairedEffectChain(sourceCard, ownerType, effects, effectIndex + 1, onComplete);
            return;
        }

        if (EffectRequiresManualUnitSelection(effect))
        {
            TryExecuteManualUnitSelectionEffect(
                sourceCard,
                ownerType,
                effect,
                sourceCard,
                () => RunUnitRepairedEffectChain(sourceCard, ownerType, effects, effectIndex + 1, onComplete));
            return;
        }

        ApplyEffectRespectingLookAsync(
            sourceCard,
            ownerType,
            effect,
            () => RunUnitRepairedEffectChain(sourceCard, ownerType, effects, effectIndex + 1, onComplete));
    }
}
