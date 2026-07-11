using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>手札からの捨て札（DiscardFromHand）と公開 UI。</summary>
public partial class BattleGameMain
{
    private GameObject _activeHandDiscardRevealRoot;

    private static bool EffectRequiresManualHandSelection(EffectData effect)
    {
        return effect != null && effect.type.RequiresManualHandSelection();
    }

    private PlayerType ResolveHandDiscardOwner(PlayerType sourceOwner, EffectData effect)
    {
        if (effect == null)
        {
            return sourceOwner;
        }

        if (effect.target == TargetType.EnemyPlayer)
        {
            return sourceOwner == PlayerType.Player ? PlayerType.Enemy : PlayerType.Player;
        }

        return sourceOwner;
    }

    private CardGameRule ResolveHandRule(PlayerType handOwner)
    {
        return handOwner == PlayerType.Player ? cardGameRule : enemyCardGameRule;
    }

    private List<CardController> CollectSelectableHandCards(PlayerType handOwner)
    {
        CardGameRule rule = ResolveHandRule(handOwner);
        List<CardController> hand = CollectHandControllers(rule);
        List<CardController> result = new List<CardController>(hand.Count);
        for (int i = 0; i < hand.Count; i++)
        {
            CardController card = hand[i];
            if (card != null && card.Data != null)
            {
                result.Add(card);
            }
        }

        return result;
    }

    /// <param name="onDone">true=必要枚数を捨てた / false=キャンセル・手札不足で中断</param>
    private void TryExecuteManualHandSelectionEffect(
        CardController sourceCard,
        PlayerType ownerType,
        EffectData effect,
        Action<bool> onDone)
    {
        if (effect == null || effect.type != EffectType.DiscardFromHand)
        {
            onDone?.Invoke(true);
            return;
        }

        PlayerType handOwner = ResolveHandDiscardOwner(ownerType, effect);
        int magnitude = ResolveEffectMagnitude(effect, ownerType, sourceCard);
        int requiredCount = effect.GetHandDiscardRequiredCount(magnitude);
        if (effect.UsesHandMultiSelection(requiredCount))
        {
            StartCoroutine(ExecuteMultiDiscardFromHandSelectionCoroutine(
                sourceCard,
                ownerType,
                handOwner,
                effect,
                requiredCount,
                onDone));
            return;
        }

        StartCoroutine(ExecuteDiscardFromHandSelectionCoroutine(
            sourceCard,
            ownerType,
            handOwner,
            effect,
            requiredCount,
            onDone));
    }

    private IEnumerator ExecuteMultiDiscardFromHandSelectionCoroutine(
        CardController sourceCard,
        PlayerType ownerType,
        PlayerType handOwner,
        EffectData effect,
        int requiredCount,
        Action<bool> onDone)
    {
        List<CardController> candidates = CollectSelectableHandCards(handOwner);
        if (candidates.Count < requiredCount)
        {
            Debug.LogWarning(
                $"[DiscardFromHand] 手札が{requiredCount}枚未満のため中断 (owner:{handOwner} "
                + $"hand:{candidates.Count} source:{sourceCard?.Data?.cardName})");
            onDone?.Invoke(false);
            yield break;
        }

        if (handOwner == PlayerType.Enemy)
        {
            List<CardController> aiPicks = PickEnemyAiHandDiscardTargets(candidates, requiredCount);
            if (aiPicks.Count < requiredCount)
            {
                onDone?.Invoke(false);
                yield break;
            }

            for (int i = 0; i < aiPicks.Count; i++)
            {
                yield return DiscardHandCardWithRevealCoroutine(aiPicks[i], handOwner, effect, ownerType);
            }

            onDone?.Invoke(true);
            yield break;
        }

        bool resolved = false;
        List<CardController> selected = null;
        OpenManualMultiHandTargetSelectionUI(
            sourceCard,
            handOwner,
            effect,
            candidates,
            requiredCount,
            picks =>
            {
                selected = picks;
                resolved = true;
            });

        yield return new WaitUntil(() => resolved);

        if (selected == null || selected.Count < requiredCount)
        {
            Debug.Log(
                $"[DiscardFromHand] {requiredCount}枚未選択のため中断 (source:{sourceCard?.Data?.cardName})");
            onDone?.Invoke(false);
            yield break;
        }

        for (int i = 0; i < selected.Count; i++)
        {
            yield return DiscardHandCardWithRevealCoroutine(selected[i], handOwner, effect, ownerType);
        }

        onDone?.Invoke(true);
    }

