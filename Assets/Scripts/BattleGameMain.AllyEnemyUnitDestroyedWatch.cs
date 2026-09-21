using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 味方ユニットが敵ユニットを破壊したときの場監視（例: GD02-002 ガンダムエピオン）。
/// キル元は監視ユニット自身でも他の味方でもよい。
/// </summary>
public partial class BattleGameMain
{
    private struct AllyEnemyUnitDestroyedWatchBlock
    {
        public CardController Watcher;
        public TimedEffectData Timed;
        public int BlockIndex;
    }

    /// <summary>
    /// 敵ユニット破壊パイプラインから呼ぶ。キル元オーナーのバトルゾーン監視カードを解決する。
    /// </summary>
    private void NotifyAllyEnemyUnitDestroyedWatch(
        CardController destroyedUnit,
        PlayerType destroyedOwner,
        CardController destroyedBy,
        bool destroyedByBattleDamage,
        bool destroyedByEffectDamage,
        Action onComplete)
    {
        if (ShouldSkipAutomaticEffectsInTestPlay())
        {
            onComplete?.Invoke();
            return;
        }

        if (!TryResolveEnemyUnitKillContext(
                destroyedUnit,
                destroyedOwner,
                destroyedBy,
                out CardController killer,
                out PlayerType killerOwner))
        {
            onComplete?.Invoke();
            return;
        }

        List<CardController> watchers = CollectOwnerUnitsWithAllyEnemyUnitDestroyedWatch(killerOwner);
        if (watchers.Count == 0)
        {
            onComplete?.Invoke();
            return;
        }

        List<AllyEnemyUnitDestroyedWatchBlock> blocks = new List<AllyEnemyUnitDestroyedWatchBlock>();
        for (int i = 0; i < watchers.Count; i++)
        {
            CardController watcher = watchers[i];
            EffectActivationContext context = BuildAllyEnemyUnitDestroyedActivationContext(
                killerOwner,
                watcher,
                killer,
                destroyedByBattleDamage,
                destroyedByEffectDamage);
            CollectAllyEnemyUnitDestroyedBlocks(watcher, killerOwner, context, blocks);
        }

        if (blocks.Count == 0)
        {
            onComplete?.Invoke();
            return;
        }

        Debug.Log(
            $"[AllyEnemyUnitDestroyed] killer:{killer.Data.cardName}(id:{killer.Data.id}) "
            + $"owner:{killerOwner} battleDmg:{destroyedByBattleDamage} "
            + $"watchers:{watchers.Count} blocks:{blocks.Count}");

        RunAllyEnemyUnitDestroyedWatchTimedBlocks(
            killerOwner,
            killer,
            destroyedByBattleDamage,
            destroyedByEffectDamage,
            blocks,
            0,
            onComplete);
    }

    private List<CardController> CollectOwnerUnitsWithAllyEnemyUnitDestroyedWatch(PlayerType ownerType)
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

