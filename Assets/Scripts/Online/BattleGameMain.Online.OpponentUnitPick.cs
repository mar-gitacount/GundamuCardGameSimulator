using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// オンライン：相手が自軍ユニットを選ぶ（Sazabi の opponentChooses 等）。
/// 要求側は effectthink で待ち、選択側は自軍 UI で選ぶ。
/// </summary>
public partial class BattleGameMain
{
    private int _nextOnlineOpponentUnitPickRequestId = 1;
    private int _pendingOnlineOpponentUnitPickRequestId;
    private CardController _pendingOnlineOpponentUnitPickSource;
    private CardController _pendingOnlineOpponentUnitPickAttacker;
    private PlayerType _pendingOnlineOpponentUnitPickAttackerOwner;
    private EffectData _pendingOnlineOpponentUnitPickEffect;
    private System.Action _pendingOnlineOpponentUnitPickStepResolved;
    private System.Action _pendingOnlineOpponentUnitPickSkipAll;
    private bool _onlineOpponentUnitPickUiOpen;

    [Serializable]
    private class OnlineOpponentUnitPickPayload
    {
        public int requestId;
        public int sourceCardId;
        public int attackerInstanceId;
        public int effectType;
        public int effectTarget;
        public bool optionalPlayerConfirm;
        public bool opponentChoosesTarget;
        public int[] candidateInstanceIds;
        public int chosenInstanceId;
        public bool skipped;
    }

    /// <summary>
    /// 相手クライアントへユニット選択を依頼する。成功時 true（effectthink 表示済み）。
    /// </summary>
    private bool TryBeginOnlineOpponentUnitPick(
        CardController sourceCard,
        CardController attacker,
        PlayerType attackerOwner,
        EffectData effect,
        List<CardController> candidates,
        System.Action stepResolved,
        System.Action onSkipAll)
    {
        if (!IsOnlineBattle()
            || sourceCard == null
            || effect == null
            || candidates == null
            || candidates.Count == 0)
        {
            return false;
        }

        List<int> instanceIds = new List<int>(candidates.Count);
        for (int i = 0; i < candidates.Count; i++)
        {
            CardController c = candidates[i];
            if (c == null || c.BattleInstanceId <= 0)
            {
                continue;
            }

            instanceIds.Add(c.BattleInstanceId);
        }

        if (instanceIds.Count == 0)
        {
            Debug.LogWarning("[OnAttack][Online] OpponentUnitPick: candidate instanceId がありません。");
            return false;
        }

        int requestId = _nextOnlineOpponentUnitPickRequestId++;
        if (_nextOnlineOpponentUnitPickRequestId <= 0)
        {
            _nextOnlineOpponentUnitPickRequestId = 1;
        }

        _pendingOnlineOpponentUnitPickRequestId = requestId;
        _pendingOnlineOpponentUnitPickSource = sourceCard;
        _pendingOnlineOpponentUnitPickAttacker = attacker;
        _pendingOnlineOpponentUnitPickAttackerOwner = attackerOwner;
        _pendingOnlineOpponentUnitPickEffect = effect;
        _pendingOnlineOpponentUnitPickStepResolved = stepResolved;
        _pendingOnlineOpponentUnitPickSkipAll = onSkipAll;

        var payload = new OnlineOpponentUnitPickPayload
        {
            requestId = requestId,
            sourceCardId = sourceCard.Data != null ? sourceCard.Data.id : 0,
            attackerInstanceId = attacker != null ? attacker.BattleInstanceId : 0,
            effectType = (int)effect.type,
            effectTarget = (int)effect.target,
            optionalPlayerConfirm = effect.optionalPlayerConfirm,
            opponentChoosesTarget = effect.opponentChoosesTarget,
            candidateInstanceIds = instanceIds.ToArray()
        };

        SendOnlineBattleMessage(EosOnlineBattleMessage.CreateOpponentUnitPickRequest(
            JsonUtility.ToJson(payload)));
        ShowOnlineEffectThinkOverlay();
        Debug.Log(
            $"[OnAttack][Online] OpponentUnitPick wait request:{requestId} "
            + $"candidates:{instanceIds.Count} effect:{effect.type}");
        return true;
    }

