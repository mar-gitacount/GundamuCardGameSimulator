using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>手札捨て公開のオンライン同期（OK まで効果チェーン停止）と discardthink 待機。</summary>
public partial class BattleGameMain
{
    private int _handDiscardRevealRequestIdCounter;
    private int _pendingHandDiscardRevealRequestId;
    private bool _handDiscardRevealRemoteCompleteReceived;

    private int _discardThinkRequestIdCounter;
    private int _activeLocalDiscardThinkRequestId;
    private int _pendingRemoteDiscardThinkRequestId;
    private GameObject _activeOnlineDiscardThinkRoot;
    private bool isOnlineDiscardThinkPauseOpen;

    private void ResetOnlineHandDiscardRevealState()
    {
        _handDiscardRevealRequestIdCounter = 0;
        _pendingHandDiscardRevealRequestId = 0;
        _handDiscardRevealRemoteCompleteReceived = false;
        CloseHandDiscardRevealPanelIfAny();
        CloseOnlineOpponentCardConfirmWaitOverlay();
        EndOnlineDiscardThinkForLocalHandSelect();
        CloseOnlineDiscardThinkOverlay();
        _discardThinkRequestIdCounter = 0;
        _pendingRemoteDiscardThinkRequestId = 0;
    }

    /// <summary>
    /// 自分が手札捨て選択中であることを相手に伝え、discardthink で止めさせる。
    /// </summary>
    private void BeginOnlineDiscardThinkForLocalHandSelect()
    {
        if (!IsOnlineBattle() || _applyingRemoteBattleAction)
        {
            return;
        }

        if (_activeLocalDiscardThinkRequestId > 0)
        {
            return;
        }

        int requestId = ++_discardThinkRequestIdCounter;
        _activeLocalDiscardThinkRequestId = requestId;
        SendOnlineBattleMessage(EosOnlineBattleMessage.CreateDiscardThinkWait(
            OnlineBattleActionPayload.CreateDiscardThinkWait(requestId)));
        Debug.Log($"[OnlineBattle] DiscardThinkWait sent. requestId={requestId}");
    }

    /// <summary>
    /// 公開などで相手の discardthink が閉じたあと、続きの手札選択前に再送する。
    /// </summary>
    private void RestartOnlineDiscardThinkForLocalHandSelect()
    {
        if (!IsOnlineBattle() || _applyingRemoteBattleAction)
        {
            return;
        }

        EndOnlineDiscardThinkForLocalHandSelect();
        BeginOnlineDiscardThinkForLocalHandSelect();
    }

    /// <summary>手札捨て選択終了を相手へ通知し、discardthink を閉じさせる。</summary>
    private void EndOnlineDiscardThinkForLocalHandSelect()
    {
        int requestId = _activeLocalDiscardThinkRequestId;
        if (requestId <= 0)
        {
            return;
        }

        _activeLocalDiscardThinkRequestId = 0;
        if (!IsOnlineBattle())
        {
            return;
        }

        SendOnlineBattleMessage(EosOnlineBattleMessage.CreateDiscardThinkComplete(
            OnlineBattleActionPayload.CreateDiscardThinkComplete(requestId)));
        Debug.Log($"[OnlineBattle] DiscardThinkComplete sent. requestId={requestId}");
    }

    private void HandleRemoteDiscardThinkWait(string payload)
    {
        if (!OnlineBattleActionPayload.TryParse(payload, out OnlineBattleActionPayload action)
            || action.requestId <= 0)
        {
            Debug.LogWarning($"[OnlineBattle] Invalid DiscardThinkWait payload: {payload}");
            return;
        }

        _pendingRemoteDiscardThinkRequestId = action.requestId;
        ShowOnlineDiscardThinkOverlay();
        Debug.Log($"[OnlineBattle] DiscardThinkWait received. requestId={action.requestId}");
    }

    private void HandleRemoteDiscardThinkComplete(string payload)
    {
        if (!OnlineBattleActionPayload.TryParse(payload, out OnlineBattleActionPayload action))
        {
            Debug.LogWarning($"[OnlineBattle] Invalid DiscardThinkComplete payload: {payload}");
            return;
        }

        if (_pendingRemoteDiscardThinkRequestId > 0
            && action.requestId > 0
            && action.requestId != _pendingRemoteDiscardThinkRequestId)
        {
            Debug.Log(
                $"[OnlineBattle] Ignored DiscardThinkComplete requestId={action.requestId} "
                + $"pending={_pendingRemoteDiscardThinkRequestId}");
            return;
        }

        _pendingRemoteDiscardThinkRequestId = 0;
        CloseOnlineDiscardThinkOverlay();
        Debug.Log($"[OnlineBattle] DiscardThinkComplete received. requestId={action.requestId}");
    }

    private void ShowOnlineDiscardThinkOverlay()
    {
        if (_activeOnlineDiscardThinkRoot != null)
        {
            isOnlineDiscardThinkPauseOpen = true;
            return;
        }

        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            return;
        }

        isOnlineDiscardThinkPauseOpen = true;
        GameObject root = new GameObject(
            "OnlineDiscardThinkPause",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        _activeOnlineDiscardThinkRoot = root;
        root.transform.SetParent(canvas.transform, false);
        root.transform.SetAsLastSibling();
        root.SetFullSize();

        Image dim = root.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.55f);
        dim.raycastTarget = true;

