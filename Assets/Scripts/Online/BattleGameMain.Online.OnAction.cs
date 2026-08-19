using System.Collections;
using System.Collections.Generic;
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
    private string _onlineOnActionActiveContext;
    private const float OnlineOnActionOpponentWaitTimeoutSeconds = 30f;
    private Coroutine _onlineOnActionOpponentWaitTimeoutCoroutine;
    private PlayerType _onlineOnActionWaitLocalActingSide;
    private string _pendingOnlineOnActionBeginJson;
    private int _onlineActiveActionStepSessionId;
    private bool _onlineActionStepPlayerEnded;
    private bool _onlineActionStepEnemyEnded;
    private bool _onlineActionStepIsTurnEnd;
    private readonly HashSet<CardController> _onlineActionStepUsedCards = new HashSet<CardController>();
    private void ResetOnlineOnActionState()
    {
        _pendingOnlineOnActionRequestId = 0;
        _pendingOnlineOnActionCallback = null;
        _onlineOnActionResponseRequestId = 0;
        _onlineOnActionOpponentWaitRequestId = 0;
        _onlineOnActionActiveContext = null;
        _actionStepSession = null;
        ResetOnlineActionStepEndedTracking();
        CloseAllOnlineActionStepUi();
        _pendingOnlineOnActionBeginJson = null;
        ResetOnlineOnActionCommandRevealState();
        EndActionStepCommandResolve();
        CloseResourcePaymentOverlay(_activeResourcePaymentOverlay);
    }
    private void ResetOnlineActionStepEndedTracking()
    {
        _onlineActionStepPlayerEnded = false;
        _onlineActionStepEnemyEnded = false;
        _onlineActionStepUsedCards.Clear();
        _onlineActionStepIsTurnEnd = false;
    }
    private void MarkEndedOnLocalView(PlayerType side)
    {
        if (IsActionStepSessionActive)
        {
            _actionStepSession.MarkEnded(side);
            return;
        }
        if (side == PlayerType.Player)
        {
            _onlineActionStepPlayerEnded = true;
        }
        else
        {
            _onlineActionStepEnemyEnded = true;
        }
    }
    private bool IsOnlineActionStepSideEnded(PlayerType side)
    {
        if (IsActionStepSessionActive)
        {
            return _actionStepSession.IsEnded(side);
        }
        return side == PlayerType.Player
            ? _onlineActionStepPlayerEnded
            : _onlineActionStepEnemyEnded;
    }
    private bool IsOnlineActionStepBothEnded()
    {
        if (IsActionStepSessionActive)
        {
            return _actionStepSession.BothEnded;
        }
        return _onlineActionStepPlayerEnded && _onlineActionStepEnemyEnded;
    }
    private void ApplyLocalActionStepPass(PlayerType side, ActionStepPassKind passKind)
    {
        if (passKind != ActionStepPassKind.ActionEnd)
        {
            return;
        }
        MarkEndedOnLocalView(side);
    }
    private void GetLocalActionStepEndedFlagsForSend(out int playerEnded, out int enemyEnded)
    {
        if (IsActionStepSessionActive)
        {
            playerEnded = _actionStepSession.PlayerEnded ? 1 : 0;
            enemyEnded = _actionStepSession.EnemyEnded ? 1 : 0;
            return;
        }
        playerEnded = _onlineActionStepPlayerEnded ? 1 : 0;
        enemyEnded = _onlineActionStepEnemyEnded ? 1 : 0;
    }
    private void ApplyMirroredRemoteActionStepEndedFlags(int senderPlayerEnded, int senderEnemyEnded)
    {
        if (senderPlayerEnded > 0)
        {
            MarkEndedOnLocalView(PlayerType.Enemy);
        }
        if (senderEnemyEnded > 0)
        {
            MarkEndedOnLocalView(PlayerType.Player);
        }
    }
    private void CloseAllOnlineActionStepUi()
    {
        CloseActionStepPopupState();
        ClearOnlineOnActionOpponentWaitState();
        _pendingOnlineOnActionRequestId = 0;
        _pendingOnlineOnActionCallback = null;
        _onlineOnActionResponseRequestId = 0;
    }
    private int GetActiveActionStepSessionIdForSend()
    {
        return IsActionStepSessionActive ? _actionStepSession.SessionId : 0;
    }
    private void SendOnlineActionStepResolution(int requestId, PlayerType actingSide, ActionStepPassKind passKind)
    {
        if (!IsOnlineBattle() || requestId <= 0)
        {
            return;
        }
        ApplyLocalActionStepPass(actingSide, passKind);
        GetLocalActionStepEndedFlagsForSend(out int playerEnded, out int enemyEnded);
        Gundam2024RuleScript.PlayerState localPlayer = gundamRule?.Player;
        int resource = localPlayer != null ? localPlayer.resource : 0;
        int exResource = localPlayer != null ? localPlayer.exResource : 0;
        int level = localPlayer != null ? localPlayer.level : 0;
        int actingZoneSide = actingSide == PlayerType.Player
            ? (int)PlayerType.Player
            : (int)PlayerType.Enemy;
        int turnEndCommit = 0;
        if (passKind == ActionStepPassKind.ActionEnd
            && playerEnded > 0
            && enemyEnded > 0
            && (_onlineActionStepIsTurnEnd
                || (IsActionStepSessionActive && !_actionStepSession.IsAttackContext)))
        {
            turnEndCommit = 1;
        }

        bool sent = SendOnlineBattleMessage(EosOnlineBattleMessage.CreateOnActionEnd(
            OnlineBattleActionPayload.CreateOnActionEnd(
                requestId,
                actingZoneSide,
                (int)passKind,
                playerEnded,
                enemyEnded,
                resource,
                exResource,
                level,
                turnEndCommit)));
        if (!sent)
        {
            Debug.LogError(
                $"[OnlineBattle] OnActionEnd send FAILED requestId={requestId}. "
                + "Opponent may remain on Waiting for opponent action step.");
        }
        CloseAllOnlineActionStepUi();
        Debug.Log(
            $"[OnlineBattle] OnAction end sent. requestId={requestId} pass:{passKind} zone:{actingZoneSide} "
            + $"ended(player:{playerEnded} enemy:{enemyEnded}) turnEndCommit:{turnEndCommit} sent:{sent}");
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
        if (onStepDone == null && !IsActionStepSessionActive)
        {
            return;
        }
        if (side == PlayerType.Enemy)
        {
            if (currentPlayerType != PlayerType.Player)
            {
                if (IsActionStepSessionActive)
                {
                    AdvanceActionStepSession(PlayerType.Enemy, ActionStepPassKind.Pass);
                }
                else
                {
                    onStepDone?.Invoke();
                }
                return;
            }
            if (!TryBeginOnlineOnActionWaitForRemoteZone(
                    PlayerType.Enemy,
                    context,
                    onStepDone,
                    attackingUnitInAttackFlow))
            {
                if (IsActionStepSessionActive)
                {
                    AdvanceActionStepSession(PlayerType.Enemy, ActionStepPassKind.ActionEnd);
                }
                else
                {
                    onStepDone?.Invoke();
                }
            }
            return;
        }
        if (currentPlayerType != PlayerType.Player)
        {
            if (IsActionStepSessionActive)
            {
                AdvanceActionStepSession(PlayerType.Player, ActionStepPassKind.Pass);
            }
            else
            {
                onStepDone?.Invoke();
            }
            return;
        }
        int requestId = ++_onlineOnActionRequestIdCounter;
        _pendingOnlineOnActionRequestId = requestId;
        int attackerInstanceId = attackingUnitInAttackFlow != null ? attackingUnitInAttackFlow.BattleInstanceId : 0;
        string beginMessage = EosOnlineBattleMessage.CreateOnActionBegin(
            OnlineBattleActionPayload.CreateOnActionBegin(
                requestId,
                (int)PlayerType.Player,
                context,
                attackerInstanceId,
                GetActiveActionStepSessionIdForSend()));
        _pendingOnlineOnActionBeginJson = beginMessage;
        SendOnlineBattleMessage(beginMessage);
        System.Action complete = () =>
        {
            if (!IsActionStepSessionActive)
            {
                SendOnlineActionStepResolution(requestId, PlayerType.Player, ActionStepPassKind.Pass);
                onStepDone?.Invoke();
            }
        };
        if (!TryOpenOnActionCommandSelection(
                PlayerType.Player,
                context,
                complete,
                attackingUnitInAttackFlow))
        {
            Debug.Log("[OnlineBattle] Local Player OnAction UI could not open — auto pass.");
            if (IsActionStepSessionActive)
            {
                AdvanceActionStepSession(PlayerType.Player, ActionStepPassKind.ActionEnd);
            }
            else
            {
                SendOnlineActionStepResolution(requestId, PlayerType.Player, ActionStepPassKind.ActionEnd);
                onStepDone?.Invoke();
            }
        }
    }
    private bool TryBeginOnlineOnActionWaitForRemoteZone(
        PlayerType actingZoneSideOnAttackerClient,
        string context,
        System.Action onComplete,
        CardController attackingUnitInAttackFlow)
    {
        if (!IsOnlineBattle() || (onComplete == null && !IsActionStepSessionActive))
        {
            return false;
        }
        int requestId = ++_onlineOnActionRequestIdCounter;
        _pendingOnlineOnActionRequestId = requestId;
        _pendingOnlineOnActionCallback = onComplete;
        int attackerInstanceId = attackingUnitInAttackFlow != null ? attackingUnitInAttackFlow.BattleInstanceId : 0;
        RememberTurnEndActionStepFromContext(context);
        string beginMessage = EosOnlineBattleMessage.CreateOnActionBegin(
            OnlineBattleActionPayload.CreateOnActionBegin(
                requestId,
                (int)actingZoneSideOnAttackerClient,
                context,
                attackerInstanceId,
                GetActiveActionStepSessionIdForSend()));
        _pendingOnlineOnActionBeginJson = beginMessage;
        bool sent = SendOnlineBattleMessage(beginMessage);
        if (!sent)
        {
            Debug.LogError(
                $"[OnlineBattle] OnActionBegin send FAILED requestId={requestId} zone={actingZoneSideOnAttackerClient}. "
                + "Skip wait to avoid deadlock.");
            _pendingOnlineOnActionRequestId = 0;
            _pendingOnlineOnActionCallback = null;
            if (IsActionStepSessionActive)
            {
                AdvanceActionStepSession(actingZoneSideOnAttackerClient, ActionStepPassKind.ActionEnd);
            }
            else
            {
                onComplete?.Invoke();
            }
            return true;
        }
        BeginOnlineOnActionOpponentWait(
            requestId,
            actingZoneSideOnAttackerClient,
            "Action Step",
            "Waiting for opponent action step…");
        Debug.Log($"[OnlineBattle] OnAction wait started. requestId={requestId} zone={actingZoneSideOnAttackerClient} context={context}");
        return true;
    }
    private void HandleRemoteOnActionBegin(string payload)
    {
        if (!OnlineBattleActionPayload.TryParse(payload, out OnlineBattleActionPayload action)
            || action.action != OnlineBattleActionPayload.OnActionBegin)
        {
            Debug.LogWarning($"[OnlineBattle] Invalid OnActionBegin payload: {payload}");
            return;
        }
        if (action.actionStepSessionId > 0 && action.actionStepSessionId != _onlineActiveActionStepSessionId)
        {
            _onlineActiveActionStepSessionId = action.actionStepSessionId;
            ResetOnlineActionStepEndedTracking();
            Debug.Log($"[OnlineBattle] Action step session sync id:{action.actionStepSessionId}");
        }

        if (_isActionStepCommandResolving || _activeResourcePaymentOverlay != null)
        {
            if (action.actingZoneSide == (int)PlayerType.Enemy)
            {
                _onlineOnActionResponseRequestId = action.requestId;
            }

            Debug.Log(
                $"[OnlineBattle] OnActionBegin deferred while paying/resolving. requestId:{action.requestId}");
            return;
        }

        RememberTurnEndActionStepFromContext(action.onActionContext);
        if (action.actingZoneSide == (int)PlayerType.Player)
        {
            _pendingOnlineOnActionBeginJson = null;
            BeginOnlineOnActionOpponentWait(
                action.requestId,
                MirrorOnlineActingZoneToLocalPlayerType(action.actingZoneSide),
                "Action Step",
                "Waiting for opponent action step…");
            return;
        }
        if (action.actingZoneSide != (int)PlayerType.Enemy)
        {
            Debug.LogWarning($"[OnlineBattle] Ignored OnActionBegin for unknown zone:{action.actingZoneSide}");
            return;
        }
        if (action.requestId == _onlineOnActionResponseRequestId
            && (isOnActionPopupOpen
                || _isActionStepCommandResolving
                || _activeResourcePaymentOverlay != null))
        {
            Debug.Log($"[OnlineBattle] Duplicate OnActionBegin ignored. requestId:{action.requestId}");
            return;
        }
        if (IsOnlineActionStepSideEnded(PlayerType.Player))
        {
            Debug.Log("[OnlineBattle] Local player already ActionEnded — auto reply without UI.");
            SendOnlineActionStepResolution(action.requestId, PlayerType.Player, ActionStepPassKind.ActionEnd);
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
            _onlineOnActionResponseRequestId = 0;
        };
        bool opened;
        try
        {
            opened = TryOpenOnActionCommandSelection(
                PlayerType.Player,
                context,
                completeAndNotify,
                attackingUnit);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[OnlineBattle] OnActionBegin UI open threw: {ex}");
            opened = false;
        }
        if (!opened)
        {
            Debug.Log("[OnlineBattle] OnActionBegin: UI could not open — auto pass.");
            SendOnlineActionStepResolution(action.requestId, PlayerType.Player, ActionStepPassKind.ActionEnd);
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
        ActionStepPassKind passKind = action.actionStepPassKind == (int)ActionStepPassKind.ActionEnd
            ? ActionStepPassKind.ActionEnd
            : ActionStepPassKind.Pass;
        bool matchesPending = action.requestId == _onlineOnActionOpponentWaitRequestId
            || action.requestId == _pendingOnlineOnActionRequestId
            || action.requestId == _onlineOnActionResponseRequestId;
        System.Action pendingCallback = matchesPending ? _pendingOnlineOnActionCallback : null;
        CloseAllOnlineActionStepUi();
        ApplyRemoteOnActionResourceSnapshot(
            Gundam2024RuleScript.PlayerSide.Enemy,
            action.resourceAfter,
            action.exResourceAfter,
            action.levelAfter);
        ApplyMirroredRemoteActionStepEndedFlags(
            action.sessionPlayerActionEnded,
            action.sessionEnemyActionEnded);
        PlayerType actingSide = MirrorOnlineActingZoneToLocalPlayerType(action.actingZoneSide);
        if (action.turnEndCommit > 0)
        {
            _onlineActionStepIsTurnEnd = true;
            MarkEndedOnLocalView(PlayerType.Player);
            MarkEndedOnLocalView(PlayerType.Enemy);
        }
        else if (passKind == ActionStepPassKind.ActionEnd
            && action.sessionPlayerActionEnded == 0
            && action.sessionEnemyActionEnded == 0)
        {
            MarkEndedOnLocalView(actingSide);
        }
        if (IsActionStepSessionActive)
        {
            AdvanceActionStepSession(actingSide, passKind);
        }
        else if (IsOnlineActionStepBothEnded())
        {
            Debug.Log("[OnlineBattle] Both players ActionEnded on remote — closing action step UI.");
        }

        pendingCallback?.Invoke();
        Debug.Log(
            $"[OnlineBattle] OnAction end received. requestId={action.requestId} pass:{passKind} "
            + $"zone:{action.actingZoneSide} bothEnded:{IsOnlineActionStepBothEnded()} "
            + $"turnEndCommit:{action.turnEndCommit}");
        TryAdvanceTurnAfterOnlineTurnEndActionStep();
    }

    private void RememberTurnEndActionStepFromContext(string context)
    {
        if (string.IsNullOrWhiteSpace(context))
        {
            return;
        }

        if (context.IndexOf("turn end", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            _onlineActionStepIsTurnEnd = true;
        }
        else if (context.IndexOf("attack", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            _onlineActionStepIsTurnEnd = false;
        }
    }

    /// <summary>
    /// ターン終了アクションステップが両者 ActionEnd したら、別の EndTurn を待たず次ターンを始める。
    /// EndTurn パケット欠落で相手だけ止まるのを防ぐ。
    /// </summary>
    private void TryAdvanceTurnAfterOnlineTurnEndActionStep()
    {
        if (!IsOnlineBattle() || !_onlineActionStepIsTurnEnd || !IsOnlineActionStepBothEnded())
        {
            return;
        }

        if (isEndTurnFlowRunning || isMatchFinished || IsLocalTurnAlreadyPlayable())
        {
            return;
        }

        // ターンを終えた側（ローカルが Player）で次ターン開始すると End 不能になる。
        if (currentPlayerType != PlayerType.Enemy)
        {
            return;
        }

        Debug.Log("[OnlineBattle] Turn-end action step both ended — start local turn without waiting for EndTurn packet.");
        StartCoroutine(ApplyRemoteOpponentEndedTurnCoroutine());
    }
    private void BeginOnlineOnActionOpponentWait(
        int requestId,
        PlayerType localActingSide,
        string label,
        string subtitle)
    {
        StopOnlineOnActionOpponentWaitTimeout();
        _onlineOnActionOpponentWaitRequestId = requestId;
        _onlineOnActionWaitLocalActingSide = localActingSide;
        ShowOnlineOnActionOpponentWaitOverlay(label, subtitle);
        _onlineOnActionOpponentWaitTimeoutCoroutine = StartCoroutine(
            OnlineOnActionOpponentWaitTimeoutCoroutine(requestId));
        Debug.Log(
            $"[OnlineBattle] Opponent OnAction wait started. requestId={requestId} localActing={localActingSide} "
            + $"timeout={OnlineOnActionOpponentWaitTimeoutSeconds}s");
    }

    private void ClearOnlineOnActionOpponentWaitState()
    {
        StopOnlineOnActionOpponentWaitTimeout();
        _onlineOnActionOpponentWaitRequestId = 0;
        CloseOnlineOnActionOpponentWaitOverlay();
        _pendingOnlineOnActionBeginJson = null;
    }

    private void StopOnlineOnActionOpponentWaitTimeout()
    {
        if (_onlineOnActionOpponentWaitTimeoutCoroutine == null)
        {
            return;
        }

        StopCoroutine(_onlineOnActionOpponentWaitTimeoutCoroutine);
        _onlineOnActionOpponentWaitTimeoutCoroutine = null;
    }

    private IEnumerator OnlineOnActionOpponentWaitTimeoutCoroutine(int requestId)
    {
        const float retryIntervalSeconds = 1.25f;
        float deadline = Time.realtimeSinceStartup + OnlineOnActionOpponentWaitTimeoutSeconds;
        while (_onlineOnActionOpponentWaitRequestId == requestId
            && IsOnlineBattle()
            && !isMatchFinished
            && Time.realtimeSinceStartup < deadline)
        {
            yield return new WaitForSecondsRealtime(retryIntervalSeconds);
            if (_onlineOnActionOpponentWaitRequestId != requestId || !IsOnlineBattle() || isMatchFinished)
            {
                yield break;
            }

            if (!string.IsNullOrEmpty(_pendingOnlineOnActionBeginJson))
            {
                bool resent = SendOnlineBattleMessage(_pendingOnlineOnActionBeginJson);
                Debug.Log($"[OnlineBattle] OnActionBegin resent. requestId={requestId} sent:{resent}");
            }
        }

        _onlineOnActionOpponentWaitTimeoutCoroutine = null;
        if (_onlineOnActionOpponentWaitRequestId != requestId
            || !IsOnlineBattle()
            || isMatchFinished)
        {
            yield break;
        }

        ForceTimeoutOnlineOnActionOpponentWait(requestId);
    }

    /// <summary>
    /// 相手のアクションステップ応答が一定時間来ないとき、相手を ActionEnd 扱いしてローカルを強制進行する。
    /// </summary>
    private void ForceTimeoutOnlineOnActionOpponentWait(int requestId)
    {
        if (_onlineOnActionOpponentWaitRequestId != requestId)
        {
            return;
        }

        PlayerType actingSide = _onlineOnActionWaitLocalActingSide;
        System.Action pendingCallback = _pendingOnlineOnActionCallback;

        Debug.LogWarning(
            $"[OnlineBattle] OnAction opponent wait timed out after {OnlineOnActionOpponentWaitTimeoutSeconds}s "
            + $"requestId={requestId} localActing={actingSide} — force ActionEnd and continue.");

        // Clear で requestId を消す前に Advance するため、先に待ち UI だけ閉じる。
        StopOnlineOnActionOpponentWaitTimeout();
        CloseOnlineOnActionOpponentWaitOverlay();
        _onlineOnActionOpponentWaitRequestId = 0;
        _pendingOnlineOnActionRequestId = 0;
        _pendingOnlineOnActionCallback = null;
        _pendingOnlineOnActionBeginJson = null;

        if (IsActionStepSessionActive)
        {
            AdvanceActionStepSession(actingSide, ActionStepPassKind.ActionEnd);
            return;
        }

        MarkEndedOnLocalView(actingSide);
        pendingCallback?.Invoke();
        if (_onlineActionStepIsTurnEnd)
        {
            MarkEndedOnLocalView(PlayerType.Player);
            MarkEndedOnLocalView(PlayerType.Enemy);
            TryAdvanceTurnAfterOnlineTurnEndActionStep();
        }
    }

    /// <summary>ターン終了アクションステップ全体が終わらないときの強制完了。</summary>
    private void ForceCompleteActionStepSessionForTimeout(string reason)
    {
        Debug.LogWarning($"[OnlineBattle] Force complete action step session: {reason}");
        CloseAllOnlineActionStepUi();
        if (!IsActionStepSessionActive)
        {
            return;
        }

        CompleteActionStepSession();
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
        if (gundamRule != null)
        {
            state.exResource = Mathf.Min(gundamRule.MaxExResource, state.exResource);
        }
        state.level = Mathf.Max(0, level);
        state.resource = Mathf.Min(state.resource, state.TotalLevel);
        SyncResourceViewsFromRule(ruleSide);
    }
}
