using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>オンライン対戦のシールド破壊同期（防御側の OK / バースト完了まで攻撃側を待機）。</summary>
public partial class BattleGameMain
{
    private struct OnlineDeferredEnemyShieldBreak
    {
        public int Count;
        public bool SimultaneousReveal;
    }

    private int _onlineShieldBreakRequestIdCounter;
    private int _pendingOnlineShieldBreakRequestId;
    private bool _onlineShieldBreakCompleteReceived;
    private bool _onlineShieldAttackNotifySent;
    private bool isOnlineShieldBreakThinkPauseOpen;
    private GameObject _activeShieldBreakThinkRoot;
    private OnlineDeferredEnemyShieldBreak? _onlineDeferredEnemyShieldBreak;
    /// <summary>攻撃側ミラー用。防御側のバースト完了待ち中に破壊されたシールド ID（ShieldAttack 送信時の peek）。</summary>
    private int[] _pendingAttackerMirrorBrokenShieldCardIds;
    private bool _recordingRemoteShieldBreakTrashIds;
    private readonly List<int> _remoteShieldBreakTrashedCardIds = new List<int>();
    private readonly List<OnlineBurstDeployedUnitRecord> _remoteShieldBreakBurstDeployedUnits =
        new List<OnlineBurstDeployedUnitRecord>();

    private struct OnlineBurstDeployedUnitRecord
    {
        public int CardId;
        public int InstanceId;
        public int Ap;
        public int Hp;
        public int PrintedType;
    }

    private void ResetOnlineShieldBreakSyncState()
    {
        _onlineShieldBreakRequestIdCounter = 0;
        _pendingOnlineShieldBreakRequestId = 0;
        _onlineShieldBreakCompleteReceived = false;
        _onlineShieldAttackNotifySent = false;
        _onlineDeferredEnemyShieldBreak = null;
        _pendingAttackerMirrorBrokenShieldCardIds = null;
        _recordingRemoteShieldBreakTrashIds = false;
        _remoteShieldBreakTrashedCardIds.Clear();
        _remoteShieldBreakBurstDeployedUnits.Clear();
        ClearPendingDefenderDeployedBaseHpForOnlineSync();
        CloseOnlineShieldBreakThinkOverlay();
    }

    private bool ShouldDeferEnemyShieldBreakToRemoteDefender(Gundam2024RuleScript.PlayerSide side)
    {
        // AI戦と同様、効果ダメージ等のシールド破壊はローカルで即処理する。
        // 遅延するのはシールド攻撃フロー中のみ（防御側がバーストを解決するため）。
        return IsOnlineBattle()
            && currentPlayerType == PlayerType.Player
            && !_applyingRemoteBattleAction
            && side == Gundam2024RuleScript.PlayerSide.Enemy
            && isShieldAttackResolving;
    }

    private List<ShieldBreakTaken> CollectEnemyShieldTakenCardsForOnlineDisplay(
        int brokenCount,
        bool simultaneousReveal)
    {
        List<ShieldBreakTaken> takenCards = new List<ShieldBreakTaken>(brokenCount);
        CardGameRule rule = enemyCardGameRule;
        if (rule == null || brokenCount <= 0)
        {
            return takenCards;
        }

        if (simultaneousReveal && brokenCount > 1)
        {
            SuppressBreakingLayout layout = BuildSuppressBreakingLayout(rule, brokenCount);
            for (int i = 0; i < layout.BreakingZoneIndices.Count; i++)
            {
                int zoneIndex = layout.BreakingZoneIndices[i];
                if (rule.TryGetShieldZoneCardAt(zoneIndex, out ShieldBreakTaken taken))
                {
                    takenCards.Add(taken);
                }
            }
        }
        else
        {
            for (int i = 0; i < brokenCount; i++)
            {
                if (rule.TryGetShieldZoneCardAt(i, out ShieldBreakTaken taken))
                {
                    takenCards.Add(taken);
                }
            }
        }

        return takenCards;
    }

    private static int[] ExtractShieldBreakCardIds(IReadOnlyList<ShieldBreakTaken> takenCards)
    {
        if (takenCards == null || takenCards.Count == 0)
        {
            return null;
        }

        List<int> ids = new List<int>(takenCards.Count);
        for (int i = 0; i < takenCards.Count; i++)
        {
            ShieldBreakTaken taken = takenCards[i];
            int cardId = taken.Data != null ? taken.Data.id : taken.CardId;
            if (cardId > 0)
            {
                ids.Add(cardId);
            }
        }

        return ids.Count > 0 ? ids.ToArray() : null;
    }