    private void HandleRemoteOpponentUnitPickRequest(string payloadJson)
    {
        OnlineOpponentUnitPickPayload payload = null;
        try
        {
            payload = JsonUtility.FromJson<OnlineOpponentUnitPickPayload>(payloadJson);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[OnAttack][Online] OpponentUnitPickRequest parse failed: {e.Message}");
            return;
        }

        if (payload == null || payload.requestId <= 0)
        {
            return;
        }

        List<CardController> localCandidates = new List<CardController>();
        if (payload.candidateInstanceIds != null)
        {
            for (int i = 0; i < payload.candidateInstanceIds.Length; i++)
            {
                int instanceId = payload.candidateInstanceIds[i];
                CardController unit = FindUnitByInstanceIdEitherZone(instanceId);
                if (unit == null || unit.Data == null || !unit.Data.IsUnitLike() || unit.CurrentHp <= 0)
                {
                    continue;
                }

                // 受信側では自分の場のユニットだけ選べる
                if (ResolveCardOwner(unit.transform) != PlayerType.Player)
                {
                    continue;
                }

                localCandidates.Add(unit);
            }
        }

        if (localCandidates.Count == 0)
        {
            Debug.LogWarning(
                $"[OnAttack][Online] OpponentUnitPickRequest: 選択候補なし request:{payload.requestId}");
            SendOnlineOpponentUnitPickResponse(payload.requestId, 0, skipped: true);
            return;
        }

        EffectData effect = new EffectData
        {
            type = (EffectType)payload.effectType,
            target = (TargetType)payload.effectTarget,
            value = 1,
            selectionMode = EffectSelectionMode.SelectSingle,
            optionalPlayerConfirm = payload.optionalPlayerConfirm,
            opponentChoosesTarget = payload.opponentChoosesTarget
        };

        _onlineOpponentUnitPickUiOpen = true;
        OpenOnlineOpponentUnitPickUi(
            payload.requestId,
            effect,
            localCandidates);
    }

    private void OpenOnlineOpponentUnitPickUi(
        int requestId,
        EffectData effect,
        List<CardController> candidates)
    {
        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            SendOnlineOpponentUnitPickResponse(requestId, 0, skipped: true);
            _onlineOpponentUnitPickUiOpen = false;
            return;
        }

        GameObject root = new GameObject(
            "OnlineOpponentUnitPick",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        activeOnActionPopupRoot = root;
        isOnActionPopupOpen = true;
        root.transform.SetParent(canvas.transform, false);
        root.transform.SetAsLastSibling();
        root.SetFullSize();
        Image bg = root.GetComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.55f);
        bg.raycastTarget = true;