    private IEnumerator ExecuteDiscardFromHandSelectionCoroutine(
        CardController sourceCard,
        PlayerType ownerType,
        PlayerType handOwner,
        EffectData effect,
        int remaining,
        Action<bool> onDone)
    {
        if (remaining <= 0)
        {
            onDone?.Invoke(true);
            yield break;
        }

        List<CardController> candidates = CollectSelectableHandCards(handOwner);
        if (candidates.Count == 0)
        {
            Debug.LogWarning(
                $"[DiscardFromHand] 手札が空のため中断 (owner:{handOwner} source:{sourceCard?.Data?.cardName})");
            onDone?.Invoke(false);
            yield break;
        }

        if (handOwner == PlayerType.Enemy)
        {
            CardController picked = PickEnemyAiHandDiscardTarget(candidates);
            if (picked != null)
            {
                yield return DiscardHandCardWithRevealCoroutine(picked, handOwner, effect, ownerType);
            }

            yield return ExecuteDiscardFromHandSelectionCoroutine(
                sourceCard,
                ownerType,
                handOwner,
                effect,
                remaining - 1,
                onDone);
            yield break;
        }

        bool resolved = false;
        CardController selected = null;
        OpenManualHandTargetSelectionUI(
            sourceCard,
            handOwner,
            effect,
            candidates,
            picked =>
            {
                selected = picked;
                resolved = true;
            });

        yield return new WaitUntil(() => resolved);

        if (selected == null)
        {
            Debug.Log(
                $"[DiscardFromHand] キャンセルにより中断 (source:{sourceCard?.Data?.cardName})");
            onDone?.Invoke(false);
            yield break;
        }

        yield return DiscardHandCardWithRevealCoroutine(selected, handOwner, effect, ownerType);

        yield return ExecuteDiscardFromHandSelectionCoroutine(
            sourceCard,
            ownerType,
            handOwner,
            effect,
            remaining - 1,
            onDone);
    }

    private IEnumerator DiscardHandCardWithRevealCoroutine(
        CardController handCard,
        PlayerType handOwner,
        EffectData effect,
        PlayerType effectOwner)
    {
        if (handCard == null || handCard.Data == null)
        {
            yield break;
        }

        int cardId = handCard.Data.id;
        string cardName = handCard.Data.cardName;
        bool reveal = effect != null && effect.revealDiscardedToOpponent;

        DiscardHandCardInstance(handCard, handOwner);

        if (!reveal)
        {
            yield break;
        }

        if (handOwner == PlayerType.Player)
        {
            RecordEnemyAiMemorizedPlayerTrashCard(cardId, "DiscardFromHand");
        }

        yield return WaitForHandDiscardRevealAcknowledgedCoroutine(
            cardId,
            cardName,
            handOwner,
            effectOwner,
            isInitiator: handOwner == PlayerType.Player && effectOwner == PlayerType.Player);
    }

    private void DiscardHandCardInstance(CardController handCard, PlayerType handOwner)
    {
        if (handCard == null || handCard.Data == null)
        {
            return;
        }

        CardGameRule rule = ResolveHandRule(handOwner);
        rule?.AddCardToTrash(handCard.Data.id);
        RemoveCardFromHandLists(handCard, handOwner);
        Destroy(handCard.gameObject);
        Debug.Log(
            $"[DiscardFromHand] {handCard.Data.cardName}(id:{handCard.Data.id}) → trash side:{handOwner}");
    }

    private CardController PickEnemyAiHandDiscardTarget(List<CardController> candidates)
    {
        List<CardController> picks = PickEnemyAiHandDiscardTargets(candidates, 1);
        return picks.Count > 0 ? picks[0] : null;
    }