    private int[] PeekEnemyShieldCardIdsForOnlineSync(int brokenCount, bool simultaneousReveal)
    {
        return ExtractShieldBreakCardIds(
            CollectEnemyShieldTakenCardsForOnlineDisplay(brokenCount, simultaneousReveal));
    }

    /// <summary>
    /// 攻撃側画面の相手シールドゾーンを、AI戦の ProcessShieldBreakBatch と同様に実カードごと破壊する。
    /// </summary>
    private void ApplyAttackerEnemyZoneShieldBreakVisualSync(
        int brokenCount,
        int[] preferredCardIds,
        bool simultaneousReveal)
    {
        if (brokenCount <= 0 || enemyCardGameRule == null)
        {
            return;
        }

        List<ShieldBreakTaken> takenCards = new List<ShieldBreakTaken>(brokenCount);
        bool isSuppress = simultaneousReveal && brokenCount > 1;

        if (isSuppress)
        {
            SuppressBreakingLayout layout = BuildSuppressBreakingLayout(enemyCardGameRule, brokenCount);
            if (layout.BreakingZoneIndices.Count > 0)
            {
                SuppressBreakPlayerChoice choice = BuildEnemySuppressChoice(layout);
                takenCards = DetachShieldCardsBySuppressChoice(enemyCardGameRule, choice);
            }
        }
        else if (preferredCardIds != null && preferredCardIds.Length > 0)
        {
            for (int i = 0; i < preferredCardIds.Length && takenCards.Count < brokenCount; i++)
            {
                if (enemyCardGameRule.TryDetachShieldCardById(preferredCardIds[i], out ShieldBreakTaken taken, revealFace: true))
                {
                    takenCards.Add(taken);
                }
            }
        }

        while (takenCards.Count < brokenCount)
        {
            if (!enemyCardGameRule.TryTakeTopShieldCardForBreak(out ShieldBreakTaken taken))
            {
                break;
            }

            takenCards.Add(taken);
        }

        for (int i = 0; i < takenCards.Count; i++)
        {
            enemyCardGameRule.CommitShieldCardToTrash(takenCards[i]);
        }

        enemyCardGameRule.DestroyUnregisteredShieldZoneVisuals();
        ReconcileShieldStateWithZone(Gundam2024RuleScript.PlayerSide.Enemy, force: true);
        SyncAllResourceViewsFromRule();
        Debug.Log(
            $"[OnlineBattle] Attacker-side enemy shield visual sync. detached={takenCards.Count}/{brokenCount}");
    }

    /// <summary>効果ダメージ等で変化した相手の shield / exBase を防御側へ同期する。</summary>
    private void NotifyLocalDefenderAreaStateSync()
    {
        if (_applyingRemoteBattleAction || !IsOnlineBattle() || currentPlayerType != PlayerType.Player)
        {
            return;
        }

        Gundam2024RuleScript.PlayerState defender = gundamRule.Enemy;
        int defenderDeployedBaseHpAfter = ConsumePendingDefenderDeployedBaseHpForOnlineSync();
        if (defenderDeployedBaseHpAfter < 0)
        {
            defenderDeployedBaseHpAfter = ResolveOnlineSyncDeployedBaseHp(Gundam2024RuleScript.PlayerSide.Enemy);
        }

        SendOnlineBattleMessage(EosOnlineBattleMessage.CreateAttack(
            OnlineBattleActionPayload.CreateShieldAttack(
                attackerInstanceId: 0,
                defenderShieldAfter: defender.shield,
                defenderExBaseAfter: defender.exBase,
                directAttackWin: false,
                defenderDeployedBaseHpAfter: defenderDeployedBaseHpAfter,
                includeDefenderAreaSnapshot: true)));
        Debug.Log(
            $"[OnlineBattle] Defender area state sync sent. shield={defender.shield} exBase={defender.exBase} "
            + $"baseHp:{defenderDeployedBaseHpAfter}");
    }

