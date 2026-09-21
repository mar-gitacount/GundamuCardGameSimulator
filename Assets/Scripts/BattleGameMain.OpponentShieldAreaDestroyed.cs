using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 相手シールドエリアのカードをダメージ破壊したとき（OnOpponentShieldAreaCardDestroyed）。
/// 破壊したユニット／搭乗パイロットに加え、場の味方監視ユニット（例: GD02-001 サイコ・ガンダム）も解決する。
/// </summary>
public partial class BattleGameMain
{
    private struct OpponentShieldAreaDestroyedBlock
    {
        public CardController EffectSource;
        public TimedEffectData Timed;
        public int BlockIndex;
    }

    private IEnumerator WaitOnOpponentShieldAreaCardDestroyedCoroutine(
        CardController sourceUnit,
        PlayerType ownerType)
    {
        bool done = false;
        TriggerOnOpponentShieldAreaCardDestroyed(sourceUnit, ownerType, () => done = true);
        yield return new WaitUntil(() => done);
    }

    /// <summary>
    /// シールド攻撃・Breach・効果ダメージなどで相手シールドエリアのカードを破壊した直後に呼ぶ。
    /// </summary>
    private void TriggerOnOpponentShieldAreaCardDestroyed(
        CardController sourceUnit,
        PlayerType ownerType,
        Action onComplete = null)
    {
        if (sourceUnit == null || sourceUnit.Data == null || !sourceUnit.Data.IsUnitLike())
        {
            onComplete?.Invoke();
            return;
        }

        List<OpponentShieldAreaDestroyedBlock> blocks = new List<OpponentShieldAreaDestroyedBlock>();

        // 破壊者自身（＋搭乗パイロット）の効果
        EffectActivationContext destroyerContext = BuildOpponentShieldAreaDestroyedContext(
            ownerType,
            sourceUnit,
            sourceUnit);
        CollectOpponentShieldAreaDestroyedBlocks(sourceUnit, ownerType, destroyerContext, blocks);
        if (sourceUnit.MountedPilot != null)
        {
            CollectOpponentShieldAreaDestroyedBlocks(
                sourceUnit.MountedPilot,
                ownerType,
                destroyerContext,
                blocks);
        }

        // 場の味方監視ユニット（破壊者以外）。DestroyingCard に破壊者を載せる。
        List<CardController> zone = ownerType == PlayerType.Player
            ? playerBattleZoneCards
            : enemyBattleZoneCards;
        if (zone != null)
        {
            for (int i = 0; i < zone.Count; i++)
            {
                CardController watcher = zone[i];
                if (watcher == null
                    || watcher == sourceUnit
                    || watcher.Data == null
                    || !watcher.Data.IsUnitLike()
                    || watcher.CurrentHp <= 0)
                {
                    continue;
                }

                if (!CardHasOpponentShieldAreaDestroyedWatch(watcher))
                {
                    continue;
                }

                EffectActivationContext watcherContext = BuildOpponentShieldAreaDestroyedContext(
                    ownerType,
                    watcher,
                    sourceUnit);
                CollectOpponentShieldAreaDestroyedBlocks(watcher, ownerType, watcherContext, blocks);
            }
        }

        if (blocks.Count == 0)
        {
            onComplete?.Invoke();
            return;
        }

        Debug.Log(
            $"[OnOpponentShieldAreaCardDestroyed] destroyer:{sourceUnit.Data.cardName}(id:{sourceUnit.Data.id}) "
            + $"blocks:{blocks.Count}");

        RunOnOpponentShieldAreaCardDestroyedTimedBlocks(ownerType, blocks, 0, onComplete);
    }

    private EffectActivationContext BuildOpponentShieldAreaDestroyedContext(
        PlayerType ownerType,
        CardController effectSource,
        CardController destroyerUnit)
    {
        return new EffectActivationContext(
            ownerType,
            effectSource,
            playerBattleZoneCards,
            enemyBattleZoneCards,
            CollectHandControllers(cardGameRule),
            CollectHandControllers(enemyCardGameRule),
            isOwnerTurn: ownerType == currentPlayerType,
            mountHostUnit: effectSource != null && effectSource.Data != null && effectSource.Data.IsUnitLike()
                ? effectSource
                : destroyerUnit,
            mountedPilot: effectSource != null && effectSource.Data != null && effectSource.Data.IsUnitLike()
                ? effectSource.MountedPilot
                : destroyerUnit != null ? destroyerUnit.MountedPilot : null,
            observedCards: GetActiveObservedCardsForActivation(),
            ownerTrashCardIds: cardGameRule.GetTrashCardIds(),
            opponentTrashCardIds: enemyCardGameRule.GetTrashCardIds(),
            priorChainDealtDamage: GetEffectChainDealtDamage(),
            destroyingCard: destroyerUnit,
            hasDestroyingCardOwner: true,
            destroyingCardOwner: ownerType,
            destroyedByBattleDamage: false,
            destroyedByEffectDamage: true,
            ownerActivatedSpecialMoveCommandThisTurn: HasOwnerActivatedSpecialMoveCommandThisTurn(ownerType),
            ownerHasDeployedBase: HasActiveDeployedBaseForRuleSide(ToRuleSide(ownerType)),
            ownerActivatedResourceByEffectThisTurn: HasOwnerActivatedResourceByEffectThisTurn(ownerType));
    }

