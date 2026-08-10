using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>オンライン対戦のマリガン同期と mulliganthink 待機。</summary>
public partial class BattleGameMain
{
    private bool _remoteMulliganDecideReceived;
    private bool _remoteMulliganBootstrapReceived;
    private OnlineMulliganSyncPayload _remoteMulliganBootstrapPayload;
    private bool isMulliganPromptOpen;
    private bool isMulliganThinkPauseOpen;
    private GameObject _activeMulliganThinkRoot;
    private readonly List<int> _onlineBrokenShieldCardIdsForAttackNotify = new List<int>();

    private void ResetOnlineMulliganSyncState()
    {
        _remoteMulliganDecideReceived = false;
        _remoteMulliganBootstrapReceived = false;
        _remoteMulliganBootstrapPayload = null;
        isMulliganPromptOpen = false;
        CloseMulliganThinkOverlay();
        _onlineBrokenShieldCardIdsForAttackNotify.Clear();
    }

    private void RecordOnlineBrokenShieldCardIdsForSync(IReadOnlyList<ShieldBreakTaken> takenCards)
    {
        if (!IsOnlineBattle() || takenCards == null)
        {
            return;
        }

        _onlineBrokenShieldCardIdsForAttackNotify.Clear();
        for (int i = 0; i < takenCards.Count; i++)
        {
            ShieldBreakTaken taken = takenCards[i];
            int cardId = taken.Data != null ? taken.Data.id : taken.CardId;
            if (cardId > 0)
            {
                _onlineBrokenShieldCardIdsForAttackNotify.Add(cardId);
            }
        }
    }

    private int[] ConsumeOnlineBrokenShieldCardIdsForAttackNotify()
    {
        if (_onlineBrokenShieldCardIdsForAttackNotify.Count == 0)
        {
            return null;
        }

        int[] ids = _onlineBrokenShieldCardIdsForAttackNotify.ToArray();
        _onlineBrokenShieldCardIdsForAttackNotify.Clear();
        return ids;
    }

    /// <summary>オンライン：マリガン → 相手待機 → シールド設置 → 相手シールド同期。</summary>
    private IEnumerator RunOnlineMulliganAndBootstrapCoroutine(Canvas canvas, int openingHandSize, int exBasePoints)
    {
        ResetOnlineMulliganSyncState();

        if (canvas == null)
        {
            Debug.LogWarning("[OnlineBattle] Mulligan skipped — no canvas.");
            yield break;
        }

        bool? playerChoice = null;
        isMulliganPromptOpen = true;
        yield return MulliganPromptCoroutine(
            canvas,
            GameLocale.T(
                "手札を山札に戻して5枚引き直しますか？（マリガン）",
                "Do you want to shuffle your hand and draw 5 cards again? (Mulligan)"),
            value => playerChoice = value);
        isMulliganPromptOpen = false;

        if (playerChoice == true)
        {
            PerformMulligan(cardGameRule, playerHandCards, openingHandSize, PlayerType.Player);
            Debug.Log("[OnlineBattle] プレイヤー：マリガン実行。");
        }
        else
        {
            Debug.Log("[OnlineBattle] プレイヤー：マリガン見送り。");
        }

        SendOnlineBattleMessage(EosOnlineBattleMessage.CreateMulliganSync(
            OnlineMulliganSyncPayload.ToJsonDecide(playerChoice == true, cardGameRule.GetRemainingCount())));

        yield return WaitForRemoteMulliganDecideCoroutine(canvas);

        int exBasePointsValue = exBasePoints;
        cardGameRule.SetupShieldFromDeckAfterMulligan(
            CardImagePrefab, OnCardClicked, OpeningShieldCardCount, exBasePointsValue);

        int[] localShieldIds = new int[cardGameRule.GetShieldCardIds().Count];
        for (int i = 0; i < localShieldIds.Length; i++)
        {
            localShieldIds[i] = cardGameRule.GetShieldCardIds()[i];
        }

        SendOnlineBattleMessage(EosOnlineBattleMessage.CreateMulliganSync(
            OnlineMulliganSyncPayload.ToJsonBootstrap(cardGameRule.GetRemainingCount(), localShieldIds)));

        yield return WaitForRemoteMulliganBootstrapCoroutine(canvas);

        if (_remoteMulliganBootstrapPayload != null)
        {
            ApplyRemoteOpponentShieldBootstrap(_remoteMulliganBootstrapPayload, exBasePointsValue);
        }
        else
        {
            Debug.LogWarning("[OnlineBattle] Remote mulligan bootstrap missing — opponent shields may desync.");
        }

        // シールド設置後の山札残数を相手へ同期（手札枚数も合わせて送る）
        NotifyLocalPlayerHandDeckSnapshot();
    }

