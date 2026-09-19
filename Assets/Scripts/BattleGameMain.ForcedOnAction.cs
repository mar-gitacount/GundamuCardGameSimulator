using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// アクションステップの強制発動（forcedAction）。
/// TimedEffectData.forcedAction=true の未使用ソースがあるあいだ、
/// ActionEnd / Cancel を封じる。ST12-011 以外でもデータ設定だけで再利用可能。
/// </summary>
public partial class BattleGameMain
{
    /// <summary>OnAction ブロックに forcedAction が付いているソースか。</summary>
    private bool IsForcedOnActionSource(CardController source)
    {
        TimedEffectData timed = FindFirstOnActionTimedBlock(source, out _);
        return timed != null && timed.forcedAction;
    }

    /// <summary>
    /// いま発動可能な強制 OnAction ソース一覧（条件・ターン1回を満たすもの）。
    /// </summary>
    private List<CardController> CollectPendingForcedOnActionSources(PlayerType side)
    {
        List<CardController> result = new List<CardController>();
        List<CardController> selectable = CollectOnActionSelectableSources(side);
        for (int i = 0; i < selectable.Count; i++)
        {
            CardController source = selectable[i];
            if (source == null || !IsForcedOnActionSource(source))
            {
                continue;
            }

            if (IsActionStepCardUsedForSide(side, source))
            {
                continue;
            }

            result.Add(source);
        }

        return result;
    }

    /// <summary>未消化の強制 OnAction が残っているか。</summary>
    private bool HasPendingForcedOnAction(PlayerType side)
    {
        return CollectPendingForcedOnActionSources(side).Count > 0;
    }

    /// <summary>Confirm 選択に、未消化の強制ソースがすべて含まれているか。</summary>
    private bool SelectedCommandsIncludeAllPendingForced(
        PlayerType side,
        IList<CardController> selectedCommands)
    {
        List<CardController> pending = CollectPendingForcedOnActionSources(side);
        if (pending.Count == 0)
        {
            return true;
        }

        if (selectedCommands == null || selectedCommands.Count == 0)
        {
            return false;
        }

        for (int i = 0; i < pending.Count; i++)
        {
            CardController forced = pending[i];
            bool found = false;
            for (int j = 0; j < selectedCommands.Count; j++)
            {
                if (ReferenceEquals(selectedCommands[j], forced)
                    || (forced != null
                        && selectedCommands[j] != null
                        && forced.BattleInstanceId > 0
                        && selectedCommands[j].BattleInstanceId == forced.BattleInstanceId))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// ActionEnd / 空の Cancel(Pass) が強制 OnAction で拒否されるべきか。
    /// </summary>
    private bool ShouldBlockActionStepPassOrEnd(
        PlayerType side,
        ActionStepPassKind passKind,
        IList<CardController> selectedCommands)
    {
        if (!HasPendingForcedOnAction(side))
        {
            return false;
        }

        if (passKind == ActionStepPassKind.ActionEnd)
        {
            return true;
        }

        // Cancel = 選択なし Pass
        if (passKind == ActionStepPassKind.Pass
            && (selectedCommands == null || selectedCommands.Count == 0))
        {
            return true;
        }

        // Confirm だが強制を含まない
        if (passKind == ActionStepPassKind.Pass
            && !SelectedCommandsIncludeAllPendingForced(side, selectedCommands))
        {
            return true;
        }

        return false;
    }

    /// <summary>敵 AI: 強制 OnAction を先にすべて解決してから通常 AI / ActionEnd へ。</summary>
    private bool TryExecuteEnemyForcedOnActionsIfAny(
        System.Action onStepDone,
        CardController attackingUnitInAttackFlow)
    {
        List<CardController> forced = CollectPendingForcedOnActionSources(PlayerType.Enemy);
        if (forced.Count == 0)
        {
            return false;
        }

        Debug.Log(
            $"[ForcedOnAction] Enemy resolving {forced.Count} forced source(s) "
            + $"(first:{forced[0].Data?.cardName})");
        ExecuteForcedOnActionQueue(
            PlayerType.Enemy,
            forced,
            0,
            onStepDone,
            attackingUnitInAttackFlow);
        return true;
    }

    private void ExecuteForcedOnActionQueue(
        PlayerType side,
        List<CardController> queue,
        int index,
        System.Action onDone,
        CardController attackingUnitInAttackFlow)
    {
        while (index < queue.Count)
        {
            CardController source = queue[index];
            if (source != null
                && source.Data != null
                && IsForcedOnActionSource(source)
                && CanExecuteOnActionCardNow(side, source)
                && !IsActionStepCardUsedForSide(side, source))
            {
                break;
            }

            index++;
        }

        if (index >= queue.Count)
        {
            onDone?.Invoke();
            return;
        }

        CardController next = queue[index];
        TryExecuteEnemyOnActionCommand(
            side,
            next,
            () => ExecuteForcedOnActionQueue(
                side,
                queue,
                index + 1,
                onDone,
                attackingUnitInAttackFlow),
            attackingUnitInAttackFlow);
    }
}
