using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// オンライン対戦の OnAction（アクションステップ）同期。
/// 攻撃・ターン終了とも「Enemy ゾーン → Player ゾーン」の順。カードが無くてもステップを差し込む。
/// </summary>
public partial class BattleGameMain
{
    private int _onlineOnActionRequestIdCounter;
    private int _pendingOnlineOnActionRequestId;
    private System.Action _pendingOnlineOnActionCallback;
    private int _onlineOnActionResponseRequestId;
    private int _onlineOnActionOpponentWaitRequestId;
    private bool isOnlineOnActionOpponentWaitOpen;
    private GameObject _activeOnActionOpponentWaitRoot;

    private void ResetOnlineOnActionState()
    {
        _onlineOnActionRequestIdCounter = 0;
        _pendingOnlineOnActionRequestId = 0;
        _pendingOnlineOnActionCallback = null;
        _onlineOnActionResponseRequestId = 0;
        _onlineOnActionOpponentWaitRequestId = 0;
        CloseOnlineOnActionOpponentWaitOverlay();
    }

    private bool ShouldBlockOnlineLocalPlayDueToOnAction()
    {
        return IsOnlineBattle()
            && (isOnlineOnActionOpponentWaitOpen || _pendingOnlineOnActionRequestId > 0);
    }

    private bool TryBlockOnlinePlayDueToOpponentOnAction(string context)
    {
        if (!ShouldBlockOnlineLocalPlayDueToOnAction())
        {
            return false;
        }

        Debug.Log($"[OnlineBattle] Wait for opponent OnAction selection. ({context})");
        return true;
    }

