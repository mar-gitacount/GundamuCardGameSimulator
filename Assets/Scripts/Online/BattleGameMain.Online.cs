using UnityEngine;

public partial class BattleGameMain
{
    private IBattleOpponent battleOpponent;
    private bool networkBattleHooksRegistered;

    private void InitializeBattleOpponent()
    {
        battleOpponent = EosOnlineMatchState.HasActiveMatch
            ? (IBattleOpponent)new NetworkBattleOpponent()
            : new CpuBattleOpponent();

        RegisterNetworkBattleHooksIfNeeded();
    }

    private void RegisterNetworkBattleHooksIfNeeded()
    {
        if (networkBattleHooksRegistered || !EosOnlineMatchState.HasActiveMatch)
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
        if (!EosOnlineMatchState.HasActiveMatch)
        {
            playerGoesFirst = false;
            return false;
        }

        playerGoesFirst = EosOnlineMatchState.LocalPlayerGoesFirst;
        return true;
    }

    private void NotifyLocalPlayerEndedTurn()
    {
        if (!EosOnlineMatchState.HasActiveMatch || currentPlayerType != PlayerType.Player)
        {
            return;
        }

        if (EosP2PTestService.Instance == null)
        {
            Debug.LogWarning("[OnlineBattle] Cannot send EndTurn: P2P service not found.");
            return;
        }

        if (string.IsNullOrWhiteSpace(EosOnlineMatchState.RemoteProductUserId))
        {
            Debug.LogWarning("[OnlineBattle] Cannot send EndTurn: remote ProductUserId is not set.");
            return;
        }

        EosP2PTestService.Instance.SendText(
            EosOnlineMatchState.RemoteProductUserId,
            EosOnlineBattleMessage.CreateEndTurn());
    }

    private void OnOnlineBattleMessageReceived(string peerId, string payload)
    {
        if (!EosOnlineMatchState.HasActiveMatch || battleOpponent == null || !battleOpponent.IsNetwork)
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
                if (currentPlayerType == PlayerType.Enemy)
                {
                    Debug.Log("[OnlineBattle] Remote EndTurn received. Advancing local turn.");
                    ChangePhase(BattlePhase.EndTurn);
                }
                break;
            case "PlayCard":
                Debug.Log($"[OnlineBattle] PlayCard received: {message.payload}");
                break;
            case "Attack":
                Debug.Log($"[OnlineBattle] Attack received: {message.payload}");
                break;
        }
    }
}
