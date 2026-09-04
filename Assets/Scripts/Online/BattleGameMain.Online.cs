using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class BattleGameMain
{
    private IBattleOpponent battleOpponent;
    private bool networkBattleHooksRegistered;
    private int _nextBattleInstanceId = 1;
    private bool _applyingRemoteBattleAction;
    private int _onlineAttackRequestIdCounter;
    private int _pendingOnlineBlockRequestId;
    private System.Action<int> _pendingOnlineBlockCallback;
    private int _onlineEndTurnRequestIdCounter;
    private int _pendingOnlineEndTurnRequestId;
    private int _appliedRemoteEndTurnRequestId;
    private Coroutine _pendingOnlineEndTurnRetryCoroutine;
    /// <summary>自分ターンの Draw 以降が実際に始まっていれば true。相手ターンの MainPhase 残留と区別する。</summary>
    private bool _onlineLocalTurnSequenceStarted;
    private List<OnlineBattleUnitEffectChange> _pendingOnlineEffectChanges;
    private bool _onlineEffectSyncActive;

    private bool ShouldUseOnlineBlockPhase(PlayerType attackerOwner)
    {
        return IsOnlineBattle() && attackerOwner == PlayerType.Player && !_applyingRemoteBattleAction;
    }

    private void AssignBattleInstanceIdFromNetwork(CardController controller, int instanceId)
    {
        if (controller == null || instanceId <= 0)
        {
            return;
        }

        controller.AssignBattleInstanceId(instanceId);
        // ローカル採番と衝突しないよう、受信 ID 以上にカウンタを進める
        if (instanceId >= _nextBattleInstanceId)
        {
            _nextBattleInstanceId = instanceId + 1;
        }
    }

    private static PlayerType MirrorOnlineZoneOwner(PlayerType senderZoneOwner)
    {
        return senderZoneOwner == PlayerType.Player ? PlayerType.Enemy : PlayerType.Player;
    }

    /// <summary>ユニットが属するバトルゾーン（ローカル視点）。</summary>
    private PlayerType ResolveBattleZoneSideForUnit(CardController unit)
    {
        if (unit == null)
        {
            return PlayerType.Player;
        }

        if (playerBattleZoneCards != null && playerBattleZoneCards.Contains(unit))
        {
            return PlayerType.Player;
        }

        return PlayerType.Enemy;
    }

    /// <summary>送信側のゾーン指定から、受信側のミラー上のユニットを解決する。</summary>
    private CardController FindBattleZoneUnitForRemoteSync(int instanceId, PlayerType senderZoneOwner)
    {
        if (instanceId <= 0)
        {
            return null;
        }

        return FindBattleZoneUnitByInstanceId(instanceId, MirrorOnlineZoneOwner(senderZoneOwner));
    }

    /// <summary>攻撃側が受け取るブロック応答のブロッカー（相手ミラー＝Enemy ゾーン）。</summary>
    private CardController FindBlockerUnitFromRemoteResponse(int blockerInstanceId)
    {
        return FindBattleZoneUnitByInstanceId(blockerInstanceId, PlayerType.Enemy);
    }

    private CardController FindEffectSyncTargetUnit(OnlineBattleUnitEffectChange change)
    {
        if (change == null)
        {
            return null;
        }

        bool hasZoneOwner = change.targetZoneOwnerSide == (int)PlayerType.Player
            || change.targetZoneOwnerSide == (int)PlayerType.Enemy;

        // BattleInstanceId を優先（G-fred 等の全員ダメージで途中撃破後にスロットがずれても特定できる）
        if (change.targetInstanceId > 0)
        {
            if (hasZoneOwner)
            {
                PlayerType localZone = MirrorOnlineZoneOwner((PlayerType)change.targetZoneOwnerSide);
                CardController byInstance = FindBattleZoneUnitByInstanceId(
                    change.targetInstanceId,
                    localZone,
                    change.targetCardId);
                if (byInstance != null)
                {
                    Debug.Log(
                        $"[EffectSync][FindByInstance][OK] unit={FormatOnlineEffectSyncUnit(byInstance)} "
                        + FormatOnlineEffectSyncChange(change));
                    return byInstance;
                }
            }

            CardController byInstanceEither = FindUnitByInstanceIdEitherZone(change.targetInstanceId, change.targetCardId);
            if (byInstanceEither != null)
            {
                Debug.Log(
                    $"[EffectSync][FindByInstanceEither][OK] unit={FormatOnlineEffectSyncUnit(byInstanceEither)} "
                    + FormatOnlineEffectSyncChange(change));
                return byInstanceEither;
            }
        }

        return FindEffectSyncTargetUnitBySlot(change, hasZoneOwner);
    }

    private static bool MatchesEffectSyncTargetCard(CardController unit, OnlineBattleUnitEffectChange change)
    {
        if (unit == null || unit.Data == null || change == null)
        {
            return false;
        }

        return change.targetCardId < 0 || unit.Data.id == change.targetCardId;
    }

    private static string FormatOnlineEffectSyncChange(OnlineBattleUnitEffectChange change)
    {
        if (change == null)
        {
            return "null";
        }

        return $"kind={change.changeKind} inst={change.targetInstanceId} "
            + $"zone={change.targetZoneOwnerSide} idx={change.targetZoneIndex} "
            + $"cardId={change.targetCardId} hpAfter={change.hpAfter} "
            + $"stat={change.signedStatValue}/{change.statTarget}/{change.duration}";
    }

    private static string FormatOnlineEffectSyncUnit(CardController unit)
    {
        if (unit == null || unit.Data == null)
        {
            return "null";
        }

        return $"{unit.Data.cardName}(cardId:{unit.Data.id} inst:{unit.BattleInstanceId} "
            + $"HP:{unit.CurrentHp} AP:{unit.CurrentPower} {(unit.IsRestState ? "REST" : "ACTIVE")})";
    }

    private CardController FindEffectSyncTargetUnitBySlot(OnlineBattleUnitEffectChange change, bool hasZoneOwner)
    {
        if (!hasZoneOwner || change == null || change.targetCardId < 0 || change.targetZoneIndex < 0)
        {
            Debug.Log(
                $"[EffectSync][FindBySlot][Skip] hasZoneOwner={hasZoneOwner} "
                + FormatOnlineEffectSyncChange(change));
            return null;
        }

        List<CardController> zone = MirrorOnlineZoneOwner((PlayerType)change.targetZoneOwnerSide) == PlayerType.Player
            ? playerBattleZoneCards
            : enemyBattleZoneCards;
        if (zone == null || change.targetZoneIndex >= zone.Count)
        {
            Debug.LogWarning(
                $"[EffectSync][FindBySlot][Fail:Range] zoneCount={(zone != null ? zone.Count : -1)} "
                + FormatOnlineEffectSyncChange(change));
            return null;
        }

        CardController bySlot = zone[change.targetZoneIndex];
        if (bySlot != null && bySlot.Data != null && bySlot.Data.id == change.targetCardId)
        {
            Debug.Log(
                $"[EffectSync][FindBySlot][OK] slotUnit={FormatOnlineEffectSyncUnit(bySlot)} "
                + FormatOnlineEffectSyncChange(change));
            return bySlot;
        }

        if (change.targetInstanceId > 0)
        {
            for (int i = 0; i < zone.Count; i++)
            {
                CardController candidate = zone[i];
                if (candidate != null && candidate.BattleInstanceId == change.targetInstanceId
                    && MatchesEffectSyncTargetCard(candidate, change))
                {
                    Debug.Log(
                        $"[EffectSync][FindBySlotScanInstance][OK] unit={FormatOnlineEffectSyncUnit(candidate)} "
                        + FormatOnlineEffectSyncChange(change));
                    return candidate;
                }
            }
        }

        Debug.LogWarning(
            $"[EffectSync][FindBySlot][Fail:CardMismatch] slotUnit={FormatOnlineEffectSyncUnit(bySlot)} "
            + FormatOnlineEffectSyncChange(change));
        return null;
    }

    private bool TryQueueOnlineUnitTargetChange(CardController target, OnlineBattleUnitEffectChange change)
    {
        if (!_onlineEffectSyncActive || target == null || target.Data == null || change == null)
        {
            return false;
        }

        PlayerType zoneOwner = ResolveBattleZoneSideForUnit(target);
        int zoneIndex = ResolveBattleZoneIndexForOnlineEffect(target, zoneOwner);
        if (target.BattleInstanceId <= 0 && zoneIndex < 0)
        {
            return false;
        }

        change.targetInstanceId = target.BattleInstanceId;
        change.targetZoneOwnerSide = (int)zoneOwner;
        change.targetCardId = target.Data.id;
        change.targetZoneIndex = zoneIndex;
        _pendingOnlineEffectChanges.Add(change);
        Debug.Log(
            $"[EffectSync][SendQueue] target={FormatOnlineEffectSyncUnit(target)} "
            + FormatOnlineEffectSyncChange(change));
        return true;
    }

    private int ResolveBattleZoneIndexForOnlineEffect(CardController target, PlayerType zoneOwner)
    {
        List<CardController> zone = zoneOwner == PlayerType.Player ? playerBattleZoneCards : enemyBattleZoneCards;
        if (zone == null || target == null)
        {
            return -1;
        }

        for (int i = 0; i < zone.Count; i++)
        {
            if (zone[i] == target)
            {
                return i;
            }
        }

        return -1;
    }

    private bool IsOnlineBattle()
    {
        return EosOnlineMatchState.HasActiveMatch;
    }

    private void InitializeBattleOpponent()
    {
        battleOpponent = IsOnlineBattle()
            ? (IBattleOpponent)new NetworkBattleOpponent()
            : IsTestPlayBattle()
                ? (IBattleOpponent)new TestPlayBattleOpponent()
                : new CpuBattleOpponent();

        RegisterNetworkBattleHooksIfNeeded();
    }

    private void ConfigureOnlineBattleDecks(ref Dictionary<int, int> playerDeck, ref Dictionary<int, int> enemyDeck)
    {
        if (!IsOnlineBattle())
        {
            return;
        }

        playerDeck = playerDeck ?? new Dictionary<int, int>();
        enemyDeck = new Dictionary<int, int>(playerDeck);
        Debug.Log("[OnlineBattle] Using mirrored deck data for opponent zone bootstrap.");
    }

    private int? GetOnlineDeckSeed(bool isPlayerDeck)
    {
        if (!IsOnlineBattle())
        {
            return null;
        }

        return isPlayerDeck ? EosOnlineMatchState.Seed : EosOnlineMatchState.Seed + 1;
    }

    private bool IsLocalOnlineTurn()
    {
        return !IsOnlineBattle() || currentPlayerType == PlayerType.Player;
    }

    private bool ShouldSkipEnemyMulliganOnline()
    {
        return IsOnlineBattle();
    }

    private bool ShouldSkipEnemyOpeningHandOnline()
    {
        return IsOnlineBattle();
    }

    private bool ShouldSkipEnemyDrawOnline()
    {
        return IsOnlineBattle();
    }

    private void ResetOnlineBattleInstanceIds()
    {
        _nextBattleInstanceId = 1;
        _onlineAttackRequestIdCounter = 0;
        _onlineEndTurnRequestIdCounter = 0;
        _pendingOnlineBlockRequestId = 0;
        _pendingOnlineBlockCallback = null;
        StopPendingOnlineEndTurnRetry();
        _pendingOnlineEndTurnRequestId = 0;
        _appliedRemoteEndTurnRequestId = 0;
        _pendingOnlineEffectChanges = null;
        _onlineEffectSyncActive = false;
        ResetOnlineOnActionState();
        ResetOnlineMulliganSyncState();
        ResetOnlineShieldBreakSyncState();
        ResetOnlineHandDiscardRevealState();
        ResetOnlineOnActionCommandRevealState();
        ResetOnlineZoneSyncState();
    }

    private void AssignBattleInstanceIdIfNeeded(CardController controller)
    {
        if (controller == null || controller.Data == null || !controller.Data.IsUnitLike())
        {
            return;
        }

        if (controller.BattleInstanceId > 0)
        {
            return;
        }

        controller.AssignBattleInstanceId(_nextBattleInstanceId++);
    }

    private int AllocateBattleInstanceId()
    {
        return _nextBattleInstanceId++;
    }

    private CardController FindBattleZoneUnitByInstanceId(int instanceId, PlayerType zoneOwner, int requiredCardId = -1)
    {
        if (instanceId <= 0)
        {
            return null;
        }

        List<CardController> zone = zoneOwner == PlayerType.Player ? playerBattleZoneCards : enemyBattleZoneCards;
        if (zone == null)
        {
            return null;
        }

        CardController fallback = null;
        for (int i = 0; i < zone.Count; i++)
        {
            CardController card = zone[i];
            if (card == null || card.BattleInstanceId != instanceId)
            {
                continue;
            }

            if (requiredCardId < 0 || (card.Data != null && card.Data.id == requiredCardId))
            {
                return card;
            }

            if (fallback == null)
            {
                fallback = card;
            }
        }

        return requiredCardId >= 0 ? null : fallback;
    }

    /// <summary>ローカル専用。オンライン同期ではゾーン明示の検索を使うこと。</summary>
    private CardController FindUnitByInstanceIdEitherZone(int instanceId, int requiredCardId = -1)
    {
        CardController unit = FindBattleZoneUnitByInstanceId(instanceId, PlayerType.Player, requiredCardId);
        if (unit != null)
        {
            return unit;
        }

        return FindBattleZoneUnitByInstanceId(instanceId, PlayerType.Enemy, requiredCardId);
    }

    private bool ShouldSyncOnlineEffects(PlayerType ownerType)
    {
        // ローカル人間が発動した効果はターン・フェーズに関係なく同期する（防御側 OnAction の Close Combat 等）。
        return IsOnlineBattle()
            && !_applyingRemoteBattleAction
            && ownerType == PlayerType.Player;
    }

    /// <summary>攻撃フロー権限側（currentPlayerType==Player）からユニットの REST を相手へ同期する。</summary>
    private void SyncOnlineRestFromAttackAuthority(CardController unit)
    {
        if (!IsOnlineBattle() || unit == null || unit.BattleInstanceId <= 0
            || currentPlayerType != PlayerType.Player || _applyingRemoteBattleAction)
        {
            return;
        }

        BeginOnlineEffectSyncBatch(PlayerType.Player);
        QueueOnlineUnitRest(unit);
        FlushOnlineEffectSyncBatch();
    }

    // BeginOnlineEffectSyncBatch   … バッチ開始（溜め始める）
    private void BeginOnlineEffectSyncBatch(PlayerType ownerType)
    {
        if (!ShouldSyncOnlineEffects(ownerType))
        {
            Debug.Log(
                $"[EffectSync][BeginSkip] owner:{ownerType} "
                + $"isOnline:{IsOnlineBattle()} applyingRemote:{_applyingRemoteBattleAction} "
                + $"nested:{_onlineEffectSyncActive}");
            // 進行中のバッチを潰さない（相手ターン中の破壊→Refresh 連鎖で Flush が空になる原因）
            return;
        }

        _pendingOnlineEffectChanges ??= new List<OnlineBattleUnitEffectChange>();
        if (!_onlineEffectSyncActive)
        {
            _pendingOnlineEffectChanges.Clear();
            _onlineEffectSyncActive = true;
            Debug.Log($"[EffectSync][Begin] owner:{ownerType}");
        }
        else
        {
            Debug.Log(
                $"[EffectSync][BeginNested] owner:{ownerType} "
                + $"pending:{_pendingOnlineEffectChanges.Count}");
        }
    }
    // BeginOnlineEffectSyncBatch   … バッチ開始（溜め始める）
    // オンライン対戦で溜めた効果変更をまとめて相手に送る処理。
    // EOS P2P は 1 パケット約 1170 バイト制限があるため、超過時は分割送信する。
    private const int OnlineEffectSyncMaxMessageUtf8Bytes = 1100;

    private void FlushOnlineEffectSyncBatch()
    {
        if (!_onlineEffectSyncActive || _pendingOnlineEffectChanges == null || _pendingOnlineEffectChanges.Count == 0)
        {
            int pendingCount = _pendingOnlineEffectChanges != null ? _pendingOnlineEffectChanges.Count : -1;
            if (pendingCount > 0)
            {
                Debug.LogWarning(
                    $"[EffectSync][FlushSkip] active:{_onlineEffectSyncActive} pending:{pendingCount}");
            }

            _onlineEffectSyncActive = false;
            return;
        }

        List<OnlineBattleUnitEffectChange> pending = _pendingOnlineEffectChanges;
        int totalChanges = pending.Count;
        int damageCount = 0;
        for (int i = 0; i < pending.Count; i++)
        {
            OnlineBattleUnitEffectChange change = pending[i];
            Debug.Log($"[EffectSync][SendFlush] #{i} {FormatOnlineEffectSyncChange(change)}");
            if (change != null && change.changeKind == OnlineBattleEffectSyncPayload.ChangeKindDamage)
            {
                damageCount++;
            }
        }

        int chunkIndex = 0;
        int sentChanges = 0;
        int cursor = 0;
        while (cursor < pending.Count)
        {
            int chunkCount = 0;
            string messageJson = null;
            while (cursor + chunkCount < pending.Count)
            {
                int nextCount = chunkCount + 1;
                OnlineBattleUnitEffectChange[] chunk = new OnlineBattleUnitEffectChange[nextCount];
                for (int i = 0; i < nextCount; i++)
                {
                    chunk[i] = pending[cursor + i];
                }

                string effectJson = OnlineBattleEffectSyncPayload.ToJson(chunk);
                if (string.IsNullOrWhiteSpace(effectJson))
                {
                    break;
                }

                string candidateMessage = EosOnlineBattleMessage.CreateEffectSync(effectJson);
                int utf8Bytes = System.Text.Encoding.UTF8.GetByteCount(candidateMessage);
                if (chunkCount > 0 && utf8Bytes > OnlineEffectSyncMaxMessageUtf8Bytes)
                {
                    break;
                }

                messageJson = candidateMessage;
                chunkCount = nextCount;

                // 1 件でも上限を超える場合はそのまま送る（分割不可）
                if (utf8Bytes > OnlineEffectSyncMaxMessageUtf8Bytes)
                {
                    Debug.LogWarning(
                        $"[EffectSync][ChunkOversized] bytes={utf8Bytes} limit={OnlineEffectSyncMaxMessageUtf8Bytes} "
                        + $"chunkChanges=1 {FormatOnlineEffectSyncChange(chunk[0])}");
                    break;
                }
            }

            if (chunkCount <= 0 || string.IsNullOrWhiteSpace(messageJson))
            {
                Debug.LogWarning(
                    $"[EffectSync][ChunkFail] remaining={pending.Count - cursor} total={totalChanges}");
                break;
            }

            bool sent = SendOnlineBattleMessage(messageJson);
            Debug.Log(
                $"[OnlineBattle] Effect sync chunk sent={sent} #{chunkIndex} "
                + $"changes={chunkCount}/{totalChanges} damageTotal={damageCount} "
                + $"bytes={System.Text.Encoding.UTF8.GetByteCount(messageJson)}");
            if (!sent)
            {
                break;
            }

            sentChanges += chunkCount;
            cursor += chunkCount;
            chunkIndex++;
        }

        if (sentChanges < totalChanges)
        {
            Debug.LogError(
                $"[OnlineBattle] Effect sync incomplete. sent={sentChanges}/{totalChanges} "
                + $"(EOS packet size limit or send failure).");
        }

        pending.Clear();
        _onlineEffectSyncActive = false;
    }

    /// <summary>ダメージ同期をローカル破壊より先に送る（撃破副作用でバッチが潰れるのを防ぐ）。</summary>
    private void FlushOnlineEffectSyncBatchAfterDamageQueue(bool ownedNestedBatch)
    {
        if (ownedNestedBatch)
        {
            return;
        }

        FlushOnlineEffectSyncBatch();
    }

    private void QueueOnlineUnitDamage(CardController target, CardController destroyer = null)
    {
        if (!_onlineEffectSyncActive || target == null)
        {
            if (target != null)
            {
                Debug.LogWarning(
                    $"[EffectDamage][OnlineQueueDamageSkip] active:{_onlineEffectSyncActive} "
                    + $"target={FormatOnlineEffectSyncUnit(target)}");
            }

            return;
        }

        AssignBattleInstanceIdIfNeeded(target);
        if (destroyer != null)
        {
            AssignBattleInstanceIdIfNeeded(destroyer);
        }

        Debug.Log($"[EffectDamage][OnlineQueueDamage] target={FormatOnlineEffectSyncUnit(target)}");
        OnlineBattleUnitEffectChange change = new OnlineBattleUnitEffectChange
        {
            changeKind = OnlineBattleEffectSyncPayload.ChangeKindDamage,
            hpAfter = target.CurrentHp,
            destroyerInstanceId = destroyer != null ? destroyer.BattleInstanceId : 0
        };
        bool queued = TryQueueOnlineUnitTargetChange(target, change);
        if (!queued)
        {
            Debug.LogWarning($"[EffectDamage][OnlineQueueDamageFail] target={FormatOnlineEffectSyncUnit(target)}");
            return;
        }

        if (target.CurrentHp <= 0)
        {
            change.onDestroyedRequestId = PrepareOnlineOnDestroyedWait(target);
        }
    }

    private void QueueOnlineUnitRepair(CardController target)
    {
        if (!_onlineEffectSyncActive || target == null)
        {
            return;
        }

        AssignBattleInstanceIdIfNeeded(target);
        bool queued = TryQueueOnlineUnitTargetChange(target, new OnlineBattleUnitEffectChange
        {
            changeKind = OnlineBattleEffectSyncPayload.ChangeKindRepair,
            hpAfter = target.CurrentHp
        });
        Debug.Log(
            $"[EffectSync][QueueRepair] queued:{queued} unit={FormatOnlineEffectSyncUnit(target)} "
            + $"hpAfter:{target.CurrentHp}");
    }

    private void QueueOnlineUnitStat(
        CardController target,
        int signedValue,
        EffectStatTarget statTarget,
        EffectDuration duration,
        string statModifierSourceKey = null)
    {
        TryQueueOnlineUnitTargetChange(target, new OnlineBattleUnitEffectChange
        {
            changeKind = OnlineBattleEffectSyncPayload.ChangeKindStat,
            signedStatValue = signedValue,
            statTarget = (int)statTarget,
            duration = (int)duration,
            statModifierSourceKey = statModifierSourceKey ?? string.Empty
        });
    }

    private void QueueOnlineClearStatGrantsFromSource(CardController grantingUnit, PlayerType battleZoneOwnerSide)
    {
        if (!_onlineEffectSyncActive || grantingUnit == null || grantingUnit.BattleInstanceId <= 0)
        {
            return;
        }

        _pendingOnlineEffectChanges.Add(new OnlineBattleUnitEffectChange
        {
            changeKind = OnlineBattleEffectSyncPayload.ChangeKindClearStatGrantsFromSource,
            grantSourceInstanceId = grantingUnit.BattleInstanceId,
            grantSourceZoneOwnerSide = (int)battleZoneOwnerSide
        });
    }

    private void QueueOnlineRefreshOwnerTurnFieldPassives()
    {
        if (!_onlineEffectSyncActive || _pendingOnlineEffectChanges == null)
        {
            Debug.LogWarning(
                $"[OwnerTurnField][OnlineQueueRefreshSkip] active:{_onlineEffectSyncActive} "
                + $"pendingNull:{_pendingOnlineEffectChanges == null}");
            return;
        }

        // 同一バッチ内に既にあれば積まない（ネスト多重呼び出し対策）
        for (int i = 0; i < _pendingOnlineEffectChanges.Count; i++)
        {
            OnlineBattleUnitEffectChange existing = _pendingOnlineEffectChanges[i];
            if (existing != null
                && existing.changeKind == OnlineBattleEffectSyncPayload.ChangeKindRefreshOwnerTurnFieldPassives)
            {
                Debug.Log(
                    $"[OwnerTurnField][OnlineQueueRefreshDup] pending:{_pendingOnlineEffectChanges.Count} "
                    + $"(already queued, skip)");
                return;
            }
        }

        _pendingOnlineEffectChanges.Add(new OnlineBattleUnitEffectChange
        {
            changeKind = OnlineBattleEffectSyncPayload.ChangeKindRefreshOwnerTurnFieldPassives
        });
        Debug.Log(
            $"[OwnerTurnField][OnlineQueueRefresh] pending:{_pendingOnlineEffectChanges.Count} "
            + $"side:{currentPlayerType}");
    }

    private void QueueOnlineUnitRest(CardController target)
    {
        TryQueueOnlineUnitTargetChange(target, new OnlineBattleUnitEffectChange
        {
            changeKind = OnlineBattleEffectSyncPayload.ChangeKindRest
        });
    }

    private void QueueOnlineUnitActivate(CardController target)
    {
        TryQueueOnlineUnitTargetChange(target, new OnlineBattleUnitEffectChange
        {
            changeKind = OnlineBattleEffectSyncPayload.ChangeKindActivate
        });
    }

    /// <summary>Kindhearted 等の破壊防止シールドを相手クライアントへ同期する（付与サイド付き）。</summary>
    private void QueueOnlinePreventAllyDestroyByEnemyEffect(PlayerType grantedSide)
    {
        if (!IsOnlineBattle() || _applyingRemoteBattleAction || !_onlineEffectSyncActive)
        {
            return;
        }

        if (_pendingOnlineEffectChanges == null)
        {
            _pendingOnlineEffectChanges = new List<OnlineBattleUnitEffectChange>();
        }

        for (int i = 0; i < _pendingOnlineEffectChanges.Count; i++)
        {
            OnlineBattleUnitEffectChange existing = _pendingOnlineEffectChanges[i];
            if (existing != null
                && existing.changeKind == OnlineBattleEffectSyncPayload.ChangeKindPreventAllyDestroyByEnemyEffect
                && existing.targetZoneOwnerSide == (int)grantedSide)
            {
                return;
            }
        }

        _pendingOnlineEffectChanges.Add(new OnlineBattleUnitEffectChange
        {
            changeKind = OnlineBattleEffectSyncPayload.ChangeKindPreventAllyDestroyByEnemyEffect,
            // 送信側視点の付与サイド（受信でミラーする）
            targetZoneOwnerSide = (int)grantedSide
        });
    }

    private void QueueOnlineUnitDestroy(
        CardController target,
        CardController destroyer = null,
        bool byCardEffect = true)
    {
        Debug.Log($"[EffectDamage][OnlineQueueDestroy] target={FormatOnlineEffectSyncUnit(target)}");
        if (destroyer != null)
        {
            AssignBattleInstanceIdIfNeeded(destroyer);
        }

        OnlineBattleUnitEffectChange change = new OnlineBattleUnitEffectChange
        {
            changeKind = OnlineBattleEffectSyncPayload.ChangeKindDestroy,
            hpAfter = 0,
            destroyerInstanceId = destroyer != null ? destroyer.BattleInstanceId : 0,
            nonEffectDestroy = byCardEffect ? 0 : 1
        };
        bool queued = TryQueueOnlineUnitTargetChange(target, change);
        if (!queued)
        {
            Debug.LogWarning($"[EffectDamage][OnlineQueueDestroyFail] target={FormatOnlineEffectSyncUnit(target)}");
            return;
        }

        change.onDestroyedRequestId = PrepareOnlineOnDestroyedWait(target);
    }

    private void QueueOnlineUnitBounce(CardController target)
    {
        if (target == null)
        {
            return;
        }

        if (!_onlineEffectSyncActive)
        {
            Debug.LogWarning(
                $"[EffectBounce][OnlineQueueBounceSkip] active:false "
                + $"target={FormatOnlineEffectSyncUnit(target)}");
            return;
        }

        AssignBattleInstanceIdIfNeeded(target);
        if (target.BattleInstanceId <= 0)
        {
            Debug.LogWarning("[OnlineBattle] Bounce sync skipped: unit has no BattleInstanceId.");
            return;
        }

        Debug.Log($"[EffectQueueOnlineUnitBounce] target={FormatOnlineEffectSyncUnit(target)}");
        bool queued = TryQueueOnlineUnitTargetChange(target, new OnlineBattleUnitEffectChange
        {
            changeKind = OnlineBattleEffectSyncPayload.ChangeKindBounce
        });
        if (!queued)
        {
            Debug.LogWarning($"[EffectBounce][OnlineQueueBounceFail] target={FormatOnlineEffectSyncUnit(target)}");
        }
    }

    private void QueueOnlineUnitReturnToDeckBottom(CardController target)
    {
        TryQueueOnlineUnitTargetChange(target, new OnlineBattleUnitEffectChange
        {
            changeKind = OnlineBattleEffectSyncPayload.ChangeKindReturnToDeckBottom
        });
    }

    private void RegisterNetworkBattleHooksIfNeeded()
    {
        if (networkBattleHooksRegistered || !IsOnlineBattle())
        {
            return;
        }

        if (EosP2PTestService.Instance != null)
        {
            EosP2PTestService.Instance.MessageReceived += OnOnlineBattleMessageReceived;
            networkBattleHooksRegistered = true;
        }
    }

    private void UnregisterNetworkBattleHooksIfNeeded()
    {
        if (!networkBattleHooksRegistered)
        {
            return;
        }

        if (EosP2PTestService.Instance != null)
        {
            EosP2PTestService.Instance.MessageReceived -= OnOnlineBattleMessageReceived;
        }

        networkBattleHooksRegistered = false;
    }

    /// <summary>アプリ終了時にバトル側の P2P 購読だけを外す（UI 破棄は行わない）。</summary>
    public void ShutdownOnlineNetworkingForQuit()
    {
        UnregisterNetworkBattleHooksIfNeeded();
        ResetOnlineBattleInstanceIds();
    }

    public void StartEnemyAiTurn()
    {
        StartCoroutine(EnemyActionCoroutine());
    }

    public void EnterRemoteEnemyMainPhase()
    {
        Debug.Log("[OnlineBattle] Waiting for remote input (EndTurn / PlayCard / Attack).");
    }

    private bool TryOverrideTurnOrderFromOnlineMatch(out bool playerGoesFirst)
    {
        if (!IsOnlineBattle())
        {
            playerGoesFirst = false;
            return false;
        }

        playerGoesFirst = EosOnlineMatchState.LocalPlayerGoesFirst;
        return true;
    }

    private void NotifyLocalPlayerEndedTurn()
    {
        if (!IsOnlineBattle() || currentPlayerType != PlayerType.Player)
        {
            return;
        }

        int requestId = ++_onlineEndTurnRequestIdCounter;
        _pendingOnlineEndTurnRequestId = requestId;
        StopPendingOnlineEndTurnRetry();

        bool sent = SendOnlineBattleMessage(EosOnlineBattleMessage.CreateEndTurn(
            OnlineBattleActionPayload.CreateEndTurn(requestId)));
        Debug.Log($"[OnlineBattle] EndTurn sent. requestId:{requestId} sent:{sent}");

        if (sent)
        {
            _pendingOnlineEndTurnRetryCoroutine = StartCoroutine(CoRetryPendingOnlineEndTurn(requestId));
        }
        else
        {
            Debug.LogWarning($"[OnlineBattle] EndTurn initial send failed. requestId:{requestId}");
        }
    }

    private void StopPendingOnlineEndTurnRetry()
    {
        if (_pendingOnlineEndTurnRetryCoroutine == null)
        {
            return;
        }

        StopCoroutine(_pendingOnlineEndTurnRetryCoroutine);
        _pendingOnlineEndTurnRetryCoroutine = null;
    }

    private IEnumerator CoRetryPendingOnlineEndTurn(int requestId)
    {
        const float retryIntervalSeconds = 1.25f;
        const float retryTimeoutSeconds = 12f;
        float deadline = Time.realtimeSinceStartup + retryTimeoutSeconds;

        while (IsOnlineBattle()
            && !isMatchFinished
            && _pendingOnlineEndTurnRequestId == requestId
            && Time.realtimeSinceStartup < deadline)
        {
            yield return new WaitForSecondsRealtime(retryIntervalSeconds);

            if (_pendingOnlineEndTurnRequestId != requestId || !IsOnlineBattle() || isMatchFinished)
            {
                yield break;
            }

            bool resent = SendOnlineBattleMessage(EosOnlineBattleMessage.CreateEndTurn(
                OnlineBattleActionPayload.CreateEndTurn(requestId)));
            Debug.Log($"[OnlineBattle] EndTurn resent. requestId:{requestId} sent:{resent}");
        }

        if (_pendingOnlineEndTurnRequestId == requestId)
        {
            Debug.LogWarning($"[OnlineBattle] EndTurn ack timeout. requestId:{requestId}");
            _pendingOnlineEndTurnRetryCoroutine = null;
        }
    }

    private void NotifyLocalPlayCardDeployed(
        CardController cardController,
        PlayerType deployTargetZoneOwner = PlayerType.Player,
        bool allowOffTurnDeploy = false,
        bool deployAsRested = false)
    {
        if (!IsOnlineBattle() || cardController == null || cardController.Data == null)
        {
            return;
        }

        // 通常配備は自分のターンのみ。バースト配備などは allowOffTurnDeploy で許可。
        if (!allowOffTurnDeploy && currentPlayerType != PlayerType.Player)
        {
            return;
        }

        if (!cardController.Data.IsUnitLike())
        {
            return;
        }

        AssignBattleInstanceIdIfNeeded(cardController);
        if (cardController.BattleInstanceId <= 0)
        {
            Debug.LogWarning("[OnlineBattle] Deploy sync skipped: unit has no BattleInstanceId.");
            return;
        }

        int overrideAp = 0;
        int overrideHp = 0;
        bool forceUnit = false;
        int printedType = -1;
        if (cardController.IsTemporaryBurstBattleUnit)
        {
            forceUnit = true;
            allowOffTurnDeploy = true;
            overrideAp = cardController.Data.power;
            overrideHp = cardController.Data.hp;
            printedType = (int)cardController.TemporaryBurstBattleUnitPrintedType;
        }

        int resourceAfter = 0;
        int exResourceAfter = 0;
        int levelAfter = 0;
        bool includeResource = false;
        if (gundamRule?.Player != null)
        {
            includeResource = true;
            Gundam2024RuleScript.PlayerState payState = gundamRule.Player;
            resourceAfter = payState.resource;
            exResourceAfter = payState.exResource;
            levelAfter = payState.level;
        }

        int requestId = 0;
        if (!_applyingRemoteBattleAction)
        {
            requestId = BeginPendingOpponentCardConfirmRequest();
        }

        bool sent = SendOnlineBattleMessage(EosOnlineBattleMessage.CreatePlayCard(
            OnlineBattleActionPayload.CreateDeployUnit(
                cardController.Data.id,
                cardController.BattleInstanceId,
                (int)deployTargetZoneOwner,
                allowOffTurnDeploy,
                overrideAp,
                overrideHp,
                forceUnit,
                printedType,
                deployAsRested,
                includeResource,
                resourceAfter,
                exResourceAfter,
                levelAfter,
                requestId)));
        Debug.Log(
            $"[OnlineBattle] DeployUnit sync sent id:{cardController.Data.id} inst:{cardController.BattleInstanceId} "
            + $"resource:{resourceAfter} ex:{exResourceAfter} level:{levelAfter} includeRes:{includeResource} "
            + $"requestId:{requestId}");

        if (requestId > 0 && sent)
        {
            StartCoroutine(CoWaitOpponentCardConfirmAfterLocalPlay(requestId));
        }
        else if (requestId > 0)
        {
            ClearPendingOpponentCardConfirmRequest();
        }
    }

    private IEnumerator CoWaitOpponentCardConfirmAfterLocalPlay(int requestId)
    {
        yield return WaitForOpponentCardConfirmCompleteWithOverlayCoroutine(requestId);
        ClearPendingOpponentCardConfirmRequest();
        Debug.Log($"[OpponentCardConfirm][Ack] requestId:{requestId}");
    }

    /// <summary>
    /// 自分側のコスト支払い直後のリソース／EX／レベルを相手へ送る。
    /// 配備・搭乗などで支払いが終わった時点で呼び、ターン終了の OnAction を待たせない。
    /// </summary>
    private void NotifyLocalPlayerResourceSnapshotAfterCost()
    {
        if (_applyingRemoteBattleAction || !IsOnlineBattle() || gundamRule?.Player == null)
        {
            return;
        }

        Gundam2024RuleScript.PlayerState state = gundamRule.Player;
        SendOnlineBattleMessage(EosOnlineBattleMessage.CreateResourceState(
            OnlineBattleActionPayload.CreateResourceState(
                (int)PlayerType.Player,
                state.resource,
                state.exResource,
                state.level)));
        Debug.Log(
            $"[OnlineBattle] ResourceState (after cost) sent "
            + $"resource:{state.resource} ex:{state.exResource} level:{state.level}");
    }

    /// <summary>EXリソース増減を相手へスナップショット同期する（AddExResource 等）。</summary>
    private void NotifyLocalExResourceDeltaIfNeeded(PlayerType target, int amount)
    {
        if (amount == 0
            || !IsOnlineBattle()
            || _applyingRemoteBattleAction
            || gundamRule == null)
        {
            return;
        }

        Gundam2024RuleScript.PlayerSide side = ToRuleSide(target);
        Gundam2024RuleScript.PlayerState state = GetRuleState(side);
        if (state == null)
        {
            return;
        }

        SendOnlineBattleMessage(EosOnlineBattleMessage.CreateResourceState(
            OnlineBattleActionPayload.CreateResourceState(
                (int)target,
                state.resource,
                state.exResource,
                state.level)));
        Debug.Log(
            $"[OnlineBattle] ResourceState sent zone:{target} "
            + $"resource:{state.resource} ex:{state.exResource} level:{state.level} delta:{amount}");
    }

    private void HandleRemoteResourceState(string payload)
    {
        if (!OnlineBattleActionPayload.TryParse(payload, out OnlineBattleActionPayload action)
            || action.action != OnlineBattleActionPayload.ResourceState)
        {
            Debug.LogWarning($"[OnlineBattle] Invalid ResourceState payload: {payload}");
            return;
        }

        PlayerType senderZone = action.actingZoneSide == (int)PlayerType.Enemy
            ? PlayerType.Enemy
            : PlayerType.Player;
        PlayerType localZone = MirrorOnlineZoneOwner(senderZone);
        Gundam2024RuleScript.PlayerSide ruleSide = ToRuleSide(localZone);
        ApplyRemoteOnActionResourceSnapshot(
            ruleSide,
            action.resourceAfter,
            action.exResourceAfter,
            action.levelAfter);
        Debug.Log(
            $"[OnlineBattle] ResourceState applied localZone:{localZone} "
            + $"resource:{action.resourceAfter} ex:{action.exResourceAfter} level:{action.levelAfter}");
    }

    private void NotifyLocalPilotMounted(CardController hostUnit, CardController pilotCard)
    {
        if (_applyingRemoteBattleAction || !IsOnlineBattle() || currentPlayerType != PlayerType.Player
            || hostUnit == null || pilotCard == null || pilotCard.Data == null
            || hostUnit.BattleInstanceId <= 0)
        {
            return;
        }

        int requestId = BeginPendingOpponentCardConfirmRequest();
        bool sent = SendOnlineBattleMessage(EosOnlineBattleMessage.CreateMountPilot(
            OnlineBattleActionPayload.CreateMountPilot(
                hostUnit.BattleInstanceId,
                pilotCard.Data.id,
                includeResourceSnapshot: gundamRule?.Player != null,
                resourceAfter: gundamRule?.Player != null ? gundamRule.Player.resource : 0,
                exResourceAfter: gundamRule?.Player != null ? gundamRule.Player.exResource : 0,
                levelAfter: gundamRule?.Player != null ? gundamRule.Player.level : 0,
                requestId: requestId)));
        Debug.Log(
            $"[OnlineBattle] MountPilot sync sent. host={hostUnit.BattleInstanceId} pilot={pilotCard.Data.id} "
            + $"resource:{(gundamRule?.Player != null ? gundamRule.Player.resource : -1)} requestId:{requestId}");

        if (sent)
        {
            StartCoroutine(CoWaitOpponentCardConfirmAfterLocalPlay(requestId));
        }
        else
        {
            ClearPendingOpponentCardConfirmRequest();
        }
    }

    private bool SendOnlineBattleMessage(string json)
    {
        if (EosP2PTestService.Instance == null)
        {
            Debug.LogWarning("[OnlineBattle] P2P service not found.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(EosOnlineMatchState.RemoteProductUserId))
        {
            Debug.LogWarning("[OnlineBattle] Remote ProductUserId is not set.");
            return false;
        }

        bool sent = EosP2PTestService.Instance.SendText(EosOnlineMatchState.RemoteProductUserId, json);
        if (!sent)
        {
            Debug.LogError(
                $"[OnlineBattle] P2P send failed. utf8Bytes={System.Text.Encoding.UTF8.GetByteCount(json ?? string.Empty)}");
        }

        return sent;
    }

    private void OnOnlineBattleMessageReceived(string peerId, string payload)
    {
        if (!IsOnlineBattle() || battleOpponent == null || !battleOpponent.IsNetwork)
        {
            return;
        }

        if (!string.Equals(peerId, EosOnlineMatchState.RemoteProductUserId, System.StringComparison.Ordinal))
        {
            return;
        }

        if (!EosOnlineBattleMessage.TryParse(payload, out EosOnlineBattleMessage message))
        {
            return;
        }

        try
        {
            DispatchOnlineBattleMessage(message);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[OnlineBattle] Dispatch threw type={message.type}: {ex}");
        }
    }

    private void DispatchOnlineBattleMessage(EosOnlineBattleMessage message)
    {
        if (message == null)
        {
            return;
        }

        switch (message.type)
        {
            case "EndTurn":
                HandleRemoteEndTurn(message.payload);
                break;
            case EosOnlineBattleMessage.EndTurnAck:
                HandleRemoteEndTurnAck(message.payload);
                break;
            case "PlayCard":
                StartCoroutine(HandleRemotePlayCardCoroutine(message.payload));
                break;
            case "Attack":
                HandleRemoteAttack(message.payload);
                break;
            case "AttackDeclare":
                HandleRemoteAttackDeclare(message.payload);
                break;
            case "BlockResponse":
                HandleRemoteBlockResponse(message.payload);
                break;
            case "EffectSync":
                HandleRemoteEffectSync(message.payload);
                break;
            case "MountPilot":
                StartCoroutine(HandleRemoteMountPilotCoroutine(message.payload));
                break;
            case "OnActionBegin":
                HandleRemoteOnActionBegin(message.payload);
                break;
            case "OnActionEnd":
                HandleRemoteOnActionEnd(message.payload);
                break;
            case "OnActionCommandUsed":
                HandleRemoteOnActionCommandUsed(message.payload);
                break;
            case EosOnlineBattleMessage.CommandPlayRevealComplete:
                HandleRemoteCommandPlayRevealComplete(message.payload);
                break;
            case "MulliganSync":
                HandleRemoteMulliganSync(message.payload);
                break;
            case "ShieldBreakComplete":
                HandleRemoteShieldBreakComplete(message.payload);
                break;
            case "ZoneSync":
                HandleRemoteZoneSync(message.payload);
                break;
            case "HandDiscardReveal":
                HandleRemoteHandDiscardReveal(message.payload);
                break;
            case "HandDiscardRevealComplete":
                HandleRemoteHandDiscardRevealComplete(message.payload);
                break;
            case EosOnlineBattleMessage.DiscardThinkWait:
                HandleRemoteDiscardThinkWait(message.payload);
                break;
            case EosOnlineBattleMessage.DiscardThinkComplete:
                HandleRemoteDiscardThinkComplete(message.payload);
                break;
            case EosOnlineBattleMessage.ResourceState:
                HandleRemoteResourceState(message.payload);
                break;
            case EosOnlineBattleMessage.HandDeckState:
                HandleRemoteHandDeckState(message.payload);
                break;
            case EosOnlineBattleMessage.OnDestroyedComplete:
                HandleRemoteOnDestroyedComplete(message.payload);
                break;
            case EosOnlineBattleMessage.EffectThinkWait:
                HandleRemoteEffectThinkWait(message.payload);
                break;
            case EosOnlineBattleMessage.OpponentUnitPickRequest:
                HandleRemoteOpponentUnitPickRequest(message.payload);
                break;
            case EosOnlineBattleMessage.OpponentUnitPickResponse:
                HandleRemoteOpponentUnitPickResponse(message.payload);
                break;
        }
    }

    private bool TryBeginOnlineBlockWait(
        CardController attacker,
        bool isShieldAttack,
        CardController originalDefender,
        System.Action<int> onBlockerInstanceIdResolved)
    {
        if (!ShouldUseOnlineBlockPhase(PlayerType.Player) || attacker == null || onBlockerInstanceIdResolved == null)
        {
            return false;
        }

        if (AttackerIgnoresBlockRedirect(attacker))
        {
            return false;
        }

        if (CollectSelectableBlockRedirectUnits(PlayerType.Player).Count <= 0)
        {
            return false;
        }

        int requestId = ++_onlineAttackRequestIdCounter;
        _pendingOnlineBlockRequestId = requestId;
        _pendingOnlineBlockCallback = onBlockerInstanceIdResolved;

        string attackKind = isShieldAttack
            ? OnlineBattleActionPayload.AttackKindShield
            : OnlineBattleActionPayload.AttackKindUnitVsUnit;
        int defenderInstanceId = originalDefender != null ? originalDefender.BattleInstanceId : 0;

        SendOnlineBattleMessage(EosOnlineBattleMessage.CreateAttackDeclare(
            OnlineBattleActionPayload.CreateAttackDeclare(
                requestId,
                attackKind,
                attacker.BattleInstanceId,
                defenderInstanceId)));

        attackFlowPipelinePhase = AttackFlowPipelinePhase.AwaitingBlockUi;
        Debug.Log($"[OnlineBattle] Waiting for block response. requestId={requestId}");
        return true;
    }

    private void HandleRemoteAttackDeclare(string payload)
    {
        if (!OnlineBattleActionPayload.TryParse(payload, out OnlineBattleActionPayload action))
        {
            Debug.LogWarning($"[OnlineBattle] Invalid AttackDeclare payload: {payload}");
            return;
        }

        if (currentPlayerType != PlayerType.Enemy)
        {
            Debug.Log("[OnlineBattle] Ignored AttackDeclare because it is not opponent turn locally.");
            return;
        }

        CardController attacker = FindBattleZoneUnitByInstanceId(action.attackerInstanceId, PlayerType.Enemy);
        if (attacker == null)
        {
            Debug.LogWarning($"[OnlineBattle] AttackDeclare attacker not found: {action.attackerInstanceId}");
            SendOnlineBlockResponse(action.requestId, 0);
            return;
        }

        bool isShieldAttack = string.Equals(
            action.attackKind,
            OnlineBattleActionPayload.AttackKindShield,
            System.StringComparison.Ordinal);
        CardController declaredTarget = null;
        if (!isShieldAttack && action.defenderInstanceId > 0)
        {
            declaredTarget = FindBattleZoneUnitByInstanceId(action.defenderInstanceId, PlayerType.Player);
        }

        RegisterAttackFlowContextForOnAction(
            attacker,
            PlayerType.Enemy,
            isShieldAttack ? AttackFlowStrikeKind.Shield : AttackFlowStrikeKind.UnitVsUnit,
            declaredTarget,
            null);

        // オフライン AI 攻撃時のブロック UI（attackerOwner==Enemy）と同じ Close / Cancel 構造。
        CardController selectedBlocker = null;
        int requestId = action.requestId;
        System.Action passBlockAndSendResponse = () =>
        {
            attackFlowPipelinePhase = AttackFlowPipelinePhase.None;
            ClearPendingBlockRedirectSelection();
            SendOnlineBlockResponse(requestId, 0);
        };

        attackFlowPipelinePhase = AttackFlowPipelinePhase.AwaitingBlockUi;
        bool opened = TryOpenAttackedSideUnitsPanel(
            PlayerType.Enemy,
            attacker,
            selected =>
            {
                if (selected == null)
                {
                    ClearPendingBlockRedirectSelection();
                    return;
                }

                selectedBlocker = selected;
            },
            () =>
            {
                attackFlowPipelinePhase = AttackFlowPipelinePhase.None;
                int blockerInstanceId = 0;
                if (selectedBlocker != null
                    && IsBlockRedirectReactionReady(selectedBlocker, PlayerType.Player))
                {
                    blockerInstanceId = selectedBlocker.BattleInstanceId;
                }

                SendOnlineBlockResponse(requestId, blockerInstanceId);
            },
            passBlockAndSendResponse,
            declaredAttackTargetForDisplay: declaredTarget);

        if (!opened)
        {
            attackFlowPipelinePhase = AttackFlowPipelinePhase.None;
            Debug.Log("[OnlineBattle] No block UI opened — sending pass.");
            SendOnlineBlockResponse(requestId, 0);
        }
    }

    private void SendOnlineBlockResponse(int requestId, int blockerInstanceId)
    {
        SendOnlineBattleMessage(EosOnlineBattleMessage.CreateBlockResponse(
            OnlineBattleActionPayload.CreateBlockResponse(requestId, blockerInstanceId)));
        Debug.Log($"[OnlineBattle] Block response sent. requestId={requestId} blocker={blockerInstanceId}");
    }

    private void HandleRemoteBlockResponse(string payload)
    {
        if (!OnlineBattleActionPayload.TryParse(payload, out OnlineBattleActionPayload action))
        {
            Debug.LogWarning($"[OnlineBattle] Invalid BlockResponse payload: {payload}");
            return;
        }

        if (action.requestId != _pendingOnlineBlockRequestId || _pendingOnlineBlockCallback == null)
        {
            Debug.Log($"[OnlineBattle] Ignored BlockResponse requestId={action.requestId}");
            return;
        }

        System.Action<int> callback = _pendingOnlineBlockCallback;
        _pendingOnlineBlockCallback = null;
        _pendingOnlineBlockRequestId = 0;

        if (attackFlowPipelinePhase == AttackFlowPipelinePhase.AwaitingBlockUi)
        {
            attackFlowPipelinePhase = AttackFlowPipelinePhase.None;
        }

        callback.Invoke(Mathf.Max(0, action.blockerInstanceId));
    }

    /// <summary>オンライン攻撃側: 相手の BlockResponse 後にブロック確定または OnAction へ進める（再入ループを避ける）。</summary>
    private void ResumeOnlineUnitAttackAfterBlockResponse(
        CardController attacker,
        CardController defender,
        PlayerType attackerOwner,
        PlayerType defenderOwner,
        int blockerInstanceId)
    {
        if (blockerInstanceId > 0)
        {
            CardController onlineBlocker = FindBlockerUnitFromRemoteResponse(blockerInstanceId);
            if (onlineBlocker != null)
            {
                CommitBlockRedirectSelection(attacker, onlineBlocker, ref defender, ref defenderOwner);
                TryResolveBlockRedirectUnitCombatWithOnActionSteps(
                    attacker,
                    onlineBlocker,
                    attackerOwner,
                    defenderOwner);
                return;
            }
        }

        RegisterAttackFlowContextForOnAction(
            attacker,
            attackerOwner,
            AttackFlowStrikeKind.UnitVsUnit,
            defender,
            null);
        pendingOnAttackEffectResolvedAttacker = attacker;
        RunOnActionStepsImmediatelyAfterBlockPass(
            attacker,
            defender,
            attackerOwner,
            defenderOwner,
            AttackFlowStrikeKind.UnitVsUnit);
    }

    /// <summary>オンライン攻撃側: シールド攻撃の BlockResponse 後にブロック確定または OnAction へ進める。</summary>
    private void ResumeOnlineShieldAttackAfterBlockResponse(
        CardController attacker,
        PlayerType attackerOwner,
        int blockerInstanceId)
    {
        if (blockerInstanceId > 0)
        {
            CardController onlineBlocker = FindBlockerUnitFromRemoteResponse(blockerInstanceId);
            if (onlineBlocker != null)
            {
                PlayerType blockerOwner = ResolveCardOwner(onlineBlocker.transform);
                if (IsBlockRedirectReactionReady(onlineBlocker, blockerOwner))
                {
                    ApplyDefenderOnAttackReactionEffects(onlineBlocker, attacker, blockerOwner);
                    BeginShieldAttackBlockRedirectFlow(onlineBlocker);
                    TryResolveBlockRedirectUnitCombatWithOnActionSteps(
                        attacker,
                        onlineBlocker,
                        attackerOwner,
                        blockerOwner);
                    return;
                }
            }
        }

        PlayerType defenderSide = attackerOwner == PlayerType.Player ? PlayerType.Enemy : PlayerType.Player;
        RegisterAttackFlowContextForOnAction(
            attacker,
            attackerOwner,
            AttackFlowStrikeKind.Shield,
            null,
            null);
        pendingOnAttackEffectResolvedAttacker = attacker;
        RunOnActionStepsImmediatelyAfterBlockPass(
            attacker,
            null,
            attackerOwner,
            defenderSide,
            AttackFlowStrikeKind.Shield);
    }

    private void HandleRemoteEndTurn(string payload)
    {
        if (!OnlineBattleActionPayload.TryParse(payload, out OnlineBattleActionPayload action)
            || action.action != OnlineBattleActionPayload.EndTurn
            || action.requestId <= 0)
        {
            Debug.LogWarning($"[OnlineBattle] Invalid EndTurn payload: {payload}");
            return;
        }

        // ACK はターン切替より先に返す。遅延端末の再送を止め、相手待ちを解除する。
        SendOnlineBattleMessage(EosOnlineBattleMessage.CreateEndTurnAck(
            OnlineBattleActionPayload.CreateEndTurnAck(action.requestId)));
        CloseAllOnlineActionStepUi();

        if (_appliedRemoteEndTurnRequestId == action.requestId)
        {
            Debug.Log($"[OnlineBattle] Duplicate EndTurn ignored. requestId:{action.requestId}");
            return;
        }

        // 再送 EndTurn が、既に始まった自分ターンを再度 StartTurn すると
        // isEndTurnFlowRunning / 待ち UI で End ボタンが押せなくなる。
        if (isEndTurnFlowRunning || IsLocalTurnAlreadyPlayable())
        {
            _appliedRemoteEndTurnRequestId = action.requestId;
            Debug.Log(
                $"[OnlineBattle] EndTurn ignored, local turn already started or applying. "
                + $"requestId:{action.requestId} phase:{currentPhase} endTurnRunning:{isEndTurnFlowRunning} "
                + $"seqStarted:{_onlineLocalTurnSequenceStarted}");
            return;
        }

        if (currentPlayerType == PlayerType.Player)
        {
            Debug.LogWarning(
                $"[OnlineBattle] EndTurn arrived while local side is Player. "
                + $"requestId:{action.requestId} phase:{currentPhase} — apply opponent-ended-turn.");
        }

        _appliedRemoteEndTurnRequestId = action.requestId;
        Debug.Log(
            $"[OnlineBattle] Remote EndTurn received. requestId:{action.requestId} "
            + $"localCurrent:{currentPlayerType} Applying turn switch.");
        StartCoroutine(ApplyRemoteOpponentEndedTurnCoroutine());
    }

    /// <summary>自分ターンのメイン進行が既に始まっていれば、遅延 EndTurn で再開始しない。</summary>
    private bool IsLocalTurnAlreadyPlayable()
    {
        if (currentPlayerType != PlayerType.Player)
        {
            return false;
        }

        if (isTurnPhaseSequenceRunning || _onlineLocalTurnSequenceStarted)
        {
            return true;
        }

        return currentPhase == BattlePhase.DrawPhase
            || currentPhase == BattlePhase.ResourcePhase
            || currentPhase == BattlePhase.ActivePhase;
    }

    private void HandleRemoteEndTurnAck(string payload)
    {
        if (!OnlineBattleActionPayload.TryParse(payload, out OnlineBattleActionPayload action)
            || action.action != OnlineBattleActionPayload.EndTurnAck
            || action.requestId <= 0)
        {
            Debug.LogWarning($"[OnlineBattle] Invalid EndTurnAck payload: {payload}");
            return;
        }

        if (action.requestId != _pendingOnlineEndTurnRequestId)
        {
            Debug.Log($"[OnlineBattle] Ignored EndTurnAck requestId:{action.requestId} pending:{_pendingOnlineEndTurnRequestId}");
            return;
        }

        StopPendingOnlineEndTurnRetry();
        _pendingOnlineEndTurnRequestId = 0;
        Debug.Log($"[OnlineBattle] EndTurn ack received. requestId:{action.requestId}");
    }

    /// <summary>
    /// 相手がターン終了したとき、ローカルで EndTurn コルーチン（OnAction 含む）を再実行せずターンだけ進める。
    /// 非アクティブ側の OnAction は相手の <see cref="ExecuteEndTurnCoroutine"/> からの OnActionBegin で既に処理済み。
    /// </summary>
    private IEnumerator ApplyRemoteOpponentEndedTurnCoroutine()
    {
        isEndTurnFlowRunning = true;
        try
        {
            CloseAllOnlineActionStepUi();
            yield return WaitForShieldBreakFlowCompleteCoroutine(45f);
            yield return WaitForBattleFlowIdleBeforeEndTurnCoroutine();
            ClearStaleAttackAndShieldFlagsForEndTurn();
            if (ShouldBlockOnlineLocalPlayDueToOnAction())
            {
                CloseAllOnlineActionStepUi();
            }

            pendingUnitAttackAttacker = null;
            pendingOnAttackEffectResolvedAttacker = null;
            PlayerType endingTurnSide = PlayerType.Enemy;
            TriggerAllTimedEffectsForSide(endingTurnSide, EffectTiming.OnTurnEnd);
            ClearTimedStatModifiersForAllInPlayCards(EffectDuration.UntilEndOfTurn);
            ClearAttackActiveEnemyGrants(EffectDuration.UntilEndOfTurn);
            ClearNotDirectAttackGrants(EffectDuration.UntilEndOfTurn);
            ClearFirstStrikeGrants(EffectDuration.UntilEndOfTurn);
            ClearHighMobilityUntilEndOfTurnGrantsForAllInPlayUnits();
            ClearBreachUntilEndOfTurnGrantsForAllInPlayUnits();
            ClearSuppressUntilEndOfTurnGrantsForAllInPlayUnits();
            ClearCopiedKeywordsUntilEndOfTurnForAllInPlayUnits();
            ClearPreventAllyDestroyByEnemyEffectUntilEot();
            DumpTurnResourceUsageLogs(endingTurnSide, "end turn (remote)");

            currentPlayerType = PlayerType.Player;
            RefreshAllFieldOwnerTurnPassives();
            AdvanceRuleToNextTurnStart();
            UpdateEndTurnButtonVisibility();

            Debug.Log("[OnlineBattle] Remote opponent turn ended. Starting local turn.");
            float seqDeadline = Time.realtimeSinceStartup + 8f;
            while (isTurnPhaseSequenceRunning && Time.realtimeSinceStartup < seqDeadline)
            {
                yield return null;
            }

            if (isTurnPhaseSequenceRunning)
            {
                Debug.LogWarning("[OnlineBattle] Aborting leftover turn sequence so local turn can start.");
                AbortTurnPhaseSequenceForRemoteTurnSwitch();
            }

            if (!isEnemyMainPhaseCoroutineRunning)
            {
                StartCoroutine(ExecuteTurnPhaseSequenceCoroutine());
            }
            else
            {
                Debug.LogWarning(
                    $"[OnlineBattle] Could not start local turn sequence. enemyMain:{isEnemyMainPhaseCoroutineRunning}");
            }
        }
        finally
        {
            isEndTurnFlowRunning = false;
        }
    }

    private IEnumerator HandleRemotePlayCardCoroutine(string payload)
    {
        if (!OnlineBattleActionPayload.TryParse(payload, out OnlineBattleActionPayload action))
        {
            Debug.LogWarning($"[OnlineBattle] Invalid PlayCard payload: {payload}");
            yield break;
        }

        // DeployUnit の off-turn 配備、および出資者 DeployExBase（cardId=0）の
        // 【アクション】発動は、相手ターン中でも受信する。
        bool allowOffTurnPlayCard =
            (action.action == OnlineBattleActionPayload.DeployUnit
             && action.deployUnitExtras != null
             && action.deployUnitExtras.allowOffTurnDeploy)
            || (action.action == OnlineBattleActionPayload.DeployBase && action.cardId <= 0);

        if (currentPlayerType != PlayerType.Enemy && !allowOffTurnPlayCard)
        {
            Debug.Log("[OnlineBattle] Ignored remote PlayCard because it is not opponent turn locally.");
            SendOpponentCardConfirmComplete(action.requestId);
            yield break;
        }

        if (action.action == OnlineBattleActionPayload.DeployUnit)
        {
            CardController deployed = ApplyRemoteDeployUnit(action);
            // 攻撃中の効果配備などで、相手の OK 待ちに攻撃フロー／アクションステップが止まらないよう先に Ack
            SendOpponentCardConfirmComplete(action.requestId);
            if (deployed != null)
            {
                yield return ShowCommandUseAcknowledgementCoroutine(
                    deployed,
                    null,
                    null,
                    GameLocale.T("相手 — ユニット配備", "Opponent — Unit Deploy"));
            }

            yield break;
        }

        if (action.action == OnlineBattleActionPayload.DeployBase)
        {
            ApplyRemoteDeployBase(action);
            yield break;
        }

        if (action.action == OnlineBattleActionPayload.DeployShield)
        {
            ApplyRemoteDeployShield(action);
        }
    }

//    　ApplyRemoteDeployUnit … 相手のカードデプロイを適用する
    private CardController ApplyRemoteDeployUnit(OnlineBattleActionPayload action)
    {
        if (action == null || DeckSettinObject.Instance == null)
        {
            return null;
        }

        int cardId = action.cardId;
        int instanceId = action.instanceId;
        PlayerType senderZoneOwner = action.deployTargetZoneOwnerSide == (int)PlayerType.Enemy
            ? PlayerType.Enemy
            : PlayerType.Player;
        PlayerType localZoneOwner = MirrorOnlineZoneOwner(senderZoneOwner);
        CardGameRule rule = localZoneOwner == PlayerType.Player ? cardGameRule : enemyCardGameRule;
        List<CardController> zone = localZoneOwner == PlayerType.Player ? playerBattleZoneCards : enemyBattleZoneCards;

        if (instanceId > 0 && FindUnitByInstanceIdEitherZone(instanceId) != null)
        {
            Debug.Log($"[OnlineBattle] Remote deploy skipped (duplicate inst:{instanceId})");
            return null;
        }

        CardData printed = DeckSettinObject.Instance.GetCardDataById(cardId);
        if (printed == null || rule?.PlayerDeployPanel == null)
        {
            Debug.LogWarning($"[OnlineBattle] Unknown card id for remote deploy: {cardId}");
            return null;
        }

        OnlineDeployUnitExtras extras = action.deployUnitExtras;
        CardData cardData = printed;
        bool temporaryBurstUnit = extras != null && extras.deployForceUnitType;
        int overrideAp = extras != null ? extras.deployOverrideAp : 0;
        int overrideHp = extras != null ? extras.deployOverrideHp : 0;
        if (temporaryBurstUnit || overrideAp > 0 || overrideHp > 0)
        {
            cardData = Instantiate(printed);
            cardData.name = printed.cardName + (temporaryBurstUnit ? " (BattleUnit)" : " (Runtime)");
            if (temporaryBurstUnit)
            {
                cardData.type = Type.Unit;
                cardData.link = new List<UnitLinkPilotSlot>();
            }

            if (overrideAp > 0)
            {
                cardData.power = overrideAp;
            }

            if (overrideHp > 0)
            {
                cardData.hp = overrideHp;
            }
        }

        GameObject cardObject = Instantiate(CardImagePrefab, rule.PlayerDeployPanel);
        CardController controller = cardObject.GetComponent<CardController>();
        controller.SetUp(cardData, OnCardClicked);
        if (temporaryBurstUnit)
        {
            Type printedType = extras != null && extras.deployPrintedType >= 0
                ? (Type)extras.deployPrintedType
                : Type.Pilot;
            controller.MarkTemporaryBurstBattleUnit(printedType, printed.power, printed.hp);
        }

        rule.TryPlaceUnitInBattleZone(controller);

        if (zone != null && !zone.Contains(controller))
        {
            zone.Add(controller);
        }

        if (cardData.IsUnitLike())
        {
            controller.ResetRuntimeStatsFromData();
            ApplyUnitDeployFieldAttackState(controller);
            if (instanceId > 0)
            {
                AssignBattleInstanceIdFromNetwork(controller, instanceId);
            }
            else
            {
                AssignBattleInstanceIdIfNeeded(controller);
            }

            if (extras != null && extras.deployAsRested)
            {
                ApplyDeployedUnitRestedState(controller);
            }
        }

        RefreshAllFieldOwnerTurnPassives();
        ApplyRemoteDeployCostResourceSnapshotIfPresent(action);
        Debug.Log(
            $"[OnlineBattle] Remote unit deployed zone:{localZoneOwner} senderZone:{senderZoneOwner} "
            + $"{cardData.cardName} ({cardId}) inst:{controller.BattleInstanceId} "
            + $"forceUnit:{temporaryBurstUnit} AP:{cardData.power} HP:{cardData.hp} "
            + $"rested:{(extras != null && extras.deployAsRested)} "
            + $"includeRes:{action.includeResourceSnapshot} resource:{action.resourceAfter}");
        return controller;
    }

    /// <summary>
    /// 相手の配備／搭乗ペイロードに含まれる支払後リソースを、ローカルの Enemy 側へ即反映する。
    /// </summary>
    private void ApplyRemoteDeployCostResourceSnapshotIfPresent(OnlineBattleActionPayload action)
    {
        if (action == null || !action.includeResourceSnapshot)
        {
            return;
        }

        // 送信側の Player 支払い → 受信側では Enemy ゾーン
        ApplyRemoteOnActionResourceSnapshot(
            Gundam2024RuleScript.PlayerSide.Enemy,
            action.resourceAfter,
            action.exResourceAfter,
            action.levelAfter);
        Debug.Log(
            $"[OnlineBattle] Deploy cost resource applied to Enemy "
            + $"resource:{action.resourceAfter} ex:{action.exResourceAfter} level:{action.levelAfter}");
    }

    private void NotifyLocalShieldAttackResolved(
        CardController attacker,
        int defenderShieldAfter,
        int defenderExBaseAfter,
        bool directAttackWin)
    {
        if (_applyingRemoteBattleAction || !IsOnlineBattle() || currentPlayerType != PlayerType.Player
            || attacker == null || attacker.BattleInstanceId <= 0)
        {
            return;
        }

        int defenderDeployedBaseHpAfter = ConsumePendingDefenderDeployedBaseHpForOnlineSync();
        if (defenderDeployedBaseHpAfter < 0)
        {
            Gundam2024RuleScript.PlayerSide defenderSide = Gundam2024RuleScript.PlayerSide.Enemy;
            defenderDeployedBaseHpAfter = ResolveOnlineSyncDeployedBaseHp(defenderSide);
        }

        SendOnlineBattleMessage(EosOnlineBattleMessage.CreateAttack(
            OnlineBattleActionPayload.CreateShieldAttack(
                attacker.BattleInstanceId,
                defenderShieldAfter,
                defenderExBaseAfter,
                directAttackWin,
                ConsumeOnlineBrokenShieldCardIdsForAttackNotify(),
                defenderDeployedBaseHpAfter: defenderDeployedBaseHpAfter)));
    }

    private void NotifyLocalUnitAttackResolved(
        CardController attacker,
        CardController defender,
        int attackerHpAfter,
        int defenderHpAfter,
        bool blockCombat = false,
        bool skipAttackDeclarationRest = false,
        bool includeDefenderAreaSnapshot = false,
        int defenderShieldAfter = -1,
        int defenderExBaseAfter = -1,
        int defenderDeployedBaseHpAfter = -1)
    {
        if (_applyingRemoteBattleAction || !IsOnlineBattle() || currentPlayerType != PlayerType.Player
            || attacker == null || defender == null
            || attacker.BattleInstanceId <= 0 || defender.BattleInstanceId <= 0)
        {
            return;
        }

        // 防御側撃破時の OnDestroyed は所有者クライアントで解決し、攻撃側は effectthink で待つ
        int defenderOnDestroyedRequestId = 0;
        if (defenderHpAfter <= 0)
        {
            defenderOnDestroyedRequestId = PrepareOnlineOnDestroyedWait(defender);
        }

        SendOnlineBattleMessage(EosOnlineBattleMessage.CreateAttack(
            OnlineBattleActionPayload.CreateUnitAttack(
                attacker.BattleInstanceId,
                defender.BattleInstanceId,
                attackerHpAfter,
                defenderHpAfter,
                blockCombat,
                skipAttackDeclarationRest,
                includeDefenderAreaSnapshot,
                defenderShieldAfter,
                defenderExBaseAfter,
                defenderDeployedBaseHpAfter,
                defenderOnDestroyedRequestId)));
    }

    private void HandleRemoteAttack(string payload)
    {
        if (!OnlineBattleActionPayload.TryParse(payload, out OnlineBattleActionPayload action))
        {
            Debug.LogWarning($"[OnlineBattle] Invalid Attack payload: {payload}");
            return;
        }

        // シールド破壊ハンドシェイク（requestId>0）は防御側が必ず解決する必要がある。
        // ターン同期のズレで無視すると攻撃側が待ち続ける。
        bool isShieldBreakHandshake = action.action == OnlineBattleActionPayload.ShieldAttack
            && action.requestId > 0;
        if (currentPlayerType != PlayerType.Enemy && !isShieldBreakHandshake)
        {
            Debug.Log("[OnlineBattle] Ignored remote Attack because it is not opponent turn locally.");
            return;
        }

        if (currentPlayerType != PlayerType.Enemy && isShieldBreakHandshake)
        {
            Debug.LogWarning(
                $"[OnlineBattle] Accepting ShieldAttack handshake requestId={action.requestId} "
                + $"despite currentPlayerType={currentPlayerType}");
        }

        _applyingRemoteBattleAction = true;
        try
        {
            if (action.action == OnlineBattleActionPayload.ShieldAttack)
            {
                ApplyRemoteShieldAttack(action);
            }
            else if (action.action == OnlineBattleActionPayload.UnitAttack)
            {
                ApplyRemoteUnitAttack(action);
            }
        }
        finally
        {
            _applyingRemoteBattleAction = false;
            ResumeDeferredRemoteDestroyedResolutionsIfNeeded();
        }
    }

    private void ApplyRemoteShieldAttack(OnlineBattleActionPayload action)
    {
        CardController attacker = FindBattleZoneUnitByInstanceId(action.attackerInstanceId, PlayerType.Enemy);
        if (attacker != null)
        {
            CommitUnitAttackDeclaration(attacker, PlayerType.Enemy);
        }

        if (action.directAttackWin)
        {
            Debug.Log("[OnlineBattle] Remote direct attack win received.");
            HandleDirectAttackWinLose(PlayerType.Enemy);
            return;
        }

        ApplyRemoteDeployedBaseHpUpdate(
            Gundam2024RuleScript.PlayerSide.Player,
            action.defenderDeployedBaseHpAfter);

        Gundam2024RuleScript.PlayerState defender = gundamRule.Player;
        int oldShield = defender.shield;
        int oldExBase = defender.exBase;
        defender.shield = Mathf.Max(0, action.defenderShieldAfter);
        defender.exBase = Mathf.Max(0, action.defenderExBaseAfter);

        int brokenCount = Mathf.Max(0, oldShield - defender.shield);
        if (brokenCount > 0)
        {
            StartCoroutine(ApplyRemoteDefenderShieldBreakCoroutine(
                Gundam2024RuleScript.PlayerSide.Player,
                brokenCount,
                action.shieldBreakSimultaneousReveal,
                action.brokenShieldCardIds,
                action.requestId));
        }
        else if (action.requestId > 0)
        {
            // 破壊枚数0でも handshake requestId があるなら完了を返さないと攻撃側が UI 待ちで固まる
            Debug.LogWarning(
                $"[OnlineBattle] ShieldAttack requestId={action.requestId} but brokenCount=0 — send Complete immediately.");
            SendOnlineShieldBreakComplete(action.requestId, Gundam2024RuleScript.PlayerSide.Player);
        }

        SyncResourceViewsFromRule(Gundam2024RuleScript.PlayerSide.Player);
        SyncBaseZoneHeaderDisplay(Gundam2024RuleScript.PlayerSide.Player);
        ReconcileShieldStateWithZone(Gundam2024RuleScript.PlayerSide.Player, force: true);
        ClearEndOfBattleCombatModifiers("remote shield attack");
        Debug.Log(
            $"[OnlineBattle] Remote shield attack applied. shield={oldShield}->{defender.shield} "
            + $"exBase={oldExBase}->{defender.exBase} baseHp:{action.defenderDeployedBaseHpAfter}");
    }

    private IEnumerator ApplyRemoteShieldBreakByCardIdsCoroutine(
        Gundam2024RuleScript.PlayerSide side,
        int[] cardIds)
    {
        if (cardIds == null || cardIds.Length == 0 || isMatchFinished)
        {
            yield break;
        }

        CardGameRule rule = side == Gundam2024RuleScript.PlayerSide.Player ? cardGameRule : enemyCardGameRule;
        PlayerType shieldOwner = side == Gundam2024RuleScript.PlayerSide.Player ? PlayerType.Player : PlayerType.Enemy;
        if (rule == null)
        {
            yield break;
        }

        List<ShieldBreakTaken> takenCards = new List<ShieldBreakTaken>(cardIds.Length);
        for (int i = 0; i < cardIds.Length; i++)
        {
            if (rule.TryDetachShieldCardById(cardIds[i], out ShieldBreakTaken taken, revealFace: true))
            {
                takenCards.Add(taken);
            }
        }

        if (takenCards.Count == 0)
        {
            yield break;
        }

        isShieldBreakFlowOpen = true;
        try
        {
            yield return ShowShieldBreakRevealCoroutine(takenCards, shieldOwner, simultaneousReveal: false);
            yield return ResolveBurstEffectsForTakenCardsCoroutine(takenCards, shieldOwner);
            for (int i = 0; i < takenCards.Count; i++)
            {
                CommitShieldBreakTakenAfterBurst(takenCards[i], rule, shieldOwner);
            }
        }
        finally
        {
            CardGameRule cleanupRule = side == Gundam2024RuleScript.PlayerSide.Player ? cardGameRule : enemyCardGameRule;
            cleanupRule?.DestroyUnregisteredShieldZoneVisuals();
            ReconcileShieldStateWithZone(side, force: true);
            SyncAllResourceViewsFromRule();
            if (!shieldBreakQueueRunning && pendingShieldBreakBatches.Count == 0)
            {
                isShieldBreakFlowOpen = false;
            }
        }
    }

    private void ApplyRemoteUnitAttack(OnlineBattleActionPayload action)
    {
        CardController attacker = FindBattleZoneUnitByInstanceId(action.attackerInstanceId, PlayerType.Enemy);
        CardController defender = FindBattleZoneUnitByInstanceId(action.defenderInstanceId, PlayerType.Player);
        if (attacker == null || defender == null)
        {
            Debug.LogWarning(
                $"[OnlineBattle] Remote unit attack target not found. attacker={action.attackerInstanceId} defender={action.defenderInstanceId}");
            return;
        }

        if (!action.skipAttackDeclarationRest)
        {
            CommitUnitAttackDeclaration(attacker, PlayerType.Enemy);
        }

        defender.SetCurrentHpForSync(action.defenderHp);
        attacker.SetCurrentHpForSync(action.attackerHp);

        if (action.blockCombat && defender.CurrentHp > 0)
        {
            SetUnitRestAndTriggerEffects(defender, PlayerType.Player);
        }

        if (defender.CurrentHp <= 0)
        {
            // オンライン戦闘撃破: 所有者側で OnDestroyed を解決（ヘッドトークン配備など）
            // UnitAttack.requestId = 防御側 OnDestroyed 待機 ID（専用フィールドはパケット肥大化のため使わない）
            int defenderOnDestroyedRequestId = action.requestId;
            if (defenderOnDestroyedRequestId > 0
                || HasOnDestroyedResolution(defender)
                || (defender.MountedPilot != null && HasOnDestroyedResolution(defender.MountedPilot)))
            {
                ApplyRemoteDestroyedUnitWithOnDestroyedEffects(
                    defender,
                    defenderOnDestroyedRequestId,
                    attacker);
            }
            else
            {
                ApplyRemoteUnitRemovedFromField(defender);
            }
        }

        if (attacker.CurrentHp <= 0)
        {
            // 攻撃側ユニットの OnDestroyed は攻撃側クライアントで解決済み／解決中
            ApplyRemoteUnitRemovedFromField(attacker);
        }

        // エフェクトバトル撃破時の突破など、同メッセージに同梱された防御領域スナップショットを適用
        if (action.includeDefenderAreaSnapshot)
        {
            ApplyRemoteDefenderAreaSnapshotFromAttackPayload(action);
        }

        SyncAllResourceViewsFromRule();
        // リモート側でも「このバトル中」修飾を落とす（EffectSync で付いたキラデバフ／ザクAP+2 など）
        ClearEndOfBattleCombatModifiers("remote unit attack");
        Debug.Log(
            $"[OnlineBattle] Remote unit attack applied. attackerHp={action.attackerHp} defenderHp={action.defenderHp} "
            + $"areaSnap:{action.includeDefenderAreaSnapshot} baseHp:{action.defenderDeployedBaseHpAfter}");
    }

    /// <summary>UnitAttack / ShieldAttack に同梱された防御側 shield / exBase / 配備ベース HP をローカル Player 側へ反映。</summary>
    private void ApplyRemoteDefenderAreaSnapshotFromAttackPayload(OnlineBattleActionPayload action)
    {
        if (action == null)
        {
            return;
        }

        ApplyRemoteDeployedBaseHpUpdate(
            Gundam2024RuleScript.PlayerSide.Player,
            action.defenderDeployedBaseHpAfter);

        Gundam2024RuleScript.PlayerState defender = gundamRule != null ? gundamRule.Player : null;
        if (defender != null)
        {
            if (action.defenderShieldAfter >= 0)
            {
                defender.shield = Mathf.Max(0, action.defenderShieldAfter);
            }

            if (action.defenderExBaseAfter >= 0)
            {
                defender.exBase = Mathf.Max(0, action.defenderExBaseAfter);
            }
        }

        SyncResourceViewsFromRule(Gundam2024RuleScript.PlayerSide.Player);
        SyncBaseZoneHeaderDisplay(Gundam2024RuleScript.PlayerSide.Player);
        ReconcileShieldStateWithZone(Gundam2024RuleScript.PlayerSide.Player, force: true);
        Debug.Log(
            $"[OnlineBattle] Defender area snapshot applied from attack payload. "
            + $"shield={action.defenderShieldAfter} exBase={action.defenderExBaseAfter} "
            + $"baseHp={action.defenderDeployedBaseHpAfter}");
    }

    private void HandleRemoteEffectSync(string payload)
    {
        if (!IsOnlineBattle())
        {
            return;
        }

        if (!OnlineBattleEffectSyncPayload.TryParse(payload, out OnlineBattleEffectSyncPayload sync))
        {
            Debug.LogWarning($"[OnlineBattle] Invalid EffectSync payload: {payload}");
            return;
        }

        Debug.Log(
            $"[EffectSync][ReceiveMessage] changes={sync.unitChanges?.Length ?? 0} "
            + $"payloadLength={(payload != null ? payload.Length : 0)}");

        _applyingRemoteBattleAction = true;
        try
        {
            ApplyRemoteEffectSync(sync);
        }
        finally
        {
            _applyingRemoteBattleAction = false;
            ResumeDeferredRemoteDestroyedResolutionsIfNeeded();
        }
    }
    // リモート効果同期でのユニット変更を適用
    // 非プレイヤーのゾーンのユニット変更を適用
    private void ApplyRemoteEffectSync(OnlineBattleEffectSyncPayload sync)
    {
        // リモート効果同期でのユニット変更を適用
        // ユニット変更は、ユニットの HP、ステータス、ステートなどの変更を表す
        // ユニット変更は、ユニットの ID、カード ID、ゾーンインデックス、ゾーンオーナー側などの情報を含む
        // ユニット変更は、ユニットの変更種別を表す
        // ユニット変更は、ユニットの変更種別に応じて適用される
        // ユニット変更は、ユニットの変更種別に応じて適用される
        OnlineBattleUnitEffectChange[] changes = sync.unitChanges;
        Debug.Log("状態変更されるユニットの数:" + changes.Length);
        if (changes == null)
        {
            Debug.LogWarning("[EffectSync][ApplyStart] changes=null");
            return;
        }

        Debug.Log($"[EffectSync][ApplyStart] changes={changes.Length}");
        int unitCount = 0;
        for (int i = 0; i < changes.Length; i++)
        {
            unitCount++;
            Debug.Log($"[EffectSync][ApplyStart] unitCount={unitCount}");
            OnlineBattleUnitEffectChange change = changes[i];
            if (change == null)
            {
                Debug.LogWarning($"[EffectSync][RecvChange][data is null] #{i} null");
                continue;
            }

            Debug.Log($"[EffectSync][RecvChange] #{i} {FormatOnlineEffectSyncChange(change)}");

            if (change.changeKind == OnlineBattleEffectSyncPayload.ChangeKindClearStatGrantsFromSource)
            {
                Debug.Log($"[EffectSync][RecvChange] ClearStatGrantsFromSource change.grantSourceInstanceId={change.grantSourceInstanceId}");
                if (change.grantSourceInstanceId > 0)
                {
                    CardController exclude = null;
                    Debug.Log($"[EffectSync][RecvChange] ClearStatGrantsFromSource change.grantSourceZoneOwnerSide={change.grantSourceZoneOwnerSide}");
                    if (change.grantSourceZoneOwnerSide == (int)PlayerType.Player
                        || change.grantSourceZoneOwnerSide == (int)PlayerType.Enemy)
                    {
                        exclude = FindBattleZoneUnitForRemoteSync(
                            change.grantSourceInstanceId,
                            (PlayerType)change.grantSourceZoneOwnerSide);
                    }

                    ClearStatGrantsFromBattleInstanceOnAllFieldUnits(
                        change.grantSourceInstanceId,
                        exclude: exclude,
                        queueOnlineStatDeltas: false);
                }

                continue;
            }

            if (change.changeKind == OnlineBattleEffectSyncPayload.ChangeKindRefreshOwnerTurnFieldPassives)
            {
                Debug.Log(
                    $"[OwnerTurnField][RecvRefresh] applying local recompute "
                    + $"currentSide:{currentPlayerType} applyingRemote:{_applyingRemoteBattleAction}");
                RefreshAllFieldOwnerTurnPassives();
                continue;
            }

            if (change.changeKind == OnlineBattleEffectSyncPayload.ChangeKindPreventAllyDestroyByEnemyEffect)
            {
                PlayerType senderSide =
                    change.targetZoneOwnerSide == (int)PlayerType.Enemy
                        ? PlayerType.Enemy
                        : PlayerType.Player;
                ApplyRemotePreventAllyDestroyByEnemyEffect(senderSide);
                continue;
            }

            CardController unit = FindEffectSyncTargetUnit(change);
            Debug.Log($"[EffectSync][TargetResolved] #{i} unit={FormatOnlineEffectSyncUnit(unit)}");
            if (unit == null)
            {
                Debug.LogWarning(
                    $"[OnlineBattle] Effect sync target not found: instanceId={change.targetInstanceId} "
                    + $"cardId={change.targetCardId} zoneIndex={change.targetZoneIndex} "
                    + $"zone={change.targetZoneOwnerSide} kind={change.changeKind}");
                continue;
            }

            switch (change.changeKind)
            {
                case OnlineBattleEffectSyncPayload.ChangeKindDamage:
                {
                    int beforeHp = unit.CurrentHp;
                    unit.SetCurrentHpForSync(change.hpAfter);
                    Debug.Log(
                        $"[EffectSync][ApplyDamage] #{i} {FormatOnlineEffectSyncUnit(unit)} "
                        + $"HP:{beforeHp}->{unit.CurrentHp} hpAfterPayload={change.hpAfter}");
                    if (change.hpAfter <= 0)
                    {
                        Debug.Log($"[EffectSync][DamageToTrash] #{i} unit={FormatOnlineEffectSyncUnit(unit)}");
                        NotifyAttackFlowParticipantRemovedDuringOnAction(unit);
                        // 相手の効果ダメージ（G-fred の全体ダメージ等）で破壊された場合も
                        // 自分の「カード効果で破壊された時」監視カードを解決する。
                        NotifyRemoteEffectDestroyForLocalWatchers(unit, change);
                        ApplyRemoteDestroyedUnitWithOnDestroyedEffects(
                            unit,
                            change.onDestroyedRequestId,
                            FindDestroyerForRemoteOnDestroyed(change.destroyerInstanceId));
                    }
                    else
                    {
                        Debug.Log(
                            $"[EffectSync][DamageNoTrash] #{i} unit={FormatOnlineEffectSyncUnit(unit)} "
                            + $"hpAfterPayload={change.hpAfter}");
                    }

                    break;
                }

                case OnlineBattleEffectSyncPayload.ChangeKindRepair:
                {
                    //リペアがない場合 自分のターン終わりに発動しない
                    // 二重送信の温床になっている？
                    int beforeHp = unit.CurrentHp;
                    unit.SetCurrentHpForSync(change.hpAfter);
                    Debug.Log(
                        $"ターンプレイヤー:" + currentPlayerType + "[EffectSync][ApplyRepair] #{i} {FormatOnlineEffectSyncUnit(unit)} "
                        + $"HP:{beforeHp}->{unit.CurrentHp}");
                    break;
                }
                case OnlineBattleEffectSyncPayload.ChangeKindStat:
                {
                    EffectStatTarget statTarget = (EffectStatTarget)change.statTarget;
                    string statKey = change.statModifierSourceKey;
                    if (!string.IsNullOrEmpty(statKey)
                        && unit.HasStatModifierFromSource(statKey, statTarget))
                    {
                        Debug.Log(
                            $"[EffectSync][ApplyStat] skip duplicate sourceKey={statKey} "
                            + $"unit={FormatOnlineEffectSyncUnit(unit)}");
                        break;
                    }

                    Debug.Log(
                        $"[EffectSync][ApplyStat] #{i} unit={FormatOnlineEffectSyncUnit(unit)} "
                        + $"value={change.signedStatValue} stat={change.statTarget} duration={change.duration}");
                    ApplyStatEffect(
                        unit,
                        change.signedStatValue,
                        statTarget,
                        (EffectDuration)change.duration,
                        string.IsNullOrEmpty(statKey) ? null : statKey);
                    break;
                }

                case OnlineBattleEffectSyncPayload.ChangeKindRest:
                    Debug.Log($"[EffectSync][ApplyRest] #{i} unit={FormatOnlineEffectSyncUnit(unit)}");
                    TryApplyRestToUnit(unit);
                    break;

                case OnlineBattleEffectSyncPayload.ChangeKindActivate:
                    Debug.Log($"[EffectSync][ApplyActivate] #{i} unit={FormatOnlineEffectSyncUnit(unit)}");
                    TryApplyActivateToUnit(unit);
                    break;

                case OnlineBattleEffectSyncPayload.ChangeKindDestroy:
                    Debug.Log($"[EffectSync][ApplyDestroy] #{i} unit={FormatOnlineEffectSyncUnit(unit)}");
                    // Kindhearted 等：受信側でも相手の効果破壊を防ぐ
                    {
                        PlayerType remoteEffectOwner = ResolveUnitOwnerForDestroyPrevent(unit) == PlayerType.Player
                            ? PlayerType.Enemy
                            : PlayerType.Player;
                        if (TryPreventEnemyEffectDestroy(unit, remoteEffectOwner))
                        {
                            Debug.Log(
                                $"[EffectSync][ApplyDestroy] prevented by Kindhearted-like shield "
                                + $"unit={FormatOnlineEffectSyncUnit(unit)}");
                            break;
                        }
                    }

                    NotifyAttackFlowParticipantRemovedDuringOnAction(unit);
                    unit.SetCurrentHpForSync(0);
                    NotifyRemoteEffectDestroyForLocalWatchers(unit, change);
                    ApplyRemoteDestroyedUnitWithOnDestroyedEffects(
                        unit,
                        change.onDestroyedRequestId,
                        FindDestroyerForRemoteOnDestroyed(change.destroyerInstanceId));
                    break;

                case OnlineBattleEffectSyncPayload.ChangeKindBounce:
                    Debug.Log($"[EffectSync][ApplyBounce] #{i} unit={FormatOnlineEffectSyncUnit(unit)}");
                    if (unit.Data != null && unit.Data.IsUnitToken())
                    {
                        TryVanishBattleUnitTokenFromZone(unit);
                    }
                    else
                    {
                        TryReturnBattleUnitToHand(unit);
                    }

                    break;

                case OnlineBattleEffectSyncPayload.ChangeKindReturnToDeckBottom:
                    Debug.Log($"[EffectSync][ApplyDeckBottom] #{i} unit={FormatOnlineEffectSyncUnit(unit)}");
                    TryReturnBattleUnitToDeckBottom(unit);
                    break;
            }
        }
      

        RefreshAllFieldOwnerTurnPassives();
        SyncAllResourceViewsFromRule();
        // ユニット変更が適用された後のユニットの状ユニット
        Debug.Log($"[OnlineBattle] Remote effect sync applied. changes={changes.Length}");
        Debug.Log($"--------------------------------");
    }

    /// <summary>リモート破壊時効果用に、破壊元ユニットを instanceId から探す。</summary>
    private CardController FindDestroyerForRemoteOnDestroyed(int destroyerInstanceId)
    {
        if (destroyerInstanceId <= 0)
        {
            return null;
        }

        return FindUnitByInstanceIdEitherZone(destroyerInstanceId);
    }

    /// <summary>リモート効果同期でのユニット破棄。トラッシュは ZoneSync で反映済みのため場から除去のみ。</summary>
    private void ApplyRemoteUnitToTrash(CardController unit)
    {
        Debug.Log($"[EffectSync][ApplyRemoteUnitToTrash] unit={FormatOnlineEffectSyncUnit(unit)}");
        ApplyRemoteUnitRemovedFromField(unit);
    }

    private IEnumerator HandleRemoteMountPilotCoroutine(string payload)
    {
        if (!OnlineBattleActionPayload.TryParse(payload, out OnlineBattleActionPayload action)
            || action.action != OnlineBattleActionPayload.MountPilot)
        {
            Debug.LogWarning($"[OnlineBattle] Invalid MountPilot payload: {payload}");
            yield break;
        }

        if (currentPlayerType != PlayerType.Enemy)
        {
            Debug.Log("[OnlineBattle] Ignored MountPilot because it is not opponent turn locally.");
            SendOpponentCardConfirmComplete(action.requestId);
            yield break;
        }

        CardController hostUnit = null;
        CardController pilotController = null;
        _applyingRemoteBattleAction = true;
        try
        {
            ApplyRemoteMountPilot(action, out hostUnit, out pilotController);
        }
        finally
        {
            _applyingRemoteBattleAction = false;
        }

        if (pilotController != null)
        {
            List<CardController> hosts = hostUnit != null
                ? new List<CardController> { hostUnit }
                : null;
            yield return ShowCommandUseAcknowledgementCoroutine(
                pilotController,
                null,
                hosts,
                GameLocale.T("相手 — パイロット搭乗", "Opponent — Pilot Mount"));
        }

        SendOpponentCardConfirmComplete(action.requestId);
    }

    private void ApplyRemoteMountPilot(
        OnlineBattleActionPayload action,
        out CardController hostUnit,
        out CardController pilotController)
    {
        hostUnit = null;
        pilotController = null;
        // 相手が Player ゾーンに配備した MS への搭乗 → 受信側は Enemy ゾーンで検索
        hostUnit = FindBattleZoneUnitForRemoteSync(action.instanceId, PlayerType.Player);
        if (hostUnit == null)
        {
            Debug.LogWarning($"[OnlineBattle] MountPilot host not found: {action.instanceId}");
            return;
        }

        if (!hostUnit.CanMountPilot())
        {
            Debug.LogWarning($"[OnlineBattle] MountPilot host cannot mount pilot: {action.instanceId}");
            return;
        }

        if (DeckSettinObject.Instance == null)
        {
            return;
        }

        CardData pilotData = DeckSettinObject.Instance.GetCardDataById(action.cardId);
        if (pilotData == null)
        {
            Debug.LogWarning($"[OnlineBattle] MountPilot unknown pilot card id: {action.cardId}");
            return;
        }

        PlayerType hostOwner = ResolveCardOwner(hostUnit.transform);
        GameObject pilotObject = Instantiate(CardImagePrefab, hostUnit.transform);
        pilotController = pilotObject.GetComponent<CardController>();
        pilotController.SetUp(pilotData, OnCardClicked);

        if (!hostUnit.TryAttachPilot(pilotController))
        {
            Destroy(pilotObject);
            pilotController = null;
            Debug.LogWarning("[OnlineBattle] MountPilot TryAttachPilot failed on remote.");
            return;
        }

        ApplyOnPilotMountedAllyBuffsDirect(hostUnit, pilotController, hostOwner);
        ApplyUnitAttackFlgFromLink(hostUnit, hostOwner);
        TryGrantOperationMeteorFirstStrikeOnPilotMount(hostUnit, pilotController, hostOwner);
        // 相手クライアントは MountPilot 受信のみ行い、OnLink 等の stat は EffectSync で受け取る。
        // TriggerOnLinkEffects をここで再生すると EffectSync の AP+1 と二重になる（例: デルタプラス+リディで AP8）。
        RefreshAllHandsConditionalOnHandAuto();
        RefreshAllFieldOwnerTurnPassives();
        ApplyRemoteDeployCostResourceSnapshotIfPresent(action);
        if (!action.includeResourceSnapshot)
        {
            SyncAllResourceViewsFromRule();
        }

        Debug.Log(
            $"[OnlineBattle] Remote pilot mounted. host={action.instanceId} pilot={action.cardId} "
            + $"AP:{hostUnit.CurrentPower} HP:{hostUnit.CurrentHp} "
            + $"includeRes:{action.includeResourceSnapshot} resource:{action.resourceAfter}");
    }
}
