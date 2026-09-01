using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>味方ユニットの攻撃宣言監視（The-O 等の【搭乗中】他 Repair ユニット攻撃時効果）。</summary>
public partial class BattleGameMain
{
    /// <summary>OnAllyUnitAttack の Lv 比較参照（攻撃ユニット）。手動選択フィルタ用。</summary>
    private CardController _allyUnitAttackStatCompareReference;

    /// <summary>
    /// 味方ユニットが攻撃宣言した直後（攻撃者自身の OnAttack 非戦闘効果の後）に呼ぶ。
    /// </summary>
    private void NotifyAllyUnitAttack(
        PlayerType ownerType,
        CardController attackingUnit,
        Action onComplete = null)
    {
        if (attackingUnit == null || attackingUnit.Data == null || !attackingUnit.Data.IsUnitLike())
        {
            onComplete?.Invoke();
            return;
        }

        if (attackingUnit.GetTurnEndRepairAmount() <= 0)
        {
            onComplete?.Invoke();
            return;
        }

        List<CardController> watchers = CollectOwnerBattleUnitsWithAllyUnitAttackWatch(ownerType, attackingUnit);
        if (watchers.Count == 0)
        {
            onComplete?.Invoke();
            return;
        }

        Debug.Log(
            $"[AllyUnitAttackWatch] attacker:{attackingUnit.Data.cardName}(id:{attackingUnit.Data.id}) "
            + $"watchers:{watchers.Count} owner:{ownerType}");

        RunAllyUnitAttackWatchUnits(ownerType, attackingUnit, watchers, 0, onComplete);
    }

    private List<CardController> CollectOwnerBattleUnitsWithAllyUnitAttackWatch(
        PlayerType ownerType,
        CardController attackingUnit)
    {
        List<CardController> result = new List<CardController>();
        List<CardController> zone = ownerType == PlayerType.Player
            ? playerBattleZoneCards
            : enemyBattleZoneCards;
        if (zone == null)
        {
            return result;
        }

        for (int i = 0; i < zone.Count; i++)
        {
            CardController unit = zone[i];
            if (unit == null || unit.Data == null || !unit.Data.IsUnitLike() || unit.CurrentHp <= 0)
            {
                continue;
            }

            if (attackingUnit != null && ReferenceEquals(unit, attackingUnit))
            {
                continue;
            }

            if (unit.Data.timedEffects == null)
            {
                continue;
            }

            for (int t = 0; t < unit.Data.timedEffects.Count; t++)
            {
                if (unit.Data.timedEffects[t].IsOnAllyUnitAttackResolutionBlock())
                {
                    result.Add(unit);
                    break;
                }
            }
        }

        return result;
    }

    private EffectActivationContext BuildAllyUnitAttackActivationContext(
        PlayerType ownerType,
        CardController watcher,
        CardController attackingUnit)
    {
        Gundam2024RuleScript.PlayerState ownerState = ownerType == PlayerType.Player
            ? gundamRule.Player
            : gundamRule.Enemy;

        return new EffectActivationContext(
            ownerType,
            watcher,
            playerBattleZoneCards,
            enemyBattleZoneCards,
            CollectHandControllers(cardGameRule),
            CollectHandControllers(enemyCardGameRule),
            isOwnerTurn: ownerType == currentPlayerType,
            mountHostUnit: attackingUnit,
            mountedPilot: watcher.MountedPilot,
            observedCards: GetActiveObservedCardsForActivation(),
            ownerTrashCardIds: cardGameRule.GetTrashCardIds(),
            opponentTrashCardIds: enemyCardGameRule.GetTrashCardIds(),
            priorChainDealtDamage: GetEffectChainDealtDamage(),
            ownerHasDeployedBase: HasActiveDeployedBaseForRuleSide(ToRuleSide(ownerType)),
            ownerTotalLevel: ownerState.TotalLevel);
    }

    private void RunAllyUnitAttackWatchUnits(
        PlayerType ownerType,
        CardController attackingUnit,
        List<CardController> watchers,
        int index,
        Action onComplete)
    {
        if (watchers == null || index >= watchers.Count)
        {
            onComplete?.Invoke();
            return;
        }

        CardController watcher = watchers[index];
        if (watcher == null || watcher.Data == null)
        {
            RunAllyUnitAttackWatchUnits(ownerType, attackingUnit, watchers, index + 1, onComplete);
            return;
        }

        EffectActivationContext activationContext = BuildAllyUnitAttackActivationContext(
            ownerType,
            watcher,
            attackingUnit);

        List<TimedEffectData> blocks = new List<TimedEffectData>();
        for (int t = 0; t < watcher.Data.timedEffects.Count; t++)
        {
            TimedEffectData timed = watcher.Data.timedEffects[t];
            if (timed == null || !timed.IsOnAllyUnitAttackResolutionBlock())
            {
                continue;
            }

            if (!CanRunTimedBlockAtChainTime(timed, activationContext, "OnAllyUnitAttack"))
            {
                continue;
            }

            blocks.Add(timed);
        }

        if (blocks.Count == 0)
        {
            RunAllyUnitAttackWatchUnits(ownerType, attackingUnit, watchers, index + 1, onComplete);
            return;
        }

        RunAllyUnitAttackWatchTimedBlocks(
            ownerType,
            watcher,
            attackingUnit,
            blocks,
            0,
            () => RunAllyUnitAttackWatchUnits(ownerType, attackingUnit, watchers, index + 1, onComplete));
    }

