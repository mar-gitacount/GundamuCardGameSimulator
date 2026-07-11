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
    private List<OnlineBattleUnitEffectChange> _pendingOnlineEffectChanges;
    private bool _onlineEffectSyncActive;

    /// <summary>相手が配備したユニット用。ローカル手番ユニット ID（1〜）との衝突を避ける。</summary>
    private const int RemoteBattleInstanceIdOffset = 100000;

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

        // 相手ミラー用 ID はローカル採番カウンタに影響させない
        controller.AssignBattleInstanceId(instanceId);
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

        // 効果同期は送信時点の盤面スロット（zone + index + cardId）のみで解決する。
        // instanceId / cardId のみのフォールバックは誤解決の原因になるため使わない。
        return FindEffectSyncTargetUnitBySlot(change, hasZoneOwner);
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
        _pendingOnlineBlockRequestId = 0;
        _pendingOnlineBlockCallback = null;
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

        if (_nextBattleInstanceId >= RemoteBattleInstanceIdOffset)
        {
            Debug.LogError("[OnlineBattle] Local BattleInstanceId space exhausted.");
            return;
        }

        controller.AssignBattleInstanceId(_nextBattleInstanceId++);
    }

    private int AllocateBattleInstanceId()
    {
        return _nextBattleInstanceId++;
    }

    private CardController FindBattleZoneUnitByInstanceId(int instanceId, PlayerType zoneOwner)
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

        for (int i = 0; i < zone.Count; i++)
        {
            CardController card = zone[i];
            if (card != null && card.BattleInstanceId == instanceId)
            {
                return card;
            }
        }

        return null;
    }

    /// <summary>ローカル専用。オンライン同期ではゾーン明示の検索を使うこと。</summary>
    private CardController FindUnitByInstanceIdEitherZone(int instanceId)
    {
        if (instanceId <= 0)
        {
            return null;
        }

        if (IsRemoteMappedBattleInstanceId(instanceId))
        {
            CardController mappedEnemy = FindBattleZoneUnitByInstanceId(instanceId, PlayerType.Enemy);
            if (mappedEnemy != null)
            {
                return mappedEnemy;
            }
        }

        CardController owned = FindBattleZoneUnitByInstanceId(instanceId, PlayerType.Player);
        if (owned != null)
        {
            return owned;
        }

        return FindOpponentUnitByPeerInstanceId(instanceId);
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

        for (int i = 0; i < _pendingOnlineEffectChanges.Count; i++)
        {
            Debug.Log($"[EffectSync][SendFlush] #{i} {FormatOnlineEffectSyncChange(_pendingOnlineEffectChanges[i])}");
        }

        string json = OnlineBattleEffectSyncPayload.ToJson(_pendingOnlineEffectChanges.ToArray());
        if (!string.IsNullOrWhiteSpace(json))
        {
            SendOnlineBattleMessage(EosOnlineBattleMessage.CreateEffectSync(json));
            Debug.Log($"[OnlineBattle] Effect sync sent. changes={_pendingOnlineEffectChanges.Count}");
        }

        _pendingOnlineEffectChanges.Clear();
        _onlineEffectSyncActive = false;
    }

    private void QueueOnlineUnitDamage(CardController target)
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

        Debug.Log($"[EffectDamage][OnlineQueueDamage] target={FormatOnlineEffectSyncUnit(target)}");
        bool queued = TryQueueOnlineUnitTargetChange(target, new OnlineBattleUnitEffectChange
        {
            changeKind = OnlineBattleEffectSyncPayload.ChangeKindDamage,
            hpAfter = target.CurrentHp
        });
        if (!queued)
        {
            Debug.LogWarning($"[EffectDamage][OnlineQueueDamageFail] target={FormatOnlineEffectSyncUnit(target)}");
        }
    }

    private void QueueOnlineUnitRepair(CardController target)
    {
        if (!_onlineEffectSyncActive || target == null)
        {
            return;
        }

        TryQueueOnlineUnitTargetChange(target, new OnlineBattleUnitEffectChange
        {
            changeKind = OnlineBattleEffectSyncPayload.ChangeKindRepair,
            hpAfter = target.CurrentHp
        });
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

    private void QueueOnlineClearStatGrantsFromSource(CardController grantingUnit)
    {
        if (!_onlineEffectSyncActive || grantingUnit == null || grantingUnit.BattleInstanceId <= 0)
        {
            return;
        }

        _pendingOnlineEffectChanges.Add(new OnlineBattleUnitEffectChange
        {
            changeKind = OnlineBattleEffectSyncPayload.ChangeKindClearStatGrantsFromSource,
            grantSourceInstanceId = grantingUnit.BattleInstanceId,
            grantSourceZoneOwnerSide = (int)ResolveBattleZoneSideForUnit(grantingUnit)
        });
    }

    private void QueueOnlineRefreshOwnerTurnFieldPassives()
    {
        if (!_onlineEffectSyncActive)
        {
            return;
        }

        _pendingOnlineEffectChanges.Add(new OnlineBattleUnitEffectChange
        {
            changeKind = OnlineBattleEffectSyncPayload.ChangeKindRefreshOwnerTurnFieldPassives
        });
    }

    /// <summary>攻撃フロー終了時：UntilEndOfBattle の Buff/Debuff を盤面全体から解除し、オンラインなら相手へも同期。</summary>
    private void ClearAttackScopedTimedStatModifiers()
    {
        ClearTimedStatModifiersForAllInPlayCards(EffectDuration.UntilEndOfBattle);
        ClearAttackActiveEnemyGrants(EffectDuration.UntilEndOfBattle);
        SendOnlineClearTimedStatModifiersByDurationIfNeeded(EffectDuration.UntilEndOfBattle);
    }

    /// <summary>相手の攻撃完了通知を受けた側で、攻撃スコープの補正をローカル解除する。</summary>
    private void ApplyRemoteAttackScopedTimedStatModifierCleanup()
    {
        ClearTimedStatModifiersForAllInPlayCards(EffectDuration.UntilEndOfBattle);
        ClearAttackActiveEnemyGrants(EffectDuration.UntilEndOfBattle);
    }

    private void SendOnlineClearTimedStatModifiersByDurationIfNeeded(EffectDuration duration)
    {
        if (!IsOnlineBattle() || _applyingRemoteBattleAction || currentPlayerType != PlayerType.Player)
        {
            return;
        }

        string json = OnlineBattleEffectSyncPayload.ToJson(new[]
        {
            new OnlineBattleUnitEffectChange
            {
                changeKind = OnlineBattleEffectSyncPayload.ChangeKindClearTimedStatModifiersByDuration,
                duration = (int)duration
            }
        });
        if (!string.IsNullOrWhiteSpace(json))
        {
            SendOnlineBattleMessage(EosOnlineBattleMessage.CreateEffectSync(json));
            Debug.Log($"[OnlineBattle] Attack-scoped stat clear sync sent. duration={duration}");
        }
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

    private void QueueOnlineUnitDestroy(CardController target)
    {
        Debug.Log($"[EffectDamage][OnlineQueueDestroy] target={FormatOnlineEffectSyncUnit(target)}");
        bool queued = TryQueueOnlineUnitTargetChange(target, new OnlineBattleUnitEffectChange
        {
            changeKind = OnlineBattleEffectSyncPayload.ChangeKindDestroy,
            hpAfter = 0
        });
        if (!queued)
        {
            Debug.LogWarning($"[EffectDamage][OnlineQueueDestroyFail] target={FormatOnlineEffectSyncUnit(target)}");
        }
    }

    private void QueueOnlineUnitBounce(CardController target)
    {
        TryQueueOnlineUnitTargetChange(target, new OnlineBattleUnitEffectChange
        {
            changeKind = OnlineBattleEffectSyncPayload.ChangeKindBounce
        });
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

        SendOnlineBattleMessage(EosOnlineBattleMessage.CreateEndTurn());
    }

    private void NotifyLocalPlayCardDeployed(CardController cardController, bool deployToOpponentField = false)
    {
        if (!IsOnlineBattle() || currentPlayerType != PlayerType.Player || cardController == null || cardController.Data == null)
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

        SendOnlineBattleMessage(EosOnlineBattleMessage.CreatePlayCard(
            OnlineBattleActionPayload.CreateDeployUnit(
                cardController.Data.id,
                cardController.BattleInstanceId,
                deployToOpponentField)));
    }

    /// <summary>場からユニット／トークンを除去したことを相手へ即時同期（戦闘破壊・バウンス・消滅）。</summary>
    private void SendOnlineUnitFieldRemovalSync(int targetInstanceId, string changeKind)
    {
        if (!IsOnlineBattle() || _applyingRemoteBattleAction || currentPlayerType != PlayerType.Player
            || targetInstanceId <= 0 || string.IsNullOrEmpty(changeKind))
        {
            return;
        }

        int syncInstanceId = ToSyncInstanceId(targetInstanceId);
        if (syncInstanceId <= 0)
        {
            return;
        }

        string json = OnlineBattleEffectSyncPayload.ToJson(new[]
        {
            new OnlineBattleUnitEffectChange
            {
                targetInstanceId = syncInstanceId,
                changeKind = changeKind,
                hpAfter = 0
            }
        });
        if (!string.IsNullOrWhiteSpace(json))
        {
            SendOnlineBattleMessage(EosOnlineBattleMessage.CreateEffectSync(json));
            Debug.Log($"[OnlineBattle] Unit removal sync sent. instanceId={targetInstanceId} kind={changeKind}");
        }
    }

    private void NotifyLocalPilotMounted(CardController hostUnit, CardController pilotCard)
    {
        if (_applyingRemoteBattleAction || !IsOnlineBattle() || currentPlayerType != PlayerType.Player
            || hostUnit == null || pilotCard == null || pilotCard.Data == null
            || hostUnit.BattleInstanceId <= 0)
        {
            return;
        }

        SendOnlineBattleMessage(EosOnlineBattleMessage.CreateMountPilot(
            OnlineBattleActionPayload.CreateMountPilot(ToSyncInstanceId(hostUnit), pilotCard.Data.id)));
        Debug.Log(
            $"[OnlineBattle] MountPilot sync sent. host={hostUnit.BattleInstanceId} pilot={pilotCard.Data.id}");
    }

    private void SendOnlineBattleMessage(string json)
    {
        if (EosP2PTestService.Instance == null)
        {
            Debug.LogWarning("[OnlineBattle] P2P service not found.");
            return;
        }

        if (string.IsNullOrWhiteSpace(EosOnlineMatchState.RemoteProductUserId))
        {
            Debug.LogWarning("[OnlineBattle] Remote ProductUserId is not set.");
            return;
        }

        EosP2PTestService.Instance.SendText(EosOnlineMatchState.RemoteProductUserId, json);
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

        switch (message.type)
        {
            case "EndTurn":
                HandleRemoteEndTurn();
                break;
            case "PlayCard":
                HandleRemotePlayCard(message.payload);
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
                HandleRemoteMountPilot(message.payload);
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
        int defenderInstanceId = originalDefender != null ? ToSyncInstanceId(originalDefender) : 0;

        SendOnlineBattleMessage(EosOnlineBattleMessage.CreateAttackDeclare(
            OnlineBattleActionPayload.CreateAttackDeclare(
                requestId,
                attackKind,
                ToSyncInstanceId(attacker),
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

        CardController attacker = FindOpponentUnitByPeerInstanceId(action.attackerInstanceId);
        if (attacker == null)
        {
            Debug.LogWarning($"[OnlineBattle] AttackDeclare attacker not found: {action.attackerInstanceId}");
            SendOnlineBlockResponse(action.requestId, 0);
            return;
        }

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
                    blockerInstanceId = ToSyncInstanceId(selectedBlocker);
                }

                SendOnlineBlockResponse(requestId, blockerInstanceId);
            },
            passBlockAndSendResponse);

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

    private void HandleRemoteEndTurn()
    {
        if (currentPlayerType != PlayerType.Enemy)
        {
            Debug.Log("[OnlineBattle] Ignored remote EndTurn because it is not opponent turn locally.");
            return;
        }

        Debug.Log("[OnlineBattle] Remote EndTurn received. Applying turn switch (OnAction already handled via P2P).");
        StartCoroutine(ApplyRemoteOpponentEndedTurnCoroutine());
    }

    /// <summary>
    /// 相手がターン終了したとき、ローカルで EndTurn コルーチン（OnAction 含む）を再実行せずターンだけ進める。
    /// 非アクティブ側の OnAction は相手の <see cref="ExecuteEndTurnCoroutine"/> からの OnActionBegin で既に処理済み。
    /// </summary>
    private IEnumerator ApplyRemoteOpponentEndedTurnCoroutine()
    {
        isEndTurnFlowRunning = true;
        yield return WaitForShieldBreakFlowCompleteCoroutine();
        yield return WaitForBattleFlowIdleCoroutine();

        pendingUnitAttackAttacker = null;
        pendingOnAttackEffectResolvedAttacker = null;
        PlayerType endingTurnSide = PlayerType.Enemy;

        ApplyTurnEndRepairForAllInPlayUnits();
        TriggerAllTimedEffectsForSide(endingTurnSide, EffectTiming.OnTurnEnd);
        ClearTimedStatModifiersForAllInPlayCards(EffectDuration.UntilEndOfTurn);
        ClearAttackActiveEnemyGrants(EffectDuration.UntilEndOfTurn);
        DumpTurnResourceUsageLogs(endingTurnSide, "end turn (remote)");

        currentPlayerType = PlayerType.Player;
        RefreshAllFieldOwnerTurnPassives();
        AdvanceRuleToNextTurnStart();
        UpdateEndTurnButtonVisibility();

        Debug.Log("[OnlineBattle] Remote opponent turn ended. Starting local turn.");
        ChangePhase(BattlePhase.StartTurn);
        isEndTurnFlowRunning = false;
    }

    private void HandleRemotePlayCard(string payload)
    {
        if (!OnlineBattleActionPayload.TryParse(payload, out OnlineBattleActionPayload action))
        {
            Debug.LogWarning($"[OnlineBattle] Invalid PlayCard payload: {payload}");
            return;
        }

        if (currentPlayerType != PlayerType.Enemy)
        {
            Debug.Log("[OnlineBattle] Ignored remote PlayCard because it is not opponent turn locally.");
            return;
        }

        if (action.action == OnlineBattleActionPayload.DeployUnit)
        {
            ApplyRemoteDeployUnit(action.cardId, action.instanceId, action.deployToOpponentField);
            return;
        }

        if (action.action == OnlineBattleActionPayload.DeployBase)
        {
            ApplyRemoteDeployBase(action);
            return;
        }

        if (action.action == OnlineBattleActionPayload.DeployShield)
        {
            ApplyRemoteDeployShield(action);
        }
    }

    private void ApplyRemoteDeployUnit(int cardId, int instanceId, bool deployToOpponentField)
    {
        if (DeckSettinObject.Instance == null)
        {
            return;
        }

        CardData cardData = DeckSettinObject.Instance.GetCardDataById(cardId);
        if (cardData == null)
        {
            Debug.LogWarning($"[OnlineBattle] Unknown card id for remote deploy: {cardId}");
            return;
        }

        CardGameRule ownerRule = deployToOpponentField ? cardGameRule : enemyCardGameRule;
        List<CardController> zone = deployToOpponentField ? playerBattleZoneCards : enemyBattleZoneCards;
        if (ownerRule?.PlayerDeployPanel == null)
        {
            return;
        }

        GameObject cardObject = Instantiate(CardImagePrefab, ownerRule.PlayerDeployPanel);
        CardController controller = cardObject.GetComponent<CardController>();
        controller.SetUp(cardData, OnCardClicked);

        if (!zone.Contains(controller))
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
        }

        RefreshAllFieldOwnerTurnPassives();
        Debug.Log($"[OnlineBattle] Remote unit deployed on opponent field: {cardData.cardName} ({cardId})");
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
        bool blockCombat = false)
    {
        if (_applyingRemoteBattleAction || !IsOnlineBattle() || currentPlayerType != PlayerType.Player
            || attacker == null || defender == null
            || attacker.BattleInstanceId <= 0 || defender.BattleInstanceId <= 0)
        {
            return;
        }

        SendOnlineBattleMessage(EosOnlineBattleMessage.CreateAttack(
            OnlineBattleActionPayload.CreateUnitAttack(
                ToSyncInstanceId(attacker),
                ToSyncInstanceId(defender),
                attackerHpAfter,
                defenderHpAfter,
                blockCombat)));
    }

    private void HandleRemoteAttack(string payload)
    {
        if (!OnlineBattleActionPayload.TryParse(payload, out OnlineBattleActionPayload action))
        {
            Debug.LogWarning($"[OnlineBattle] Invalid Attack payload: {payload}");
            return;
        }

        if (currentPlayerType != PlayerType.Enemy)
        {
            Debug.Log("[OnlineBattle] Ignored remote Attack because it is not opponent turn locally.");
            return;
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
        }
    }

    private void ApplyRemoteShieldAttack(OnlineBattleActionPayload action)
    {
        CardController attacker = FindOpponentUnitByPeerInstanceId(action.attackerInstanceId);
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

        ApplyRemoteAttackScopedTimedStatModifierCleanup();
        SyncResourceViewsFromRule(Gundam2024RuleScript.PlayerSide.Player);
        ReconcileShieldStateWithZone(Gundam2024RuleScript.PlayerSide.Player, force: true);
        Debug.Log(
            $"[OnlineBattle] Remote shield attack applied. shield={defender.shield} exBase={defender.exBase} "
            + $"baseHp:{action.defenderDeployedBaseHpAfter}");
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
        CardController attacker = FindOpponentUnitByPeerInstanceId(action.attackerInstanceId);
        CardController defender = FindOwnedUnitBySyncInstanceId(action.defenderInstanceId);
        if (attacker == null || defender == null)
        {
            Debug.LogWarning(
                $"[OnlineBattle] Remote unit attack target not found. attacker={action.attackerInstanceId} defender={action.defenderInstanceId}");
            return;
        }

        CommitUnitAttackDeclaration(attacker, PlayerType.Enemy);
        defender.SetCurrentHpForSync(action.defenderHp);
        attacker.SetCurrentHpForSync(action.attackerHp);

        if (action.blockCombat && defender.CurrentHp > 0)
        {
            SetUnitRestAndTriggerEffects(defender, PlayerType.Player);
        }

        if (defender.CurrentHp <= 0)
        {
            ApplyRemoteUnitRemovedFromField(defender);
        }

        if (attacker.CurrentHp <= 0)
        {
            ApplyRemoteUnitRemovedFromField(attacker);
        }

        ApplyRemoteAttackScopedTimedStatModifierCleanup();
        SyncAllResourceViewsFromRule();
        Debug.Log(
            $"[OnlineBattle] Remote unit attack applied. attackerHp={action.attackerHp} defenderHp={action.defenderHp}");
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
        }
    }

    private void ApplyRemoteEffectSync(OnlineBattleEffectSyncPayload sync)
    {
        OnlineBattleUnitEffectChange[] changes = sync.unitChanges;
        if (changes == null)
        {
            Debug.LogWarning("[EffectSync][ApplyStart] changes=null");
            return;
        }

        Debug.Log($"[EffectSync][ApplyStart] changes={changes.Length}");
        for (int i = 0; i < changes.Length; i++)
        {
            OnlineBattleUnitEffectChange change = changes[i];
            if (change == null)
            {
                Debug.LogWarning($"[EffectSync][RecvChange] #{i} null");
                continue;
            }

            Debug.Log($"[EffectSync][RecvChange] #{i} {FormatOnlineEffectSyncChange(change)}");

            if (change.changeKind == OnlineBattleEffectSyncPayload.ChangeKindClearStatGrantsFromSource)
            {
                if (change.grantSourceInstanceId > 0)
                {
                    CardController exclude = null;
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
                RefreshAllFieldOwnerTurnPassives();
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
                        + $"HP:{beforeHp}->{unit.CurrentHp}");
                    if (change.hpAfter <= 0)
                    {
                        Debug.Log($"[EffectSync][DamageToTrash] #{i} unit={FormatOnlineEffectSyncUnit(unit)}");
                        NotifyAttackFlowParticipantRemovedDuringOnAction(unit);
                        ApplyRemoteUnitToTrash(unit);
                    }

                    break;
                }

                case OnlineBattleEffectSyncPayload.ChangeKindRepair:
                {
                    int beforeHp = unit.CurrentHp;
                    unit.SetCurrentHpForSync(change.hpAfter);
                    Debug.Log(
                        $"[EffectSync][ApplyRepair] #{i} {FormatOnlineEffectSyncUnit(unit)} "
                        + $"HP:{beforeHp}->{unit.CurrentHp}");
                    break;
                }

                case OnlineBattleEffectSyncPayload.ChangeKindStat:
                    Debug.Log(
                        $"[EffectSync][ApplyStat] #{i} unit={FormatOnlineEffectSyncUnit(unit)} "
                        + $"value={change.signedStatValue} stat={change.statTarget} duration={change.duration}");
                    ApplyStatEffect(
                        unit,
                        change.signedStatValue,
                        (EffectStatTarget)change.statTarget,
                        (EffectDuration)change.duration,
                        string.IsNullOrEmpty(change.statModifierSourceKey) ? null : change.statModifierSourceKey);
                    break;

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
                    NotifyAttackFlowParticipantRemovedDuringOnAction(unit);
                    unit.SetCurrentHpForSync(0);
                    ApplyRemoteUnitToTrash(unit);
                    break;

                case OnlineBattleEffectSyncPayload.ChangeKindBounce:
                    Debug.Log($"[EffectSync][ApplyBounce] #{i} unit={FormatOnlineEffectSyncUnit(unit)}");
                    TryReturnBattleUnitToHand(unit);
                    break;

                case OnlineBattleEffectSyncPayload.ChangeKindReturnToDeckBottom:
                    Debug.Log($"[EffectSync][ApplyDeckBottom] #{i} unit={FormatOnlineEffectSyncUnit(unit)}");
                    TryReturnBattleUnitToDeckBottom(unit);
                    break;
            }
        }

        RefreshAllFieldOwnerTurnPassives();
        SyncAllResourceViewsFromRule();
        Debug.Log($"[OnlineBattle] Remote effect sync applied. changes={changes.Length}");
    }

    /// <summary>リモート効果同期でのユニット破棄。トラッシュは ZoneSync で反映済みのため場から除去のみ。</summary>
    private void ApplyRemoteUnitToTrash(CardController unit)
    {
        Debug.Log($"[EffectSync][ApplyRemoteUnitToTrash] unit={FormatOnlineEffectSyncUnit(unit)}");
        ApplyRemoteUnitRemovedFromField(unit);
    }

    private void HandleRemoteMountPilot(string payload)
    {
        if (currentPlayerType != PlayerType.Enemy)
        {
            Debug.Log("[OnlineBattle] Ignored MountPilot because it is not opponent turn locally.");
            return;
        }

        if (!OnlineBattleActionPayload.TryParse(payload, out OnlineBattleActionPayload action)
            || action.action != OnlineBattleActionPayload.MountPilot)
        {
            Debug.LogWarning($"[OnlineBattle] Invalid MountPilot payload: {payload}");
            return;
        }

        _applyingRemoteBattleAction = true;
        try
        {
            ApplyRemoteMountPilot(action);
        }
        finally
        {
            _applyingRemoteBattleAction = false;
        }
    }

    private void ApplyRemoteMountPilot(OnlineBattleActionPayload action)
    {
        // 相手が Player ゾーンに配備した MS への搭乗 → 受信側は Enemy ゾーンで検索
        CardController hostUnit = FindBattleZoneUnitForRemoteSync(action.instanceId, PlayerType.Player);
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
        CardController pilotController = pilotObject.GetComponent<CardController>();
        pilotController.SetUp(pilotData, OnCardClicked);

        if (!hostUnit.TryAttachPilot(pilotController))
        {
            Destroy(pilotObject);
            Debug.LogWarning("[OnlineBattle] MountPilot TryAttachPilot failed on remote.");
            return;
        }

        ApplyUnitAttackFlgFromLink(hostUnit, hostOwner);
        RefreshAllFieldOwnerTurnPassives();
        SyncAllResourceViewsFromRule();
        Debug.Log(
            $"[OnlineBattle] Remote pilot mounted. host={action.instanceId} pilot={action.cardId} "
            + $"AP:{hostUnit.CurrentPower} HP:{hostUnit.CurrentHp}");
    }
}
