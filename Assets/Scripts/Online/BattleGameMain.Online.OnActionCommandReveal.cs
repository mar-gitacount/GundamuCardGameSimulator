using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// オンライン：自分がコマンドを使ったとき相手へカード公開し、相手 OK まで進行を止める。
/// OnMain / OnAction 双方で使う。
/// </summary>
public partial class BattleGameMain
{
    private GameObject _activeOnActionCommandRevealRoot;
    private int _commandPlayRevealRequestIdCounter;
    private int _pendingCommandPlayRevealRequestId;
    private bool _commandPlayRevealRemoteCompleteReceived;

    private void ResetOnlineOnActionCommandRevealState()
    {
        _commandPlayRevealRequestIdCounter = 0;
        _pendingCommandPlayRevealRequestId = 0;
        _commandPlayRevealRemoteCompleteReceived = false;
        CloseOnActionCommandRevealPanelIfAny();
        CloseOnlineOpponentCardConfirmWaitOverlay();
    }

    /// <summary>
    /// 相手へコマンド公開し、オンラインかつ自分が initiator のとき相手 OK まで待つ。
    /// </summary>
    private IEnumerator WaitForOpponentCommandPlayRevealAcknowledgedCoroutine(
        CardController command,
        string context,
        CardController targetUnitOrNull = null)
    {
        if (command == null || command.Data == null || !command.Data.IsCommand())
        {
            yield break;
        }

        if (!IsOnlineBattle() || _applyingRemoteBattleAction)
        {
            yield break;
        }

        int requestId = ++_commandPlayRevealRequestIdCounter;
        _pendingCommandPlayRevealRequestId = requestId;
        _commandPlayRevealRemoteCompleteReceived = false;

        bool includeRes = gundamRule?.Player != null;
        int resourceAfter = includeRes ? gundamRule.Player.resource : 0;
        int exAfter = includeRes ? gundamRule.Player.exResource : 0;
        int levelAfter = includeRes ? gundamRule.Player.level : 0;
        int targetCardId = targetUnitOrNull != null && targetUnitOrNull.Data != null
            ? targetUnitOrNull.Data.id
            : -1;

        string json = OnlineBattleActionPayload.CreateOnActionCommandUsed(
            command.Data.id,
            (int)PlayerType.Player,
            context ?? string.Empty,
            command.CurrentCost,
            command.CurrentLevel,
            targetCardId,
            includeRes,
            resourceAfter,
            exAfter,
            levelAfter,
            requestId);
        SendOnlineBattleMessage(EosOnlineBattleMessage.CreateOnActionCommandUsed(json));
        Debug.Log(
            $"[CommandPlayReveal][Send] card:{command.Data.cardName}(id:{command.Data.id}) "
            + $"context:{context} requestId:{requestId} targetCardId:{targetCardId}");

        LogOnActionCommandUsedBoardSnapshotCompact("localSend", PlayerType.Player, command, targetUnitOrNull);

        yield return WaitForOpponentCardConfirmCompleteWithOverlayCoroutine(requestId);
        ClearPendingOpponentCardConfirmRequest();
        Debug.Log($"[CommandPlayReveal][Ack] requestId:{requestId}");
    }

    private void HandleRemoteOnActionCommandUsed(string payload)
    {
        if (!OnlineBattleActionPayload.TryParse(payload, out OnlineBattleActionPayload action)
            || action.action != OnlineBattleActionPayload.OnActionCommandUsed)
        {
            Debug.LogWarning($"[OnlineBattle] Invalid OnActionCommandUsed payload: {payload}");
            return;
        }

        if (action.includeResourceSnapshot)
        {
            PlayerType senderZone = action.actingZoneSide == (int)PlayerType.Enemy
                ? PlayerType.Enemy
                : PlayerType.Player;
            PlayerType localZone = MirrorOnlineZoneOwner(senderZone);
            ApplyRemoteOnActionResourceSnapshot(
                ToRuleSide(localZone),
                action.resourceAfter,
                action.exResourceAfter,
                action.levelAfter);
        }

        CardData commandData = DeckSettinObject.Instance != null
            ? DeckSettinObject.Instance.GetCardDataById(action.cardId)
            : null;
        string commandName = commandData != null ? commandData.cardName : $"id:{action.cardId}";

        CardController targetUnit = null;
        if (action.targetCardId > 0)
        {
            targetUnit = FindBattleZoneUnitByCardId(action.targetCardId);
        }

        Debug.Log(
            $"[CommandPlayReveal][Receive] card:{commandName}(id:{action.cardId}) "
            + $"context:{action.onActionContext} requestId:{action.requestId}");

        LogOnActionCommandUsedBoardSnapshotCompact(
            "remoteReceive",
            PlayerType.Enemy,
            commandData,
            targetUnit);

        StartCoroutine(HandleRemoteCommandPlayRevealCoroutine(action, commandData, commandName, targetUnit));
    }