    private IEnumerator RunOnlineAttackerEnemyShieldBreakHandshakeCoroutine(
        CardController attacker,
        OnlineDeferredEnemyShieldBreak deferred)
    {
        if (attacker == null || deferred.Count <= 0)
        {
            isShieldBreakFlowOpen = false;
            yield break;
        }

        int[] cardIds = PeekEnemyShieldCardIdsForOnlineSync(deferred.Count, deferred.SimultaneousReveal);
        Gundam2024RuleScript.PlayerState defender = gundamRule.Enemy;
        int requestId = ++_onlineShieldBreakRequestIdCounter;
        _pendingOnlineShieldBreakRequestId = requestId;
        _onlineShieldBreakCompleteReceived = false;
        _pendingAttackerMirrorBrokenShieldCardIds = cardIds != null && cardIds.Length > 0
            ? (int[])cardIds.Clone()
            : null;
        int defenderDeployedBaseHpAfter = ConsumePendingDefenderDeployedBaseHpForOnlineSync();
        if (defenderDeployedBaseHpAfter < 0)
        {
            defenderDeployedBaseHpAfter = ResolveOnlineSyncDeployedBaseHp(Gundam2024RuleScript.PlayerSide.Enemy);
        }

        string attackPayload = OnlineBattleActionPayload.CreateShieldAttack(
            attacker.BattleInstanceId,
            defender.shield,
            defender.exBase,
            directAttackWin: false,
            cardIds,
            requestId,
            deferred.SimultaneousReveal,
            defenderDeployedBaseHpAfter);
        string attackMessage = EosOnlineBattleMessage.CreateAttack(attackPayload);
        int attackBytes = System.Text.Encoding.UTF8.GetByteCount(attackMessage);
        bool attackSent = SendOnlineBattleMessage(attackMessage);
        _onlineShieldAttackNotifySent = true;
        Debug.Log(
            $"[OnlineBattle] ShieldAttack handshake requestId={requestId} utf8Bytes={attackBytes} sent:{attackSent}");
        if (!attackSent)
        {
            // 防御側へ届かないため Complete は来ない。ローカルミラー破壊へ即フォールバック。
            Debug.LogError(
                $"[OnlineBattle] ShieldAttack send FAILED requestId={requestId} bytes={attackBytes} — "
                + "skip wait; apply local attacker mirror sync.");
            ApplyAttackerEnemyZoneShieldBreakVisualSync(deferred.Count, cardIds, deferred.SimultaneousReveal);
            _pendingOnlineShieldBreakRequestId = 0;
            _pendingAttackerMirrorBrokenShieldCardIds = null;
            isShieldBreakFlowOpen = false;
            yield break;
        }

        // 攻撃側にも破壊シールド内容を表示（OKで閉じる）。その後／並行して相手バースト完了を待つ。
        List<ShieldBreakTaken> displayCards = CollectEnemyShieldTakenCardsForOnlineDisplay(
            deferred.Count,
            deferred.SimultaneousReveal);
        yield return ShowOnlineAttackerShieldBreakRevealAndWaitCoroutine(
            displayCards,
            deferred.SimultaneousReveal);

        if (_onlineShieldBreakCompleteReceived)
        {
            Debug.Log(
                "[OnlineBattle] ShieldBreakComplete received — attacker mirror already synced from defender snapshot.");
        }
        else
        {
            ApplyAttackerEnemyZoneShieldBreakVisualSync(deferred.Count, cardIds, deferred.SimultaneousReveal);
            Debug.LogWarning(
                "[OnlineBattle] ShieldBreakComplete not received — attacker visual sync applied from local rule.");
        }

        _pendingOnlineShieldBreakRequestId = 0;
        _pendingAttackerMirrorBrokenShieldCardIds = null;
        isShieldBreakFlowOpen = false;
    }

    private void BeginRemoteShieldBreakTrashIdRecording()
    {
        _recordingRemoteShieldBreakTrashIds = true;
        _remoteShieldBreakTrashedCardIds.Clear();
        _remoteShieldBreakBurstDeployedUnits.Clear();
    }

    private int[] EndRemoteShieldBreakTrashIdRecording()
    {
        _recordingRemoteShieldBreakTrashIds = false;
        return _remoteShieldBreakTrashedCardIds.Count > 0
            ? _remoteShieldBreakTrashedCardIds.ToArray()
            : null;
    }

