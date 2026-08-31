using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>味方ユニットへのパイロットセット監視（例: フリーダムガンダムの AP-2）。</summary>
public partial class BattleGameMain
{
    /// <summary>
    /// 自分のユニットへパイロットがセットされた直後に呼ぶ。
    /// 搭乗先・パイロットは MountHostUnit / MountedPilot として渡す。
    /// </summary>
    private void NotifyAllyPilotMounted(
        PlayerType ownerType,
        CardController mountHostUnit,
        CardController mountedPilot,
        Action onComplete = null)
    {
        if (!ShouldRunAllyPilotMountWatchEffects(ownerType))
        {
            onComplete?.Invoke();
            return;
        }

        if (mountHostUnit == null
            || mountHostUnit.Data == null
            || mountedPilot == null
            || mountedPilot.Data == null)
        {
            onComplete?.Invoke();
            return;
        }

        List<CardController> watchers = CollectOwnerBattleUnitsWithAllyPilotMountWatch(ownerType);
        if (watchers.Count == 0)
        {
            onComplete?.Invoke();
            return;
        }

        Debug.Log(
            $"[AllyPilotMountWatch] mounted:{mountedPilot.Data.cardName}(id:{mountedPilot.Data.id}) "
            + $"→ {mountHostUnit.Data.cardName}(id:{mountHostUnit.Data.id}) watchers:{watchers.Count} "
            + $"owner:{ownerType}");

        RunAllyPilotMountWatchUnits(ownerType, mountHostUnit, mountedPilot, watchers, 0, onComplete);
    }

    private List<CardController> CollectOwnerBattleUnitsWithAllyPilotMountWatch(PlayerType ownerType)
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

            if (unit.Data.timedEffects == null)
            {
                continue;
            }

