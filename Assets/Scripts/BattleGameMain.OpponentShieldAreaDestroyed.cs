using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 相手シールドエリアのカードをダメージ破壊したとき（OnOpponentShieldAreaCardDestroyed）。
/// </summary>
public partial class BattleGameMain
{
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

        if (sourceUnit.Data.timedEffects == null || sourceUnit.Data.timedEffects.Count == 0)
        {
            onComplete?.Invoke();
            return;
        }

        EffectActivationContext activationContext = BuildActivationContext(ownerType, sourceUnit);
        List<TimedEffectData> blocks = new List<TimedEffectData>();
        List<int> blockIndices = new List<int>();
        for (int i = 0; i < sourceUnit.Data.timedEffects.Count; i++)
        {
            TimedEffectData timed = sourceUnit.Data.timedEffects[i];
            if (timed == null || !timed.IsOnOpponentShieldAreaCardDestroyedResolutionBlock())
            {
                continue;
            }

            if (timed.oncePerTurn && HasUsedPaidActivationThisTurn(ownerType, sourceUnit, i))
            {
                Debug.Log(
                    $"[OnOpponentShieldAreaCardDestroyed] ターン1回使用済みのためスキップ "
                    + $"{sourceUnit.Data.cardName}(id:{sourceUnit.Data.id}) block:{i}");
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

            blocks.Add(timed);
            blockIndices.Add(i);
        }

        if (blocks.Count == 0)
        {
            onComplete?.Invoke();
            return;
        }

        Debug.Log(
            $"[OnOpponentShieldAreaCardDestroyed] {sourceUnit.Data.cardName}(id:{sourceUnit.Data.id}) "
            + $"blocks:{blocks.Count}");

        RunOnOpponentShieldAreaCardDestroyedTimedBlocks(
            sourceUnit,
            ownerType,
            blocks,
            blockIndices,
            0,
            onComplete);
    }

    private void RunOnOpponentShieldAreaCardDestroyedTimedBlocks(
        CardController sourceUnit,
        PlayerType ownerType,
        List<TimedEffectData> blocks,
        List<int> blockIndices,
        int index,
        Action onComplete)
    {
        if (blocks == null || index >= blocks.Count)
        {
            onComplete?.Invoke();
            return;
        }

        TimedEffectData timed = blocks[index];
        int blockIndex = blockIndices != null && index < blockIndices.Count ? blockIndices[index] : index;
        if (timed != null && timed.oncePerTurn)
        {
            MarkPaidActivationUsedThisTurn(ownerType, sourceUnit, blockIndex);
        }

        TryExecuteOnOpponentShieldAreaCardDestroyedEffectChain(
            sourceUnit,
            ownerType,
            timed != null ? timed.GetResolvedEffects() : null,
            0,
            () => RunOnOpponentShieldAreaCardDestroyedTimedBlocks(
                sourceUnit,
                ownerType,
                blocks,
                blockIndices,
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
