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
        return CollectSelectableHandCards(handOwner, excludeSource: null);
    }

    /// <summary>
    /// 手札候補。excludeSource があるときはそのインスタンスのみ除外
    /// （同名の別カードは候補に残す。発動中コマンドの自己捨て防止）。
    /// </summary>
    private List<CardController> CollectSelectableHandCards(PlayerType handOwner, CardController excludeSource)
    {
        CardGameRule rule = ResolveHandRule(handOwner);
        List<CardController> hand = CollectHandControllers(rule);
        List<CardController> result = new List<CardController>(hand.Count);
        for (int i = 0; i < hand.Count; i++)
        {
            CardController card = hand[i];
            if (card == null || card.Data == null)
            {
                continue;
            }

            if (excludeSource != null && ReferenceEquals(card, excludeSource))
            {
                continue;
            }

            result.Add(card);
        }

        return result;
    }

    /// <summary>手札 UI 表示用（発動元インスタンスも含む）。</summary>
    private List<CardController> CollectHandCardsForDiscardDisplay(PlayerType handOwner)
    {
        return CollectSelectableHandCards(handOwner, excludeSource: null);
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

        List<CardController> candidates = CollectSelectableHandCards(handOwner, excludeSource: sourceCard);
        if (effect != null)
        {
            for (int ci = candidates.Count - 1; ci >= 0; ci--)
            {
                CardController candidate = candidates[ci];
                if (candidate?.Data == null || !effect.MatchesHandDiscardCandidate(candidate.Data))
                {
                    candidates.RemoveAt(ci);
                }
            }
        }

        List<CardController> displayCards = CollectHandCardsForDiscardDisplay(handOwner);
        if (candidates.Count == 0)
        {
            Debug.LogWarning(
                $"[DiscardFromHand] 捨てられる手札がありません (owner:{handOwner} discarded:{discardedCount}/{requiredCount} "
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
        BeginOnlineDiscardThinkForLocalHandSelect();
        try
        {
            OpenManualHandTargetSelectionUI(
                sourceCard,
                handOwner,
                effect,
                displayCards,
                candidates,
                picked =>
                {
                    selected = picked;
                    resolved = true;
                });

            yield return new WaitUntil(() => resolved);
        }
        finally
        {
            EndOnlineDiscardThinkForLocalHandSelect();
        }

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

    /// <summary>自分のエンドフェイズ：手札が10枚を超えていれば上限まで捨てる（相手に公開・スキップ不可）。</summary>
    private const int MaxHandSizeAtEndPhase = 10;

    private IEnumerator CoEnforceEndPhaseHandSizeLimit(PlayerType endingTurnSide)
    {
        EffectData discardEffect = new EffectData
        {
            type = EffectType.DiscardFromHand,
            target = TargetType.SelfPlayer,
            value = 1,
            revealDiscardedToOpponent = true,
            forbidSkipHandDiscard = true
        };

        while (true)
        {
            List<CardController> hand = CollectSelectableHandCards(endingTurnSide);
            int currentCount = hand.Count;
            int excess = currentCount - MaxHandSizeAtEndPhase;
            if (excess <= 0)
            {
                yield break;
            }

            discardEffect.value = excess;
            Debug.Log(
                $"[EndPhase] Hand size limit: side:{endingTurnSide} count:{currentCount} "
                + $"limit:{MaxHandSizeAtEndPhase} discardRemaining:{excess}");

            if (endingTurnSide == PlayerType.Enemy)
            {
                CardController aiPick = PickEnemyAiHandDiscardTarget(hand);
                if (aiPick == null)
                {
                    yield break;
                }

                yield return DiscardHandCardWithRevealCoroutine(
                    aiPick,
                    endingTurnSide,
                    discardEffect,
                    endingTurnSide);
                continue;
            }

            // プレイヤー：1枚ずつ選択（スキップ不可）。捨てたカードは相手に公開。
            bool resolved = false;
            CardController selected = null;
            string title = FormatEndPhaseHandLimitDiscardTitle(currentCount, excess);
            BeginOnlineDiscardThinkForLocalHandSelect();
            try
            {
                OpenManualHandTargetSelectionUI(
                    null,
                    endingTurnSide,
                    discardEffect,
                    hand,
                    hand,
                    picked =>
                    {
                        selected = picked;
                        resolved = true;
                    },
                    titleOverride: title);

                yield return new WaitUntil(() => resolved);
            }
            finally
            {
                EndOnlineDiscardThinkForLocalHandSelect();
            }

            if (selected == null)
            {
                // forbidSkip のはずだが、万一のため強制選択
                selected = PickEnemyAiHandDiscardTarget(hand);
                if (selected == null)
                {
                    yield break;
                }
            }

            yield return DiscardHandCardWithRevealCoroutine(
                selected,
                endingTurnSide,
                discardEffect,
                endingTurnSide);
        }
    }

    private static string FormatEndPhaseHandLimitDiscardTitle(int currentCount, int excess)
    {
        return GameLocale.T(
            $"エンドフェイズ：手札上限は{MaxHandSizeAtEndPhase}枚です（現在{currentCount}枚）。\n"
            + $"{excess}枚をトラッシュに捨ててください（相手に公開）",
            $"End Phase: Hand limit is {MaxHandSizeAtEndPhase} (now {currentCount}).\n"
            + $"Discard {excess} card(s) to Trash (revealed to opponent)");
    }

    private void OpenManualHandTargetSelectionUI(
        CardController source,
        PlayerType handOwner,
        EffectData effect,
        List<CardController> displayCards,
        List<CardController> selectableCandidates,
        Action<CardController> onPicked,
        string titleOverride = null)
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

        TextMeshProUGUI title = root.CreateChildTextCustom("ManualHandTargetTitle", UIAnchor.TopCenter, 720, 64);
        title.text = !string.IsNullOrEmpty(titleOverride)
            ? titleOverride
            : FormatHandDiscardSelectionTitle(effect, source);
        title.color = Color.white;
        title.fontSize = 22;
        title.alignment = TextAlignmentOptions.Center;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -20f);

        GameObject scrollGo = root.CreateGridScrollView(680, 360, UIAnchor.TopCenter);
        RectTransform scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.anchoredPosition = new Vector2(0f, -80f);
        scrollGo.ConfigureGridCellFromViewportHeight(0.78f, 56f);
        ScrollRect sr = scrollGo.GetComponent<ScrollRect>();
        RectTransform content = sr != null ? sr.content : null;

        HashSet<CardController> selectableSet = new HashSet<CardController>();
        if (selectableCandidates != null)
        {
            for (int s = 0; s < selectableCandidates.Count; s++)
            {
                if (selectableCandidates[s] != null)
                {
                    selectableSet.Add(selectableCandidates[s]);
                }
            }
        }

        IReadOnlyList<CardController> cardsToShow = displayCards != null && displayCards.Count > 0
            ? displayCards
            : selectableCandidates;

        bool resolved = false;
        if (cardsToShow != null)
        {
            for (int i = 0; i < cardsToShow.Count; i++)
            {
                CardController handCard = cardsToShow[i];
                if (handCard == null || handCard.Data == null || content == null)
                {
                    continue;
                }

                GameObject cardItem = Instantiate(CardImagePrefab, content);
                CardController preview = cardItem.GetComponent<CardController>();
                preview.SetUp(handCard.Data, _ => { });
                bool canSelect = selectableSet.Contains(handCard);
                Button btn = cardItem.GetComponent<Button>() ?? cardItem.AddComponent<Button>();
                if (!canSelect)
                {
                    // 発動元自身など：表示は残すが選べない（グレイアウト）
                    ApplyHandDiscardUnavailableVisual(cardItem);
                    btn.interactable = false;
                    continue;
                }

                CardController pickedRef = handCard;
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
        }

        // forbidSkipHandDiscard: Skip 不可（手札がある限り必ず選択）
        if (effect == null || !effect.forbidSkipHandDiscard)
        {
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
    }

    private static void ApplyHandDiscardUnavailableVisual(GameObject cardItem)
    {
        if (cardItem == null)
        {
            return;
        }

        Image[] images = cardItem.GetComponentsInChildren<Image>(true);
        for (int i = 0; i < images.Length; i++)
        {
            Image img = images[i];
            if (img == null)
            {
                continue;
            }

            Color c = img.color;
            c.a *= 0.45f;
            c.r *= 0.55f;
            c.g *= 0.55f;
            c.b *= 0.55f;
            img.color = c;
        }

        TextMeshProUGUI[] labels = cardItem.GetComponentsInChildren<TextMeshProUGUI>(true);
        for (int i = 0; i < labels.Length; i++)
        {
            if (labels[i] != null)
            {
                labels[i].color = new Color(0.55f, 0.55f, 0.55f, 0.85f);
            }
        }
    }

    private static string FormatHandDiscardSelectionTitle(EffectData effect, CardController source)
    {
        int count = effect != null && effect.value > 0 ? effect.value : 1;
        string revealHintJa = effect != null && effect.revealDiscardedToOpponent
            ? "（相手に公開）"
            : string.Empty;
        string revealHintEn = effect != null && effect.revealDiscardedToOpponent
            ? " (reveal to opponent)"
            : string.Empty;
        string sourceName = source != null && source.Data != null ? source.Data.cardName : string.Empty;
        return string.IsNullOrEmpty(sourceName)
            ? GameLocale.T(
                $"手札から{count}枚をトラッシュに捨てる{revealHintJa}",
                $"Discard {count} card(s) from hand to Trash{revealHintEn}")
            : GameLocale.T(
                $"手札から{count}枚をトラッシュに捨てる{revealHintJa} — {sourceName}",
                $"Discard {count} card(s) from hand to Trash{revealHintEn} — {sourceName}");
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
        else if (isOpponentView)
        {
            title.SetLocalizedText("相手が手札から捨てたカード（公開）", "Opponent discarded from hand (revealed)");
        }
        else
        {
            title.SetLocalizedText("捨てたカードを相手に公開", "Reveal discarded card to opponent");
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
