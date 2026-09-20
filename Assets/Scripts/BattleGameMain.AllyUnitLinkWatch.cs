using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 味方ユニットがリンクしたときの監視（例: ST14-016 グリプス2）。
/// 配備ベース／バトルゾーンの監視カードから OnAllyUnitLinked を解決する。
/// </summary>
public partial class BattleGameMain
{
    /// <summary>
    /// Link 成立直後に呼ぶ。自ターン中のみ（activationConditions の OwnerTurn と併用）。
    /// </summary>
    private void NotifyAllyUnitLinked(
        PlayerType ownerType,
        CardController linkedUnit,
        CardController linkedPilot,
        Action onComplete = null)
    {
        if (ShouldSkipAutomaticEffectsInTestPlay())
        {
            onComplete?.Invoke();
            return;
        }

        if (linkedUnit == null
            || linkedUnit.Data == null
            || linkedPilot == null
            || linkedPilot.Data == null
            || !UnitLinkExtensions.HasValidLinkPilot(linkedUnit.Data, linkedPilot))
        {
            onComplete?.Invoke();
            return;
        }

        List<CardController> watchers = CollectOwnerCardsWithAllyUnitLinkWatch(ownerType);
        if (watchers.Count == 0)
        {
            onComplete?.Invoke();
            return;
        }

        Debug.Log(
            $"[AllyUnitLinkWatch] linked:{linkedUnit.Data.cardName}(id:{linkedUnit.Data.id}) "
            + $"pilot:{linkedPilot.Data.cardName}(id:{linkedPilot.Data.id}) "
            + $"watchers:{watchers.Count} owner:{ownerType}");

        RunAllyUnitLinkWatchCards(ownerType, linkedUnit, linkedPilot, watchers, 0, onComplete);
    }

    private List<CardController> CollectOwnerCardsWithAllyUnitLinkWatch(PlayerType ownerType)
    {
        List<CardController> result = new List<CardController>();

        CardController baseCard = GetDeployedBaseForRuleSide(ToRuleSide(ownerType));
        if (baseCard != null
            && baseCard.Data != null
            && IsCardInBaseSlot(baseCard)
            && baseCard.CurrentHp > 0
            && CardHasAllyUnitLinkedWatch(baseCard))
        {
            result.Add(baseCard);
        }

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

            if (CardHasAllyUnitLinkedWatch(unit))
            {
                result.Add(unit);
            }
        }