    private void ApplyRemoteOpponentShieldBootstrap(OnlineMulliganSyncPayload payload, int exBasePoints)
    {
        if (payload == null || enemyCardGameRule == null)
        {
            return;
        }

        int[] shieldIds = payload.shieldCardIds ?? System.Array.Empty<int>();
        enemyCardGameRule.SetupShieldFromCardIds(
            CardImagePrefab,
            OnCardClicked,
            shieldIds,
            exBasePoints);
        enemyCardGameRule.TrimDeckToRemainingCount(payload.deckRemainCount);
        Debug.Log(
            $"[OnlineBattle] Opponent shield zone synced. shields={shieldIds.Length} deckRemain={payload.deckRemainCount}");
    }

    private IEnumerator WaitForRemoteMulliganDecideCoroutine(Canvas canvas)
    {
        if (_remoteMulliganDecideReceived)
        {
            yield break;
        }

        ShowMulliganThinkOverlay(canvas);
        yield return new WaitUntil(() => _remoteMulliganDecideReceived || !IsOnlineBattle());
        CloseMulliganThinkOverlay();
    }

    private IEnumerator WaitForRemoteMulliganBootstrapCoroutine(Canvas canvas)
    {
        if (_remoteMulliganBootstrapReceived)
        {
            yield break;
        }

        ShowMulliganThinkOverlay(canvas);
        yield return new WaitUntil(() => _remoteMulliganBootstrapReceived || !IsOnlineBattle());
        CloseMulliganThinkOverlay();
    }

    private void ShowMulliganThinkOverlay(Canvas canvas)
    {
        if (canvas == null)
        {
            return;
        }

        CloseMulliganThinkOverlay();
        isMulliganThinkPauseOpen = true;

        GameObject root = new GameObject("MulliganThinkPause", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        _activeMulliganThinkRoot = root;
        root.transform.SetParent(canvas.transform, false);
        root.transform.SetAsLastSibling();
        root.SetFullSize();
        Image dim = root.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.5f);
        dim.raycastTarget = true;

        TextMeshProUGUI title = root.CreateChildTextCustom("MulliganThinkTitle", UIAnchor.TopCenter, 720, 56);
        title.SetLocalizedText("マリガン待機中", "Waiting for mulligan");
        title.color = new Color(1f, 0.95f, 0.2f, 1f);
        title.fontSize = 26;
        title.alignment = TextAlignmentOptions.Center;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -100f);

        TextMeshProUGUI sub = root.CreateChildTextCustom("MulliganThinkSub", UIAnchor.TopCenter, 720, 40);
        sub.SetLocalizedText("相手のマリガンを待っています…", "Waiting for opponent's mulligan...");
        sub.color = Color.white;
        sub.fontSize = 18;
        sub.alignment = TextAlignmentOptions.Center;
        sub.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -150f);
    }

    private void CloseMulliganThinkOverlay()
    {
        isMulliganThinkPauseOpen = false;
        if (_activeMulliganThinkRoot != null)
        {
            Destroy(_activeMulliganThinkRoot);
            _activeMulliganThinkRoot = null;
        }
    }

    private void HandleRemoteMulliganSync(string payload)
    {
        if (!OnlineMulliganSyncPayload.TryParse(payload, out OnlineMulliganSyncPayload sync))
        {
            Debug.LogWarning($"[OnlineBattle] Invalid MulliganSync payload: {payload}");
            return;
        }

        if (sync.phase == OnlineMulliganSyncPayload.PhaseDecide)
        {
            _remoteMulliganDecideReceived = true;
            Debug.Log(
                $"[OnlineBattle] Remote mulligan decide received. performed={sync.performedMulligan} deck={sync.deckRemainCount}");
            return;
        }

        if (sync.phase == OnlineMulliganSyncPayload.PhaseBootstrap)
        {
            _remoteMulliganBootstrapPayload = sync;
            _remoteMulliganBootstrapReceived = true;
            Debug.Log(
                $"[OnlineBattle] Remote mulligan bootstrap received. shields={(sync.shieldCardIds != null ? sync.shieldCardIds.Length : 0)} deck={sync.deckRemainCount}");
        }
    }
}
