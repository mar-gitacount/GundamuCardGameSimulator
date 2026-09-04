using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// EXリソースがゲームから除外されたときの監視
/// （例: キャリバーン、スレッタ【リンク中】〔学園〕コマンドEX支払い）。
/// コスト支払い時点では保留し、カード解決後に Flush する。
/// 自分ターン（Main）・相手ターン（Action）どちらでも誘発する。
/// </summary>
public partial class BattleGameMain
{
    /// <summary>搭乗パイロットの OnExResourceRemoved 用 oncePerTurn キー（ユニットブロック index と衝突しない）。</summary>
    private const int ExRemovedPilotOncePerTurnBlockBase = 910000;

    private struct PendingExResourceRemoved
    {
        public PlayerType OwnerType;
        public int RemovedCount;
        /// <summary>EX 支払いの対象カード（コマンド等）。未設定可。</summary>
        public CardData PaidCard;
    }

    private struct ExRemovedWatchEntry
    {
        public CardController HostUnit;
        public CardController EffectSource;
        public TimedEffectData Timed;
        public int OncePerTurnBlockIndex;
    }

    private readonly List<PendingExResourceRemoved> _pendingExResourceRemoved =
        new List<PendingExResourceRemoved>();
    private bool _exRemovedFlushRunning;

    /// <summary>
    /// EX がゲームから除外されたことを保留キューへ積む（返金では呼ばない）。
    /// 実際の効果解決は <see cref="FlushPendingExResourceRemovedWatchesCoroutine"/>。
    /// </summary>
    private void EnqueueExResourceRemoved(PlayerType ownerType, int removedCount, CardData paidCard = null)
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
                if (merged.PaidCard == null && paidCard != null)
                {
                    merged.PaidCard = paidCard;
                }

