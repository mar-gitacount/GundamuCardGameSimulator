using System.Collections;
using UnityEngine;

/// <summary>手札捨て公開のオンライン同期（OK まで効果チェーン停止）。</summary>
public partial class BattleGameMain
{
    private int _handDiscardRevealRequestIdCounter;
    private int _pendingHandDiscardRevealRequestId;
    private bool _handDiscardRevealRemoteCompleteReceived;

    private void ResetOnlineHandDiscardRevealState()
    {
        _handDiscardRevealRequestIdCounter = 0;
        _pendingHandDiscardRevealRequestId = 0;
        _handDiscardRevealRemoteCompleteReceived = false;
        CloseHandDiscardRevealPanelIfAny();
    }

    private IEnumerator WaitForHandDiscardRevealAcknowledgedCoroutine(
        int cardId,
        string cardName,
        PlayerType handOwner,
        PlayerType effectOwner,
        bool isInitiator)
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
        yield return ShowHandDiscardRevealPanelCoroutine(cardId, cardName, isOpponentView);

        if (IsOnlineBattle() && isInitiator && !_applyingRemoteBattleAction && requestId > 0)
        {
            yield return new WaitUntil(() => _handDiscardRevealRemoteCompleteReceived);
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
        yield return ShowHandDiscardRevealPanelCoroutine(cardId, cardName, isOpponentView: true);
        SendOnlineHandDiscardRevealComplete(requestId);
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