    private List<CardController> PickEnemyAiHandDiscardTargets(List<CardController> candidates, int count)
    {
        List<CardController> picks = new List<CardController>();
        if (candidates == null || candidates.Count == 0 || count <= 0)
        {
            return picks;
        }

        List<CardController> sorted = new List<CardController>(candidates);
        sorted.Sort((a, b) =>
        {
            int costA = a != null ? a.CurrentCost : int.MaxValue;
            int costB = b != null ? b.CurrentCost : int.MaxValue;
            return costA.CompareTo(costB);
        });

        int take = Mathf.Min(count, sorted.Count);
        for (int i = 0; i < take; i++)
        {
            if (sorted[i] != null)
            {
                picks.Add(sorted[i]);
            }
        }

        return picks;
    }

    private void OpenManualHandTargetSelectionUI(
        CardController source,
        PlayerType handOwner,
        EffectData effect,
        List<CardController> candidates,
        Action<CardController> onPicked)
    {
        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            onPicked?.Invoke(null);
            return;
        }

        DestroyActiveOnActionPopupIfAny();
        GameObject root = new GameObject(
            "ManualHandTargetSelect",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        activeOnActionPopupRoot = root;
        isOnActionPopupOpen = true;
        root.transform.SetParent(canvas.transform, false);
        root.transform.SetAsLastSibling();
        root.SetFullSize();
        Image dim = root.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.55f);
        dim.raycastTarget = true;

