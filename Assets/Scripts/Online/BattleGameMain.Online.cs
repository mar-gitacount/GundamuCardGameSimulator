using System.Collections.Generic;
using UnityEngine;

public partial class BattleGameMain
{
    private IBattleOpponent battleOpponent;
    private bool networkBattleHooksRegistered;
    private int _nextBattleInstanceId = 1;
    private bool _applyingRemoteBattleAction;

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

    private bool ShouldSkipOnActionPauseForOnline()
    {
        return IsOnlineBattle();
    }

    private void ResetOnlineBattleInstanceIds()
    {
        _nextBattleInstanceId = 1;
    }

    private void AssignBattleInstanceIdIfNeeded(CardController controller)
    {
        if (controller == null || controller.Data == null || controller.Data.type != Type.Unit)
        {
            return;
        }

        if (controller.BattleInstanceId > 0)
        {
            return;
        }

        controller.AssignBattleInstanceId(_nextBattleInstanceId++);
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

        if (cardController.Data.type != Type.Unit)
        {
            return;
        }

        SendOnlineBattleMessage(EosOnlineBattleMessage.CreatePlayCard(
            OnlineBattleActionPayload.CreateDeployUnit(cardController.Data.id)));
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
        }
    }

    private void HandleRemoteEndTurn()
    {
        if (currentPlayerType != PlayerType.Enemy)
        {
            Debug.Log("[OnlineBattle] Ignored remote EndTurn because it is not opponent turn locally.");
            return;
        }

        Debug.Log("[OnlineBattle] Remote EndTurn received. Advancing local turn.");
        ChangePhase(BattlePhase.EndTurn);
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
            ApplyRemoteDeployUnit(action.cardId);
        }
    }

    private void ApplyRemoteDeployUnit(int cardId)
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

        if (cardData.type == Type.Unit)
        {
            controller.ResetRuntimeStatsFromData();
            controller.SetAttackFlg(AttackFlg.False);
            controller.SetUnitRestVisual(false);
            AssignBattleInstanceIdIfNeeded(controller);
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
                directAttackWin)));
    }

    private void NotifyLocalUnitAttackResolved(
        CardController attacker,
        CardController defender,
        int attackerHpAfter,
        int defenderHpAfter)
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
                defenderHpAfter)));
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

        if (oldShield > defender.shield)
        {
            OnGundamShieldDamaged(
                Gundam2024RuleScript.PlayerSide.Player,
                oldShield,
                defender.shield,
                false);
        }

        SyncResourceViewsFromRule(Gundam2024RuleScript.PlayerSide.Player);
        ReconcileShieldStateWithZone(Gundam2024RuleScript.PlayerSide.Player, force: true);
        Debug.Log(
            $"[OnlineBattle] Remote shield attack applied. shield={defender.shield} exBase={defender.exBase}");
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

        if (defender.CurrentHp <= 0)
        {
            SendCardToTrash(defender, PlayerType.Player, attacker);
        }

        if (attacker.CurrentHp <= 0)
        {
            SendCardToTrash(attacker, PlayerType.Enemy);
        }

        SyncAllResourceViewsFromRule();
        Debug.Log(
            $"[OnlineBattle] Remote unit attack applied. attackerHp={action.attackerHp} defenderHp={action.defenderHp}");
    }
}