    private void RecordRemoteShieldBreakTrashedCardIdIfNeeded(ShieldBreakTaken taken)
    {
        if (!_recordingRemoteShieldBreakTrashIds)
        {
            return;
        }

        int cardId = taken.Data != null ? taken.Data.id : taken.CardId;
        if (cardId > 0)
        {
            _remoteShieldBreakTrashedCardIds.Add(cardId);
        }
    }

    /// <summary>防御側バーストでバトルゾーン配備したユニットを ShieldBreakComplete 用に記録。</summary>
    private void RecordRemoteShieldBreakBurstDeployedUnitIfNeeded(CardController unit)
    {
        if (!_recordingRemoteShieldBreakTrashIds || unit == null || unit.Data == null)
        {
            return;
        }

        AssignBattleInstanceIdIfNeeded(unit);
        if (unit.BattleInstanceId <= 0)
        {
            Debug.LogWarning(
                $"[OnlineBattle] Burst deploy unit record skipped (no instanceId): {unit.Data.cardName}");
            return;
        }

        int printedType = unit.IsTemporaryBurstBattleUnit
            ? (int)unit.TemporaryBurstBattleUnitPrintedType
            : (int)unit.Data.type;

        _remoteShieldBreakBurstDeployedUnits.Add(new OnlineBurstDeployedUnitRecord
        {
            CardId = unit.Data.id,
            InstanceId = unit.BattleInstanceId,
            Ap = Mathf.Max(0, unit.Data.power),
            Hp = Mathf.Max(0, unit.CurrentHp > 0 ? unit.CurrentHp : unit.Data.hp),
            PrintedType = printedType
        });

        Debug.Log(
            $"[OnlineBattle] Burst deploy unit recorded: {unit.Data.cardName}(id:{unit.Data.id}) "
            + $"inst:{unit.BattleInstanceId} AP:{unit.Data.power} HP:{unit.Data.hp}");
    }

    private void BuildBurstDeployedUnitSnapshotArrays(
        out int[] cardIds,
        out int[] instanceIds,
        out int[] aps,
        out int[] hps,
        out int[] printedTypes)
    {
        int count = _remoteShieldBreakBurstDeployedUnits.Count;
        if (count <= 0)
        {
            cardIds = null;
            instanceIds = null;
            aps = null;
            hps = null;
            printedTypes = null;
            return;
        }

        cardIds = new int[count];
        instanceIds = new int[count];
        aps = new int[count];
        hps = new int[count];
        printedTypes = new int[count];
        for (int i = 0; i < count; i++)
        {
            OnlineBurstDeployedUnitRecord record = _remoteShieldBreakBurstDeployedUnits[i];
            cardIds[i] = record.CardId;
            instanceIds[i] = record.InstanceId;
            aps[i] = record.Ap;
            hps[i] = record.Hp;
            printedTypes[i] = record.PrintedType;
        }
    }