        TextMeshProUGUI title = root.CreateChildTextCustom("ManualHandTargetTitle", UIAnchor.TopCenter, 720, 48);
        title.text = FormatHandDiscardSelectionTitle(effect, source, 1);
        title.color = Color.white;
        title.fontSize = 24;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -20f);

        GameObject scrollGo = root.CreateGridScrollView(680, 360, UIAnchor.TopCenter);
        RectTransform scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.anchoredPosition = new Vector2(0f, -80f);
        scrollGo.ConfigureGridCellFromViewportHeight(0.78f, 56f);
        ScrollRect sr = scrollGo.GetComponent<ScrollRect>();
        RectTransform content = sr != null ? sr.content : null;

        bool resolved = false;
        for (int i = 0; i < candidates.Count; i++)
        {
            CardController handCard = candidates[i];
            if (handCard == null || handCard.Data == null || content == null)
            {
                continue;
            }

            GameObject cardItem = Instantiate(CardImagePrefab, content);
            CardController preview = cardItem.GetComponent<CardController>();
            preview.SetUp(handCard.Data, _ => { });
            CardController pickedRef = handCard;
            Button btn = cardItem.GetComponent<Button>() ?? cardItem.AddComponent<Button>();
            btn.onClick.AddListener(() =>
            {
                if (resolved)
                {
                    return;
                }

                resolved = true;
                Destroy(root);
                activeOnActionPopupRoot = null;
                isOnActionPopupOpen = false;
                onPicked?.Invoke(pickedRef);
            });
        }

        Button cancel = root.CreateChildButton("CancelSkipDiscard");
        RectTransform cancelRt = cancel.GetComponent<RectTransform>();
        cancelRt.sizeDelta = new Vector2(200f, 46f);
        cancelRt.anchoredPosition = new Vector2(0f, 48f);
        cancel.onClick.AddListener(() =>
        {
            if (resolved)
            {
                return;
            }

            resolved = true;
            Destroy(root);
            activeOnActionPopupRoot = null;
            isOnActionPopupOpen = false;
            onPicked?.Invoke(null);
        });
    }

    private void OpenManualMultiHandTargetSelectionUI(
        CardController source,
        PlayerType handOwner,
        EffectData effect,
        List<CardController> candidates,
        int requiredCount,
        Action<List<CardController>> onConfirmed)
    {
        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            onConfirmed?.Invoke(new List<CardController>());
            return;
        }

        DestroyActiveOnActionPopupIfAny();
        GameObject root = new GameObject(
            "ManualMultiHandTargetSelect",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        activeOnActionPopupRoot = root;
        isOnActionPopupOpen = true;
        root.transform.SetParent(canvas.transform, false);
        root.transform.SetAsLastSibling();
        root.SetFullSize();
        Image dim = root.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.55f);
        dim.raycastTarget = true;

        TextMeshProUGUI title = root.CreateChildTextCustom("ManualMultiHandTargetTitle", UIAnchor.TopCenter, 720, 48);
        title.text = FormatHandDiscardSelectionTitle(effect, source, requiredCount);
        title.color = Color.white;
        title.fontSize = 24;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -20f);

        TextMeshProUGUI summary = root.CreateChildTextCustom("ManualMultiHandTargetSummary", UIAnchor.TopCenter, 720, 32);
        summary.text = $"カードをタップで選択（赤＝対象）（{requiredCount}枚）→ OK で確定";
        summary.color = new Color(0.9f, 0.9f, 0.9f);
        summary.fontSize = 18;
        summary.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -58f);

        GameObject scrollGo = root.CreateGridScrollView(680, 360, UIAnchor.TopCenter);
        RectTransform scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.anchoredPosition = new Vector2(0f, -100f);
        scrollGo.ConfigureGridCellFromViewportHeight(0.78f, 56f);
        ScrollRect sr = scrollGo.GetComponent<ScrollRect>();
        RectTransform content = sr != null ? sr.content : null;

        List<CardController> selected = new List<CardController>();
        bool resolved = false;
        int selectMax = effect != null
            ? effect.GetSelectMaxCount(candidates.Count)
            : requiredCount;
        if (selectMax <= 0 || selectMax > requiredCount)
        {
            selectMax = requiredCount;
        }

        void CloseWithSelection(List<CardController> picks)
        {
            if (resolved)
            {
                return;
            }

            resolved = true;
            Destroy(root);
            activeOnActionPopupRoot = null;
            isOnActionPopupOpen = false;
            onConfirmed?.Invoke(picks ?? new List<CardController>());
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            CardController handCard = candidates[i];
            if (handCard == null || handCard.Data == null || content == null)
            {
                continue;
            }

            GameObject cardItem = Instantiate(CardImagePrefab, content);
            CardController preview = cardItem.GetComponent<CardController>();
            preview.SetUp(handCard.Data, _ => { });
            Button btn = cardItem.GetComponent<Button>() ?? cardItem.AddComponent<Button>();
            Image baseImage = cardItem.GetComponent<Image>();
            Color original = baseImage != null ? baseImage.color : Color.white;
            CardController pickedRef = handCard;
            btn.onClick.AddListener(() =>
            {
                if (resolved)
                {
                    return;
                }

                if (selected.Contains(pickedRef))
                {
                    selected.Remove(pickedRef);
                    if (baseImage != null)
                    {
                        baseImage.color = original;
                    }
                }
                else
                {
                    if (selected.Count >= selectMax)
                    {
                        Debug.Log($"手札は最大{selectMax}枚まで選択できます。");
                        return;
                    }

                    selected.Add(pickedRef);
                    if (baseImage != null)
                    {
                        baseImage.color = ManualMultiSelectHighlightColor;
                    }
                }
            });
        }

        Button okBtn = root.CreateChildButton("OK");
        RectTransform okRt = okBtn.GetComponent<RectTransform>();
        okRt.sizeDelta = new Vector2(160f, 44f);
        okRt.anchorMin = new Vector2(0.5f, 0f);
        okRt.anchorMax = new Vector2(0.5f, 0f);
        okRt.pivot = new Vector2(0.5f, 0f);
        okRt.anchoredPosition = new Vector2(-90f, 36f);
        TextMeshProUGUI okLabel = okBtn.GetComponentInChildren<TextMeshProUGUI>();
        if (okLabel != null)
        {
            okLabel.text = "OK";
        }

        okBtn.onClick.AddListener(() =>
        {
            if (selected.Count < requiredCount)
            {
                Debug.Log($"手札を{requiredCount}枚選択してください。");
                return;
            }

            CloseWithSelection(new List<CardController>(selected));
        });

        Button cancel = root.CreateChildButton("キャンセル");
        RectTransform cancelRt = cancel.GetComponent<RectTransform>();
        cancelRt.sizeDelta = new Vector2(160f, 44f);
        cancelRt.anchorMin = new Vector2(0.5f, 0f);
        cancelRt.anchorMax = new Vector2(0.5f, 0f);
        cancelRt.pivot = new Vector2(0.5f, 0f);
        cancelRt.anchoredPosition = new Vector2(90f, 36f);
        cancel.onClick.AddListener(() => CloseWithSelection(new List<CardController>()));
    }

    private static string FormatHandDiscardSelectionTitle(EffectData effect, CardController source, int count)
    {
        string revealHint = effect != null && effect.revealDiscardedToOpponent
            ? "（相手に公開）"
            : string.Empty;
        string sourceName = source != null && source.Data != null ? source.Data.cardName : string.Empty;
        return string.IsNullOrEmpty(sourceName)
            ? $"手札から{count}枚をトラッシュに捨てる{revealHint}"
            : $"手札から{count}枚をトラッシュに捨てる{revealHint} — {sourceName}";
    }

    private IEnumerator ShowHandDiscardRevealPanelCoroutine(
        int cardId,
        string cardName,
        bool isOpponentView)
    {
        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            yield break;
        }

        CloseHandDiscardRevealPanelIfAny();
        CardData data = DeckSettinObject.Instance != null
            ? DeckSettinObject.Instance.GetCardDataById(cardId)
            : null;

        GameObject root = new GameObject(
            "HandDiscardReveal",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        _activeHandDiscardRevealRoot = root;
        isOnActionPopupOpen = true;
        root.transform.SetParent(canvas.transform, false);
        root.transform.SetAsLastSibling();
        root.SetFullSize();
        Image dim = root.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.65f);
        dim.raycastTarget = true;

        TextMeshProUGUI title = root.CreateChildTextCustom("HandDiscardRevealTitle", UIAnchor.TopCenter, 720, 48);
        title.text = isOpponentView
            ? "相手が手札から捨てたカード（公開）"
            : "捨てたカードを相手に公開";
        title.color = Color.white;
        title.fontSize = 24;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -30f);

        TextMeshProUGUI nameLabel = root.CreateChildTextCustom("HandDiscardRevealName", UIAnchor.TopCenter, 720, 36);
        nameLabel.text = data != null ? data.cardName : cardName;
        nameLabel.color = new Color(0.95f, 0.95f, 0.95f);
        nameLabel.fontSize = 20;
        nameLabel.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -72f);

        if (CardImagePrefab != null && data != null)
        {
            GameObject cardGo = Instantiate(CardImagePrefab, root.transform);
            RectTransform cardRt = cardGo.GetComponent<RectTransform>();
            if (cardRt != null)
            {
                cardRt.anchorMin = new Vector2(0.5f, 0.5f);
                cardRt.anchorMax = new Vector2(0.5f, 0.5f);
                cardRt.pivot = new Vector2(0.5f, 0.5f);
                cardRt.sizeDelta = new Vector2(180f, 252f);
                cardRt.anchoredPosition = new Vector2(0f, 20f);
            }

            CardController preview = cardGo.GetComponent<CardController>();
            preview?.SetUp(data, _ => { });
            Button blocker = cardGo.GetComponent<Button>();
            if (blocker != null)
            {
                blocker.interactable = false;
            }
        }

        bool acknowledged = false;
        Button ok = root.CreateChildButton("HandDiscardRevealOk");
        RectTransform okRt = ok.GetComponent<RectTransform>();
        okRt.sizeDelta = new Vector2(180f, 46f);
        okRt.anchoredPosition = new Vector2(0f, -200f);
        TextMeshProUGUI okLabel = ok.GetComponentInChildren<TextMeshProUGUI>();
        if (okLabel != null)
        {
            okLabel.text = "OK";
        }

        ok.onClick.AddListener(() => { acknowledged = true; });
        yield return new WaitUntil(() => acknowledged);

        CloseHandDiscardRevealPanelIfAny();
    }

    private void CloseHandDiscardRevealPanelIfAny()
    {
        if (_activeHandDiscardRevealRoot != null)
        {
            Destroy(_activeHandDiscardRevealRoot);
            _activeHandDiscardRevealRoot = null;
        }

        if (activeOnActionPopupRoot == null)
        {
            isOnActionPopupOpen = false;
        }
    }
}
