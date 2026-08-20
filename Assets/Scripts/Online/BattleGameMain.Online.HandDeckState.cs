using UnityEngine;

/// <summary>オンライン：自サイドの手札枚数／山札残数と相手手札伏せ UI の同期。</summary>
public partial class BattleGameMain
{
    private int _lastSyncedPlayerHandUiCount = -1;
    private int _lastSyncedEnemyHandUiCount = -1;
    private int _lastSentOnlineHandUiCount = -1;

    /// <summary>
    /// 自分視点の Player ゾーン手札・山札を相手へ送る。
    /// カード ID は送らず枚数のみ（手札内容の秘匿を維持）。
    /// 枚数は手札 UI 上の生存カードを正とする（同一 CardData の複数枚をリストが取りこぼしてもずれない）。
    /// </summary>
    private void NotifyLocalPlayerHandDeckSnapshot()
    {
        if (_applyingRemoteBattleAction || !IsOnlineBattle() || cardGameRule == null)
        {
            return;
        }

        int handCount = Mathf.Max(0, cardGameRule.CountHandZoneCards());
        int deckRemain = Mathf.Max(0, cardGameRule.GetRemainingCount());
        _lastSentOnlineHandUiCount = handCount;
        SendOnlineBattleMessage(EosOnlineBattleMessage.CreateHandDeckState(
            OnlineBattleActionPayload.CreateHandDeckState(
                (int)PlayerType.Player,
                handCount,
                deckRemain)));
        Debug.Log(
            $"[OnlineBattle] HandDeckState sent hand:{handCount} deckRemain:{deckRemain}");
    }

    /// <summary>手札 UI から消える Destroy 直後でもリスト基準で確実に送る。</summary>
    private void NotifyLocalPlayerHandDeckSnapshotAfterHandListChange()
    {
        NotifyLocalPlayerHandDeckSnapshot();
    }

    private void HandleRemoteHandDeckState(string payload)
    {
        if (!OnlineBattleActionPayload.TryParse(payload, out OnlineBattleActionPayload action)
            || action.action != OnlineBattleActionPayload.HandDeckState)
        {
            Debug.LogWarning($"[OnlineBattle] Invalid HandDeckState payload: {payload}");
            return;
        }

        PlayerType senderZone = action.actingZoneSide == (int)PlayerType.Enemy
            ? PlayerType.Enemy
            : PlayerType.Player;
        PlayerType localZone = MirrorOnlineZoneOwner(senderZone);

        // 受信したのは相手の手札／山札 → ローカルでは Enemy ゾーンへミラー
        if (localZone != PlayerType.Enemy || enemyCardGameRule == null)
        {
            Debug.LogWarning(
                $"[OnlineBattle] HandDeckState ignored unexpected localZone:{localZone}");
            return;
        }

        // 欠落時は -1（フィールド初期値）。正当な 0 枚は反映する。
        if (action.deckRemainCount >= 0)
        {
            ApplyOnlineEnemyDeckRemain(action.deckRemainCount);
        }

        if (action.handCount >= 0)
        {
            int handCount = action.handCount;
            SyncGundamRuleHandCount(PlayerType.Enemy, handCount);

            if (CardImagePrefab != null)
            {
                enemyCardGameRule.SetOnlineOpponentHandTotalCount(handCount, CardImagePrefab);
            }
            else
            {
                enemyCardGameRule.RefreshHandCountDisplay();
            }

            RebuildEnemyHandCardListFromUi();
        }

        Debug.Log(
            $"[OnlineBattle] HandDeckState applied localEnemy hand:{action.handCount} deckRemain:{action.deckRemainCount}");
    }

    /// <summary>相手山札残数を権威値へ同期し UI／ルール状態も更新する。</summary>
    private void ApplyOnlineEnemyDeckRemain(int deckRemain)
    {
        if (enemyCardGameRule == null || deckRemain < 0)
        {
            return;
        }

        enemyCardGameRule.SetDeckRemainCount(deckRemain);
        SyncGundamRuleDeckCount(PlayerType.Enemy, deckRemain);
    }

    /// <summary>Enemy 手札 UI（既知カードのみ）から enemyHandCards を再構築する。伏せトークンは含めない。</summary>
    private void RebuildEnemyHandCardListFromUi()
    {
        enemyHandCards.Clear();
        RectTransform content = enemyCardGameRule != null ? enemyCardGameRule.HandScrollContent : null;
        if (content == null)
        {
            return;
        }

        for (int i = 0; i < content.childCount; i++)
        {
            CardController cc = content.GetChild(i).GetComponent<CardController>();
            if (cc == null || cc.IsOnlineOpponentHandPlaceholder || cc.Data == null)
            {
                continue;
            }

            if (!enemyHandCards.Contains(cc.Data))
            {
                enemyHandCards.Add(cc.Data);
            }
        }
    }

    private void SyncGundamRuleHandCount(PlayerType owner, int handCount)
    {
        if (gundamRule == null)
        {
            return;
        }

        Gundam2024RuleScript.PlayerState state = owner == PlayerType.Player
            ? gundamRule.Player
            : gundamRule.Enemy;
        if (state != null)
        {
            state.handCount = Mathf.Max(0, handCount);
        }
    }

    /// <summary>
    /// 手札ヘッダー枚数を毎フレーム実 UI に合わせる。
    /// オンラインは自手札枚数が変わったら相手へ送り直す。
    /// </summary>
    private void SyncLiveHandCountDisplays()
    {
        if (cardGameRule == null && enemyCardGameRule == null)
        {
            return;
        }

        int playerUi = cardGameRule != null ? cardGameRule.CountHandZoneCards() : 0;
        int enemyUi = enemyCardGameRule != null ? enemyCardGameRule.CountHandZoneCards() : 0;

        if (playerUi != _lastSyncedPlayerHandUiCount)
        {
            cardGameRule?.RefreshHandCountDisplay(playerUi);
            SyncGundamRuleHandCount(PlayerType.Player, playerUi);
            _lastSyncedPlayerHandUiCount = playerUi;
        }

        if (enemyUi != _lastSyncedEnemyHandUiCount)
        {
            enemyCardGameRule?.RefreshHandCountDisplay(enemyUi);
            SyncGundamRuleHandCount(PlayerType.Enemy, enemyUi);
            _lastSyncedEnemyHandUiCount = enemyUi;
        }

        if (IsOnlineBattle()
            && !_applyingRemoteBattleAction
            && playerUi != _lastSentOnlineHandUiCount)
        {
            NotifyLocalPlayerHandDeckSnapshot();
        }
    }
}