    private static bool CardHasOpponentShieldAreaDestroyedWatch(CardController card)
    {
        if (card?.Data?.timedEffects == null)
        {
            return false;
        }

        for (int t = 0; t < card.Data.timedEffects.Count; t++)
        {
            if (card.Data.timedEffects[t].IsOnOpponentShieldAreaCardDestroyedResolutionBlock())
            {
                return true;
            }
        }

        return false;
    }

    private void CollectOpponentShieldAreaDestroyedBlocks(
        CardController effectSource,
        PlayerType ownerType,
        EffectActivationContext activationContext,
        List<OpponentShieldAreaDestroyedBlock> blocks)
    {
        if (effectSource?.Data?.timedEffects == null || blocks == null)
        {
            return;
        }

        for (int i = 0; i < effectSource.Data.timedEffects.Count; i++)
        {
            TimedEffectData timed = effectSource.Data.timedEffects[i];
            if (timed == null || !timed.IsOnOpponentShieldAreaCardDestroyedResolutionBlock())
            {
                continue;
            }

            if (timed.oncePerTurn && HasUsedPaidActivationThisTurn(ownerType, effectSource, i))
            {
                Debug.Log(
                    $"[OnOpponentShieldAreaCardDestroyed] ターン1回使用済みのためスキップ "
                    + $"{effectSource.Data.cardName}(id:{effectSource.Data.id}) block:{i}");
                continue;
            }

            if (!EffectActivationEvaluator.AreTimedConditionsMet(timed, activationContext))
            {
                continue;
            }

            if (!CanRunTimedBlockAtChainTime(timed, activationContext, "OnOpponentShieldAreaCardDestroyed"))
            {
                continue;
            }

            blocks.Add(new OpponentShieldAreaDestroyedBlock
            {
                EffectSource = effectSource,
                Timed = timed,
                BlockIndex = i
            });
        }
    }

    private void RunOnOpponentShieldAreaCardDestroyedTimedBlocks(
        PlayerType ownerType,
        List<OpponentShieldAreaDestroyedBlock> blocks,
        int index,
        Action onComplete)
    {
        if (blocks == null || index >= blocks.Count)
        {
            onComplete?.Invoke();
            return;
        }

        OpponentShieldAreaDestroyedBlock block = blocks[index];
        CardController effectSource = block.EffectSource;
        TimedEffectData timed = block.Timed;
        if (timed != null && timed.oncePerTurn && effectSource != null)
        {
            MarkPaidActivationUsedThisTurn(ownerType, effectSource, block.BlockIndex);
        }

        TryExecuteOnOpponentShieldAreaCardDestroyedEffectChain(
            effectSource,
            ownerType,
            timed != null ? timed.GetResolvedEffects() : null,
            0,
            () => RunOnOpponentShieldAreaCardDestroyedTimedBlocks(
                ownerType,
                blocks,
                index + 1,
                onComplete));
    }

    private void TryExecuteOnOpponentShieldAreaCardDestroyedEffectChain(
        CardController sourceCard,
        PlayerType ownerType,
        IReadOnlyList<EffectData> effects,
        int effectIndex,
        Action onDone)
    {
        if (effects == null || effectIndex >= effects.Count)
        {
            onDone?.Invoke();
            return;
        }

        EffectData effect = effects[effectIndex];
        if (effect == null)
        {
            TryExecuteOnOpponentShieldAreaCardDestroyedEffectChain(
                sourceCard, ownerType, effects, effectIndex + 1, onDone);
            return;
        }

        EffectActivationContext activationContext = BuildActivationContext(ownerType, sourceCard);
        if (!ShouldApplyChainedEffect(effect, activationContext, "OnOpponentShieldAreaCardDestroyed"))
        {
            TryExecuteOnOpponentShieldAreaCardDestroyedEffectChain(
                sourceCard, ownerType, effects, effectIndex + 1, onDone);
            return;
        }

        if (EffectRequiresManualUnitSelection(effect))
        {
            TryExecuteManualUnitSelectionEffect(
                sourceCard,
                ownerType,
                effect,
                sourceCard,
                () => TryExecuteOnOpponentShieldAreaCardDestroyedEffectChain(
                    sourceCard, ownerType, effects, effectIndex + 1, onDone));
            return;
        }

        ApplyEffectRespectingLookAsync(
            sourceCard,
            ownerType,
            effect,
            () => TryExecuteOnOpponentShieldAreaCardDestroyedEffectChain(
                sourceCard, ownerType, effects, effectIndex + 1, onDone));
    }
}