        TextMeshProUGUI title = root.CreateChildTextCustom(
            "DiscardThinkTitle",
            UIAnchor.TopCenter,
            720,
            56);
        title.text = "discardthink";
        title.color = new Color(1f, 0.95f, 0.2f, 1f);
        title.fontSize = 26;
        title.alignment = TextAlignmentOptions.Center;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -100f);

        TextMeshProUGUI sub = root.CreateChildTextCustom(
            "DiscardThinkSub",
            UIAnchor.TopCenter,
            720,
            44);
        sub.SetLocalizedText(
            "相手が手札を捨てるカードを選んでいます…",
            "Opponent is choosing cards to discard from hand...");
        sub.color = Color.white;
        sub.fontSize = 18;
        sub.alignment = TextAlignmentOptions.Center;
        sub.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -150f);
    }

    private void CloseOnlineDiscardThinkOverlay()
    {
        isOnlineDiscardThinkPauseOpen = false;
        if (_activeOnlineDiscardThinkRoot != null)
        {
            Destroy(_activeOnlineDiscardThinkRoot);
            _activeOnlineDiscardThinkRoot = null;
        }
    }

    private IEnumerator WaitForHandDiscardRevealAcknowledgedCoroutine(
        int cardId,
        string cardName,
        PlayerType handOwner,
        PlayerType effectOwner,
        bool isInitiator,
        string revealTitle = null)
    {
        int requestId = 0;
        if (IsOnlineBattle() && isInitiator && !_applyingRemoteBattleAction)
        {
            requestId = ++_handDiscardRevealRequestIdCounter;
            _pendingHandDiscardRevealRequestId = requestId;
            _handDiscardRevealRemoteCompleteReceived = false;
            SendOnlineBattleMessage(EosOnlineBattleMessage.CreateHandDiscardReveal(
                OnlineBattleActionPayload.CreateHandDiscardReveal(cardId, requestId)));
            Debug.Log($"[OnlineBattle] HandDiscardReveal sent. cardId={cardId} requestId={requestId}");
        }

        bool isOpponentView = handOwner == PlayerType.Enemy;
        yield return ShowHandDiscardRevealPanelCoroutine(cardId, cardName, isOpponentView, revealTitle);

        if (IsOnlineBattle() && isInitiator && !_applyingRemoteBattleAction && requestId > 0)
        {
            // 自分の公開 OK 後は、相手の確認完了まで操作不可
            ShowOnlineOpponentCardConfirmWaitOverlay();
            yield return new WaitUntil(() =>
                _handDiscardRevealRemoteCompleteReceived || !IsOnlineBattle());
            CloseOnlineOpponentCardConfirmWaitOverlay();
            _pendingHandDiscardRevealRequestId = 0;
        }
    }

    private void SendOnlineHandDiscardRevealComplete(int requestId)
    {
        if (!IsOnlineBattle() || requestId <= 0)
        {
            return;
        }

        SendOnlineBattleMessage(EosOnlineBattleMessage.CreateHandDiscardRevealComplete(
            OnlineBattleActionPayload.CreateHandDiscardRevealComplete(requestId)));
        Debug.Log($"[OnlineBattle] HandDiscardRevealComplete sent. requestId={requestId}");
    }

    private void HandleRemoteHandDiscardReveal(string payload)
    {
        if (!OnlineBattleActionPayload.TryParse(payload, out OnlineBattleActionPayload action))
        {
            Debug.LogWarning($"[OnlineBattle] Invalid HandDiscardReveal payload: {payload}");
            return;
        }

        // 公開 UI を出す前に discardthink を閉じる
        CloseOnlineDiscardThinkOverlay();
        _pendingRemoteDiscardThinkRequestId = 0;

        _pendingHandDiscardRevealRequestId = action.requestId;
        _handDiscardRevealRemoteCompleteReceived = false;
        CardData data = DeckSettinObject.Instance != null
            ? DeckSettinObject.Instance.GetCardDataById(action.cardId)
            : null;
        string cardName = data != null ? data.cardName : $"id:{action.cardId}";
        StartCoroutine(HandleRemoteHandDiscardRevealCoroutine(action.cardId, cardName, action.requestId));
    }

    private IEnumerator HandleRemoteHandDiscardRevealCoroutine(int cardId, string cardName, int requestId)
    {
        // effectthink の上に公開 UI を出せるように、いったん think を閉じる（OK 後に完了が来る想定）
        if (isOnlineEffectThinkPauseOpen)
        {
            CloseOnlineEffectThinkOverlay();
        }

        if (isOnlineDiscardThinkPauseOpen)
        {
            CloseOnlineDiscardThinkOverlay();
        }

        yield return ShowHandDiscardRevealPanelCoroutine(cardId, cardName, isOpponentView: true);
        SendOnlineHandDiscardRevealComplete(requestId);

        // 公開 OK 時点でまだ破壊時待機が残っていれば解除（Look 完了通知の取りこぼし対策）
        if (_pendingRemoteOnDestroyedRequestIds.Count > 0)
        {
            Debug.Log(
                $"[OnDestroyed][Online] clear effectthink after reveal OK "
                + $"pending:{_pendingRemoteOnDestroyedRequestIds.Count}");
            _pendingRemoteOnDestroyedRequestIds.Clear();
            CloseOnlineEffectThinkOverlay();
        }
    }

    private void HandleRemoteHandDiscardRevealComplete(string payload)
    {
        if (!OnlineBattleActionPayload.TryParse(payload, out OnlineBattleActionPayload action))
        {
            Debug.LogWarning($"[OnlineBattle] Invalid HandDiscardRevealComplete payload: {payload}");
            return;
        }

        if (action.requestId != _pendingHandDiscardRevealRequestId || _pendingHandDiscardRevealRequestId <= 0)
        {
            Debug.Log(
                $"[OnlineBattle] Ignored HandDiscardRevealComplete requestId={action.requestId} pending={_pendingHandDiscardRevealRequestId}");
            return;
        }

        _handDiscardRevealRemoteCompleteReceived = true;
        Debug.Log($"[OnlineBattle] HandDiscardRevealComplete received. requestId={action.requestId}");
    }
}
