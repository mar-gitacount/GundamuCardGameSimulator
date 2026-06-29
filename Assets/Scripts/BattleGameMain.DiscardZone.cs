using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>トラッシュ／除外ゾーン UI と除外（Exile）効果。</summary>
public partial class BattleGameMain
{
    private GameObject _activeDiscardZoneInspectRoot;

    private readonly struct TrashExileCandidate
    {
        public TrashExileCandidate(int trashIndex, int cardId, CardData data)
        {
            TrashIndex = trashIndex;
            CardId = cardId;
            Data = data;
        }

        public int TrashIndex { get; }
        public int CardId { get; }
        public CardData Data { get; }
    }

    /// <summary>トラッシュ／除外ゾーンの一覧（現在の表示モードに応じる）。</summary>
    private void OpenDiscardZoneInspectionPanel(CardGameRule rule)
    {
        if (rule == null || CardImagePrefab == null)
        {
            return;
        }

        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            return;
        }

        if (_activeDiscardZoneInspectRoot != null)
        {
            Destroy(_activeDiscardZoneInspectRoot);
            _activeDiscardZoneInspectRoot = null;
        }

        bool showingExile = rule.DiscardZoneViewMode == DiscardZoneViewMode.Exile;
        IReadOnlyList<int> ids = showingExile ? rule.GetExileCardIds() : rule.GetTrashCardIds();

        GameObject root = new GameObject("DiscardZoneInspectRoot", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        _activeDiscardZoneInspectRoot = root;
        root.transform.SetParent(canvas.transform, false);
        root.transform.SetAsLastSibling();
        root.SetFullSize();
        Image dim = root.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.55f);
        dim.raycastTarget = true;

