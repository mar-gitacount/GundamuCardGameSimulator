using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>OnAction コマンド使用を相手へ通知し、カード UI を表示する。</summary>
public partial class BattleGameMain
{
    private GameObject _activeOnActionCommandRevealRoot;

    private void ResetOnlineOnActionCommandRevealState()
    {
        CloseOnActionCommandRevealPanelIfAny();
    }

    /// <summary>
    /// ローカル人間が OnAction コマンドを使用したとき、相手へカード情報を送る。
    /// </summary>
    private void TryNotifyLocalOnActionCommandUsed(
        CardController command,
        PlayerType side,
        CardController targetUnitOrNull = null)
    {
        if (!IsOnlineBattle() || _applyingRemoteBattleAction || side != PlayerType.Player)
        {
            return;
        }

        if (command == null || command.Data == null || command.Data.type != Type.Command)
        {
            return;
        }

        int targetCardId = targetUnitOrNull != null && targetUnitOrNull.Data != null
            ? targetUnitOrNull.Data.id
            : -1;

        string json = OnlineBattleActionPayload.CreateOnActionCommandUsed(
            command.Data.id,
            (int)side,
            _onlineOnActionActiveContext ?? string.Empty,
            command.CurrentCost,
            command.CurrentLevel,
            targetCardId);
        SendOnlineBattleMessage(EosOnlineBattleMessage.CreateOnActionCommandUsed(json));

        Debug.Log(
            $"[OnActionCommandUsed][Send] card:{command.Data.cardName}(id:{command.Data.id}) "
            + $"cost:{command.CurrentCost} lv:{command.CurrentLevel} targetCardId:{targetCardId} "
            + $"context:{_onlineOnActionActiveContext}");

        LogOnActionCommandUsedBoardSnapshotCompact("localSend", side, command, targetUnitOrNull);
    }

    private void HandleRemoteOnActionCommandUsed(string payload)
    {
        if (!OnlineBattleActionPayload.TryParse(payload, out OnlineBattleActionPayload action)
            || action.action != OnlineBattleActionPayload.OnActionCommandUsed)
        {
            Debug.LogWarning($"[OnlineBattle] Invalid OnActionCommandUsed payload: {payload}");
            return;
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

        PlayerType actingSide = action.actingZoneSide == (int)PlayerType.Enemy
            ? PlayerType.Enemy
            : PlayerType.Player;

        Debug.Log(
            $"[OnActionCommandUsed][Receive] card:{commandName}(id:{action.cardId}) "
            + $"cost:{action.cardCost} lv:{action.cardLevel} targetCardId:{action.targetCardId} "
            + $"actingSide:{actingSide} context:{action.onActionContext}");

        LogOnActionCommandUsedBoardSnapshotCompact("remoteReceive", actingSide, commandData, targetUnit);

        StartCoroutine(ShowOnActionCommandUsedRevealPanelCoroutine(action, commandData, commandName, targetUnit));
    }

    /// <summary>フル [BoardSnapshot] ではなく 1 行の AP/HP 要約（Editor GPU 負荷軽減）。</summary>
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
        root.transform.SetParent(canvas.transform, false);
        root.transform.SetAsLastSibling();
        root.SetFullSize();
        Image dim = root.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.72f);
        dim.raycastTarget = true;

        TextMeshProUGUI title = root.CreateChildTextCustom("OnActionCommandRevealTitle", UIAnchor.TopCenter, 760, 52);
        title.text = "相手がアクションステップでコマンドを使用";
        title.color = new Color(1f, 0.92f, 0.35f, 1f);
        title.fontSize = 26;
        title.fontStyle = FontStyles.Bold;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -36f);

        TextMeshProUGUI nameLabel = root.CreateChildTextCustom("OnActionCommandRevealName", UIAnchor.TopCenter, 760, 36);
        nameLabel.text = commandData != null
            ? $"{commandData.cardName}  (Lv.{action.cardLevel} / Cost {action.cardCost})"
            : $"{commandName}  (Lv.{action.cardLevel} / Cost {action.cardCost})";
        nameLabel.color = Color.white;
        nameLabel.fontSize = 22;
        nameLabel.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -84f);

        if (!string.IsNullOrWhiteSpace(action.onActionContext))
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

        float elapsed = 0f;
        const float autoCloseSeconds = 4f;
        while (!acknowledged && elapsed < autoCloseSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        CloseOnActionCommandRevealPanelIfAny();
    }

    private void CloseOnActionCommandRevealPanelIfAny()
    {
        if (_activeOnActionCommandRevealRoot != null)
        {
            Destroy(_activeOnActionCommandRevealRoot);
            _activeOnActionCommandRevealRoot = null;
        }
    }
}