            for (int t = 0; t < unit.Data.timedEffects.Count; t++)
            {
                if (unit.Data.timedEffects[t].IsOnAllyPilotMountedResolutionBlock())
                {
                    result.Add(unit);
                    break;
                }
            }
        }

        return result;
    }

    private EffectActivationContext BuildAllyPilotMountActivationContext(
        PlayerType ownerType,
        CardController watcher,
        CardController mountHostUnit,
        CardController mountedPilot)
    {
        return new EffectActivationContext(
            ownerType,
            watcher,
            playerBattleZoneCards,
            enemyBattleZoneCards,
            CollectHandControllers(cardGameRule),
            CollectHandControllers(enemyCardGameRule),
            isOwnerTurn: ownerType == currentPlayerType,
            mountHostUnit: mountHostUnit,
            mountedPilot: mountedPilot,
            observedCards: Array.Empty<CardData>(),
            ownerTrashCardIds: cardGameRule.GetTrashCardIds(),
            opponentTrashCardIds: enemyCardGameRule.GetTrashCardIds(),
            priorChainDealtDamage: GetEffectChainDealtDamage(),
            ownerActivatedSpecialMoveCommandThisTurn: HasOwnerActivatedSpecialMoveCommandThisTurn(ownerType),
            ownerHasDeployedBase: HasActiveDeployedBaseForRuleSide(ToRuleSide(ownerType)));
    }

    private void RunAllyPilotMountWatchUnits(
        PlayerType ownerType,
        CardController mountHostUnit,
        CardController mountedPilot,
        List<CardController> watchers,
        int index,
        Action onComplete)
    {
        if (watchers == null || index >= watchers.Count)
        {
            onComplete?.Invoke();
            return;
        }

        CardController unit = watchers[index];
        if (unit == null || unit.Data?.timedEffects == null)
        {
            RunAllyPilotMountWatchUnits(
                ownerType,
                mountHostUnit,
                mountedPilot,
                watchers,
                index + 1,
                onComplete);
            return;
        }

        EffectActivationContext activationContext = BuildAllyPilotMountActivationContext(
            ownerType,
            unit,
            mountHostUnit,
            mountedPilot);
        List<TimedEffectData> blocks = new List<TimedEffectData>();
        List<int> blockIndices = new List<int>();
        for (int i = 0; i < unit.Data.timedEffects.Count; i++)
        {
            TimedEffectData timed = unit.Data.timedEffects[i];
            if (!timed.IsOnAllyPilotMountedResolutionBlock())
            {
                continue;
            }

            if (timed.oncePerTurn && HasUsedPaidActivationThisTurn(ownerType, unit, i))
            {
                continue;
            }

            if (!CanRunTimedBlockAtChainTime(timed, activationContext, "OnAllyPilotMounted"))
            {
                continue;
            }

            blocks.Add(timed);
            blockIndices.Add(i);
        }

        if (blocks.Count == 0)
        {
            RunAllyPilotMountWatchUnits(
                ownerType,
                mountHostUnit,
                mountedPilot,
                watchers,
                index + 1,
                onComplete);
            return;
        }

        BeginEffectChainObservationScope();
        RunAllyPilotMountWatchTimedBlocks(
            unit,
            ownerType,
            blocks,
            blockIndices,
            0,
            () =>
            {
                EndEffectChainObservationScope();
                RunAllyPilotMountWatchUnits(
                    ownerType,
                    mountHostUnit,
                    mountedPilot,
                    watchers,
                    index + 1,
                    onComplete);
            });
    }

    private void RunAllyPilotMountWatchTimedBlocks(
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

        RunAllyPilotMountEffectChain(
            sourceCard,
            ownerType,
            block != null ? block.GetResolvedEffects() : null,
            0,
            () => RunAllyPilotMountWatchTimedBlocks(
                sourceCard,
                ownerType,
                blocks,
                blockIndices,
                blockIndex + 1,
                onComplete));
    }

    /// <summary>
    /// 搭乗監視効果専用チェーン。Debuff 等は必ず手動選択 UI 経由（プレイヤー操作時）。
    /// </summary>
    private void RunAllyPilotMountEffectChain(
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
            RunAllyPilotMountEffectChain(sourceCard, ownerType, effects, effectIndex + 1, onComplete);
            return;
        }

        EffectActivationContext activationContext = BuildActivationContext(ownerType, sourceCard);
        if (!ShouldApplyChainedEffect(effect, activationContext, "AllyPilotMount"))
        {
            RunAllyPilotMountEffectChain(sourceCard, ownerType, effects, effectIndex + 1, onComplete);
            return;
        }

        if (EffectRequiresManualUnitSelection(effect))
        {
            List<CardController> candidates = ResolveSelectableEffectTargets(sourceCard, ownerType, effect);
            if (candidates.Count == 0)
            {
                RunAllyPilotMountEffectChain(sourceCard, ownerType, effects, effectIndex + 1, onComplete);
                return;
            }

            // 人間操作側のみ UI（オンライン相手搭乗ミラー ownerType=Enemy では AI 自動選択しない）
            if (RequiresInteractiveManualUnitSelectionUi(ownerType))
            {
                StartCoroutine(CoRunAllyPilotMountPlayerManualSelection(
                    sourceCard,
                    ownerType,
                    effect,
                    candidates,
                    () => RunAllyPilotMountEffectChain(sourceCard, ownerType, effects, effectIndex + 1, onComplete),
                    () => RunAllyPilotMountEffectChain(sourceCard, ownerType, effects, effectIndex + 1, onComplete)));
                return;
            }

            TryExecuteManualUnitSelectionEffect(
                sourceCard,
                ownerType,
                effect,
                null,
                () => RunAllyPilotMountEffectChain(sourceCard, ownerType, effects, effectIndex + 1, onComplete),
                () => RunAllyPilotMountEffectChain(sourceCard, ownerType, effects, effectIndex + 1, onComplete));
            return;
        }

        ApplyEffectRespectingLookAsync(
            sourceCard,
            ownerType,
            effect,
            () => RunAllyPilotMountEffectChain(sourceCard, ownerType, effects, effectIndex + 1, onComplete));
    }

    /// <summary>
    /// 搭乗監視のプレイヤー手動選択。搭乗ボタンのクリックが敵候補 UI に漏れないよう数フレーム待ってから表示する。
    /// </summary>
    private IEnumerator CoRunAllyPilotMountPlayerManualSelection(
        CardController sourceCard,
        PlayerType ownerType,
        EffectData effect,
        List<CardController> candidates,
        Action onDone,
        Action onSkipped)
    {
        // 搭乗ボタンのクリックが敵選択 UI に漏れるのを防ぐ
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
                        $"[AllyPilotMountWatch] プレイヤーが対象を選択: {picked.Data?.cardName}(id:{picked.Data?.id}) "
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
                        $"[AllyPilotMountWatch] 対象選択キャンセル effect:{effect?.type} "
                        + $"source:{sourceCard?.Data?.cardName}(id:{sourceCard?.Data?.id})");
                    onSkipped?.Invoke();
                }
            });

        yield return new WaitUntil(() => resolved);
    }
}