        TextMeshProUGUI title = root.CreateChildTextCustom("DiscardZoneTitle", UIAnchor.TopCenter, 560, 48);
        title.text = showingExile ? "除外（EXILE）一覧" : "トラッシュ一覧";
        title.fontSize = 28;
        title.color = showingExile ? new Color(0.85f, 0.78f, 1f, 1f) : Color.white;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -24f);

        TextMeshProUGUI subtitle = root.CreateChildTextCustom("DiscardZoneSubtitle", UIAnchor.TopCenter, 560, 32);
        subtitle.text = showingExile
            ? "除外ゾーンのカード（ゲームから除外）"
            : "トラッシュのカード";
        subtitle.fontSize = 16;
        subtitle.color = new Color(0.85f, 0.9f, 1f, 1f);
        subtitle.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -58f);

        GameObject scrollGo = root.CreateGridScrollView(560, 360, UIAnchor.TopCenter);
        RectTransform scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.anchoredPosition = new Vector2(0f, -96f);
        scrollGo.ConfigureGridCellFromViewportHeight(0.75f, 56f);

        ScrollRect sr = scrollGo.GetComponent<ScrollRect>();
        RectTransform content = sr != null ? sr.content : null;
        if (content != null)
        {
            if (ids.Count == 0)
            {
                TextMeshProUGUI empty = content.gameObject.CreateChildTextCustom("EmptyDiscardZone", UIAnchor.TopCenter, 480, 40);
                empty.text = showingExile ? "（除外ゾーンは空です）" : "（トラッシュは空です）";
                empty.fontSize = 22;
                empty.color = new Color(0.9f, 0.9f, 0.9f, 1f);
                empty.GetComponent<RectTransform>().anchoredPosition = Vector2.zero;
            }
            else
            {
                for (int i = 0; i < ids.Count; i++)
                {
                    int id = ids[i];
                    CardData data = DeckSettinObject.Instance.GetCardDataById(id);
                    if (data == null)
                    {
                        continue;
                    }

                    GameObject go = Instantiate(CardImagePrefab, content);
                    CardController cc = go.GetComponent<CardController>();
                    if (cc != null)
                    {
                        cc.SetUp(data, _ => { });
                        go.transform.localScale = new Vector3(0.4f, 0.4f, 1f);
                    }
                }
            }
        }

        Button closeBtn = root.CreateChildButton("Close");
        RectTransform closeRt = closeBtn.GetComponent<RectTransform>();
        closeRt.sizeDelta = new Vector2(160f, 44f);
        closeRt.anchorMin = new Vector2(0.5f, 0f);
        closeRt.anchorMax = new Vector2(0.5f, 0f);
        closeRt.pivot = new Vector2(0.5f, 0f);
        closeRt.anchoredPosition = new Vector2(0f, 36f);
        closeBtn.onClick.AddListener(() =>
        {
            if (_activeDiscardZoneInspectRoot == root)
            {
                _activeDiscardZoneInspectRoot = null;
            }

            Destroy(root);
        });
    }

    private void ApplyExileFromDeckEffect(
        CardController sourceCard,
        PlayerType ownerType,
        EffectData effect,
        Action onComplete = null)
    {
        int magnitude = ResolveEffectMagnitude(effect, ownerType, sourceCard);
        if (magnitude <= 0)
        {
            onComplete?.Invoke();
            return;
        }

        CardGameRule deckRule = ResolveDeckRuleForLook(ownerType, effect);
        if (deckRule == null)
        {
            onComplete?.Invoke();
            return;
        }

        bool opponentDeck = effect != null && effect.target == TargetType.EnemyPlayer;
        PlayerType deckOwner = opponentDeck
            ? (ownerType == PlayerType.Player ? PlayerType.Enemy : PlayerType.Player)
            : ownerType;
        string deckLabel = FormatLookDeckOwnerLabel(deckOwner);
        List<int> exiledCardIds = new List<int>(magnitude);

        WithZoneSyncSuppressed(() =>
        {
            for (int i = 0; i < magnitude; i++)
            {
                if (!deckRule.TryTakeCardAtDeckIndex(0, out int cardId))
                {
                    break;
                }

                CardData data = DeckSettinObject.Instance.GetCardDataById(cardId);
                deckRule.AddCardToExile(cardId);
                exiledCardIds.Add(cardId);
                if (data != null)
                {
                    ObserveCardInEffectChain(data);
                }

                Debug.Log(
                    $"[Effect] ExileFromDeck {data?.cardName ?? "?"}(id:{cardId}) deck:{deckLabel} "
                    + $"by cardId:{sourceCard?.Data?.id}");
            }
        });

        if (exiledCardIds.Count > 0)
        {
            int deckRemain = deckRule.GetRemainingCount();
            SyncGundamRuleDeckCount(deckOwner, deckRemain);
            NotifyLocalZoneDeckToExile(deckOwner, exiledCardIds, deckRemain);
        }
        onComplete?.Invoke();
    }

    private CardGameRule ResolveTrashRuleForEffect(PlayerType ownerType, EffectData effect)
    {
        bool opponentTrash = effect != null && effect.target == TargetType.EnemyPlayer;
        return opponentTrash
            ? (ownerType == PlayerType.Player ? enemyCardGameRule : cardGameRule)
            : (ownerType == PlayerType.Player ? cardGameRule : enemyCardGameRule);
    }

    private static string FormatTrashOwnerLabel(PlayerType trashOwner)
    {
        return trashOwner == PlayerType.Player ? "自分" : "相手";
    }

    private static List<TrashExileCandidate> CollectTrashExileCandidates(CardGameRule trashRule, EffectData effect)
    {
        List<TrashExileCandidate> candidates = new List<TrashExileCandidate>();
        if (trashRule == null || effect == null)
        {
            return candidates;
        }

        IReadOnlyList<int> trashIds = trashRule.GetTrashCardIds();
        for (int i = 0; i < trashIds.Count; i++)
        {
            int cardId = trashIds[i];
            CardData data = DeckSettinObject.Instance.GetCardDataById(cardId);
            if (!EffectDataExtensions.MatchesTargetCardTypeFilter(effect, data))
            {
                continue;
            }

            candidates.Add(new TrashExileCandidate(i, cardId, data));
        }

        return candidates;
    }

    private static string FormatExileFromTrashFilterLabel(EffectData effect)
    {
        if (effect == null || !effect.filterByTargetCardType)
        {
            return "カード";
        }

        return FormatCardTypeLabel(effect.targetCardType);
    }

    private void CommitTrashExileAtIndex(
        CardGameRule trashRule,
        int trashIndex,
        CardData data,
        string trashLabel,
        CardController sourceCard,
        PlayerType trashOwner)
    {
        if (trashRule == null)
        {
            return;
        }

        int removedId = -1;
        WithZoneSyncSuppressed(() =>
        {
            if (!trashRule.TryRemoveCardFromTrashAt(trashIndex, out removedId))
            {
                return;
            }

            trashRule.AddCardToExile(removedId);
        });

        if (removedId < 0)
        {
            return;
        }

        CardData resolved = data ?? DeckSettinObject.Instance.GetCardDataById(removedId);
        if (resolved != null)
        {
            ObserveCardInEffectChain(resolved);
        }

        NotifyLocalZoneTrashToExile(trashOwner, removedId);

        Debug.Log(
            $"[Effect] ExileFromTrash {resolved?.cardName ?? "?"}(id:{removedId}) trash:{trashLabel} "
            + $"by cardId:{sourceCard?.Data?.id}");
    }

    private void ApplyExileFromTrashAuto(
        CardGameRule trashRule,
        List<TrashExileCandidate> candidates,
        int pickCount,
        string trashLabel,
        CardController sourceCard,
        PlayerType trashOwner)
    {
        List<TrashExileCandidate> ordered = new List<TrashExileCandidate>(candidates);
        ordered.Sort((a, b) => b.TrashIndex.CompareTo(a.TrashIndex));
        int exiled = 0;
        for (int i = 0; i < ordered.Count && exiled < pickCount; i++)
        {
            TrashExileCandidate candidate = ordered[i];
            CommitTrashExileAtIndex(
                trashRule,
                candidate.TrashIndex,
                candidate.Data,
                trashLabel,
                sourceCard,
                trashOwner);
            exiled++;
        }
    }

    private void ApplyExileFromTrashEffect(
        CardController sourceCard,
        PlayerType ownerType,
        EffectData effect,
        Action onComplete = null)
    {
        int magnitude = ResolveEffectMagnitude(effect, ownerType, sourceCard);
        if (magnitude <= 0)
        {
            onComplete?.Invoke();
            return;
        }

        CardGameRule trashRule = ResolveTrashRuleForEffect(ownerType, effect);
        if (trashRule == null)
        {
            onComplete?.Invoke();
            return;
        }

        PlayerType trashOwner = effect != null && effect.target == TargetType.EnemyPlayer
            ? (ownerType == PlayerType.Player ? PlayerType.Enemy : PlayerType.Player)
            : ownerType;
        string trashLabel = FormatTrashOwnerLabel(trashOwner);
        List<TrashExileCandidate> candidates = CollectTrashExileCandidates(trashRule, effect);
        if (candidates.Count == 0)
        {
            onComplete?.Invoke();
            return;
        }

        int pickCount = Mathf.Min(magnitude, candidates.Count);
        if (ownerType == PlayerType.Enemy)
        {
            ApplyExileFromTrashAuto(trashRule, candidates, pickCount, trashLabel, sourceCard, trashOwner);
            onComplete?.Invoke();
            return;
        }

        StartCoroutine(ShowExileFromTrashSelectionCoroutine(
            sourceCard,
            trashRule,
            effect,
            trashLabel,
            trashOwner,
            candidates,
            pickCount,
            onComplete));
    }

    private IEnumerator ShowExileFromTrashSelectionCoroutine(
        CardController sourceCard,
        CardGameRule trashRule,
        EffectData effect,
        string trashLabel,
        PlayerType trashOwner,
        List<TrashExileCandidate> candidates,
        int pickCount,
        Action onComplete)
    {
        if (trashRule == null || candidates == null || candidates.Count == 0 || CardImagePrefab == null)
        {
            onComplete?.Invoke();
            yield break;
        }

        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            onComplete?.Invoke();
            yield break;
        }

        string filterLabel = FormatExileFromTrashFilterLabel(effect);
        DestroyActiveOnActionPopupIfAny();
        GameObject root = new GameObject(
            "ExileFromTrashSelect",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        activeOnActionPopupRoot = root;
        isOnActionPopupOpen = true;
        root.transform.SetParent(canvas.transform, false);
        root.transform.SetAsLastSibling();
        root.SetFullSize();
        Image dim = root.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.62f);
        dim.raycastTarget = true;

        TextMeshProUGUI title = root.CreateChildTextCustom("ExileTrashTitle", UIAnchor.TopCenter, 760, 48);
        title.text = "除外するカードを選択";
        title.fontSize = 26;
        title.fontStyle = FontStyles.Bold;
        title.color = Color.white;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -18f);

        TextMeshProUGUI subtitle = root.CreateChildTextCustom("ExileTrashSubtitle", UIAnchor.TopCenter, 760, 36);
        subtitle.text = pickCount <= 1
            ? $"{trashLabel}のトラッシュから{filterLabel}を1枚選んで除外"
            : $"{trashLabel}のトラッシュから{filterLabel}を{pickCount}枚選んで除外";
        subtitle.fontSize = 17;
        subtitle.color = new Color(0.85f, 0.92f, 1f, 1f);
        subtitle.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -56f);

        GameObject scrollGo = root.CreateGridScrollView(760, 400, UIAnchor.TopCenter);
        RectTransform scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.anchoredPosition = new Vector2(0f, -96f);
        scrollGo.ConfigureGridCellFromViewportHeight(0.78f, 56f);
        ScrollRect sr = scrollGo.GetComponent<ScrollRect>();
        RectTransform content = sr != null ? sr.content : null;

        HashSet<int> selectedTrashIndices = new HashSet<int>();
        Dictionary<int, GameObject> cardObjectsByTrashIndex = new Dictionary<int, GameObject>();
        bool dismissed = false;
        bool confirmed = false;
        Button okBtn = null;
        TextMeshProUGUI okLabel = null;

        void RefreshSelectionVisuals()
        {
            foreach (KeyValuePair<int, GameObject> pair in cardObjectsByTrashIndex)
            {
                SetExileTrashSelectionHighlight(pair.Value, selectedTrashIndices.Contains(pair.Key));
            }

            bool ready = selectedTrashIndices.Count >= pickCount;
            if (okBtn != null)
            {
                okBtn.interactable = ready;
            }

            if (okLabel != null)
            {
                okLabel.color = ready ? Color.white : new Color(0.55f, 0.55f, 0.55f, 1f);
            }
        }

        void ClosePopup()
        {
            isOnActionPopupOpen = false;
            activeOnActionPopupRoot = null;
            Destroy(root);
        }

        if (content != null)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                TrashExileCandidate candidate = candidates[i];
                if (candidate.Data == null)
                {
                    continue;
                }

                GameObject go = Instantiate(CardImagePrefab, content);
                cardObjectsByTrashIndex[candidate.TrashIndex] = go;
                CardController cc = go.GetComponent<CardController>();
                if (cc != null)
                {
                    int capturedIndex = candidate.TrashIndex;
                    cc.SetUp(candidate.Data, _ =>
                    {
                        if (pickCount <= 1)
                        {
                            selectedTrashIndices.Clear();
                            selectedTrashIndices.Add(capturedIndex);
                        }
                        else if (selectedTrashIndices.Contains(capturedIndex))
                        {
                            selectedTrashIndices.Remove(capturedIndex);
                        }
                        else if (selectedTrashIndices.Count < pickCount)
                        {
                            selectedTrashIndices.Add(capturedIndex);
                        }

                        RefreshSelectionVisuals();
                    });
                    go.transform.localScale = new Vector3(0.42f, 0.42f, 1f);
                }

                SetExileTrashSelectionHighlight(go, false);
            }
        }

        Button cancelBtn = root.CreateChildButton("Cancel");
        RectTransform cancelRt = cancelBtn.GetComponent<RectTransform>();
        cancelRt.sizeDelta = new Vector2(180f, 50f);
        cancelRt.anchorMin = new Vector2(0.5f, 0f);
        cancelRt.anchorMax = new Vector2(0.5f, 0f);
        cancelRt.pivot = new Vector2(0.5f, 0f);
        cancelRt.anchoredPosition = new Vector2(-110f, 36f);
        TextMeshProUGUI cancelLabel = cancelBtn.GetComponentInChildren<TextMeshProUGUI>();
        if (cancelLabel != null)
        {
            cancelLabel.text = "Cancel";
        }

        cancelBtn.onClick.AddListener(() =>
        {
            dismissed = true;
            confirmed = false;
            ClearOnMainPaidBlock();
            ClosePopup();
        });

        okBtn = root.CreateChildButton("OK");
        RectTransform okRt = okBtn.GetComponent<RectTransform>();
        okRt.sizeDelta = new Vector2(180f, 50f);
        okRt.anchorMin = new Vector2(0.5f, 0f);
        okRt.anchorMax = new Vector2(0.5f, 0f);
        okRt.pivot = new Vector2(0.5f, 0f);
        okRt.anchoredPosition = new Vector2(110f, 36f);
        okLabel = okBtn.GetComponentInChildren<TextMeshProUGUI>();
        if (okLabel != null)
        {
            okLabel.text = "OK";
        }

        okBtn.interactable = false;
        okBtn.onClick.AddListener(() =>
        {
            if (!okBtn.interactable)
            {
                return;
            }

            dismissed = true;
            confirmed = true;
            ClosePopup();
        });

        RefreshSelectionVisuals();
        yield return new WaitUntil(() => dismissed);

        if (confirmed)
        {
            if (!TryCommitOnMainPaidBlockBeforeExile())
            {
                onComplete?.Invoke();
                yield break;
            }

            List<int> orderedIndices = new List<int>(selectedTrashIndices);
            orderedIndices.Sort((a, b) => b.CompareTo(a));
            int exiled = 0;
            for (int i = 0; i < orderedIndices.Count && exiled < pickCount; i++)
            {
                int trashIndex = orderedIndices[i];
                TrashExileCandidate? match = null;
                for (int c = 0; c < candidates.Count; c++)
                {
                    if (candidates[c].TrashIndex == trashIndex)
                    {
                        match = candidates[c];
                        break;
                    }
                }

                if (!match.HasValue)
                {
                    continue;
                }

                CommitTrashExileAtIndex(
                    trashRule,
                    trashIndex,
                    match.Value.Data,
                    trashLabel,
                    sourceCard,
                    trashOwner);
                exiled++;
            }
        }

        onComplete?.Invoke();
    }

    private static void SetExileTrashSelectionHighlight(GameObject cardGo, bool selected)
    {
        if (cardGo == null)
        {
            return;
        }

        Transform outline = cardGo.transform.Find("ExileSelectionOutline");
        if (outline == null)
        {
            GameObject outlineGo = new GameObject(
                "ExileSelectionOutline",
                typeof(RectTransform),
                typeof(Image));
            outlineGo.transform.SetParent(cardGo.transform, false);
            RectTransform outlineRt = outlineGo.GetComponent<RectTransform>();
            outlineRt.anchorMin = Vector2.zero;
            outlineRt.anchorMax = Vector2.one;
            outlineRt.offsetMin = new Vector2(-6f, -6f);
            outlineRt.offsetMax = new Vector2(6f, 6f);
            Image outlineImg = outlineGo.GetComponent<Image>();
            outlineImg.color = new Color(1f, 0.82f, 0.2f, 0.92f);
            outlineImg.raycastTarget = false;
            outline = outlineGo.transform;
        }

        outline.gameObject.SetActive(selected);
    }
}