    private void SendOnlineShieldBreakComplete(
        int requestId,
        Gundam2024RuleScript.PlayerSide defenderSide,
        int[] brokenShieldCardIds = null)
    {
        if (requestId <= 0 || !IsOnlineBattle())
        {
            return;
        }

        Gundam2024RuleScript.PlayerState defenderState = GetRuleState(defenderSide);
        CardGameRule defenderRule = GetCardRuleForRuleSide(defenderSide);
        CardController deployedBase = defenderRule?.DeployedBase;
        int baseCardId = deployedBase?.Data?.id ?? 0;
        int baseHpAfter = deployedBase != null ? deployedBase.CurrentHp : 0;
        int[] shieldZoneIds = CollectShieldZoneCardIds(defenderRule);
        BuildBurstDeployedUnitSnapshotArrays(
            out int[] burstUnitCardIds,
            out int[] burstUnitInstanceIds,
            out int[] burstUnitAp,
            out int[] burstUnitHp,
            out int[] burstUnitPrintedType);

        string payload = OnlineBattleActionPayload.CreateShieldBreakComplete(
            requestId,
            defenderState != null ? defenderState.shield : -1,
            defenderState != null ? defenderState.exBase : -1,
            baseHpAfter,
            baseCardId,
            shieldZoneIds,
            brokenShieldCardIds,
            burstUnitCardIds,
            burstUnitInstanceIds,
            burstUnitAp,
            burstUnitHp,
            burstUnitPrintedType);

        string message = EosOnlineBattleMessage.CreateShieldBreakComplete(payload);
        int utf8Bytes = System.Text.Encoding.UTF8.GetByteCount(message);
        bool sent = SendOnlineBattleMessage(message);

        // ゾーン全部載せで EOS 上限超えた場合はゾーン省略で再送（shield/exBase/trashed/burst は残す）
        if (!sent && shieldZoneIds != null && shieldZoneIds.Length > 0)
        {
            Debug.LogWarning(
                $"[OnlineBattle] ShieldBreakComplete oversized/failed ({utf8Bytes}B) — retry without shieldZoneCardIds.");
            payload = OnlineBattleActionPayload.CreateShieldBreakComplete(
                requestId,
                defenderState != null ? defenderState.shield : -1,
                defenderState != null ? defenderState.exBase : -1,
                baseHpAfter,
                baseCardId,
                shieldZoneCardIds: null,
                brokenShieldCardIds,
                burstUnitCardIds,
                burstUnitInstanceIds,
                burstUnitAp,
                burstUnitHp,
                burstUnitPrintedType);
            message = EosOnlineBattleMessage.CreateShieldBreakComplete(payload);
            utf8Bytes = System.Text.Encoding.UTF8.GetByteCount(message);
            sent = SendOnlineBattleMessage(message);
        }

        Debug.Log(
            $"[OnlineBattle] ShieldBreakComplete sent. requestId={requestId} shield={defenderState?.shield} "
            + $"exBase={defenderState?.exBase} baseId={baseCardId} baseHp={baseHpAfter} zone={shieldZoneIds.Length} "
            + $"trashed={brokenShieldCardIds?.Length ?? 0} burstUnits={burstUnitCardIds?.Length ?? 0} "
            + $"utf8Bytes={utf8Bytes} sent:{sent}");
        if (!sent)
        {
            Debug.LogError(
                $"[OnlineBattle] ShieldBreakComplete send FAILED requestId={requestId} utf8Bytes={utf8Bytes}");
        }

        _remoteShieldBreakBurstDeployedUnits.Clear();
    }

    private void HandleRemoteShieldBreakComplete(string payload)
    {
        if (!OnlineBattleActionPayload.TryParse(payload, out OnlineBattleActionPayload action))
        {
            Debug.LogWarning($"[OnlineBattle] Invalid ShieldBreakComplete payload: {payload}");
            return;
        }

        if (action.requestId != _pendingOnlineShieldBreakRequestId || _pendingOnlineShieldBreakRequestId <= 0)
        {
            Debug.LogWarning(
                $"[OnlineBattle] Ignored ShieldBreakComplete requestId={action.requestId} "
                + $"pending={_pendingOnlineShieldBreakRequestId}");
            return;
        }

        if (currentPlayerType == PlayerType.Player)
        {
            ApplyRemoteDefenderAreaSnapshotFromBurst(action);
            ApplyAttackerMirrorBrokenShieldCardsToTrash(
                action.brokenShieldCardIds != null && action.brokenShieldCardIds.Length > 0
                    ? action.brokenShieldCardIds
                    : _pendingAttackerMirrorBrokenShieldCardIds);
        }

        _onlineShieldBreakCompleteReceived = true;
        Debug.Log($"[OnlineBattle] ShieldBreakComplete received. requestId={action.requestId}");
    }

    /// <summary>
    /// 防御側のバースト完了後、攻撃側ミラーの相手トラッシュへ破壊シールドを反映する。
    /// ゾーンはスナップショットで既に更新済みのため、ID のみトラッシュへ追加する。
    /// </summary>
    private void ApplyAttackerMirrorBrokenShieldCardsToTrash(int[] brokenCardIds)
    {
        if (brokenCardIds == null || brokenCardIds.Length == 0 || enemyCardGameRule == null)
        {
            return;
        }

        int added = 0;
        for (int i = 0; i < brokenCardIds.Length; i++)
        {
            int cardId = brokenCardIds[i];
            if (cardId <= 0)
            {
                continue;
            }

            enemyCardGameRule.AddCardToTrash(cardId);
            added++;
        }

        if (added > 0)
        {
            SyncResourceViewsFromRule(Gundam2024RuleScript.PlayerSide.Enemy);
            Debug.Log($"[OnlineBattle] Attacker mirror trash synced. brokenShields={added}");
        }
    }

