using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 自分の EX リソースが置かれたときの監視（GD02-022 Gエグゼス等）。
/// 効果による追加のみ。返金の戻しは含まない。
/// </summary>
public partial class BattleGameMain
{
    private const int ExPlacedPilotOncePerTurnBlockBase = 920000;

    private struct PendingExResourcePlaced
    {
        public PlayerType OwnerType;
        public int PlacedCount;
    }

    private struct ExPlacedWatchEntry
    {
        public CardController HostUnit;
        public CardController EffectSource;
        public TimedEffectData Timed;
        public int OncePerTurnBlockIndex;
    }

    private readonly List<PendingExResourcePlaced> _pendingExResourcePlaced =
        new List<PendingExResourcePlaced>();
    private bool _exPlacedFlushRunning;

    private void EnqueueExResourcePlaced(PlayerType ownerType, int placedCount)
    {
        if (placedCount <= 0)
        {
            return;
        }

        for (int i = 0; i < _pendingExResourcePlaced.Count; i++)
        {
            if (_pendingExResourcePlaced[i].OwnerType == ownerType)
            {
                PendingExResourcePlaced merged = _pendingExResourcePlaced[i];
                merged.PlacedCount += placedCount;
                _pendingExResourcePlaced[i] = merged;
                return;
            }
        }

        _pendingExResourcePlaced.Add(new PendingExResourcePlaced
        {
            OwnerType = ownerType,
            PlacedCount = placedCount
        });
    }

    private IEnumerator FlushPendingExResourcePlacedWatchesCoroutine()
    {
        if (_exPlacedFlushRunning)
        {
            yield return new WaitUntil(() => !_exPlacedFlushRunning);
            yield break;
        }

        if (_pendingExResourcePlaced.Count == 0)
        {
            yield break;
        }

        _exPlacedFlushRunning = true;
        try
        {
            yield return null;
            yield return WaitUntilBlockingChoiceOrTrashUiCleared();

            while (_pendingExResourcePlaced.Count > 0)
            {
                PendingExResourcePlaced pending = _pendingExResourcePlaced[0];
                _pendingExResourcePlaced.RemoveAt(0);

                bool finished = false;
                NotifyExResourcePlaced(pending.OwnerType, pending.PlacedCount, () => finished = true);
                yield return new WaitUntil(() => finished);
                yield return WaitUntilBlockingChoiceOrTrashUiCleared();
            }
        }
        finally
        {
            _exPlacedFlushRunning = false;
        }
    }

    private void NotifyExResourcePlaced(PlayerType ownerType, int placedCount, Action onComplete = null)
    {
        if (placedCount <= 0)
        {
            onComplete?.Invoke();
            return;
        }

        List<ExPlacedWatchEntry> entries = CollectExPlacedWatchEntries(ownerType);
        if (entries.Count == 0)
        {
            onComplete?.Invoke();
            return;
        }

        Debug.Log(
            $"[ExPlacedWatch] resolve placed:{placedCount} → entries:{entries.Count} owner:{ownerType}");
        RunExResourcePlacedWatchEntries(ownerType, entries, 0, onComplete);
    }

    private List<ExPlacedWatchEntry> CollectExPlacedWatchEntries(PlayerType ownerType)
    {
        List<ExPlacedWatchEntry> result = new List<ExPlacedWatchEntry>();
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

            AppendExPlacedWatchEntriesFromCard(
                result,
                ownerType,
                unit,
                unit,
                unit.Data.timedEffects,
                isPilotSource: false);

            CardController pilot = unit.MountedPilot;
            if (pilot?.Data?.timedEffects != null)
            {
                AppendExPlacedWatchEntriesFromCard(
                    result,
                    ownerType,
                    unit,
                    pilot,
                    pilot.Data.timedEffects,
                    isPilotSource: true);
            }
        }

        return result;
    }

    private void AppendExPlacedWatchEntriesFromCard(
        List<ExPlacedWatchEntry> result,
        PlayerType ownerType,
        CardController hostUnit,
        CardController effectSource,
        List<TimedEffectData> timedEffects,
        bool isPilotSource)
    {
        if (result == null || hostUnit == null || effectSource == null || timedEffects == null)
        {
            return;
        }

        EffectActivationContext activationContext = BuildActivationContext(ownerType, hostUnit);
        for (int t = 0; t < timedEffects.Count; t++)
        {
            TimedEffectData timed = timedEffects[t];
            if (!timed.IsOnExResourcePlacedResolutionBlock())
            {
                continue;
            }

            int onceKey = isPilotSource
                ? ExPlacedPilotOncePerTurnBlockBase + t
                : t;
            if (timed.oncePerTurn && HasUsedPaidActivationThisTurn(ownerType, hostUnit, onceKey))
            {
                continue;
            }

            if (!CanRunTimedBlockAtChainTime(timed, activationContext, "OnExResourcePlaced"))
            {
                continue;
            }

            result.Add(new ExPlacedWatchEntry
            {
                HostUnit = hostUnit,
                EffectSource = effectSource,
                Timed = timed,
                OncePerTurnBlockIndex = onceKey
            });
        }
    }

    private void RunExResourcePlacedWatchEntries(
        PlayerType ownerType,
        List<ExPlacedWatchEntry> entries,
        int index,
        Action onComplete)
    {
        if (entries == null || index >= entries.Count)
        {
            onComplete?.Invoke();
            return;
        }

        ExPlacedWatchEntry entry = entries[index];
        if (entry.HostUnit == null || entry.Timed == null)
        {
            RunExResourcePlacedWatchEntries(ownerType, entries, index + 1, onComplete);
            return;
        }

        EffectActivationContext activationContext = BuildActivationContext(ownerType, entry.HostUnit);
        if (!CanRunTimedBlockAtChainTime(entry.Timed, activationContext, "OnExResourcePlaced"))
        {
            RunExResourcePlacedWatchEntries(ownerType, entries, index + 1, onComplete);
            return;
        }

        if (entry.Timed.oncePerTurn)
        {
            MarkPaidActivationUsedThisTurn(ownerType, entry.HostUnit, entry.OncePerTurnBlockIndex);
        }

        List<TimedEffectData> blocks = new List<TimedEffectData> { entry.Timed };
        RunOnPlayedTimedBlocks(
            entry.HostUnit,
            ownerType,
            blocks,
            0,
            () => RunExResourcePlacedWatchEntries(ownerType, entries, index + 1, onComplete));
    }
}