    private void RunAllyUnitAttackWatchTimedBlocks(
        PlayerType ownerType,
        CardController watcher,
        CardController attackingUnit,
        List<TimedEffectData> blocks,
        int blockIndex,
        Action onComplete)
    {
        if (blocks == null || blockIndex >= blocks.Count)
        {
            onComplete?.Invoke();
            return;
        }

        TimedEffectData timed = blocks[blockIndex];
        IReadOnlyList<EffectData> effects = timed.GetResolvedEffects();
        if (effects == null || effects.Count == 0)
        {
            RunAllyUnitAttackWatchTimedBlocks(
                ownerType, watcher, attackingUnit, blocks, blockIndex + 1, onComplete);
            return;
        }

        BeginEffectChainObservationScope(forceNewRoot: blockIndex == 0);
        _allyUnitAttackStatCompareReference = attackingUnit;

        RunAllyUnitAttackEffectChain(
            watcher,
            ownerType,
            attackingUnit,
            effects,
            0,
            () =>
            {
                _allyUnitAttackStatCompareReference = null;
                EndEffectChainObservationScope();
                RunAllyUnitAttackWatchTimedBlocks(
                    ownerType,
                    watcher,
                    attackingUnit,
                    blocks,
                    blockIndex + 1,
                    onComplete);
            });
    }

    private void RunAllyUnitAttackEffectChain(
        CardController sourceCard,
        PlayerType ownerType,
        CardController attackingUnit,
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
            RunAllyUnitAttackEffectChain(
                sourceCard, ownerType, attackingUnit, effects, effectIndex + 1, onComplete);
            return;
        }

        EffectActivationContext activationContext = BuildAllyUnitAttackActivationContext(
            ownerType,
            sourceCard,
            attackingUnit);
        if (!ShouldApplyChainedEffect(effect, activationContext, "OnAllyUnitAttack"))
        {
            RunAllyUnitAttackEffectChain(
                sourceCard, ownerType, attackingUnit, effects, effectIndex + 1, onComplete);
            return;
        }

        if (EffectRequiresManualUnitSelection(effect))
        {
            List<CardController> candidates = ResolveSelectableEffectTargets(sourceCard, ownerType, effect);
            if (candidates.Count == 0)
            {
                RunAllyUnitAttackEffectChain(
                    sourceCard, ownerType, attackingUnit, effects, effectIndex + 1, onComplete);
                return;
            }

            if (RequiresInteractiveManualUnitSelectionUi(ownerType))
            {
                StartCoroutine(CoRunAllyUnitAttackPlayerManualSelection(
                    sourceCard,
                    ownerType,
                    effect,
                    candidates,
                    () => RunAllyUnitAttackEffectChain(
                        sourceCard, ownerType, attackingUnit, effects, effectIndex + 1, onComplete),
                    () => RunAllyUnitAttackEffectChain(
                        sourceCard, ownerType, attackingUnit, effects, effectIndex + 1, onComplete)));
                return;
            }

            TryExecuteManualUnitSelectionEffect(
                sourceCard,
                ownerType,
                effect,
                null,
                () => RunAllyUnitAttackEffectChain(
                    sourceCard, ownerType, attackingUnit, effects, effectIndex + 1, onComplete),
                () => RunAllyUnitAttackEffectChain(
                    sourceCard, ownerType, attackingUnit, effects, effectIndex + 1, onComplete));
            return;
        }

        ApplyEffect(sourceCard, ownerType, effect);
        RunAllyUnitAttackEffectChain(
            sourceCard, ownerType, attackingUnit, effects, effectIndex + 1, onComplete);
    }

    private IEnumerator CoRunAllyUnitAttackPlayerManualSelection(
        CardController sourceCard,
        PlayerType ownerType,
        EffectData effect,
        List<CardController> candidates,
        Action onDone,
        Action onSkipped)
    {
        yield return null;
        yield return null;
        yield return null;

        bool resolved = false;
        OpenManualUnitTargetSelectionUI(
            sourceCard,
            ownerType,
            effect,
            candidates,
            null,
            picked =>
            {
                resolved = true;
                if (picked != null)
                {
                    ApplyEffectToSpecificTargets(
                        sourceCard,
                        ownerType,
                        effect,
                        new List<CardController> { picked });
                    onDone?.Invoke();
                }
                else
                {
                    onSkipped?.Invoke();
                }
            });

        yield return new WaitUntil(() => resolved);
    }
}