    private IEnumerator ApplyRemoteDefenderShieldBreakCoroutine(
        Gundam2024RuleScript.PlayerSide side,
        int brokenCount,
        bool simultaneousReveal,
        int[] cardIds,
        int shieldBreakRequestId)
    {
        if (brokenCount <= 0 || isMatchFinished)
        {
            SendOnlineShieldBreakComplete(shieldBreakRequestId, side);
            yield break;
        }

        BeginRemoteShieldBreakTrashIdRecording();
        try
        {
            if (simultaneousReveal && brokenCount > 1)
            {
                yield return ProcessShieldBreakBatchCoroutine(side, brokenCount, simultaneousReveal: true);
            }
            else if (cardIds != null && cardIds.Length > 0)
            {
                yield return ApplyRemoteShieldBreakByCardIdsCoroutine(side, cardIds);
            }
            else
            {
                yield return ProcessShieldBreakBatchCoroutine(side, brokenCount, simultaneousReveal: false);
            }
        }
        finally
        {
            int[] trashedCardIds = EndRemoteShieldBreakTrashIdRecording();
            if (!shieldBreakQueueRunning && pendingShieldBreakBatches.Count == 0)
            {
                isShieldBreakFlowOpen = false;
            }

            SendOnlineShieldBreakComplete(shieldBreakRequestId, side, trashedCardIds);
        }
    }

    /// <summary>
    /// 攻撃側：破壊されたシールドを表示（OKで閉じ可能）。相手のバースト完了までは進行を待つ。
    /// </summary>
    private IEnumerator ShowOnlineAttackerShieldBreakRevealAndWaitCoroutine(
        List<ShieldBreakTaken> takenCards,
        bool simultaneousReveal)
    {
        const float waitTimeoutSeconds = 45f;
        float waitDeadline = Time.realtimeSinceStartup + waitTimeoutSeconds;

        if (takenCards == null || takenCards.Count == 0)
        {
            yield return ShowOnlineAttackerShieldBreakWaitUntilDeadlineCoroutine(waitDeadline, waitTimeoutSeconds);
            yield break;
        }

        for (int i = 0; i < takenCards.Count; i++)
        {
            if (takenCards[i].Controller != null)
            {
                takenCards[i].Controller.RevealShieldFace();
            }
        }

        isOnlineShieldBreakThinkPauseOpen = true;
        GameObject root = BuildShieldBreakRevealPanel(
            takenCards,
            PlayerType.Enemy,
            simultaneousReveal,
            suppressSelectionMode: false,
            layout: null,
            liveChoice: null,
            zoneIndices: null);
        if (root == null)
        {
            yield return ShowOnlineAttackerShieldBreakWaitUntilDeadlineCoroutine(waitDeadline, waitTimeoutSeconds);
            yield break;
        }

        _activeShieldBreakThinkRoot = root;

        Transform hintTransform = root.transform.Find("ShieldBreakHint");
        if (hintTransform != null)
        {
            TextMeshProUGUI hint = hintTransform.GetComponent<TextMeshProUGUI>();
            if (hint != null)
            {
                hint.SetLocalizedText(
                    "破壊されたシールドです。OKで閉じたあと、相手のバースト処理完了まで待機します。",
                    "Destroyed Shield. After OK, wait until the opponent finishes Burst resolution.");
            }
        }

        Transform burstBannerTransform = root.transform.Find("ShieldBurstBanner");
        if (burstBannerTransform != null)
        {
            TextMeshProUGUI burstBanner = burstBannerTransform.GetComponent<TextMeshProUGUI>();
            if (burstBanner != null)
            {
                burstBanner.SetLocalizedText("【バースト】あり（相手が解決）", "[Burst] present (opponent resolves)");
            }
        }

        bool dismissed = false;
        Button okBtn = root.CreateChildButton("OK");
        RectTransform okRt = okBtn.GetComponent<RectTransform>();
        okRt.sizeDelta = new Vector2(200f, 52f);
        okRt.anchorMin = new Vector2(0.5f, 0f);
        okRt.anchorMax = new Vector2(0.5f, 0f);
        okRt.pivot = new Vector2(0.5f, 0f);
        okRt.anchoredPosition = new Vector2(0f, 42f);
        okBtn.onClick.AddListener(() =>
        {
            dismissed = true;
            if (_activeShieldBreakThinkRoot == root)
            {
                _activeShieldBreakThinkRoot = null;
            }

            CloseShieldBreakRevealPanel(root);
        });

        // OK・Complete・タイムアウトのいずれかまでカード表示を維持
        yield return new WaitUntil(() =>
            dismissed
            || _onlineShieldBreakCompleteReceived
            || !IsOnlineBattle()
            || isMatchFinished
            || Time.realtimeSinceStartup >= waitDeadline);

        if (!dismissed && _activeShieldBreakThinkRoot == root)
        {
            CloseShieldBreakRevealPanel(root);
            _activeShieldBreakThinkRoot = null;
        }

        // 相手完了まだなら待機オーバーレイへ切り替え
        if (!_onlineShieldBreakCompleteReceived && IsOnlineBattle() && !isMatchFinished)
        {
            yield return ShowOnlineAttackerShieldBreakWaitUntilDeadlineCoroutine(waitDeadline, waitTimeoutSeconds);
            yield break;
        }

        isOnlineShieldBreakThinkPauseOpen = false;
        if (!_onlineShieldBreakCompleteReceived)
        {
            Debug.LogWarning(
                $"[OnlineBattle] ShieldBreakComplete wait timed out after {waitTimeoutSeconds}s — continue locally.");
        }
    }

