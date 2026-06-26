using UnityEngine;

/// <summary>
/// オンライン対戦の OnAction（アクションステップ）同期。
/// 攻撃フローでは AI 戦と同じく「Enemy ゾーン → Player ゾーン」の順で進行する。
/// </summary>
public partial class BattleGameMain
{
    private int _onlineOnActionRequestIdCounter;
    private int _pendingOnlineOnActionRequestId;
    private System.Action _pendingOnlineOnActionCallback;
    private int _onlineOnActionResponseRequestId;

    private void ResetOnlineOnActionState()
    {
        _onlineOnActionRequestIdCounter = 0;
        _pendingOnlineOnActionRequestId = 0;
        _pendingOnlineOnActionCallback = null;
        _onlineOnActionResponseRequestId = 0;
    }

    /// <summary>
    /// オンライン時の単側 OnAction。Enemy ゾーンは相手クライアント、Player ゾーンはローカル。
    /// </summary>
    private bool TryHandleSingleSideOnActionStepOnline(
        PlayerType side,
        string context,
        System.Action onStepDone,
        CardController attackingUnitInAttackFlow)
    {
        if (side == PlayerType.Enemy)
        {
            if (currentPlayerType != PlayerType.Player)
            {
                return false;
            }

            return TryBeginOnlineOnActionWaitForRemoteZone(
                PlayerType.Enemy,
                context,
                onStepDone,
                attackingUnitInAttackFlow);
        }

        return TryOpenOnActionCommandSelection(
            PlayerType.Player,
            context,
            onStepDone,
            attackingUnitInAttackFlow);
    }

    private bool TryBeginOnlineOnActionWaitForRemoteZone(
        PlayerType actingZoneSideOnAttackerClient,
        string context,
        System.Action onComplete,
        CardController attackingUnitInAttackFlow)
    {
        if (!IsOnlineBattle() || onComplete == null)
        {
            return false;
        }

        int requestId = ++_onlineOnActionRequestIdCounter;
        _pendingOnlineOnActionRequestId = requestId;
        _pendingOnlineOnActionCallback = onComplete;

        int attackerInstanceId = attackingUnitInAttackFlow != null ? attackingUnitInAttackFlow.BattleInstanceId : 0;
        SendOnlineBattleMessage(EosOnlineBattleMessage.CreateOnActionBegin(
            OnlineBattleActionPayload.CreateOnActionBegin(
                requestId,
                (int)actingZoneSideOnAttackerClient,
                context,
                attackerInstanceId)));

        Debug.Log($"[OnlineBattle] OnAction wait started. requestId={requestId} zone={actingZoneSideOnAttackerClient} context={context}");
        return true;
    }

    private void NotifyLocalOnActionPhaseComplete(int requestId)
    {
        if (!IsOnlineBattle() || requestId <= 0)
        {
            return;
        }

        Gundam2024RuleScript.PlayerState localPlayer = gundamRule?.Player;
        int resource = localPlayer != null ? localPlayer.resource : 0;
        int exResource = localPlayer != null ? localPlayer.exResource : 0;
        int level = localPlayer != null ? localPlayer.level : 0;

        SendOnlineBattleMessage(EosOnlineBattleMessage.CreateOnActionEnd(
            OnlineBattleActionPayload.CreateOnActionEnd(requestId, resource, exResource, level)));

        Debug.Log($"[OnlineBattle] OnAction end sent. requestId={requestId}");
    }

    private void HandleRemoteOnActionBegin(string payload)
    {
        if (!OnlineBattleActionPayload.TryParse(payload, out OnlineBattleActionPayload action)
            || action.action != OnlineBattleActionPayload.OnActionBegin)
        {
            Debug.LogWarning($"[OnlineBattle] Invalid OnActionBegin payload: {payload}");
            return;
        }

        if (action.actingZoneSide != (int)PlayerType.Enemy)
        {
            Debug.Log("[OnlineBattle] Ignored OnActionBegin for non-Enemy zone (attacker acts locally).");
            return;
        }

        _onlineOnActionResponseRequestId = action.requestId;

        CardController attackingUnit = null;
        if (action.attackerInstanceId > 0)
        {
            attackingUnit = FindBattleZoneUnitByInstanceId(action.attackerInstanceId, PlayerType.Enemy);
        }

        string context = string.IsNullOrWhiteSpace(action.onActionContext)
            ? "attack:remote-enemy-action"
            : action.onActionContext;

        System.Action completeAndNotify = () =>
        {
            NotifyLocalOnActionPhaseComplete(action.requestId);
            _onlineOnActionResponseRequestId = 0;
        };

        bool opened = TryOpenOnActionCommandSelection(
            PlayerType.Player,
            context,
            completeAndNotify,
            attackingUnit);

        if (!opened)
        {
            Debug.Log("[OnlineBattle] OnActionBegin: no eligible cards — auto pass.");
            completeAndNotify.Invoke();
        }
    }

    private void HandleRemoteOnActionEnd(string payload)
    {
        if (!OnlineBattleActionPayload.TryParse(payload, out OnlineBattleActionPayload action)
            || action.action != OnlineBattleActionPayload.OnActionEnd)
        {
            Debug.LogWarning($"[OnlineBattle] Invalid OnActionEnd payload: {payload}");
            return;
        }

        if (action.requestId != _pendingOnlineOnActionRequestId || _pendingOnlineOnActionCallback == null)
        {
            Debug.Log($"[OnlineBattle] Ignored OnActionEnd requestId={action.requestId}");
            return;
        }

        ApplyRemoteOnActionResourceSnapshot(
            Gundam2024RuleScript.PlayerSide.Enemy,
            action.resourceAfter,
            action.exResourceAfter,
            action.levelAfter);

        System.Action callback = _pendingOnlineOnActionCallback;
        _pendingOnlineOnActionCallback = null;
        _pendingOnlineOnActionRequestId = 0;
        callback.Invoke();
        Debug.Log($"[OnlineBattle] OnAction wait completed. requestId={action.requestId}");
    }

    private void ApplyRemoteOnActionResourceSnapshot(
        Gundam2024RuleScript.PlayerSide ruleSide,
        int resource,
        int exResource,
        int level)
    {
        if (gundamRule == null)
        {
            return;
        }

        Gundam2024RuleScript.PlayerState state = ruleSide == Gundam2024RuleScript.PlayerSide.Player
            ? gundamRule.Player
            : gundamRule.Enemy;
        if (state == null)
        {
            return;
        }

        state.resource = Mathf.Max(0, resource);
        state.exResource = Mathf.Max(0, exResource);
        state.level = Mathf.Max(0, level);
        state.resource = Mathf.Min(state.resource, state.TotalLevel);
        SyncResourceViewsFromRule(ruleSide);
    }
}