        return result;
    }

    private static bool CardHasAllyUnitLinkedWatch(CardController card)
    {
        if (card?.Data?.timedEffects == null)
        {
            return false;
        }

        for (int t = 0; t < card.Data.timedEffects.Count; t++)
        {
            if (card.Data.timedEffects[t].IsOnAllyUnitLinkedResolutionBlock())
            {
                return true;
            }
        }

        return false;
    }

    private EffectActivationContext BuildAllyUnitLinkActivationContext(
        PlayerType ownerType,
        CardController watcher,
        CardController linkedUnit,
        CardController linkedPilot)
    {
        return new EffectActivationContext(
            ownerType,
            watcher,
            playerBattleZoneCards,
            enemyBattleZoneCards,
            CollectHandControllers(cardGameRule),
            CollectHandControllers(enemyCardGameRule),
            isOwnerTurn: ownerType == currentPlayerType,
            mountHostUnit: linkedUnit,
            mountedPilot: linkedPilot,
            observedCards: Array.Empty<CardData>(),
            ownerTrashCardIds: cardGameRule.GetTrashCardIds(),
            opponentTrashCardIds: enemyCardGameRule.GetTrashCardIds(),
            priorChainDealtDamage: GetEffectChainDealtDamage(),
            ownerActivatedSpecialMoveCommandThisTurn: HasOwnerActivatedSpecialMoveCommandThisTurn(ownerType),
            ownerHasDeployedBase: HasActiveDeployedBaseForRuleSide(ToRuleSide(ownerType)),
            ownerActivatedResourceByEffectThisTurn: HasOwnerActivatedResourceByEffectThisTurn(ownerType));
    }

    private void RunAllyUnitLinkWatchCards(
        PlayerType ownerType,
        CardController linkedUnit,
        CardController linkedPilot,
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
        if (watcher == null || watcher.Data?.timedEffects == null)
        {
            RunAllyUnitLinkWatchCards(
                ownerType,
                linkedUnit,
                linkedPilot,
                watchers,
                index + 1,
                onComplete);
            return;
        }

        EffectActivationContext activationContext = BuildAllyUnitLinkActivationContext(
            ownerType,
            watcher,
            linkedUnit,
            linkedPilot);
        List<TimedEffectData> blocks = new List<TimedEffectData>();
        List<int> blockIndices = new List<int>();
        for (int i = 0; i < watcher.Data.timedEffects.Count; i++)
        {
            TimedEffectData timed = watcher.Data.timedEffects[i];
            if (!timed.IsOnAllyUnitLinkedResolutionBlock())
            {
                continue;
            }

            if (timed.oncePerTurn && HasUsedPaidActivationThisTurn(ownerType, watcher, i))
            {
                continue;
            }

            if (!CanRunTimedBlockAtChainTime(timed, activationContext, "OnAllyUnitLinked"))
            {
                continue;
            }

            blocks.Add(timed);
            blockIndices.Add(i);
        }

        if (blocks.Count == 0)
        {
            RunAllyUnitLinkWatchCards(
                ownerType,
                linkedUnit,
                linkedPilot,
                watchers,
                index + 1,
                onComplete);
            return;
        }

        BeginEffectChainObservationScope();
        RunAllyUnitLinkWatchTimedBlocks(
            watcher,
            ownerType,
            blocks,
            blockIndices,
            0,
            () =>
            {
                EndEffectChainObservationScope();
                RunAllyUnitLinkWatchCards(
                    ownerType,
                    linkedUnit,
                    linkedPilot,
                    watchers,
                    index + 1,
                    onComplete);
            });
    }

    private void RunAllyUnitLinkWatchTimedBlocks(
        CardController sourceCard,
        PlayerType ownerType,
        List<TimedEffectData> blocks,
        List<int> blockIndices,
        int blockIndex,
        Action onComplete)
    {
        if (blocks == null || blockIndex >= blocks.Count)
        {
            onComplete?.Invoke();
            return;
        }

        TimedEffectData block = blocks[blockIndex];
        int timedIndex = blockIndices != null && blockIndex < blockIndices.Count
            ? blockIndices[blockIndex]
            : blockIndex;
        if (block != null && block.oncePerTurn)
        {
            MarkPaidActivationUsedThisTurn(ownerType, sourceCard, timedIndex);
        }

        RunAllyUnitLinkEffectChain(
            sourceCard,
            ownerType,
            block != null ? block.GetResolvedEffects() : null,
            0,
            () => RunAllyUnitLinkWatchTimedBlocks(
                sourceCard,
                ownerType,
                blocks,
                blockIndices,
                blockIndex + 1,
                onComplete));
    }

    private void RunAllyUnitLinkEffectChain(
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
            RunAllyUnitLinkEffectChain(sourceCard, ownerType, effects, effectIndex + 1, onComplete);
            return;
        }

        EffectActivationContext activationContext = BuildActivationContext(ownerType, sourceCard);
        if (!ShouldApplyChainedEffect(effect, activationContext, "AllyUnitLink"))
        {
            RunAllyUnitLinkEffectChain(sourceCard, ownerType, effects, effectIndex + 1, onComplete);
            return;
        }

        if (EffectRequiresManualUnitSelection(effect))
        {
            List<CardController> candidates = ResolveSelectableEffectTargets(sourceCard, ownerType, effect);
            if (candidates.Count == 0)
            {
                RunAllyUnitLinkEffectChain(sourceCard, ownerType, effects, effectIndex + 1, onComplete);
                return;
            }

            if (RequiresInteractiveManualUnitSelectionUi(ownerType))
            {
                StartCoroutine(CoRunAllyUnitLinkPlayerManualSelection(
                    sourceCard,
                    ownerType,
                    effect,
                    candidates,
                    () => RunAllyUnitLinkEffectChain(sourceCard, ownerType, effects, effectIndex + 1, onComplete),
                    () => RunAllyUnitLinkEffectChain(sourceCard, ownerType, effects, effectIndex + 1, onComplete)));
                return;
            }

            TryExecuteManualUnitSelectionEffect(
                sourceCard,
                ownerType,
                effect,
                null,
                () => RunAllyUnitLinkEffectChain(sourceCard, ownerType, effects, effectIndex + 1, onComplete),
                () => RunAllyUnitLinkEffectChain(sourceCard, ownerType, effects, effectIndex + 1, onComplete));
            return;
        }

        ApplyEffectRespectingLookAsync(
            sourceCard,
            ownerType,
            effect,
            () => RunAllyUnitLinkEffectChain(sourceCard, ownerType, effects, effectIndex + 1, onComplete));
    }

    private IEnumerator CoRunAllyUnitLinkPlayerManualSelection(
        CardController sourceCard,
        PlayerType ownerType,
        EffectData effect,
        List<CardController> candidates,
        Action onDone,
        Action onSkipped)
    {
        // 搭乗／リンク直後のクリック漏れ防止
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
                    Debug.Log(
                        $"[AllyUnitLinkWatch] プレイヤーが対象を選択: {picked.Data?.cardName}(id:{picked.Data?.id}) "
                        + $"effect:{effect?.type} source:{sourceCard?.Data?.cardName}(id:{sourceCard?.Data?.id})");
                    ApplyEffectToSpecificTargets(
                        sourceCard,
                        ownerType,
                        effect,
                        new List<CardController> { picked });
                    onDone?.Invoke();
                }
                else
                {
                    Debug.Log(
                        $"[AllyUnitLinkWatch] 対象選択キャンセル effect:{effect?.type} "
                        + $"source:{sourceCard?.Data?.cardName}(id:{sourceCard?.Data?.id})");
                    onSkipped?.Invoke();
                }
            });

        yield return new WaitUntil(() => resolved);
    }
}
