using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// EXリソースがゲームから除外されたときの監視（例: キャリバーンのダメージ軽減付与）。
/// コスト支払い時点では保留し、カード解決後に Flush する。
/// </summary>
public partial class BattleGameMain
{
    private struct PendingExResourceRemoved
    {
        public PlayerType OwnerType;
        public int RemovedCount;
    }

    private readonly List<PendingExResourceRemoved> _pendingExResourceRemoved =
        new List<PendingExResourceRemoved>();
    private bool _exRemovedFlushRunning;

    /// <summary>
    /// EX がゲームから除外されたことを保留キューへ積む（返金では呼ばない）。
    /// 実際の効果解決は <see cref="FlushPendingExResourceRemovedWatchesCoroutine"/>。
    /// </summary>
    private void EnqueueExResourceRemoved(PlayerType ownerType, int removedCount)
    {
        if (removedCount <= 0)
        {
            return;
        }

        // 同一オーナーの連続除外はまとめて1回の監視にする（1回の支払い＝1誘発）
        for (int i = 0; i < _pendingExResourceRemoved.Count; i++)
        {
            if (_pendingExResourceRemoved[i].OwnerType == ownerType)
            {
                PendingExResourceRemoved merged = _pendingExResourceRemoved[i];
                merged.RemovedCount += removedCount;
                _pendingExResourceRemoved[i] = merged;
                Debug.Log(
                    $"[ExRemovedWatch] queued(merge) owner:{ownerType} removed:+{removedCount} "
                    + $"total:{merged.RemovedCount}");
                return;
            }
        }

        _pendingExResourceRemoved.Add(new PendingExResourceRemoved
        {
            OwnerType = ownerType,
            RemovedCount = removedCount
        });
        Debug.Log($"[ExRemovedWatch] queued owner:{ownerType} removed:{removedCount}");
    }

    /// <summary>返金などで EX 除外監視を取り消す。</summary>
    private void ClearPendingExResourceRemovedForSide(PlayerType ownerType)
    {
        for (int i = _pendingExResourceRemoved.Count - 1; i >= 0; i--)
        {
            if (_pendingExResourceRemoved[i].OwnerType == ownerType)
            {
                _pendingExResourceRemoved.RemoveAt(i);
            }
        }
    }

    /// <summary>保留中の EX 除外監視を、カード解決後に順に解決する。</summary>
    private IEnumerator FlushPendingExResourceRemovedWatchesCoroutine()
    {
        if (_exRemovedFlushRunning)
        {
            yield return new WaitUntil(() => !_exRemovedFlushRunning);
            yield break;
        }

        if (_pendingExResourceRemoved.Count == 0)
        {
            yield break;
        }

        _exRemovedFlushRunning = true;
        try
        {
            // 直前の FilterPanel / 支払い UI 破棄を待ってから出す
            yield return null;
            yield return WaitUntilBlockingChoiceOrTrashUiCleared();

            while (_pendingExResourceRemoved.Count > 0)
            {
                PendingExResourceRemoved pending = _pendingExResourceRemoved[0];
                _pendingExResourceRemoved.RemoveAt(0);

                bool finished = false;
                NotifyExResourceRemoved(
                    pending.OwnerType,
                    pending.RemovedCount,
                    () => finished = true);
                yield return new WaitUntil(() => finished);
                yield return WaitUntilBlockingChoiceOrTrashUiCleared();
            }
        }
        finally
        {
            _exRemovedFlushRunning = false;
        }
    }

    /// <summary>
    /// EX 除外監視を即時実行する（Flush から呼ばれる）。
    /// </summary>
    private void NotifyExResourceRemoved(
        PlayerType ownerType,
        int removedCount,
        Action onComplete = null)
    {
        if (removedCount <= 0)
        {
            onComplete?.Invoke();
            return;
        }

        List<CardController> watchers = CollectOwnerBattleUnitsWithExRemovedWatch(ownerType);
        if (watchers.Count == 0)
        {
            Debug.Log($"[ExRemovedWatch] no watchers owner:{ownerType} removed:{removedCount}");
            onComplete?.Invoke();
            return;
        }

        Debug.Log(
            $"[ExRemovedWatch] resolve removed:{removedCount} → watchers:{watchers.Count} owner:{ownerType}");

        RunExResourceRemovedWatchUnits(ownerType, watchers, 0, onComplete);
    }

    private List<CardController> CollectOwnerBattleUnitsWithExRemovedWatch(PlayerType ownerType)
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
                if (unit.Data.timedEffects[t].IsOnExResourceRemovedResolutionBlock())
                {
                    result.Add(unit);
                    break;
                }
            }
        }

        return result;
    }

    private void RunExResourceRemovedWatchUnits(
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

        CardController unit = watchers[index];
        if (unit == null || unit.Data?.timedEffects == null)
        {
            RunExResourceRemovedWatchUnits(ownerType, watchers, index + 1, onComplete);
            return;
        }

        EffectActivationContext activationContext = BuildActivationContext(ownerType, unit);
        List<TimedEffectData> blocks = new List<TimedEffectData>();
        for (int i = 0; i < unit.Data.timedEffects.Count; i++)
        {
            TimedEffectData timed = unit.Data.timedEffects[i];
            if (!timed.IsOnExResourceRemovedResolutionBlock())
            {
                continue;
            }

            if (!CanRunTimedBlockAtChainTime(timed, activationContext, "OnExResourceRemoved"))
            {
                continue;
            }

            blocks.Add(timed);
        }

        if (blocks.Count == 0)
        {
            RunExResourceRemovedWatchUnits(ownerType, watchers, index + 1, onComplete);
            return;
        }

        BeginEffectChainObservationScope();
        RunOnPlayedTimedBlocks(
            unit,
            ownerType,
            blocks,
            0,
            () =>
            {
                EndEffectChainObservationScope();
                RunExResourceRemovedWatchUnits(ownerType, watchers, index + 1, onComplete);
            });
    }

    /// <summary>リソース消費後。EX を除外した場合は監視を保留キューへ積む。</summary>
    private void AfterLocalResourceConsumed(Gundam2024RuleScript.PlayerSide side, int exUsed)
    {
        AfterLocalResourceChanged(side);
        if (exUsed <= 0)
        {
            return;
        }

        PlayerType ownerType = side == Gundam2024RuleScript.PlayerSide.Player
            ? PlayerType.Player
            : PlayerType.Enemy;
        EnqueueExResourceRemoved(ownerType, exUsed);
    }
}
