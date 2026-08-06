using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// オンライン対戦における破壊時効果の所有者側解決と、破壊側の effectthink 待機。
/// </summary>
public partial class BattleGameMain
{
    private int _nextOnlineOnDestroyedRequestId = 1;
    private readonly HashSet<int> _pendingRemoteOnDestroyedRequestIds = new HashSet<int>();
    private readonly Queue<RemoteDestroyedResolution> _remoteDestroyedResolutionQueue =
        new Queue<RemoteDestroyedResolution>();
    private GameObject _activeOnlineEffectThinkRoot;
    private bool isOnlineEffectThinkPauseOpen;
    private bool _remoteDestroyedResolutionRunning;
    /// <summary>所有者側で現在解決中の破壊時効果 requestId（Look OK で完了送信するため）。</summary>
    private int _activeResolvingOnDestroyedRequestId;
    private bool _activeResolvingOnDestroyedCompleteSent;
    /// <summary>自軍カードの破壊時効果解決中に相手へ出した EffectThinkWait の requestId。</summary>
    private int _activeLocalOnDestroyedRemoteThinkRequestId;
    /// <summary>直近の OnDestroyed で手札へ戻したカード ID（オンライン完了通知用）。</summary>
    private int _pendingOnDestroyedReturnedToHandCardId = -1;
    /// <summary>EffectSync 等のリモート適用中に積んだ破壊時解決を、適用フラグ解除後に開始する。</summary>
    private bool _resumeRemoteDestroyedAfterRemoteApply;

    private sealed class RemoteDestroyedResolution
    {
        public CardController Unit;
        public int RequestId;
        public CardController DestroyedBy;
    }

    private bool HasPendingRemoteOnDestroyedResolution =>
        _pendingRemoteOnDestroyedRequestIds.Count > 0;