    private IEnumerator ShowOnlineAttackerShieldBreakWaitUntilDeadlineCoroutine(
        float waitDeadline,
        float waitTimeoutSeconds)
    {
        if (_onlineShieldBreakCompleteReceived || !IsOnlineBattle() || isMatchFinished)
        {
            CloseOnlineShieldBreakThinkOverlay();
            yield break;
        }

        ShowOnlineShieldBreakThinkOverlay(ResolveBattleCanvas());
        yield return new WaitUntil(() =>
            _onlineShieldBreakCompleteReceived
            || !IsOnlineBattle()
            || isMatchFinished
            || Time.realtimeSinceStartup >= waitDeadline);

        if (!_onlineShieldBreakCompleteReceived)
        {
            Debug.LogWarning(
                $"[OnlineBattle] ShieldBreakComplete wait timed out after {waitTimeoutSeconds}s — continue locally.");
        }

        CloseOnlineShieldBreakThinkOverlay();
    }

    private void ShowOnlineShieldBreakThinkOverlay(Canvas canvas)
    {
        if (canvas == null)
        {
            return;
        }

        CloseOnlineShieldBreakThinkOverlay();
        isOnlineShieldBreakThinkPauseOpen = true;

        GameObject root = new GameObject(
            "ShieldBreakThinkPause",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        _activeShieldBreakThinkRoot = root;
        root.transform.SetParent(canvas.transform, false);
        root.transform.SetAsLastSibling();
        root.SetFullSize();
        Image dim = root.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.5f);
        dim.raycastTarget = true;

        TextMeshProUGUI title = root.CreateChildTextCustom("ShieldBreakThinkTitle", UIAnchor.TopCenter, 720, 56);
        title.SetLocalizedText("シールド破壊処理中", "Resolving Shield destruction");
        title.color = new Color(1f, 0.95f, 0.2f, 1f);
        title.fontSize = 26;
        title.alignment = TextAlignmentOptions.Center;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -100f);

        TextMeshProUGUI sub = root.CreateChildTextCustom("ShieldBreakThinkSub", UIAnchor.TopCenter, 720, 40);
        sub.SetLocalizedText(
            "相手のシールド破壊・バースト処理を待っています…",
            "Waiting for opponent's Shield destruction / Burst...");
        sub.color = Color.white;
        sub.fontSize = 18;
        sub.alignment = TextAlignmentOptions.Center;
        sub.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -150f);
    }

    private void CloseOnlineShieldBreakThinkOverlay()
    {
        isOnlineShieldBreakThinkPauseOpen = false;
        if (_activeShieldBreakThinkRoot != null)
        {
            CloseShieldBreakRevealPanel(_activeShieldBreakThinkRoot);
            _activeShieldBreakThinkRoot = null;
        }
    }
}
