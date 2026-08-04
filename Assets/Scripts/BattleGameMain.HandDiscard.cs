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

    /// <summary>
    /// 手札捨てを実行する。onDone(true)=要求枚数を捨て切った／onDone(false)=Skip または枚数不足。
    /// </summary>
    private void TryExecuteManualHandSelectionEffect(
        CardController sourceCard,
        PlayerType ownerType,
        EffectData effect,
        Action<bool> onDone)
    {
        if (effect == null || effect.type != EffectType.DiscardFromHand)
        {
            onDone?.Invoke(false);
            return;
        }

        PlayerType handOwner = ResolveHandDiscardOwner(ownerType, effect);
        int magnitude = ResolveEffectMagnitude(effect, ownerType, sourceCard);
        int discardCount = magnitude > 0 ? magnitude : 1;
        StartCoroutine(ExecuteDiscardFromHandSelectionCoroutine(
            sourceCard,
            ownerType,
            handOwner,
            effect,
            remaining: discardCount,
            requiredCount: discardCount,
            discardedCount: 0,
            onDone));
    }

    /// <summary>完了時に成功可否を無視する呼び出し向け。</summary>
    private void TryExecuteManualHandSelectionEffect(
        CardController sourceCard,
        PlayerType ownerType,
        EffectData effect,
        Action onDone)
    {
        TryExecuteManualHandSelectionEffect(sourceCard, ownerType, effect, _ => onDone?.Invoke());
    }

    private IEnumerator ExecuteDiscardFromHandSelectionCoroutine(
        CardController sourceCard,
        PlayerType ownerType,
        PlayerType handOwner,
        EffectData effect,
        int remaining,
        int requiredCount,
        int discardedCount,
        Action<bool> onDone)
    {
        if (remaining <= 0)
        {
            bool completed = discardedCount >= requiredCount && requiredCount > 0;
            onDone?.Invoke(completed);
            yield break;
        }

        List<CardController> candidates = CollectSelectableHandCards(handOwner);
        if (candidates.Count == 0)
        {
            Debug.LogWarning(
                $"[DiscardFromHand] 手札が空のため打ち切り (owner:{handOwner} discarded:{discardedCount}/{requiredCount} "
                + $"source:{sourceCard?.Data?.cardName})");
            onDone?.Invoke(discardedCount >= requiredCount && requiredCount > 0);
            yield break;
        }

        if (handOwner == PlayerType.Enemy)
        {
            CardController picked = PickEnemyAiHandDiscardTarget(candidates);
            int nextDiscarded = discardedCount;
            if (picked != null)
            {
                yield return DiscardHandCardWithRevealCoroutine(picked, handOwner, effect, ownerType);
                nextDiscarded++;
            }

            yield return ExecuteDiscardFromHandSelectionCoroutine(
                sourceCard,
                ownerType,
                handOwner,
                effect,
                remaining - 1,
                requiredCount,
                nextDiscarded,
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

        // CancelSkipDiscard: 以降の捨て・後続効果（山札下送り等）は不成立
        if (selected == null)
        {
            Debug.Log(
                $"[DiscardFromHand] Skip — abort remaining discard "
                + $"(discarded:{discardedCount}/{requiredCount} source:{sourceCard?.Data?.cardName})");
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
            requiredCount,
            discardedCount + 1,
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
        ObserveCardInEffectChain(handCard.Data);
        RemoveCardFromHandLists(handCard, handOwner);
        Destroy(handCard.gameObject);
        Debug.Log(
            $"[DiscardFromHand] {handCard.Data.cardName}(id:{handCard.Data.id}) → trash side:{handOwner}");
    }

    private CardController PickEnemyAiHandDiscardTarget(List<CardController> candidates)
    {
        if (candidates == null || candidates.Count == 0)
        {
            return null;
        }

        CardController worst = candidates[0];
        int worstCost = worst.CurrentCost;
        for (int i = 1; i < candidates.Count; i++)
        {
            CardController c = candidates[i];
            if (c == null || c.Data == null)
            {
                continue;
            }

            if (c.CurrentCost < worstCost)
            {
                worst = c;
                worstCost = c.CurrentCost;
            }
        }

        return worst;
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
        title.text = FormatHandDiscardSelectionTitle(effect, source);
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

    private static string FormatHandDiscardSelectionTitle(EffectData effect, CardController source)
    {
        int count = effect != null && effect.value > 0 ? effect.value : 1;
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
        bool isOpponentView,
        string revealTitle = null)
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
        if (!string.IsNullOrEmpty(revealTitle))
        {
            title.text = revealTitle;
        }
        else
        {
            title.text = isOpponentView
                ? "相手が手札から捨てたカード（公開）"
                : "捨てたカードを相手に公開";
        }

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