                _pendingExResourceRemoved[i] = merged;
                Debug.Log(
                    $"[ExRemovedWatch] queued(merge) owner:{ownerType} removed:+{removedCount} "
                    + $"total:{merged.RemovedCount} paid:{paidCard?.cardName}");
                return;
            }
        }

        _pendingExResourceRemoved.Add(new PendingExResourceRemoved
        {
            OwnerType = ownerType,
            RemovedCount = removedCount,
            PaidCard = paidCard
        });
        Debug.Log(
            $"[ExRemovedWatch] queued owner:{ownerType} removed:{removedCount} paid:{paidCard?.cardName}");
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
                    pending.PaidCard,
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
        CardData paidCard = null,
        Action onComplete = null)
    {
        if (removedCount <= 0)
        {
            onComplete?.Invoke();
            return;
        }

        List<ExRemovedWatchEntry> entries = CollectExRemovedWatchEntries(ownerType, paidCard);
        if (entries.Count == 0)
        {
            Debug.Log(
                $"[ExRemovedWatch] no watchers owner:{ownerType} removed:{removedCount} "
                + $"paid:{paidCard?.cardName}");
            onComplete?.Invoke();
            return;
        }

        Debug.Log(
            $"[ExRemovedWatch] resolve removed:{removedCount} → entries:{entries.Count} "
            + $"owner:{ownerType} paid:{paidCard?.cardName} "
            + $"(turnOwner:{currentPlayerType} → oppTurnOk)");

        RunExResourceRemovedWatchEntries(ownerType, paidCard, entries, 0, onComplete);
    }

    private List<ExRemovedWatchEntry> CollectExRemovedWatchEntries(PlayerType ownerType, CardData paidCard)
    {
        List<ExRemovedWatchEntry> result = new List<ExRemovedWatchEntry>();
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

            AppendExRemovedWatchEntriesFromCard(
                result,
                ownerType,
                unit,
                unit,
                unit.Data.timedEffects,
                isPilotSource: false,
                paidCard);

            CardController pilot = unit.MountedPilot;
            if (pilot?.Data?.timedEffects != null)
            {
                AppendExRemovedWatchEntriesFromCard(
                    result,
                    ownerType,
                    unit,
                    pilot,
                    pilot.Data.timedEffects,
                    isPilotSource: true,
                    paidCard);
            }
        }

        return result;
    }

    private void AppendExRemovedWatchEntriesFromCard(
        List<ExRemovedWatchEntry> result,
        PlayerType ownerType,
        CardController hostUnit,
        CardController effectSource,
        List<TimedEffectData> timedEffects,
        bool isPilotSource,
        CardData paidCard)
    {
        if (result == null || hostUnit == null || effectSource == null || timedEffects == null)
        {
            return;
        }

        EffectActivationContext activationContext =
            BuildExRemovedActivationContext(ownerType, hostUnit, effectSource, paidCard);

        for (int t = 0; t < timedEffects.Count; t++)
        {
            TimedEffectData timed = timedEffects[t];
            if (!timed.IsOnExResourceRemovedResolutionBlock())
            {
                continue;
            }

            int onceKey = isPilotSource
                ? ExRemovedPilotOncePerTurnBlockBase + t
                : t;
            if (timed.oncePerTurn && HasUsedPaidActivationThisTurn(ownerType, hostUnit, onceKey))
            {
                Debug.Log(
                    $"[ExRemovedWatch] oncePerTurn used → skip {effectSource.Data?.cardName} block:{t}");
                continue;
            }

            if (!CanRunTimedBlockAtChainTime(timed, activationContext, "OnExResourceRemoved"))
            {
                continue;
            }

            result.Add(new ExRemovedWatchEntry
            {
                HostUnit = hostUnit,
                EffectSource = effectSource,
                Timed = timed,
                OncePerTurnBlockIndex = onceKey
            });
        }
    }

    private EffectActivationContext BuildExRemovedActivationContext(
        PlayerType ownerType,
        CardController hostUnit,
        CardController effectSource,
        CardData paidCard)
    {
        Gundam2024RuleScript.PlayerState ownerState = ownerType == PlayerType.Player
            ? gundamRule.Player
            : gundamRule.Enemy;
        CardData[] observed = paidCard != null
            ? new[] { paidCard }
            : Array.Empty<CardData>();

        // Source はホストユニット（リンク判定・効果適用の主体）。パイロット効果もホスト基準。
        return new EffectActivationContext(
            ownerType,
            hostUnit,
            playerBattleZoneCards,
            enemyBattleZoneCards,
            CollectHandControllers(cardGameRule),
            CollectHandControllers(enemyCardGameRule),
            isOwnerTurn: ownerType == currentPlayerType,
            mountHostUnit: hostUnit,
            mountedPilot: hostUnit != null ? hostUnit.MountedPilot : null,
            observedCards: observed,
            ownerTrashCardIds: cardGameRule.GetTrashCardIds(),
            opponentTrashCardIds: enemyCardGameRule.GetTrashCardIds(),
            priorChainDealtDamage: GetEffectChainDealtDamage(),
            ownerActivatedSpecialMoveCommandThisTurn: HasOwnerActivatedSpecialMoveCommandThisTurn(ownerType),
            ownerHasDeployedBase: HasActiveDeployedBaseForRuleSide(ToRuleSide(ownerType)),
            ownerTotalLevel: ownerState != null ? ownerState.TotalLevel : -1,
            ownerExResource: ownerState != null ? ownerState.exResource : -1);
    }

    private void RunExResourceRemovedWatchEntries(
        PlayerType ownerType,
        CardData paidCard,
        List<ExRemovedWatchEntry> entries,
        int index,
        Action onComplete)
    {
        if (entries == null || index >= entries.Count)
        {
            onComplete?.Invoke();
            return;
        }

        ExRemovedWatchEntry entry = entries[index];
        if (entry.HostUnit == null || entry.Timed == null)
        {
            RunExResourceRemovedWatchEntries(ownerType, paidCard, entries, index + 1, onComplete);
            return;
        }

        // 解決直前に再評価（EX 枚数やリンク状態の最新値）
        EffectActivationContext activationContext =
            BuildExRemovedActivationContext(ownerType, entry.HostUnit, entry.EffectSource, paidCard);
        if (!CanRunTimedBlockAtChainTime(entry.Timed, activationContext, "OnExResourceRemoved"))
        {
            RunExResourceRemovedWatchEntries(ownerType, paidCard, entries, index + 1, onComplete);
            return;
        }

        if (entry.Timed.oncePerTurn)
        {
            MarkPaidActivationUsedThisTurn(ownerType, entry.HostUnit, entry.OncePerTurnBlockIndex);
        }

        BeginEffectChainObservationScope();
        if (paidCard != null)
        {
            ObserveCardInEffectChain(paidCard);
        }

        List<TimedEffectData> blocks = new List<TimedEffectData> { entry.Timed };
        RunOnPlayedTimedBlocks(
            entry.HostUnit,
            ownerType,
            blocks,
            0,
            () =>
            {
                EndEffectChainObservationScope();
                RunExResourceRemovedWatchEntries(ownerType, paidCard, entries, index + 1, onComplete);
            });
    }

    /// <summary>リソース消費後。EX を除外した場合は監視を保留キューへ積む。</summary>
    private void AfterLocalResourceConsumed(
        Gundam2024RuleScript.PlayerSide side,
        int exUsed,
        CardData paidCard = null)
    {
        AfterLocalResourceChanged(side);
        if (exUsed <= 0)
        {
            return;
        }

        PlayerType ownerType = side == Gundam2024RuleScript.PlayerSide.Player
            ? PlayerType.Player
            : PlayerType.Enemy;
        EnqueueExResourceRemoved(ownerType, exUsed, paidCard);
    }
}