        TMPro.TextMeshProUGUI title = root.CreateChildTextCustom("PickTitle", UIAnchor.TopCenter, 720, 48);
        title.text = FormatOnAttackUnitSelectionTitle(effect, PlayerType.Player);
        title.color = Color.white;
        title.fontSize = 22;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -24f);

        GameObject scrollGo = root.CreateGridScrollView(620, 420, UIAnchor.TopCenter);
        RectTransform scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.anchoredPosition = new Vector2(0f, -80f);
        scrollGo.ConfigureGridCellFromViewportHeight(0.78f, 56f);
        UnityEngine.UI.ScrollRect sr = scrollGo.GetComponent<UnityEngine.UI.ScrollRect>();
        RectTransform content = sr != null ? sr.content : null;

        bool consumed = false;
        void FinishPick(CardController unit, bool skipped)
        {
            if (consumed)
            {
                return;
            }

            consumed = true;
            _onlineOpponentUnitPickUiOpen = false;
            ReleaseOnActionPopupState(root);
            Destroy(root);
            int instanceId = unit != null ? unit.BattleInstanceId : 0;
            SendOnlineOpponentUnitPickResponse(requestId, instanceId, skipped);
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            CardController unit = candidates[i];
            if (content == null || unit == null || unit.Data == null || CardImagePrefab == null)
            {
                continue;
            }

            GameObject cardItem = Instantiate(CardImagePrefab, content);
            CardController itemCc = cardItem.GetComponent<CardController>();
            if (itemCc != null)
            {
                CardController pickedRef = unit;
                itemCc.SetUp(unit.Data, _ => FinishPick(pickedRef, skipped: false));
            }

            UnityEngine.UI.Button btn = cardItem.GetComponent<UnityEngine.UI.Button>();
            if (btn == null)
            {
                btn = cardItem.AddComponent<UnityEngine.UI.Button>();
            }

            CardController clickRef = unit;
            btn.onClick.AddListener(() => FinishPick(clickRef, skipped: false));
        }

        if (effect != null && effect.optionalPlayerConfirm && !effect.opponentChoosesTarget)
        {
            UnityEngine.UI.Button cancel = root.CreateChildButton("Cancel");
            RectTransform cancelRt = cancel.GetComponent<RectTransform>();
            cancelRt.sizeDelta = new Vector2(180f, 46f);
            cancelRt.anchoredPosition = new Vector2(0f, 48f);
            cancel.onClick.AddListener(() => FinishPick(null, skipped: true));
        }
    }

    private void SendOnlineOpponentUnitPickResponse(int requestId, int chosenInstanceId, bool skipped)
    {
        var payload = new OnlineOpponentUnitPickPayload
        {
            requestId = requestId,
            chosenInstanceId = chosenInstanceId,
            skipped = skipped
        };
        SendOnlineBattleMessage(EosOnlineBattleMessage.CreateOpponentUnitPickResponse(
            JsonUtility.ToJson(payload)));
        Debug.Log(
            $"[OnAttack][Online] OpponentUnitPick response request:{requestId} "
            + $"chosenInst:{chosenInstanceId} skipped:{skipped}");
    }

    private void HandleRemoteOpponentUnitPickResponse(string payloadJson)
    {
        OnlineOpponentUnitPickPayload payload = null;
        try
        {
            payload = JsonUtility.FromJson<OnlineOpponentUnitPickPayload>(payloadJson);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[OnAttack][Online] OpponentUnitPickResponse parse failed: {e.Message}");
            return;
        }

        if (payload == null || payload.requestId != _pendingOnlineOpponentUnitPickRequestId)
        {
            Debug.LogWarning(
                $"[OnAttack][Online] OpponentUnitPickResponse ignore request:{payload?.requestId} "
                + $"pending:{_pendingOnlineOpponentUnitPickRequestId}");
            return;
        }

        CardController source = _pendingOnlineOpponentUnitPickSource;
        CardController attacker = _pendingOnlineOpponentUnitPickAttacker;
        PlayerType attackerOwner = _pendingOnlineOpponentUnitPickAttackerOwner;
        EffectData effect = _pendingOnlineOpponentUnitPickEffect;
        System.Action stepResolved = _pendingOnlineOpponentUnitPickStepResolved;
        System.Action skipAll = _pendingOnlineOpponentUnitPickSkipAll;

        ClearPendingOnlineOpponentUnitPickState(closeThink: true);

        if (payload.skipped)
        {
            Debug.Log("[OnAttack][Online] OpponentUnitPick skipped by remote.");
            pendingOnAttackEffectResolvedAttacker = attacker;
            if (skipAll != null)
            {
                skipAll.Invoke();
            }
            else
            {
                stepResolved?.Invoke();
            }

            return;
        }

        CardController chosen = FindUnitByInstanceIdEitherZone(payload.chosenInstanceId);
        if (chosen == null || chosen.Data == null || chosen.CurrentHp <= 0)
        {
            Debug.LogWarning(
                $"[OnAttack][Online] OpponentUnitPick chosen unit missing inst:{payload.chosenInstanceId}");
            pendingOnAttackEffectResolvedAttacker = attacker;
            stepResolved?.Invoke();
            return;
        }

        ApplyEffectToSpecificTargets(
            source,
            attackerOwner,
            effect,
            new List<CardController> { chosen });
        ContinueOnAttackAfterAppliedEffect(attacker, effect, stepResolved);
    }

    private void ClearPendingOnlineOpponentUnitPickState(bool closeThink)
    {
        _pendingOnlineOpponentUnitPickRequestId = 0;
        _pendingOnlineOpponentUnitPickSource = null;
        _pendingOnlineOpponentUnitPickAttacker = null;
        _pendingOnlineOpponentUnitPickEffect = null;
        _pendingOnlineOpponentUnitPickStepResolved = null;
        _pendingOnlineOpponentUnitPickSkipAll = null;
        if (closeThink && !HasPendingRemoteOnDestroyedResolution)
        {
            CloseOnlineEffectThinkOverlay();
        }
    }

    private void ResetOnlineOpponentUnitPickState()
    {
        ClearPendingOnlineOpponentUnitPickState(closeThink: true);
        _onlineOpponentUnitPickUiOpen = false;
    }
}
