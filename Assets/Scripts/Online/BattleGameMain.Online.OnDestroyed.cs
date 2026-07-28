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

    private sealed class RemoteDestroyedResolution
    {
        public CardController Unit;
        public int RequestId;
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
        SendOnlineBattleMessage(EosOnlineBattleMessage.CreateOnDestroyedComplete(
            OnlineOnDestroyedCompletePayload.ToJson(requestId, ownerDeckRemain)));
        Debug.Log(
            $"[OnDestroyed][Online] EffectThinkWait complete sent request:{requestId} "
            + $"deckRemain:{ownerDeckRemain}");
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
        int requestId)
    {
        _remoteDestroyedResolutionQueue.Enqueue(new RemoteDestroyedResolution
        {
            Unit = unit,
            RequestId = requestId
        });
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
            if (unit != null && unit.Data != null)
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
                    if (detachedPilot != null)
                    {
                        FinishSendCardToTrash(detachedPilot, ownerType);
                    }

                    Complete();
                });
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
        SendOnlineBattleMessage(EosOnlineBattleMessage.CreateOnDestroyedComplete(
            OnlineOnDestroyedCompletePayload.ToJson(requestId, ownerDeckRemain)));
        Debug.Log(
            $"[OnDestroyed][Online] complete sent request:{requestId} deckRemain:{ownerDeckRemain}");
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

        Debug.Log(
            $"[OnDestroyed][Online] wait complete request:{complete.requestId} "
            + $"deckRemain:{complete.ownerDeckRemainCount} "
            + $"remaining:{_pendingRemoteOnDestroyedRequestIds.Count}");
        if (_pendingRemoteOnDestroyedRequestIds.Count == 0)
        {
            CloseOnlineEffectThinkOverlay();
        }
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
        _activeResolvingOnDestroyedRequestId = 0;
        _activeResolvingOnDestroyedCompleteSent = false;
        _activeLocalOnDestroyedRemoteThinkRequestId = 0;
        _nextOnlineOnDestroyedRequestId = 1;
        CloseOnlineEffectThinkOverlay();
    }
}
