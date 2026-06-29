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

    private bool ShouldUseOnlineBlockPhase(PlayerType attackerOwner)
    {
        return IsOnlineBattle() && attackerOwner == PlayerType.Player && !_applyingRemoteBattleAction;
    }

    private void RegisterBattleInstanceId(int instanceId)
    {
        _nextBattleInstanceId = Mathf.Max(_nextBattleInstanceId, instanceId + 1);
    }

    private void AssignBattleInstanceIdFromNetwork(CardController controller, int instanceId)
    {
        if (controller == null || instanceId <= 0)
        {
            return;
        }

        controller.AssignBattleInstanceId(instanceId);
        RegisterBattleInstanceId(instanceId);
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

    private CardController FindUnitByInstanceIdEitherZone(int instanceId)
    {
        CardController unit = FindBattleZoneUnitByInstanceId(instanceId, PlayerType.Player);
        if (unit != null)
        {
            return unit;
        }

        return FindBattleZoneUnitByInstanceId(instanceId, PlayerType.Enemy);
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
            _onlineEffectSyncActive = false;
            return;
        }

        _pendingOnlineEffectChanges ??= new List<OnlineBattleUnitEffectChange>();
        _pendingOnlineEffectChanges.Clear();
        _onlineEffectSyncActive = true;
    }

    private void FlushOnlineEffectSyncBatch()
    {
        if (!_onlineEffectSyncActive || _pendingOnlineEffectChanges == null || _pendingOnlineEffectChanges.Count == 0)
        {
            _onlineEffectSyncActive = false;
            return;
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
        if (!_onlineEffectSyncActive || target == null || target.BattleInstanceId <= 0)
        {
            return;
        }

        _pendingOnlineEffectChanges.Add(new OnlineBattleUnitEffectChange
        {
            targetInstanceId = target.BattleInstanceId,
            changeKind = OnlineBattleEffectSyncPayload.ChangeKindDamage,
            hpAfter = target.CurrentHp
        });
    }

    private void QueueOnlineUnitStat(
        CardController target,
        int signedValue,
        EffectStatTarget statTarget,
        EffectDuration duration)
    {
        if (!_onlineEffectSyncActive || target == null || target.BattleInstanceId <= 0)
        {
            return;
        }

        _pendingOnlineEffectChanges.Add(new OnlineBattleUnitEffectChange
        {
            targetInstanceId = target.BattleInstanceId,
            changeKind = OnlineBattleEffectSyncPayload.ChangeKindStat,
            signedStatValue = signedValue,
            statTarget = (int)statTarget,
            duration = (int)duration
        });
    }

    private void QueueOnlineUnitRest(CardController target)
    {
        if (!_onlineEffectSyncActive || target == null || target.BattleInstanceId <= 0)
        {
            return;
        }

        _pendingOnlineEffectChanges.Add(new OnlineBattleUnitEffectChange
        {
            targetInstanceId = target.BattleInstanceId,
            changeKind = OnlineBattleEffectSyncPayload.ChangeKindRest
        });
    }

    private void QueueOnlineUnitActivate(CardController target)
    {
        if (!_onlineEffectSyncActive || target == null || target.BattleInstanceId <= 0)
        {
            return;
        }

        _pendingOnlineEffectChanges.Add(new OnlineBattleUnitEffectChange
        {
            targetInstanceId = target.BattleInstanceId,
            changeKind = OnlineBattleEffectSyncPayload.ChangeKindActivate
        });
    }

    private void QueueOnlineUnitDestroy(CardController target)
    {
        if (!_onlineEffectSyncActive || target == null || target.BattleInstanceId <= 0)
        {
            return;
        }

        _pendingOnlineEffectChanges.Add(new OnlineBattleUnitEffectChange
        {
            targetInstanceId = target.BattleInstanceId,
            changeKind = OnlineBattleEffectSyncPayload.ChangeKindDestroy,
            hpAfter = 0
        });
    }

    private void QueueOnlineUnitBounce(CardController target)
    {
        if (!_onlineEffectSyncActive || target == null || target.BattleInstanceId <= 0)
        {
            return;
        }

        _pendingOnlineEffectChanges.Add(new OnlineBattleUnitEffectChange
        {
            targetInstanceId = target.BattleInstanceId,
            changeKind = OnlineBattleEffectSyncPayload.ChangeKindBounce
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

    private void NotifyLocalPlayCardDeployed(CardController cardController)
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
            OnlineBattleActionPayload.CreateDeployUnit(cardController.Data.id, cardController.BattleInstanceId)));
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
            OnlineBattleActionPayload.CreateMountPilot(hostUnit.BattleInstanceId, pilotCard.Data.id)));
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
            CardController onlineBlocker = FindUnitByInstanceIdEitherZone(blockerInstanceId);
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
            CardController onlineBlocker = FindUnitByInstanceIdEitherZone(blockerInstanceId);
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

        TriggerAllTimedEffectsForSide(endingTurnSide, EffectTiming.OnTurnEnd);
        ClearTimedStatModifiersForAllInPlayCards(EffectDuration.UntilEndOfTurn);
        ClearAttackActiveEnemyGrants(EffectDuration.UntilEndOfTurn);
        DumpTurnResourceUsageLogs(endingTurnSide, "end turn (remote)");

        currentPlayerType = PlayerType.Player;
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
            ApplyRemoteDeployUnit(action.cardId, action.instanceId);
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

    private void ApplyRemoteDeployUnit(int cardId, int instanceId)
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

        GameObject cardObject = Instantiate(CardImagePrefab, enemyCardGameRule.PlayerDeployPanel);
        CardController controller = cardObject.GetComponent<CardController>();
        controller.SetUp(cardData, OnCardClicked);

        if (!enemyBattleZoneCards.Contains(controller))
        {
            enemyBattleZoneCards.Add(controller);
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

        SendOnlineBattleMessage(EosOnlineBattleMessage.CreateAttack(
            OnlineBattleActionPayload.CreateShieldAttack(
                attacker.BattleInstanceId,
                defenderShieldAfter,
                defenderExBaseAfter,
                directAttackWin,
                ConsumeOnlineBrokenShieldCardIdsForAttackNotify())));
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
                attacker.BattleInstanceId,
                defender.BattleInstanceId,
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

        SyncResourceViewsFromRule(Gundam2024RuleScript.PlayerSide.Player);
        ReconcileShieldStateWithZone(Gundam2024RuleScript.PlayerSide.Player, force: true);
        Debug.Log(
            $"[OnlineBattle] Remote shield attack applied. shield={defender.shield} exBase={defender.exBase}");
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
                CommitShieldBreakTakenAfterBurst(takenCards[i], rule);
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
        CardController attacker = FindBattleZoneUnitByInstanceId(action.attackerInstanceId, PlayerType.Enemy);
        CardController defender = FindBattleZoneUnitByInstanceId(action.defenderInstanceId, PlayerType.Player);
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
            return;
        }

        for (int i = 0; i < changes.Length; i++)
        {
            OnlineBattleUnitEffectChange change = changes[i];
            if (change == null || change.targetInstanceId <= 0)
            {
                continue;
            }

            CardController unit = FindUnitByInstanceIdEitherZone(change.targetInstanceId);
            if (unit == null)
            {
                Debug.LogWarning(
                    $"[OnlineBattle] Effect sync target not found: instanceId={change.targetInstanceId} kind={change.changeKind}");
                continue;
            }

            switch (change.changeKind)
            {
                case OnlineBattleEffectSyncPayload.ChangeKindDamage:
                    unit.SetCurrentHpForSync(change.hpAfter);
                    if (change.hpAfter <= 0)
                    {
                        NotifyAttackFlowParticipantRemovedDuringOnAction(unit);
                        ApplyRemoteUnitToTrash(unit);
                    }

                    break;

                case OnlineBattleEffectSyncPayload.ChangeKindStat:
                    ApplyStatEffect(
                        unit,
                        change.signedStatValue,
                        (EffectStatTarget)change.statTarget,
                        (EffectDuration)change.duration);
                    break;

                case OnlineBattleEffectSyncPayload.ChangeKindRest:
                    TryApplyRestToUnit(unit);
                    break;

                case OnlineBattleEffectSyncPayload.ChangeKindActivate:
                    TryApplyActivateToUnit(unit);
                    break;

                case OnlineBattleEffectSyncPayload.ChangeKindDestroy:
                    NotifyAttackFlowParticipantRemovedDuringOnAction(unit);
                    unit.SetCurrentHpForSync(0);
                    ApplyRemoteUnitToTrash(unit);
                    break;

                case OnlineBattleEffectSyncPayload.ChangeKindBounce:
                    TryReturnBattleUnitToHand(unit);
                    break;
            }
        }

        SyncAllResourceViewsFromRule();
        Debug.Log($"[OnlineBattle] Remote effect sync applied. changes={changes.Length}");
    }

    /// <summary>リモート効果同期でのユニット破棄。トラッシュは ZoneSync で反映済みのため場から除去のみ。</summary>
    private void ApplyRemoteUnitToTrash(CardController unit)
    {
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
        CardController hostUnit = FindUnitByInstanceIdEitherZone(action.instanceId);
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
        SyncAllResourceViewsFromRule();
        Debug.Log(
            $"[OnlineBattle] Remote pilot mounted. host={action.instanceId} pilot={action.cardId} "
            + $"AP:{hostUnit.CurrentPower} HP:{hostUnit.CurrentHp}");
    }
}