    private IEnumerator HandleRemoteCommandPlayRevealCoroutine(
        OnlineBattleActionPayload action,
        CardData commandData,
        string commandName,
        CardController targetUnitOrNull)
    {
        if (isOnlineEffectThinkPauseOpen)
        {
            CloseOnlineEffectThinkOverlay();
        }

        yield return ShowOnActionCommandUsedRevealPanelCoroutine(
            action,
            commandData,
            commandName,
            targetUnitOrNull);

        if (action.requestId > 0)
        {
            SendOpponentCardConfirmComplete(action.requestId);
        }
    }

    private void HandleRemoteCommandPlayRevealComplete(string payload)
    {
        if (!OnlineBattleActionPayload.TryParse(payload, out OnlineBattleActionPayload action)
            || action.action != OnlineBattleActionPayload.CommandPlayRevealComplete)
        {
            Debug.LogWarning($"[OnlineBattle] Invalid CommandPlayRevealComplete payload: {payload}");
            return;
        }

        if (action.requestId != _pendingCommandPlayRevealRequestId || _pendingCommandPlayRevealRequestId <= 0)
        {
            Debug.Log(
                $"[CommandPlayReveal] Ignored Complete requestId={action.requestId} pending={_pendingCommandPlayRevealRequestId}");
            return;
        }

        _commandPlayRevealRemoteCompleteReceived = true;
        Debug.Log($"[CommandPlayReveal][CompleteRecv] requestId:{action.requestId}");
    }

    private void LogOnActionCommandUsedBoardSnapshotCompact(
        string phase,
        PlayerType actingSide,
        CardData commandDataOrNull,
        CardController targetUnitOrNull)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder(512);
        sb.Append("[OnActionCommandUsed][BoardCompact] phase:").Append(phase).Append(" side:").Append(actingSide);
        if (commandDataOrNull != null)
        {
            sb.Append(" cmd:").Append(commandDataOrNull.cardName).Append("(id:").Append(commandDataOrNull.id).Append(')');
        }

        if (targetUnitOrNull != null && targetUnitOrNull.Data != null)
        {
            sb.Append(" target:").Append(targetUnitOrNull.Data.cardName).Append("(id:").Append(targetUnitOrNull.Data.id)
                .Append(") AP:").Append(targetUnitOrNull.CurrentPower).Append(" HP:").Append(targetUnitOrNull.CurrentHp);
        }