            if (CardHasAllyEnemyUnitDestroyedWatch(unit))
            {
                result.Add(unit);
            }
        }

        return result;
    }

    private static bool CardHasAllyEnemyUnitDestroyedWatch(CardController card)
    {
        if (card?.Data?.timedEffects == null)
        {
            return false;
        }

        for (int t = 0; t < card.Data.timedEffects.Count; t++)
        {
            if (card.Data.timedEffects[t].IsOnAllyEnemyUnitDestroyedResolutionBlock())
            {
                return true;
            }
        }

        return false;
    }

    private EffectActivationContext BuildAllyEnemyUnitDestroyedActivationContext(
        PlayerType ownerType,
        CardController watcher,
        CardController killer,
        bool destroyedByBattleDamage,
        bool destroyedByEffectDamage)
    {
        return new EffectActivationContext(
            ownerType,
            watcher,
            playerBattleZoneCards,
            enemyBattleZoneCards,
            CollectHandControllers(cardGameRule),
            CollectHandControllers(enemyCardGameRule),
            isOwnerTurn: ownerType == currentPlayerType,
            // SourceUnitIsLinked は MountHostUnit 優先のため、監視ユニット自身を載せる
            mountHostUnit: watcher,
            mountedPilot: watcher != null ? watcher.MountedPilot : null,
            observedCards: GetActiveObservedCardsForActivation(),
            ownerTrashCardIds: cardGameRule.GetTrashCardIds(),
            opponentTrashCardIds: enemyCardGameRule.GetTrashCardIds(),
            priorChainDealtDamage: GetEffectChainDealtDamage(),
            destroyingCard: killer,
            hasDestroyingCardOwner: true,
            destroyingCardOwner: ownerType,
            destroyedByBattleDamage: destroyedByBattleDamage,
            destroyedByEffectDamage: destroyedByEffectDamage,
            sourceAttackingEnemyUnit: IsSourceAttackingEnemyUnit(killer, allowDestroyedDefender: true),
            ownerActivatedSpecialMoveCommandThisTurn: HasOwnerActivatedSpecialMoveCommandThisTurn(ownerType),
            ownerHasDeployedBase: HasActiveDeployedBaseForRuleSide(ToRuleSide(ownerType)),
            ownerActivatedResourceByEffectThisTurn: HasOwnerActivatedResourceByEffectThisTurn(ownerType));
    }

    private void CollectAllyEnemyUnitDestroyedBlocks(
        CardController watcher,
        PlayerType ownerType,
        EffectActivationContext activationContext,
        List<AllyEnemyUnitDestroyedWatchBlock> blocks)
    {
        if (watcher?.Data?.timedEffects == null || blocks == null)
        {
            return;
        }

        for (int i = 0; i < watcher.Data.timedEffects.Count; i++)
        {
            TimedEffectData timed = watcher.Data.timedEffects[i];
            if (timed == null || !timed.IsOnAllyEnemyUnitDestroyedResolutionBlock())
            {
                continue;
            }

            if (timed.oncePerTurn && HasUsedPaidActivationThisTurn(ownerType, watcher, i))
            {
                continue;
            }

            if (!CanRunTimedBlockAtChainTime(timed, activationContext, "OnAllyEnemyUnitDestroyed"))
            {
                continue;
            }

            blocks.Add(new AllyEnemyUnitDestroyedWatchBlock
            {
                Watcher = watcher,
                Timed = timed,
                BlockIndex = i
            });
        }
    }

    private void RunAllyEnemyUnitDestroyedWatchTimedBlocks(
        PlayerType ownerType,
        CardController killer,
        bool destroyedByBattleDamage,
        bool destroyedByEffectDamage,
        List<AllyEnemyUnitDestroyedWatchBlock> blocks,
        int index,
        Action onComplete)
    {
        if (blocks == null || index >= blocks.Count)
        {
            onComplete?.Invoke();
            return;
        }

        AllyEnemyUnitDestroyedWatchBlock block = blocks[index];
        CardController watcher = block.Watcher;
        TimedEffectData timed = block.Timed;
        if (timed != null && timed.oncePerTurn && watcher != null)
        {
            MarkPaidActivationUsedThisTurn(ownerType, watcher, block.BlockIndex);
        }

        RunAllyEnemyUnitDestroyedEffectChain(
            watcher,
            ownerType,
            killer,
            destroyedByBattleDamage,
            destroyedByEffectDamage,
            timed != null ? timed.GetResolvedEffects() : null,
            0,
            () => RunAllyEnemyUnitDestroyedWatchTimedBlocks(
                ownerType,
                killer,
                destroyedByBattleDamage,
                destroyedByEffectDamage,
                blocks,
                index + 1,
                onComplete));
    }

    private void RunAllyEnemyUnitDestroyedEffectChain(
        CardController watcher,
        PlayerType ownerType,
        CardController killer,
        bool destroyedByBattleDamage,
        bool destroyedByEffectDamage,
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
            RunAllyEnemyUnitDestroyedEffectChain(
                watcher,
                ownerType,
                killer,
                destroyedByBattleDamage,
                destroyedByEffectDamage,
                effects,
                effectIndex + 1,
                onComplete);
            return;
        }

        EffectActivationContext activationContext = BuildAllyEnemyUnitDestroyedActivationContext(
            ownerType,
            watcher,
            killer,
            destroyedByBattleDamage,
            destroyedByEffectDamage);

        if (!ShouldApplyChainedEffect(effect, activationContext, "OnAllyEnemyUnitDestroyed"))
        {
            RunAllyEnemyUnitDestroyedEffectChain(
                watcher,
                ownerType,
                killer,
                destroyedByBattleDamage,
                destroyedByEffectDamage,
                effects,
                effectIndex + 1,
                onComplete);
            return;
        }

        ApplyEffectRespectingLookAsync(
            watcher,
            ownerType,
            effect,
            () => RunAllyEnemyUnitDestroyedEffectChain(
                watcher,
                ownerType,
                killer,
                destroyedByBattleDamage,
                destroyedByEffectDamage,
                effects,
                effectIndex + 1,
                onComplete));
    }
}
