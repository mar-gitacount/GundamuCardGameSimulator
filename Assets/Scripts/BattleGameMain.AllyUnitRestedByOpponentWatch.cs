using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 相手のターン中、自分のユニットが相手の効果でレストになったときの監視（GD03-128 ドリテア）。
/// 配備ベース／バトルゾーンの監視カードから OnAllyUnitRestedByOpponentEffect を解決する。
/// コマンド効果・ユニット効果の Rest の両方を見る。
/// </summary>
public partial class BattleGameMain
{
    /// <summary>
    /// レストしたユニットのオーナー視点で「相手ターン中の相手効果レスト」か。
    /// オンラインで相手側の監視は相手クライアントが解決する。
    /// </summary>
    private bool ShouldNotifyAllyUnitRestedByOpponentEffect(PlayerType sourceOwner, PlayerType restOwner)
    {
        if (sourceOwner == restOwner)
        {
            return false;
        }

        if (currentPlayerType == restOwner)
        {
            return false;
        }

        if (IsOnlineBattle() && restOwner == PlayerType.Enemy)
        {
            return false;
        }

        return true;
    }

    private void NotifyAllyUnitRestedByOpponentEffect(PlayerType watcherOwner)
    {
        StartCoroutine(CoNotifyAllyUnitRestedByOpponentEffect(watcherOwner));
    }

    private IEnumerator CoNotifyAllyUnitRestedByOpponentEffect(PlayerType watcherOwner)
    {
        yield return null;
        yield return null;

        if (ShouldSkipAutomaticEffectsInTestPlay())
        {
            yield break;
        }

        List<CardController> watchers = CollectOwnerCardsWithAllyUnitRestedByOpponentWatch(watcherOwner);
        if (watchers.Count == 0)
        {
            yield break;
        }

        Debug.Log(
            $"[AllyRestByOpponentWatch] owner:{watcherOwner} watchers:{watchers.Count} turn:{currentPlayerType}");

        bool done = false;
        RunAllyUnitRestedByOpponentWatchCards(watcherOwner, watchers, 0, () => done = true);
        yield return new WaitUntil(() => done);
    }

    /// <summary>オンライン受信：相手が自分のユニットをレストした。</summary>
    private void NotifyLocalAllyUnitRestedByRemoteOpponentEffect(CardController restedUnit)
    {
        if (restedUnit == null)
        {
            return;
        }

        PlayerType restOwner = ResolveCardOwner(restedUnit.transform);
        if (!ShouldNotifyAllyUnitRestedByOpponentEffect(PlayerType.Enemy, restOwner))
        {
            return;
        }

        NotifyAllyUnitRestedByOpponentEffect(restOwner);
    }

    private List<CardController> CollectOwnerCardsWithAllyUnitRestedByOpponentWatch(PlayerType ownerType)
    {
        List<CardController> result = new List<CardController>();

        CardController baseCard = GetDeployedBaseForRuleSide(ToRuleSide(ownerType));
        if (baseCard != null
            && baseCard.Data != null
            && IsCardInBaseSlot(baseCard)
            && baseCard.CurrentHp > 0
            && CardHasAllyUnitRestedByOpponentWatch(baseCard))
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

            if (CardHasAllyUnitRestedByOpponentWatch(unit))
            {
                result.Add(unit);
            }
        }

