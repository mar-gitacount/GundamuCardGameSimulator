using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// バトルゾーンのユニット上限（6体・トークン含む）。
/// 7体目を出すときはアラート＋場の一覧から1体をトラッシュへ送り、Cancel なら配備しない。
/// </summary>
public partial class BattleGameMain
{
    /// <summary>ルール上、バトルゾーンに同時に出せるユニット／ユニットトークンの上限。</summary>
    private const int MaxBattleZoneUnits = 6;

    private bool _battleZoneCapUiOpen;

    private int CountBattleZoneUnits(PlayerType ownerType)
    {
        List<CardController> zone = ownerType == PlayerType.Player
            ? playerBattleZoneCards
            : enemyBattleZoneCards;
        if (zone == null)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < zone.Count; i++)
        {
            CardController unit = zone[i];
            if (unit == null || unit.Data == null || !unit.Data.IsUnitLike())
            {
                continue;
            }

            if (unitsPendingSendToTrash != null && unitsPendingSendToTrash.Contains(unit))
            {
                continue;
            }

            count++;
        }

        return count;
    }

    private bool IsBattleZoneAtCapacity(PlayerType ownerType)
    {
        return CountBattleZoneUnits(ownerType) >= MaxBattleZoneUnits;
    }

    /// <summary>置換候補（生存中のユニット／ユニットトークン）。</summary>
    private List<CardController> CollectBattleZoneReplaceCandidates(PlayerType ownerType)
    {
        List<CardController> zone = ownerType == PlayerType.Player
            ? playerBattleZoneCards
            : enemyBattleZoneCards;
        List<CardController> result = new List<CardController>();
        if (zone == null)
        {
            return result;
        }

        for (int i = 0; i < zone.Count; i++)
        {
            CardController unit = zone[i];
            if (unit == null || unit.Data == null || !unit.Data.IsUnitLike())
            {
                continue;
            }

            if (unitsPendingSendToTrash != null && unitsPendingSendToTrash.Contains(unit))
            {
                continue;
            }

            result.Add(unit);
        }

        return result;
    }

    /// <summary>
    /// 配備枠を確保してから続行。満杯時は UI（プレイヤー）または自動置換（敵／リモート）。
    /// Cancel 時は onCancelled。
    /// </summary>
    private void EnsureBattleZoneDeploySlotThen(
        PlayerType recipient,
        CardController incomingPreview,
        Action onSlotReady,
        Action onCancelled = null)
    {
        StartCoroutine(CoEnsureBattleZoneDeploySlot(recipient, incomingPreview, onSlotReady, onCancelled));
    }

    private IEnumerator CoEnsureBattleZoneDeploySlot(
        PlayerType recipient,
        CardController incomingPreview,
        Action onSlotReady,
        Action onCancelled)
    {
        if (!IsBattleZoneAtCapacity(recipient))
        {
            onSlotReady?.Invoke();
            yield break;
        }

        // オンライン受信側はホスト側で既に枠確保済み想定。万一満杯なら自動置換。
        bool autoResolve = _applyingRemoteBattleAction
            || recipient == PlayerType.Enemy
            || (IsOnlineBattle() && currentPlayerType != PlayerType.Player && recipient == PlayerType.Player);

        if (autoResolve)
        {
            bool madeRoom = false;
            yield return CoAutoMakeBattleZoneSlot(recipient, incomingPreview, ok => madeRoom = ok);
            if (madeRoom)
            {
                onSlotReady?.Invoke();
            }
            else
            {
                onCancelled?.Invoke();
            }

            yield break;
        }

        List<CardController> candidates = CollectBattleZoneReplaceCandidates(recipient);
        if (candidates.Count == 0)
        {
            Debug.LogWarning("[BattleZoneCap] 満杯だが置換候補がありません。配備を中止します。");
            onCancelled?.Invoke();
            yield break;
        }

        bool decided = false;
        bool accepted = false;
        OpenBattleZoneFullReplaceUI(
            incomingPreview,
            recipient,
            candidates,
            picked =>
            {
                if (picked == null)
                {
                    accepted = false;
                    decided = true;
                    return;
                }

                StartCoroutine(CoTrashBattleZoneUnitForSlot(
                    picked,
                    recipient,
                    incomingPreview,
                    () =>
                    {
                        accepted = true;
                        decided = true;
                    },
                    () =>
                    {
                        accepted = false;
                        decided = true;
                    }));
            });

        yield return new WaitUntil(() => decided);
        if (accepted)
        {
            onSlotReady?.Invoke();
        }
        else
        {
            onCancelled?.Invoke();
        }
    }

    private IEnumerator CoAutoMakeBattleZoneSlot(
        PlayerType recipient,
        CardController cause,
        Action<bool> onDone)
    {
        CardController victim = PickAutoBattleZoneReplaceVictim(recipient);
        if (victim == null)
        {
            onDone?.Invoke(false);
            yield break;
        }

        yield return CoTrashBattleZoneUnitForSlot(
            victim,
            recipient,
            cause,
            () => onDone?.Invoke(true),
            () => onDone?.Invoke(false));
    }

    /// <summary>AI／自動用: HP が低いユニットを優先して枠を空ける。</summary>
    private CardController PickAutoBattleZoneReplaceVictim(PlayerType ownerType)
    {
        List<CardController> candidates = CollectBattleZoneReplaceCandidates(ownerType);
        if (candidates.Count == 0)
        {
            return null;
        }

        CardController best = null;
        int bestHp = int.MaxValue;
        int bestAp = int.MaxValue;
        for (int i = 0; i < candidates.Count; i++)
        {
            CardController unit = candidates[i];
            int hp = unit.CurrentHp;
            int ap = unit.CurrentPower;
            if (best == null
                || hp < bestHp
                || (hp == bestHp && ap < bestAp))
            {
                best = unit;
                bestHp = hp;
                bestAp = ap;
            }
        }

        return best;
    }

    private IEnumerator CoTrashBattleZoneUnitForSlot(
        CardController victim,
        PlayerType ownerType,
        CardController cause,
        Action onDone,
        Action onFailed)
    {
        if (victim == null)
        {
            onFailed?.Invoke();
            yield break;
        }

        int pipelineBefore = _pendingSendToTrashPipelines;
        SendCardToTrash(victim, ownerType, cause);
        yield return new WaitUntil(
            () => _pendingSendToTrashPipelines <= pipelineBefore
                  && (victim == null || unitsPendingSendToTrash == null || !unitsPendingSendToTrash.Contains(victim)));

        // さらに Look 等のブロッキング UI が終わるまで待つ
        yield return WaitUntilBlockingChoiceOrTrashUiCleared();

        if (IsBattleZoneAtCapacity(ownerType))
        {
            Debug.LogWarning("[BattleZoneCap] トラッシュ後も満杯のため配備を中止します。");
            onFailed?.Invoke();
            yield break;
        }

        onDone?.Invoke();
    }

    /// <summary>満杯アラート＋バトルゾーン一覧。Cancel=null / 選択=そのユニットをトラッシュ候補。</summary>
    private void OpenBattleZoneFullReplaceUI(
        CardController incomingPreview,
        PlayerType recipient,
        List<CardController> candidates,
        Action<CardController> onPicked)
    {
        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null || candidates == null || candidates.Count == 0)
        {
            onPicked?.Invoke(null);
            return;
        }

        DestroyActiveOnActionPopupIfAny();
        GameObject root = new GameObject(
            "BattleZoneCapReplaceSelect",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        activeOnActionPopupRoot = root;
        isOnActionPopupOpen = true;
        _battleZoneCapUiOpen = true;
        root.transform.SetParent(canvas.transform, false);
        root.transform.SetAsLastSibling();
        root.SetFullSize();

        Image dim = root.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.6f);
        dim.raycastTarget = true;

        TextMeshProUGUI title = root.CreateChildTextCustom(
            "BattleZoneCapTitle",
            UIAnchor.TopCenter,
            760,
            52);
        title.SetLocalizedText(
            "バトルゾーンは6体までです",
            "Battle Zone holds up to 6 units");
        title.color = Color.white;
        title.fontSize = 26;
        title.fontStyle = FontStyles.Bold;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -18f);

        TextMeshProUGUI alert = root.CreateChildTextCustom(
            "BattleZoneCapAlert",
            UIAnchor.TopCenter,
            760,
            64);
        alert.SetLocalizedText(
            "これ以上配備するには、場のユニットを1体トラッシュへ送ってください。\nキャンセルすると配備しません。",
            "To deploy another, trash 1 unit from the field.\nCancel keeps the new unit undeployed.");
        alert.color = new Color(1f, 0.85f, 0.55f);
        alert.fontSize = 18;
        alert.alignment = TextAlignmentOptions.Center;
        alert.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -72f);

        if (CardImagePrefab != null && incomingPreview != null && incomingPreview.Data != null)
        {
            GameObject previewGo = Instantiate(CardImagePrefab, root.transform);
            RectTransform previewRt = previewGo.GetComponent<RectTransform>();
            if (previewRt != null)
            {
                previewRt.anchorMin = new Vector2(0.5f, 1f);
                previewRt.anchorMax = new Vector2(0.5f, 1f);
                previewRt.pivot = new Vector2(0.5f, 1f);
                previewRt.sizeDelta = new Vector2(110f, 154f);
                previewRt.anchoredPosition = new Vector2(0f, -150f);
            }

            CardController previewCc = previewGo.GetComponent<CardController>();
            if (previewCc != null)
            {
                previewCc.SetUp(incomingPreview.Data, _ => { });
            }

            Button previewBlocker = previewGo.GetComponent<Button>();
            if (previewBlocker != null)
            {
                previewBlocker.interactable = false;
            }

            TextMeshProUGUI incomingLabel = root.CreateChildTextCustom(
                "IncomingLabel",
                UIAnchor.TopCenter,
                400,
                28);
            incomingLabel.SetLocalizedText("配備予定", "Incoming");
            incomingLabel.fontSize = 16;
            incomingLabel.color = new Color(0.85f, 0.95f, 1f);
            incomingLabel.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -310f);
        }

        GameObject scrollGo = root.CreateGridScrollView(700, 320, UIAnchor.TopCenter);
        RectTransform scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.anchoredPosition = new Vector2(0f, -360f);
        scrollGo.ConfigureGridCellFromViewportHeight(0.78f, 56f);
        ScrollRect sr = scrollGo.GetComponent<ScrollRect>();
        RectTransform content = sr != null ? sr.content : null;

        bool resolved = false;
        bool acceptPickInput = false;
        List<Button> pickButtons = new List<Button>();

        void CloseUi()
        {
            if (root != null)
            {
                Destroy(root);
            }

            if (activeOnActionPopupRoot == root)
            {
                activeOnActionPopupRoot = null;
            }

            isOnActionPopupOpen = false;
            _battleZoneCapUiOpen = false;
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            CardController candidate = candidates[i];
            if (content == null || candidate == null || candidate.Data == null || CardImagePrefab == null)
            {
                continue;
            }

            GameObject go = Instantiate(CardImagePrefab, content);
            CardController cc = go.GetComponent<CardController>();
            if (cc != null)
            {
                cc.SetUp(candidate.Data, _ => { });
            }

            TextMeshProUGUI statLabel = go.CreateChildTextCustom(
                "CapTargetStat",
                UIAnchor.BottomCenter,
                110,
                28);
            string tokenMark = candidate.Data.IsUnitToken() ? " [Token]" : string.Empty;
            statLabel.text = "AP:" + candidate.CurrentPower + " HP:" + candidate.CurrentHp + tokenMark;
            statLabel.fontSize = 13;
            statLabel.color = Color.white;
            statLabel.alignment = TextAlignmentOptions.Center;

            Button btn = go.GetComponent<Button>();
            if (btn == null)
            {
                btn = go.AddComponent<Button>();
            }

            btn.interactable = false;
            pickButtons.Add(btn);

            CardController pickedRef = candidate;
            btn.onClick.AddListener(() =>
            {
                if (resolved || !acceptPickInput)
                {
                    return;
                }

                resolved = true;
                CloseUi();
                InvokePlayerManualUnitSelectionCallback(() => onPicked?.Invoke(pickedRef));
            });
        }

        StartCoroutine(CoEnableManualUnitPickButtonsAfterOpen(pickButtons, () => acceptPickInput = true));

        Button cancel = root.CreateChildButton(GameLocale.T("キャンセル", "Cancel"));
        RectTransform cancelRt = cancel.GetComponent<RectTransform>();
        cancelRt.sizeDelta = new Vector2(180f, 46f);
        cancelRt.anchorMin = new Vector2(0.5f, 0f);
        cancelRt.anchorMax = new Vector2(0.5f, 0f);
        cancelRt.pivot = new Vector2(0.5f, 0f);
        cancelRt.anchoredPosition = new Vector2(0f, 36f);
        cancel.onClick.AddListener(() =>
        {
            if (resolved)
            {
                return;
            }

            resolved = true;
            CloseUi();
            onPicked?.Invoke(null);
        });
    }

    /// <summary>
    /// 枠確保後に配備アクションを実行するヘルパー（手札配備など）。
    /// </summary>
    private void DeployToBattleZoneWithCapGate(
        PlayerType recipient,
        CardController incoming,
        Action deployAction,
        Action onCancelled = null)
    {
        EnsureBattleZoneDeploySlotThen(
            recipient,
            incoming,
            deployAction,
            onCancelled);
    }
}