    private static bool HasOnDestroyedResolution(CardController card)
    {
        if (card == null || card.Data == null || card.Data.timedEffects == null)
        {
            return false;
        }

        for (int i = 0; i < card.Data.timedEffects.Count; i++)
        {
            TimedEffectData timed = card.Data.timedEffects[i];
            if (timed != null && timed.IsOnUnitDestroyedResolutionBlock())
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 送信側で、相手所有カードの破壊時効果完了を待つ要求を登録する。
    /// </summary>
    private int PrepareOnlineOnDestroyedWait(CardController target)
    {
        if (!IsOnlineBattle()
            || _applyingRemoteBattleAction
            || target == null
            || ResolveCardOwner(target.transform) != PlayerType.Enemy)
        {
            return 0;
        }

        bool hasResolution = HasOnDestroyedResolution(target)
            || (target.MountedPilot != null && HasOnDestroyedResolution(target.MountedPilot));
        if (!hasResolution)
        {
            return 0;
        }

        int requestId = AllocateOnlineOnDestroyedRequestId();
        _pendingRemoteOnDestroyedRequestIds.Add(requestId);
        ShowOnlineEffectThinkOverlay();
        string pilotName = target.MountedPilot != null && target.MountedPilot.Data != null
            ? target.MountedPilot.Data.cardName
            : "-";
        Debug.Log(
            $"[OnDestroyed][Online] wait begin request:{requestId} "
            + $"target:{target.Data.cardName}(id:{target.Data.id}) pilot:{pilotName}");
        return requestId;
    }

    private int AllocateOnlineOnDestroyedRequestId()
    {
        int requestId = _nextOnlineOnDestroyedRequestId++;
        if (_nextOnlineOnDestroyedRequestId <= 0)
        {
            _nextOnlineOnDestroyedRequestId = 1;
        }

        return requestId;
    }

    /// <summary>
    /// 自軍ユニットの破壊時効果をローカル解決するあいだ、相手に effectthink を出させる。
    /// </summary>
    private int BeginOnlineRemoteEffectThinkForLocalOnDestroyed(
        CardController card,
        PlayerType ownerType)
    {
        return BeginOnlineRemoteEffectThinkForLocalOnDestroyed(card, null, ownerType);
    }

    private int BeginOnlineRemoteEffectThinkForLocalOnDestroyed(
        CardController unit,
        CardController detachedPilot,
        PlayerType ownerType)
    {
        if (!IsOnlineBattle()
            || _applyingRemoteBattleAction
            || ownerType != PlayerType.Player)
        {
            return 0;
        }

        bool hasResolution = HasOnDestroyedResolution(unit) || HasOnDestroyedResolution(detachedPilot);
        if (!hasResolution)
        {
            return 0;
        }

        int requestId = AllocateOnlineOnDestroyedRequestId();
        _activeLocalOnDestroyedRemoteThinkRequestId = requestId;
        SendOnlineBattleMessage(EosOnlineBattleMessage.CreateEffectThinkWait(
            OnlineOnDestroyedCompletePayload.ToJson(requestId, -1)));
        string unitName = unit != null && unit.Data != null ? unit.Data.cardName : "?";
        string pilotName = detachedPilot != null && detachedPilot.Data != null
            ? detachedPilot.Data.cardName
            : "-";
        Debug.Log(
            $"[OnDestroyed][Online] EffectThinkWait sent request:{requestId} "
            + $"unit:{unitName} pilot:{pilotName}");
        return requestId;
    }

    /// <summary>自軍カードの破壊時効果を所有者側で解決中か（配備同期のオフターン許可に使う）。</summary>
    private bool IsResolvingLocalOwnerOnDestroyedEffects()
    {
        return _remoteDestroyedResolutionRunning
            || _activeResolvingOnDestroyedRequestId > 0
            || _activeLocalOnDestroyedRemoteThinkRequestId > 0;
    }

    /// <summary>
    /// 自軍破壊時効果の解決完了を相手へ通知し、相手の effectthink を閉じる。
    /// </summary>
    private void EndOnlineRemoteEffectThinkForLocalOnDestroyed(int requestId)
    {
        if (requestId <= 0 || !IsOnlineBattle())
        {
            return;
        }

        // Look OK 等で既に送信済みなら二重送信しない
        if (_activeLocalOnDestroyedRemoteThinkRequestId != requestId)
        {
            return;
        }

        _activeLocalOnDestroyedRemoteThinkRequestId = 0;
        int ownerDeckRemain = cardGameRule != null ? cardGameRule.GetRemainingCount() : -1;
        int returnedId = _pendingOnDestroyedReturnedToHandCardId;
        _pendingOnDestroyedReturnedToHandCardId = -1;
        SendOnlineBattleMessage(EosOnlineBattleMessage.CreateOnDestroyedComplete(
            OnlineOnDestroyedCompletePayload.ToJson(requestId, ownerDeckRemain, returnedId)));
        Debug.Log(
            $"[OnDestroyed][Online] EffectThinkWait complete sent request:{requestId} "
            + $"deckRemain:{ownerDeckRemain} returnedToHand:{returnedId}");
    }

    private void HandleRemoteEffectThinkWait(string payload)
    {
        if (!OnlineOnDestroyedCompletePayload.TryParse(payload, out OnlineOnDestroyedCompletePayload wait))
        {
            Debug.LogWarning($"[OnDestroyed][Online] invalid EffectThinkWait payload:{payload}");
            return;
        }

        _pendingRemoteOnDestroyedRequestIds.Add(wait.requestId);
        ShowOnlineEffectThinkOverlay();
        Debug.Log($"[OnDestroyed][Online] EffectThinkWait received request:{wait.requestId}");
    }

    /// <summary>
    /// 相手所有カードの破壊時効果は相手クライアントで解決する。
    /// </summary>
    private bool ShouldDelegateOnDestroyedToRemoteOwner(PlayerType ownerType)
    {
        return IsOnlineBattle()
            && !_applyingRemoteBattleAction
            && ownerType == PlayerType.Enemy;
    }

    /// <summary>
    /// EffectSync 受信側（破壊されたカードの所有者）で破壊時効果を解決する。
    /// Look／回収が終わるまでカード実体を保持し、完了後に場から除去して送信側へ通知する。
    /// </summary>
    private void ApplyRemoteDestroyedUnitWithOnDestroyedEffects(
        CardController unit,
        int requestId,
        CardController destroyedBy = null)
    {
        _remoteDestroyedResolutionQueue.Enqueue(new RemoteDestroyedResolution
        {
            Unit = unit,
            RequestId = requestId,
            DestroyedBy = destroyedBy
        });

        // リモート適用中（_applyingRemoteBattleAction）は配備 PlayCard / Rest 同期が抑止されるため、
        // 所有者側の破壊時効果はそのフラグ解除後に開始する。
        if (_applyingRemoteBattleAction)
        {
            _resumeRemoteDestroyedAfterRemoteApply = true;
            Debug.Log(
                $"[OnDestroyed][Online] defer owner resolve until remote apply ends "
                + $"request:{requestId} queued:{_remoteDestroyedResolutionQueue.Count}");
            return;
        }

        TryRunNextRemoteDestroyedResolution();
    }

    /// <summary>リモート適用終了後に保留していた所有者破壊時解決を開始する。</summary>
    private void ResumeDeferredRemoteDestroyedResolutionsIfNeeded()
    {
        if (!_resumeRemoteDestroyedAfterRemoteApply)
        {
            return;
        }

        _resumeRemoteDestroyedAfterRemoteApply = false;
        TryRunNextRemoteDestroyedResolution();
    }

    private void TryRunNextRemoteDestroyedResolution()
    {
        if (_remoteDestroyedResolutionRunning || _remoteDestroyedResolutionQueue.Count == 0)
        {
            return;
        }

        RemoteDestroyedResolution entry = _remoteDestroyedResolutionQueue.Dequeue();
        CardController unit = entry.Unit;
        int requestId = entry.RequestId;
        _remoteDestroyedResolutionRunning = true;
        _activeResolvingOnDestroyedRequestId = requestId;
        _activeResolvingOnDestroyedCompleteSent = false;

        void Complete()
        {
            // AddSelfToHand 等で既に手札へ戻っている場合は場からの除去をスキップ
            if (unit != null && unit.Data != null && IsCardOnBattleZone(unit))
            {
                ApplyRemoteUnitToTrash(unit);
            }

            // Look OK で未送信なら、ここで完了を送る（UIなし自動解決など）
            SendOnlineOnDestroyedCompleteIfNeeded(requestId);
            _activeResolvingOnDestroyedRequestId = 0;
            _activeResolvingOnDestroyedCompleteSent = false;
            _remoteDestroyedResolutionRunning = false;
            TryRunNextRemoteDestroyedResolution();
        }

        if (unit == null || unit.Data == null)
        {
            Complete();
            return;
        }

        PlayerType ownerType = ResolveCardOwner(unit.transform);
        CardController detachedPilot = null;
        if (unit.Data.IsUnitLike() && unit.MountedPilot != null)
        {
            detachedPilot = unit.DetachMountedPilotWithoutDestroy();
        }

        bool hasAny =
            (ownerType == PlayerType.Player)
            && (HasOnDestroyedResolution(unit) || HasOnDestroyedResolution(detachedPilot));
        if (hasAny)
        {
            Debug.Log(
                $"[OnDestroyed][Online] resolve locally request:{requestId} "
                + $"card:{unit.Data.cardName}(id:{unit.Data.id}) "
                + $"pilot:{(detachedPilot != null && detachedPilot.Data != null ? detachedPilot.Data.cardName : "-")}");
            RunOrDeferUnitAndPilotOnDestroyedEffects(
                unit,
                detachedPilot,
                ownerType,
                () =>
                {
                    if (detachedPilot != null
                        && unitsPendingSendToTrash.Contains(detachedPilot))
                    {
                        FinishSendCardToTrash(detachedPilot, ownerType);
                    }
                    else if (detachedPilot != null && IsCardOnBattleZone(detachedPilot))
                    {
                        FinishSendCardToTrash(detachedPilot, ownerType);
                    }

                    Complete();
                },
                entry.DestroyedBy);
            return;
        }

        if (detachedPilot != null)
        {
            FinishSendCardToTrash(detachedPilot, ownerType);
        }

        Complete();
    }

    /// <summary>
    /// 所有者側が Look 等の OK を押したとき、破壊側の effectthink を解除する完了通知を送る。
    /// </summary>
    private void NotifyOnlineOnDestroyedPlayerAcknowledged()
    {
        if (_activeResolvingOnDestroyedRequestId > 0)
        {
            SendOnlineOnDestroyedCompleteIfNeeded(_activeResolvingOnDestroyedRequestId);
        }

        // 自軍破壊時効果（Sazabi 自軍破壊など）の Look OK でも相手の待機を先に閉じる
        if (_activeLocalOnDestroyedRemoteThinkRequestId > 0)
        {
            EndOnlineRemoteEffectThinkForLocalOnDestroyed(_activeLocalOnDestroyedRemoteThinkRequestId);
        }
    }

    private void SendOnlineOnDestroyedCompleteIfNeeded(int requestId)
    {
        if (requestId <= 0 || !IsOnlineBattle())
        {
            return;
        }

        if (_activeResolvingOnDestroyedRequestId == requestId
            && _activeResolvingOnDestroyedCompleteSent)
        {
            return;
        }

        if (_activeResolvingOnDestroyedRequestId == requestId)
        {
            _activeResolvingOnDestroyedCompleteSent = true;
        }

        // Look で自山札から手札回収した後の残数を破壊側ミラーへ伝える
        int ownerDeckRemain = cardGameRule != null ? cardGameRule.GetRemainingCount() : -1;
        int returnedId = _pendingOnDestroyedReturnedToHandCardId;
        _pendingOnDestroyedReturnedToHandCardId = -1;
        SendOnlineBattleMessage(EosOnlineBattleMessage.CreateOnDestroyedComplete(
            OnlineOnDestroyedCompletePayload.ToJson(requestId, ownerDeckRemain, returnedId)));
        Debug.Log(
            $"[OnDestroyed][Online] complete sent request:{requestId} deckRemain:{ownerDeckRemain} "
            + $"returnedToHand:{returnedId}");
    }

    private void HandleRemoteOnDestroyedComplete(string payload)
    {
        if (!OnlineOnDestroyedCompletePayload.TryParse(payload, out OnlineOnDestroyedCompletePayload complete))
        {
            Debug.LogWarning($"[OnDestroyed][Online] invalid complete payload:{payload}");
            return;
        }

        if (!_pendingRemoteOnDestroyedRequestIds.Remove(complete.requestId))
        {
            Debug.LogWarning($"[OnDestroyed][Online] unknown complete request:{complete.requestId}");
            return;
        }

        // 所有者側の Look 手札回収による山札残数を相手（Enemy）ミラーへ反映
        if (complete.ownerDeckRemainCount >= 0)
        {
            FinalizeRemoteDeckRemain(
                enemyCardGameRule,
                PlayerType.Enemy,
                complete.ownerDeckRemainCount);
        }

        if (complete.returnedToHandCardId > 0)
        {
            ApplyRemoteDestroyedUnitReturnedToHand(complete.returnedToHandCardId);
        }

        Debug.Log(
            $"[OnDestroyed][Online] wait complete request:{complete.requestId} "
            + $"deckRemain:{complete.ownerDeckRemainCount} returnedToHand:{complete.returnedToHandCardId} "
            + $"remaining:{_pendingRemoteOnDestroyedRequestIds.Count}");
        if (_pendingRemoteOnDestroyedRequestIds.Count == 0)
        {
            CloseOnlineEffectThinkOverlay();
        }
    }

    /// <summary>相手が破壊時効果でユニットを手札へ戻したのを、Enemy 手札ミラーへ反映する。</summary>
    private void ApplyRemoteDestroyedUnitReturnedToHand(int cardId)
    {
        if (cardId <= 0 || DeckSettinObject.Instance == null || CardImagePrefab == null)
        {
            return;
        }

        CardData data = DeckSettinObject.Instance.GetCardDataById(cardId);
        if (data == null || enemyCardGameRule?.HandScrollContent == null)
        {
            Debug.LogWarning($"[OnDestroyed][Online] returned-to-hand mirror failed cardId:{cardId}");
            return;
        }

        // 誤って相手トラッシュへ乗っていたら除去
        enemyCardGameRule.TryRemoveCardFromTrash(cardId, out _);

        GameObject go = Instantiate(CardImagePrefab, enemyCardGameRule.HandScrollContent);
        CardController cc = go.GetComponent<CardController>();
        if (cc == null)
        {
            Destroy(go);
            return;
        }

        cc.SetUp(data, OnCardClicked);
        RegisterCardInHandLists(cc, PlayerType.Enemy);
        enemyCardGameRule.ApplyHandZoneLayoutToCard(cc);
        enemyCardGameRule.RefreshHandCountDisplay();
        Debug.Log($"[OnDestroyed][Online] enemy hand mirror add {data.cardName}(id:{cardId})");
    }

    private void ShowOnlineEffectThinkOverlay()
    {
        if (_activeOnlineEffectThinkRoot != null)
        {
            return;
        }

        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            return;
        }

        isOnlineEffectThinkPauseOpen = true;
        GameObject root = new GameObject(
            "OnlineEffectThinkPause",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        _activeOnlineEffectThinkRoot = root;
        root.transform.SetParent(canvas.transform, false);
        root.transform.SetAsLastSibling();
        root.SetFullSize();

        Image dim = root.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.55f);
        dim.raycastTarget = true;

        TextMeshProUGUI title = root.CreateChildTextCustom(
            "EffectThinkTitle",
            UIAnchor.TopCenter,
            720,
            56);
        title.text = "effectthink";
        title.color = new Color(1f, 0.95f, 0.2f, 1f);
        title.fontSize = 26;
        title.alignment = TextAlignmentOptions.Center;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -100f);

        TextMeshProUGUI sub = root.CreateChildTextCustom(
            "EffectThinkSub",
            UIAnchor.TopCenter,
            720,
            44);
        sub.text = "相手の効果解決を待っています…";
        sub.color = Color.white;
        sub.fontSize = 18;
        sub.alignment = TextAlignmentOptions.Center;
        sub.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -150f);
    }

    private void CloseOnlineEffectThinkOverlay()
    {
        isOnlineEffectThinkPauseOpen = false;
        if (_activeOnlineEffectThinkRoot != null)
        {
            Destroy(_activeOnlineEffectThinkRoot);
            _activeOnlineEffectThinkRoot = null;
        }
    }

    private void ResetOnlineOnDestroyedWaitState()
    {
        _pendingRemoteOnDestroyedRequestIds.Clear();
        _remoteDestroyedResolutionQueue.Clear();
        _remoteDestroyedResolutionRunning = false;
        _resumeRemoteDestroyedAfterRemoteApply = false;
        _activeResolvingOnDestroyedRequestId = 0;
        _activeResolvingOnDestroyedCompleteSent = false;
        _activeLocalOnDestroyedRemoteThinkRequestId = 0;
        _pendingOnDestroyedReturnedToHandCardId = -1;
        _nextOnlineOnDestroyedRequestId = 1;
        CloseOnlineEffectThinkOverlay();
    }
}