        sb.Append(" | Player:");
        AppendBattleZoneUnitApHpInline(sb, playerBattleZoneCards);
        sb.Append(" | Enemy:");
        AppendBattleZoneUnitApHpInline(sb, enemyBattleZoneCards);
        Debug.Log(sb.ToString());
    }

    private void LogOnActionCommandUsedBoardSnapshotCompact(
        string phase,
        PlayerType actingSide,
        CardController command,
        CardController targetUnitOrNull)
    {
        LogOnActionCommandUsedBoardSnapshotCompact(
            phase,
            actingSide,
            command != null ? command.Data : null,
            targetUnitOrNull);
    }

    private IEnumerator ShowOnActionCommandUsedRevealPanelCoroutine(
        OnlineBattleActionPayload action,
        CardData commandData,
        string commandName,
        CardController targetUnitOrNull)
    {
        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            yield break;
        }

        CloseOnActionCommandRevealPanelIfAny();

        GameObject root = new GameObject(
            "OnActionCommandReveal",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        _activeOnActionCommandRevealRoot = root;
        isOnActionPopupOpen = true;
        root.transform.SetParent(canvas.transform, false);
        root.transform.SetAsLastSibling();
        root.SetFullSize();
        Image dim = root.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.72f);
        dim.raycastTarget = true;

        bool isOnMain = action != null
            && !string.IsNullOrEmpty(action.onActionContext)
            && action.onActionContext.IndexOf("OnMain", System.StringComparison.OrdinalIgnoreCase) >= 0;

        TextMeshProUGUI title = root.CreateChildTextCustom("OnActionCommandRevealTitle", UIAnchor.TopCenter, 760, 52);
        title.text = isOnMain
            ? GameLocale.T("相手がコマンドを使用（メイン）", "Opponent used a Command (Main)")
            : GameLocale.T("相手がコマンドを使用", "Opponent used a Command");
        title.color = new Color(1f, 0.92f, 0.35f, 1f);
        title.fontSize = 26;
        title.fontStyle = FontStyles.Bold;
        GameLocale.ApplyFont(title);
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -36f);

        TextMeshProUGUI nameLabel = root.CreateChildTextCustom("OnActionCommandRevealName", UIAnchor.TopCenter, 760, 36);
        int shownCost = action != null ? action.cardCost : 0;
        int shownLevel = action != null ? action.cardLevel : 0;
        nameLabel.text = commandData != null
            ? $"{commandData.cardName}  (Lv.{shownLevel} / Cost {shownCost})"
            : $"{commandName}  (Lv.{shownLevel} / Cost {shownCost})";
        nameLabel.color = Color.white;
        nameLabel.fontSize = 22;
        nameLabel.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -84f);

        if (action != null && !string.IsNullOrWhiteSpace(action.onActionContext))
        {
            TextMeshProUGUI ctxLabel = root.CreateChildTextCustom("OnActionCommandRevealContext", UIAnchor.TopCenter, 760, 28);
            ctxLabel.text = action.onActionContext;
            ctxLabel.color = new Color(0.85f, 0.85f, 0.85f);
            ctxLabel.fontSize = 16;
            ctxLabel.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -116f);
        }

        if (CardImagePrefab != null && commandData != null)
        {
            GameObject cardGo = Instantiate(CardImagePrefab, root.transform);
            RectTransform cardRt = cardGo.GetComponent<RectTransform>();
            if (cardRt != null)
            {
                cardRt.anchorMin = new Vector2(0.5f, 0.5f);
                cardRt.anchorMax = new Vector2(0.5f, 0.5f);
                cardRt.pivot = new Vector2(0.5f, 0.5f);
                cardRt.sizeDelta = new Vector2(200f, 280f);
                cardRt.anchoredPosition = new Vector2(0f, 24f);
            }

            CardController preview = cardGo.GetComponent<CardController>();
            preview?.SetUp(commandData, _ => { });
            Button blocker = cardGo.GetComponent<Button>();
            if (blocker != null)
            {
                blocker.interactable = false;
            }
        }

        if (targetUnitOrNull != null && targetUnitOrNull.Data != null)
        {
            TextMeshProUGUI targetLabel = root.CreateChildTextCustom("OnActionCommandRevealTarget", UIAnchor.TopCenter, 760, 32);
            targetLabel.text =
                $"対象: {targetUnitOrNull.Data.cardName}(id:{targetUnitOrNull.Data.id}) "
                + $"AP:{targetUnitOrNull.CurrentPower} HP:{targetUnitOrNull.CurrentHp}";
            targetLabel.color = new Color(1f, 0.75f, 0.75f);
            targetLabel.fontSize = 18;
            targetLabel.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -248f);
        }

        bool acknowledged = false;
        Button ok = root.CreateChildButton("OnActionCommandRevealOk");
        RectTransform okRt = ok.GetComponent<RectTransform>();
        okRt.sizeDelta = new Vector2(180f, 46f);
        okRt.anchoredPosition = new Vector2(0f, -290f);
        TextMeshProUGUI okLabel = ok.GetComponentInChildren<TextMeshProUGUI>();
        if (okLabel != null)
        {
            okLabel.text = "OK";
        }

        ok.onClick.AddListener(() => { acknowledged = true; });

        yield return new WaitUntil(() => acknowledged);

        CloseOnActionCommandRevealPanelIfAny();
    }

    private void CloseOnActionCommandRevealPanelIfAny()
    {
        if (_activeOnActionCommandRevealRoot != null)
        {
            Destroy(_activeOnActionCommandRevealRoot);
            _activeOnActionCommandRevealRoot = null;
        }

        isOnActionPopupOpen = activeOnActionPopupRoot != null || _activeLookDeckPopupRoot != null;
    }
}