        return result;
    }

    private static bool CardHasAllyUnitRestedByOpponentWatch(CardController card)
    {
        if (card?.Data?.timedEffects == null)
        {
            return false;
        }

        for (int t = 0; t < card.Data.timedEffects.Count; t++)
        {
            if (card.Data.timedEffects[t].IsOnAllyUnitRestedByOpponentEffectResolutionBlock())
            {
                return true;
            }
        }

        return false;
    }

    private void RunAllyUnitRestedByOpponentWatchCards(
        PlayerType ownerType,
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
            RunAllyUnitRestedByOpponentWatchCards(ownerType, watchers, index + 1, onComplete);
            return;
        }

        EffectActivationContext activationContext = BuildActivationContext(ownerType, watcher);
        List<TimedEffectData> blocks = new List<TimedEffectData>();
        List<int> blockIndices = new List<int>();
        for (int i = 0; i < watcher.Data.timedEffects.Count; i++)
        {
            TimedEffectData timed = watcher.Data.timedEffects[i];
            if (!timed.IsOnAllyUnitRestedByOpponentEffectResolutionBlock())
            {
                continue;
            }

            if (timed.oncePerTurn && HasUsedPaidActivationThisTurn(ownerType, watcher, i))
            {
                continue;
            }

            if (!CanRunTimedBlockAtChainTime(timed, activationContext, "OnAllyUnitRestedByOpponentEffect"))
            {
                continue;
            }

            blocks.Add(timed);
            blockIndices.Add(i);
        }

        if (blocks.Count == 0)
        {
            RunAllyUnitRestedByOpponentWatchCards(ownerType, watchers, index + 1, onComplete);
            return;
        }

        BeginEffectChainObservationScope();
        RunAllyUnitRestedByOpponentWatchTimedBlocks(
            watcher,
            ownerType,
            blocks,
            blockIndices,
            0,
            () =>
            {
                EndEffectChainObservationScope();
                RunAllyUnitRestedByOpponentWatchCards(ownerType, watchers, index + 1, onComplete);
            });
    }

    private void RunAllyUnitRestedByOpponentWatchTimedBlocks(
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

        RunAllyUnitRestedByOpponentEffectChain(
            sourceCard,
            ownerType,
            block != null ? block.GetResolvedEffects() : null,
            0,
            () => RunAllyUnitRestedByOpponentWatchTimedBlocks(
                sourceCard,
                ownerType,
                blocks,
                blockIndices,
                blockIndex + 1,
                onComplete));
    }

    private void RunAllyUnitRestedByOpponentEffectChain(
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
        void ContinueChain()
        {
            RunAllyUnitRestedByOpponentEffectChain(
                sourceCard,
                ownerType,
                effects,
                effectIndex + 1,
                onComplete);
        }

        if (effect == null)
        {
            ContinueChain();
            return;
        }

        EffectActivationContext activationContext = BuildActivationContext(ownerType, sourceCard);
        if (!ShouldApplyChainedEffect(effect, activationContext, "AllyRestByOpponent"))
        {
            ContinueChain();
            return;
        }

        if (EffectRequiresManualUnitSelection(effect))
        {
            List<CardController> candidates = ResolveSelectableEffectTargets(sourceCard, ownerType, effect);
            if (candidates.Count == 0)
            {
                ContinueChain();
                return;
            }

            if (RequiresInteractiveManualUnitSelectionUi(ownerType))
            {
                StartCoroutine(CoRunAllyUnitRestedByOpponentPlayerManualSelection(
                    sourceCard,
                    ownerType,
                    effect,
                    candidates,
                    ContinueChain,
                    ContinueChain));
                return;
            }

            TryExecuteManualUnitSelectionEffect(
                sourceCard,
                ownerType,
                effect,
                null,
                ContinueChain,
                ContinueChain);
            return;
        }

        ApplyEffectRespectingLookAsync(
            sourceCard,
            ownerType,
            effect,
            ContinueChain);
    }

    private IEnumerator CoRunAllyUnitRestedByOpponentPlayerManualSelection(
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
                    Debug.Log(
                        $"[AllyRestByOpponentWatch] プレイヤーが対象を選択: {picked.Data?.cardName}(id:{picked.Data?.id}) "
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
                        $"[AllyRestByOpponentWatch] 対象選択キャンセル effect:{effect?.type} "
                        + $"source:{sourceCard?.Data?.cardName}(id:{sourceCard?.Data?.id})");
                    onSkipped?.Invoke();
                }
            });

        yield return new WaitUntil(() => resolved);
    }
}