    private void RunOnlineOnActionStepBody(
        PlayerType side,
        string context,
        System.Action onStepDone,
        CardController attackingUnitInAttackFlow)
    {
        if (onStepDone == null)
        {
            return;
        }

        if (side == PlayerType.Enemy)
        {
            if (currentPlayerType != PlayerType.Player)
            {
                onStepDone.Invoke();
                return;
            }

            if (!TryBeginOnlineOnActionWaitForRemoteZone(
                    PlayerType.Enemy,
                    context,
                    onStepDone,
                    attackingUnitInAttackFlow))
            {
                onStepDone.Invoke();
            }

            return;
        }

        if (currentPlayerType != PlayerType.Player)
        {
            onStepDone.Invoke();
            return;
        }

        int requestId = ++_onlineOnActionRequestIdCounter;
        int attackerInstanceId = attackingUnitInAttackFlow != null ? ToSyncInstanceId(attackingUnitInAttackFlow) : 0;
        SendOnlineBattleMessage(EosOnlineBattleMessage.CreateOnActionBegin(
            OnlineBattleActionPayload.CreateOnActionBegin(
                requestId,
                (int)PlayerType.Player,
                context,
                attackerInstanceId)));

        System.Action complete = () =>
        {
            NotifyLocalOnActionPhaseComplete(requestId);
            onStepDone.Invoke();
        };

        if (!TryOpenOnActionCommandSelection(
                PlayerType.Player,
                context,
                complete,
                attackingUnitInAttackFlow))
        {
            Debug.Log("[OnlineBattle] Local Player OnAction UI could not open — auto pass.");
            NotifyLocalOnActionPhaseComplete(requestId);
            onStepDone.Invoke();
        }
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

        int attackerInstanceId = attackingUnitInAttackFlow != null ? ToSyncInstanceId(attackingUnitInAttackFlow) : 0;
        SendOnlineBattleMessage(EosOnlineBattleMessage.CreateOnActionBegin(
            OnlineBattleActionPayload.CreateOnActionBegin(
                requestId,
                (int)actingZoneSideOnAttackerClient,
                context,
                attackerInstanceId)));

        ShowOnlineOnActionOpponentWaitOverlay("Action Step", "Waiting for opponent action step…");
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

        if (action.actingZoneSide == (int)PlayerType.Player)
        {
            BeginOnlineOnActionOpponentWait(
                action.requestId,
                "Action Step",
                "Waiting for opponent action step…");
            return;
        }

        if (action.actingZoneSide != (int)PlayerType.Enemy)
        {
            Debug.LogWarning($"[OnlineBattle] Ignored OnActionBegin for unknown zone:{action.actingZoneSide}");
            return;
        }

        _onlineOnActionResponseRequestId = action.requestId;

        CardController attackingUnit = null;
        if (action.attackerInstanceId > 0)
        {
            attackingUnit = FindOpponentUnitByPeerInstanceId(action.attackerInstanceId);
        }

        string context = string.IsNullOrWhiteSpace(action.onActionContext)
            ? "attack:remote-enemy-action"
            : action.onActionContext;

        System.Action completeAndNotify = () =>
        {
            NotifyLocalOnActionPhaseComplete(action.requestId);
            _onlineOnActionResponseRequestId = 0;
        };

        if (!TryOpenOnActionCommandSelection(
                PlayerType.Player,
                context,
                completeAndNotify,
                attackingUnit))
        {
            Debug.Log("[OnlineBattle] OnActionBegin: UI could not open — auto pass.");
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

        bool handled = false;

        if (action.requestId == _onlineOnActionOpponentWaitRequestId)
        {
            ApplyRemoteOnActionResourceSnapshot(
                Gundam2024RuleScript.PlayerSide.Enemy,
                action.resourceAfter,
                action.exResourceAfter,
                action.levelAfter);
            ClearOnlineOnActionOpponentWaitState();
            handled = true;
        }

        if (action.requestId == _pendingOnlineOnActionRequestId && _pendingOnlineOnActionCallback != null)
        {
            ApplyRemoteOnActionResourceSnapshot(
                Gundam2024RuleScript.PlayerSide.Enemy,
                action.resourceAfter,
                action.exResourceAfter,
                action.levelAfter);

            System.Action callback = _pendingOnlineOnActionCallback;
            _pendingOnlineOnActionCallback = null;
            _pendingOnlineOnActionRequestId = 0;
            ClearOnlineOnActionOpponentWaitState();
            callback.Invoke();
            handled = true;
            Debug.Log($"[OnlineBattle] OnAction wait completed. requestId={action.requestId}");
        }

        if (!handled)
        {
            Debug.Log($"[OnlineBattle] Ignored OnActionEnd requestId={action.requestId}");
        }
    }

    private void BeginOnlineOnActionOpponentWait(int requestId, string label, string subtitle)
    {
        _onlineOnActionOpponentWaitRequestId = requestId;
        ShowOnlineOnActionOpponentWaitOverlay(label, subtitle);
        Debug.Log($"[OnlineBattle] Opponent OnAction wait started. requestId={requestId}");
    }

    private void ClearOnlineOnActionOpponentWaitState()
    {
        _onlineOnActionOpponentWaitRequestId = 0;
        CloseOnlineOnActionOpponentWaitOverlay();
    }

    private void ShowOnlineOnActionOpponentWaitOverlay(string label, string subtitle)
    {
        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            isOnlineOnActionOpponentWaitOpen = true;
            return;
        }

        CloseOnlineOnActionOpponentWaitOverlay();
        isOnlineOnActionOpponentWaitOpen = true;

        GameObject root = new GameObject(
            "OnActionOpponentWait",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        _activeOnActionOpponentWaitRoot = root;
        root.transform.SetParent(canvas.transform, false);
        root.transform.SetAsLastSibling();
        root.SetFullSize();
        Image dim = root.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.5f);
        dim.raycastTarget = true;

        TextMeshProUGUI title = root.CreateChildTextCustom("OnActionOpponentWaitTitle", UIAnchor.TopCenter, 720, 56);
        title.text = label;
        title.color = new Color(1f, 0.95f, 0.2f, 1f);
        title.fontSize = 26;
        title.alignment = TextAlignmentOptions.Center;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -100f);

        TextMeshProUGUI sub = root.CreateChildTextCustom("OnActionOpponentWaitSub", UIAnchor.TopCenter, 720, 40);
        sub.text = subtitle;
        sub.color = Color.white;
        sub.fontSize = 18;
        sub.alignment = TextAlignmentOptions.Center;
        sub.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -150f);
    }

    private void CloseOnlineOnActionOpponentWaitOverlay()
    {
        isOnlineOnActionOpponentWaitOpen = false;
        if (_activeOnActionOpponentWaitRoot != null)
        {
            Destroy(_activeOnActionOpponentWaitRoot);
            _activeOnActionOpponentWaitRoot = null;
        }
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
