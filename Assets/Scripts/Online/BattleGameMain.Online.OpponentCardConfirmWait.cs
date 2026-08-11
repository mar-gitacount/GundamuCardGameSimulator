using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// オンライン：自分がカードを出したあと、相手が確認 OK するまでの待機オーバーレイ。
/// </summary>
public partial class BattleGameMain
{
    private GameObject _activeOnlineOpponentCardConfirmWaitRoot;
    private bool isOnlineOpponentCardConfirmWaitOpen;

    private void ShowOnlineOpponentCardConfirmWaitOverlay()
    {
        if (_activeOnlineOpponentCardConfirmWaitRoot != null)
        {
            isOnlineOpponentCardConfirmWaitOpen = true;
            return;
        }

        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            isOnlineOpponentCardConfirmWaitOpen = true;
            return;
        }

        isOnlineOpponentCardConfirmWaitOpen = true;
        GameObject root = new GameObject(
            "OnlineOpponentCardConfirmWait",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        _activeOnlineOpponentCardConfirmWaitRoot = root;
        root.transform.SetParent(canvas.transform, false);
        root.transform.SetAsLastSibling();
        root.SetFullSize();

        Image dim = root.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.55f);
        dim.raycastTarget = true;

        TextMeshProUGUI title = root.CreateChildTextCustom(
            "CardConfirmWaitTitle",
            UIAnchor.TopCenter,
            760,
            56);
        title.SetLocalizedText("カード確認待ち", "Waiting for confirmation");
        title.color = new Color(1f, 0.95f, 0.2f, 1f);
        title.fontSize = 26;
        title.alignment = TextAlignmentOptions.Center;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -100f);

        TextMeshProUGUI sub = root.CreateChildTextCustom(
            "CardConfirmWaitSub",
            UIAnchor.TopCenter,
            760,
            48);
        sub.SetLocalizedText(
            "対戦相手が発動カードを確認中…",
            "Opponent is reviewing the activated card...");
        sub.color = Color.white;
        sub.fontSize = 18;
        sub.alignment = TextAlignmentOptions.Center;
        sub.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -150f);
    }

    private void CloseOnlineOpponentCardConfirmWaitOverlay()
    {
        isOnlineOpponentCardConfirmWaitOpen = false;
        if (_activeOnlineOpponentCardConfirmWaitRoot != null)
        {
            Destroy(_activeOnlineOpponentCardConfirmWaitRoot);
            _activeOnlineOpponentCardConfirmWaitRoot = null;
        }
    }

    /// <summary>
    /// requestId 付き公開の Complete を待つ（操作ブロック UI 付き）。
    /// CommandPlayRevealComplete を共用する。
    /// </summary>
    private IEnumerator WaitForOpponentCardConfirmCompleteWithOverlayCoroutine(int requestId)
    {
        if (requestId <= 0)
        {
            yield break;
        }

        ShowOnlineOpponentCardConfirmWaitOverlay();
        yield return new WaitUntil(() =>
            _commandPlayRevealRemoteCompleteReceived
            || _pendingCommandPlayRevealRequestId != requestId
            || !IsOnlineBattle());
        CloseOnlineOpponentCardConfirmWaitOverlay();
    }

    private void SendOpponentCardConfirmComplete(int requestId)
    {
        if (!IsOnlineBattle() || requestId <= 0)
        {
            return;
        }

        SendOnlineBattleMessage(EosOnlineBattleMessage.CreateCommandPlayRevealComplete(
            OnlineBattleActionPayload.CreateCommandPlayRevealComplete(requestId)));
        Debug.Log($"[OpponentCardConfirm][CompleteSent] requestId:{requestId}");
    }

    private int BeginPendingOpponentCardConfirmRequest()
    {
        int requestId = ++_commandPlayRevealRequestIdCounter;
        _pendingCommandPlayRevealRequestId = requestId;
        _commandPlayRevealRemoteCompleteReceived = false;
        return requestId;
    }

    private void ClearPendingOpponentCardConfirmRequest()
    {
        _pendingCommandPlayRevealRequestId = 0;
    }
}
