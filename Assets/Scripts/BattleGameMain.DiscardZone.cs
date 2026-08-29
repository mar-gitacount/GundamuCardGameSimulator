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
        if (showingExile)
        {
            title.SetLocalizedText("除外（EXILE）一覧", "Exile list");
        }
        else
        {
            title.SetLocalizedText("トラッシュ一覧", "Trash list");
        }

        title.fontSize = 28;
        title.color = showingExile ? new Color(0.85f, 0.78f, 1f, 1f) : Color.white;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -24f);

        TextMeshProUGUI subtitle = root.CreateChildTextCustom("DiscardZoneSubtitle", UIAnchor.TopCenter, 560, 32);
        if (showingExile)
        {
            subtitle.SetLocalizedText("除外ゾーンのカード（ゲームから除外）", "Cards in Exile (removed from the game)");
        }
        else if (IsTestPlayBattle())
        {
            subtitle.SetLocalizedText(
                "カードを選んで手札・山札・配備へ移動できます",
                "Select a card to move to hand, deck, or play");
        }
        else
        {
            subtitle.SetLocalizedText("トラッシュのカード", "Cards in Trash");
        }

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
                if (showingExile)
                {
                    empty.SetLocalizedText("（除外ゾーンは空です）", "(Exile is empty)");
                }
                else
                {
                    empty.SetLocalizedText("（トラッシュは空です）", "(Trash is empty)");
                }

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
                        int capturedIndex = i;
                        int capturedId = id;
                        CardData capturedData = data;
                        bool fromExile = showingExile;
                        bool testPlayZone = IsTestPlayBattle();
                        cc.SetUp(capturedData, clicked =>
                        {
                            if (!testPlayZone)
                            {
                                return;
                            }

                            PlayerType ownerType = rule == enemyCardGameRule
                                ? PlayerType.Enemy
                                : PlayerType.Player;
                            if (fromExile)
                            {
                                OpenTestPlayExileCardMenu(
                                    rule,
                                    ownerType,
                                    capturedIndex,
                                    capturedId,
                                    capturedData);
                            }
                            else
                            {
                                OpenTestPlayTrashCardMenu(
                                    rule,
                                    ownerType,
                                    capturedIndex,
                                    capturedId,
                                    capturedData);
                            }
                        });
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
        return trashOwner == PlayerType.Player ? "Your" : "Opponent's";
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
            if (!EffectDataExtensions.MatchesTargetFeatureFilter(effect, data))
            {
                continue;
            }
            if (!EffectDataExtensions.MatchesTargetCardTypeFilter(effect, data))
            {
                continue;
            }

            if (!EffectDataExtensions.MatchesTargetPilotIdFilter(effect, data))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(effect.targetCardNameContains)
                && (data == null
                    || !CardNameContainsMatcher.Matches(data.cardName, effect.targetCardNameContains.Trim())))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(effect.targetCardNameExcludes)
                && data != null
                && CardNameContainsMatcher.MatchesExcludeNeedle(
                    data.cardName,
                    effect.targetCardNameExcludes.Trim()))
            {
                continue;
            }

            if (!EffectDataExtensions.MatchesCardDataStatFilter(effect, data))
            {
                continue;
            }

            // 除外するカード一覧に追加
            candidates.Add(new TrashExileCandidate(i, cardId, data));
        }

        return candidates;
    }

    private static string FormatExileFromTrashFilterLabel(EffectData effect)
    {
        if (effect == null)
        {
            return "card";
        }

        string featureLabel = FormatExileTargetFeaturesEnglishLabel(effect);
        if (!string.IsNullOrEmpty(featureLabel))
        {
            return featureLabel;
        }

        if (effect.filterByTargetCardType)
        {
            return effect.targetCardType.ToString();
        }

        return "card";
    }

    private static string FormatExileTargetFeaturesEnglishLabel(EffectData effect)
    {
        if (effect == null)
        {
            return string.Empty;
        }

        IReadOnlyList<CardFeatureData> features = effect.GetTargetFeatures();
        if (features.Count == 0)
        {
            return string.Empty;
        }

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = 0; i < features.Count; i++)
        {
            CardFeatureData feature = features[i];
            if (feature == null)
            {
                continue;
            }

            string label = !string.IsNullOrWhiteSpace(feature.featureKey)
                ? feature.featureKey.Replace('_', ' ')
                : feature.displayName;
            if (string.IsNullOrWhiteSpace(label))
            {
                continue;
            }

            if (sb.Length > 0)
            {
                sb.Append(" / ");
            }

            sb.Append(label);
        }

        return sb.ToString();
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
        Action onComplete = null,
        Action onSkipped = null)
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
            (onSkipped ?? onComplete)?.Invoke();
            return;
        }

        PlayerType trashOwner = effect != null && effect.target == TargetType.EnemyPlayer
            ? (ownerType == PlayerType.Player ? PlayerType.Enemy : PlayerType.Player)
            : ownerType;
        string trashLabel = FormatTrashOwnerLabel(trashOwner);
        List<TrashExileCandidate> candidates = CollectTrashExileCandidates(trashRule, effect);
        if (candidates.Count == 0)
        {
            Debug.Log(
                $"[Effect] ExileFromTrash skipped — no candidates by cardId:{sourceCard?.Data?.id}");
            (onSkipped ?? onComplete)?.Invoke();
            return;
        }

        if (effect.requireExactExileCount && candidates.Count < magnitude)
        {
            Debug.Log(
                $"[Effect] ExileFromTrash skipped — need exact {magnitude} but candidates:{candidates.Count} "
                + $"by cardId:{sourceCard?.Data?.id}");
            (onSkipped ?? onComplete)?.Invoke();
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
            onComplete,
            onSkipped));
    }

    private IEnumerator ShowExileFromTrashSelectionCoroutine(
        CardController sourceCard,
        CardGameRule trashRule,
        EffectData effect,
        string trashLabel,
        PlayerType trashOwner,
        List<TrashExileCandidate> candidates,
        int pickCount,
        Action onComplete,
        Action onSkipped = null)
    {
        if (trashRule == null || candidates == null || candidates.Count == 0 || CardImagePrefab == null)
        {
            (onSkipped ?? onComplete)?.Invoke();
            yield break;
        }

        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            (onSkipped ?? onComplete)?.Invoke();
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
        title.text = "Select cards to Exile";
        title.fontSize = 26;
        title.fontStyle = FontStyles.Bold;
        title.color = Color.white;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -18f);

        TextMeshProUGUI subtitle = root.CreateChildTextCustom("ExileTrashSubtitle", UIAnchor.TopCenter, 760, 36);
        subtitle.text = pickCount <= 1
            ? $"Choose 1 {filterLabel} from {trashLabel} trash to Exile"
            : $"Choose {pickCount} {filterLabel} cards from {trashLabel} trash to Exile";
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
            bool finalized = false;
            yield return CoTryFinalizeOnMainPaidActivationWithUi(
                _activeOnMainPaidBlock,
                ok => finalized = ok);
            if (!finalized)
            {
                (onSkipped ?? onComplete)?.Invoke();
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

            if (exiled <= 0)
            {
                (onSkipped ?? onComplete)?.Invoke();
                yield break;
            }

            onComplete?.Invoke();
            yield break;
        }

        (onSkipped ?? onComplete)?.Invoke();
    }

    private void ApplyReturnFromTrashToDeckAndShuffleEffect(
        CardController sourceCard,
        PlayerType ownerType,
        EffectData effect,
        Action onComplete = null,
        Action onSkipped = null)
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
            (onSkipped ?? onComplete)?.Invoke();
            return;
        }

        PlayerType trashOwner = effect != null && effect.target == TargetType.EnemyPlayer
            ? (ownerType == PlayerType.Player ? PlayerType.Enemy : PlayerType.Player)
            : ownerType;
        string trashLabel = FormatTrashOwnerLabel(trashOwner);
        List<TrashExileCandidate> candidates = CollectTrashExileCandidates(trashRule, effect);
        if (candidates.Count == 0)
        {
            Debug.Log(
                $"[Effect] ReturnFromTrashToDeck skipped — no candidates by cardId:{sourceCard?.Data?.id}");
            (onSkipped ?? onComplete)?.Invoke();
            return;
        }

        if (candidates.Count < magnitude)
        {
            Debug.Log(
                $"[Effect] ReturnFromTrashToDeck skipped — need at least {magnitude} but candidates:{candidates.Count} "
                + $"by cardId:{sourceCard?.Data?.id}");
            (onSkipped ?? onComplete)?.Invoke();
            return;
        }

        int batchSize = magnitude;
        int maxPick = effect.requireExactExileCount
            ? batchSize
            : (candidates.Count / batchSize) * batchSize;
        if (maxPick < batchSize)
        {
            Debug.Log(
                $"[Effect] ReturnFromTrashToDeck skipped — maxPick:{maxPick} < batch:{batchSize} "
                + $"by cardId:{sourceCard?.Data?.id}");
            (onSkipped ?? onComplete)?.Invoke();
            return;
        }

        SetEffectChainReturnFromTrashBatchSize(batchSize);
        SetEffectChainLastReturnFromTrashCount(0);

        if (ownerType == PlayerType.Enemy)
        {
            BeginOnlineEffectSyncBatch(ownerType);
            int returned = ApplyReturnFromTrashToDeckAuto(
                trashRule,
                candidates,
                maxPick,
                trashLabel,
                sourceCard,
                trashOwner);
            MarkOnAttackTrashReturnedForCurrentAttack(returned, batchSize);
            FlushOnlineEffectSyncBatch();
            SyncAllResourceViewsFromRule();
            onComplete?.Invoke();
            return;
        }

        StartCoroutine(ShowReturnFromTrashToDeckSelectionCoroutine(
            sourceCard,
            ownerType,
            trashRule,
            effect,
            trashLabel,
            trashOwner,
            candidates,
            batchSize,
            maxPick,
            effect.requireExactExileCount,
            onComplete,
            onSkipped));
    }

    private int ApplyReturnFromTrashToDeckAuto(
        CardGameRule trashRule,
        List<TrashExileCandidate> candidates,
        int pickCount,
        string trashLabel,
        CardController sourceCard,
        PlayerType trashOwner)
    {
        List<TrashExileCandidate> ordered = new List<TrashExileCandidate>(candidates);
        ordered.Sort((a, b) => b.TrashIndex.CompareTo(a.TrashIndex));
        List<int> indices = new List<int>();
        for (int i = 0; i < ordered.Count && indices.Count < pickCount; i++)
        {
            indices.Add(ordered[i].TrashIndex);
        }

        return CommitTrashReturnToDeckAtIndices(trashRule, indices, candidates, trashLabel, sourceCard, trashOwner);
    }

    private int CommitTrashReturnToDeckAtIndices(
        CardGameRule trashRule,
        List<int> trashIndicesHighToLow,
        List<TrashExileCandidate> candidates,
        string trashLabel,
        CardController sourceCard,
        PlayerType trashOwner)
    {
        if (trashRule == null || trashIndicesHighToLow == null || trashIndicesHighToLow.Count == 0)
        {
            return 0;
        }

        List<int> removedIds = new List<int>();
        WithZoneSyncSuppressed(() =>
        {
            for (int i = 0; i < trashIndicesHighToLow.Count; i++)
            {
                int trashIndex = trashIndicesHighToLow[i];
                if (trashRule.TryRemoveCardFromTrashAt(trashIndex, out int removedId) && removedId >= 0)
                {
                    removedIds.Add(removedId);
                }
            }

            if (removedIds.Count > 0)
            {
                trashRule.ReturnCardIdsToDeckAndShuffle(removedIds);
            }
        });

        for (int i = 0; i < removedIds.Count; i++)
        {
            CardData resolved = DeckSettinObject.Instance.GetCardDataById(removedIds[i]);
            Debug.Log(
                $"[Effect] ReturnFromTrashToDeck {resolved?.cardName ?? "?"}(id:{removedIds[i]}) trash:{trashLabel} "
                + $"by cardId:{sourceCard?.Data?.id}");
        }

        return removedIds.Count;
    }

    private IEnumerator ShowReturnFromTrashToDeckSelectionCoroutine(
        CardController sourceCard,
        PlayerType ownerType,
        CardGameRule trashRule,
        EffectData effect,
        string trashLabel,
        PlayerType trashOwner,
        List<TrashExileCandidate> candidates,
        int batchSize,
        int maxPick,
        bool requireExactPickCount,
        Action onComplete,
        Action onSkipped = null)
    {
        if (trashRule == null || candidates == null || candidates.Count == 0 || CardImagePrefab == null)
        {
            (onSkipped ?? onComplete)?.Invoke();
            yield break;
        }

        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            (onSkipped ?? onComplete)?.Invoke();
            yield break;
        }

        string filterLabel = FormatExileFromTrashFilterLabel(effect);
        DestroyActiveOnActionPopupIfAny();
        GameObject root = new GameObject(
            "ReturnFromTrashToDeckSelect",
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

        TextMeshProUGUI title = root.CreateChildTextCustom("ReturnTrashTitle", UIAnchor.TopCenter, 760, 48);
        title.text = GameLocale.T("トラッシュから山札へ", "Return from trash to deck");
        title.fontSize = 26;
        title.fontStyle = FontStyles.Bold;
        title.color = Color.white;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -18f);

        TextMeshProUGUI subtitle = root.CreateChildTextCustom("ReturnTrashSubtitle", UIAnchor.TopCenter, 760, 36);
        if (requireExactPickCount && maxPick <= 1)
        {
            subtitle.text = GameLocale.T(
                $"{trashLabel}のトラッシュから{filterLabel}を1枚選んで山札に戻す",
                $"Choose 1 {filterLabel} from {trashLabel} trash to return to deck");
        }
        else if (requireExactPickCount)
        {
            subtitle.text = GameLocale.T(
                $"{trashLabel}のトラッシュから{filterLabel}を{maxPick}枚選んで山札に戻す",
                $"Choose {maxPick} {filterLabel} card(s) from {trashLabel} trash to return to deck");
        }
        else
        {
            subtitle.text = GameLocale.T(
                $"{trashLabel}のトラッシュから{filterLabel}を{batchSize}枚ずつ選んで山札に戻す（最大{maxPick}枚）",
                $"Choose {batchSize} cards at a time from {trashLabel} trash to return to deck (max {maxPick})");
        }
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
            int selectedCount = selectedTrashIndices.Count;
            bool atMaxSelection = selectedCount >= maxPick;
            // トラッシュが12〜23枚など1バッチのみ返却可能なとき、12枚選んだ時点で未選択をグレーアウト
            bool atSingleBatchCap = maxPick == batchSize
                && selectedCount >= batchSize;
            bool grayUnselectedCards = atMaxSelection || atSingleBatchCap;

            foreach (KeyValuePair<int, GameObject> pair in cardObjectsByTrashIndex)
            {
                bool isSelected = selectedTrashIndices.Contains(pair.Key);
                SetExileTrashSelectionHighlight(pair.Value, isSelected);
                ApplyTrashSelectionCardGrayedOut(pair.Value, !isSelected && grayUnselectedCards);
            }

            bool ready = requireExactPickCount
                ? selectedTrashIndices.Count >= maxPick
                : selectedTrashIndices.Count >= batchSize
                    && selectedTrashIndices.Count % batchSize == 0;
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
                        if (requireExactPickCount && maxPick <= 1)
                        {
                            selectedTrashIndices.Clear();
                            selectedTrashIndices.Add(capturedIndex);
                        }
                        else if (selectedTrashIndices.Contains(capturedIndex))
                        {
                            selectedTrashIndices.Remove(capturedIndex);
                        }
                        else if (selectedTrashIndices.Count < maxPick)
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

        Button cancelBtn = root.CreateChildButton(GameLocale.T("キャンセル", "Cancel"));
        RectTransform cancelRt = cancelBtn.GetComponent<RectTransform>();
        cancelRt.sizeDelta = new Vector2(180f, 50f);
        cancelRt.anchorMin = new Vector2(0.5f, 0f);
        cancelRt.anchorMax = new Vector2(0.5f, 0f);
        cancelRt.pivot = new Vector2(0.5f, 0f);
        cancelRt.anchoredPosition = new Vector2(-110f, 36f);

        cancelBtn.onClick.AddListener(() =>
        {
            dismissed = true;
            confirmed = false;
            ClearOnMainPaidBlock();
            ClosePopup();
        });

        okBtn = root.CreateChildButton(GameLocale.T("OK", "OK"));
        RectTransform okRt = okBtn.GetComponent<RectTransform>();
        okRt.sizeDelta = new Vector2(180f, 50f);
        okRt.anchorMin = new Vector2(0.5f, 0f);
        okRt.anchorMax = new Vector2(0.5f, 0f);
        okRt.pivot = new Vector2(0.5f, 0f);
        okRt.anchoredPosition = new Vector2(110f, 36f);
        okLabel = okBtn.GetComponentInChildren<TextMeshProUGUI>();

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
            List<int> orderedIndices = new List<int>(selectedTrashIndices);
            orderedIndices.Sort((a, b) => b.CompareTo(a));
            BeginOnlineEffectSyncBatch(ownerType);
            int returned = CommitTrashReturnToDeckAtIndices(
                trashRule,
                orderedIndices,
                candidates,
                trashLabel,
                sourceCard,
                trashOwner);
            MarkOnAttackTrashReturnedForCurrentAttack(returned, batchSize);
            FlushOnlineEffectSyncBatch();
            SyncAllResourceViewsFromRule();

            if (returned <= 0)
            {
                (onSkipped ?? onComplete)?.Invoke();
                yield break;
            }

            onComplete?.Invoke();
            yield break;
        }

        (onSkipped ?? onComplete)?.Invoke();
    }

    private void ApplyAddObservedToHandFromTrashEffect(
        CardController sourceCard,
        PlayerType ownerType,
        EffectData effect,
        Action onComplete = null)
    {
        CardGameRule trashRule = ResolveTrashRuleForEffect(ownerType, effect);
        PlayerType trashOwner = effect != null && effect.target == TargetType.EnemyPlayer
            ? (ownerType == PlayerType.Player ? PlayerType.Enemy : PlayerType.Player)
            : ownerType;
        if (trashRule == null)
        {
            onComplete?.Invoke();
            return;
        }

        List<TrashExileCandidate> observedCandidates = GetObservedMilledTrashCandidates(trashOwner);
        List<TrashExileCandidate> candidates = new List<TrashExileCandidate>();
        IReadOnlyList<int> currentTrashIds = trashRule.GetTrashCardIds();
        for (int i = 0; i < observedCandidates.Count; i++)
        {
            TrashExileCandidate candidate = observedCandidates[i];
            if (candidate.TrashIndex < 0 || candidate.TrashIndex >= currentTrashIds.Count)
            {
                continue;
            }

            if (currentTrashIds[candidate.TrashIndex] != candidate.CardId)
            {
                continue;
            }

            candidates.Add(candidate);
        }

        if (candidates.Count == 0)
        {
            onComplete?.Invoke();
            return;
        }

        int pickCount = Mathf.Max(1, ResolveEffectMagnitude(effect, ownerType, sourceCard));
        if (ownerType == PlayerType.Enemy)
        {
            ResolveAddObservedToHandFromTrashAuto(trashRule, trashOwner, effect, candidates, pickCount);
            onComplete?.Invoke();
            return;
        }

        StartCoroutine(ShowAddObservedToHandFromTrashSelectionCoroutine(
            sourceCard,
            trashRule,
            ownerType,
            trashOwner,
            effect,
            candidates,
            pickCount,
            onComplete));
    }

    private void ResolveAddObservedToHandFromTrashAuto(
        CardGameRule trashRule,
        PlayerType handOwner,
        EffectData effect,
        List<TrashExileCandidate> observedCandidates,
        int pickCount)
    {
        List<TrashExileCandidate> selectable = FilterObservedTrashCandidatesForHand(observedCandidates, effect);
        int taken = 0;
        selectable.Sort((a, b) => b.TrashIndex.CompareTo(a.TrashIndex));
        for (int i = 0; i < selectable.Count && taken < pickCount; i++)
        {
            if (TryMoveTrashCandidateToHand(trashRule, handOwner, selectable[i]))
            {
                taken++;
            }
        }
    }

    private IEnumerator ShowAddObservedToHandFromTrashSelectionCoroutine(
        CardController sourceCard,
        CardGameRule trashRule,
        PlayerType effectOwner,
        PlayerType trashOwner,
        EffectData effect,
        List<TrashExileCandidate> observedCandidates,
        int pickCount,
        Action onComplete)
    {
        if (trashRule == null || observedCandidates == null || observedCandidates.Count == 0 || CardImagePrefab == null)
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

        HashSet<int> selectableIds = new HashSet<int>();
        List<TrashExileCandidate> selectable = FilterObservedTrashCandidatesForHand(observedCandidates, effect);
        for (int i = 0; i < selectable.Count; i++)
        {
            selectableIds.Add(selectable[i].TrashIndex);
        }

        string featureLabel = effect != null ? effect.FormatTargetFeaturesLabel("/") : string.Empty;
        string typeLabel = effect != null && effect.filterByTargetCardType
            ? CardTypeExtensions.GetDisplayName(effect.targetCardType)
            : string.Empty;
        string filterLabel = string.IsNullOrEmpty(featureLabel)
            ? typeLabel
            : (string.IsNullOrEmpty(typeLabel) ? featureLabel : $"{typeLabel}・{featureLabel}");
        if (string.IsNullOrEmpty(filterLabel))
        {
            filterLabel = GameLocale.T("カード", "card");
        }

        DestroyActiveOnActionPopupIfAny();
        GameObject root = new GameObject(
            "ObservedTrashToHandSelect",
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

        TextMeshProUGUI title = root.CreateChildTextCustom("ObservedTrashToHandTitle", UIAnchor.TopCenter, 780, 48);
        title.SetLocalizedText("トラッシュに送ったカードから手札に加える", "Add to hand from cards sent to Trash");
        title.fontSize = 26;
        title.fontStyle = FontStyles.Bold;
        title.color = Color.white;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -18f);

        string sourceName = sourceCard?.Data?.cardName ?? GameLocale.T("このカード", "this card");
        TextMeshProUGUI subtitle = root.CreateChildTextCustom("ObservedTrashToHandSubtitle", UIAnchor.TopCenter, 780, 40);
        if (selectable.Count > 0)
        {
            subtitle.SetLocalizedText(
                $"{sourceName} の効果でトラッシュに送った3枚から {filterLabel} を選択して OK",
                $"{sourceName}: choose {filterLabel} from the 3 cards sent to Trash, then OK");
        }
        else
        {
            subtitle.SetLocalizedText(
                $"{sourceName} の効果でトラッシュに送った3枚に対象となる {filterLabel} はありません",
                $"{sourceName}: none of the 3 cards sent to Trash match {filterLabel}");
        }

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
                okBtn.interactable = selectable.Count == 0 || ready;
            }

            if (okLabel != null)
            {
                okLabel.color = selectable.Count == 0 || ready
                    ? Color.white
                    : new Color(0.55f, 0.55f, 0.55f, 1f);
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
            for (int i = 0; i < observedCandidates.Count; i++)
            {
                TrashExileCandidate candidate = observedCandidates[i];
                if (candidate.Data == null)
                {
                    continue;
                }

                bool canPick = selectableIds.Contains(candidate.TrashIndex);
                GameObject go = Instantiate(CardImagePrefab, content);
                cardObjectsByTrashIndex[candidate.TrashIndex] = go;
                CardController cc = go.GetComponent<CardController>();
                if (cc != null)
                {
                    int capturedIndex = candidate.TrashIndex;
                    if (canPick)
                    {
                        cc.SetUp(candidate.Data, _ =>
                        {
                            selectedTrashIndices.Clear();
                            selectedTrashIndices.Add(capturedIndex);
                            RefreshSelectionVisuals();
                        });
                    }
                    else
                    {
                        cc.SetUp(candidate.Data, _ => { });
                    }

                    go.transform.localScale = new Vector3(0.42f, 0.42f, 1f);
                }

                if (!canPick)
                {
                    CanvasGroup cg = go.GetComponent<CanvasGroup>();
                    if (cg == null)
                    {
                        cg = go.AddComponent<CanvasGroup>();
                    }

                    cg.alpha = 0.45f;
                }

                SetExileTrashSelectionHighlight(go, false);
            }
        }

        okBtn = root.CreateChildButton("OK");
        RectTransform okRt = okBtn.GetComponent<RectTransform>();
        okRt.sizeDelta = new Vector2(220f, 50f);
        okRt.anchorMin = new Vector2(0.5f, 0f);
        okRt.anchorMax = new Vector2(0.5f, 0f);
        okRt.pivot = new Vector2(0.5f, 0f);
        okRt.anchoredPosition = new Vector2(0f, 36f);
        okLabel = okBtn.GetComponentInChildren<TextMeshProUGUI>();
        if (okLabel != null)
        {
            okLabel.text = "OK";
        }

        okBtn.interactable = selectable.Count == 0;
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
            List<int> orderedIndices = new List<int>(selectedTrashIndices);
            orderedIndices.Sort((a, b) => b.CompareTo(a));
            int taken = 0;
            for (int i = 0; i < orderedIndices.Count && taken < pickCount; i++)
            {
                int trashIndex = orderedIndices[i];
                for (int c = 0; c < observedCandidates.Count; c++)
                {
                    if (observedCandidates[c].TrashIndex != trashIndex)
                    {
                        continue;
                    }

                    if (TryMoveTrashCandidateToHand(trashRule, trashOwner, observedCandidates[c]))
                    {
                        taken++;
                    }
                    break;
                }
            }
        }

        onComplete?.Invoke();
    }

    private static List<TrashExileCandidate> FilterObservedTrashCandidatesForHand(
        List<TrashExileCandidate> candidates,
        EffectData effect)
    {
        List<TrashExileCandidate> result = new List<TrashExileCandidate>();
        if (candidates == null)
        {
            return result;
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            TrashExileCandidate candidate = candidates[i];
            if (candidate.Data == null)
            {
                continue;
            }

            if (!EffectDataExtensions.MatchesTargetFeatureFilter(effect, candidate.Data))
            {
                continue;
            }

            if (!EffectDataExtensions.MatchesTargetCardTypeFilter(effect, candidate.Data))
            {
                continue;
            }

            if (!EffectDataExtensions.MatchesCardDataStatFilter(effect, candidate.Data))
            {
                continue;
            }

            result.Add(candidate);
        }

        return result;
    }

    private bool TryMoveTrashCandidateToHand(
        CardGameRule trashRule,
        PlayerType handOwner,
        TrashExileCandidate candidate)
    {
        if (trashRule == null || candidate.CardId < 0)
        {
            return false;
        }

        if (!trashRule.TryRemoveCardFromTrashAt(candidate.TrashIndex, out int removedId))
        {
            return false;
        }

        if (removedId != candidate.CardId)
        {
            trashRule.AddCardToTrash(removedId);
            return false;
        }

        CardGameRule handRule = handOwner == PlayerType.Player ? cardGameRule : enemyCardGameRule;
        AddCardIdToHand(handRule, handOwner, removedId);
        Debug.Log(
            $"[Effect] AddObservedToHandFromTrash {candidate.Data?.cardName ?? "?"}(id:{removedId}) "
            + $"handOwner:{handOwner}");
        return true;
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

    /// <summary>選択上限到達時、未選択のトラッシュカードをグレーアウトしてタップ不可にする。</summary>
    private static void ApplyTrashSelectionCardGrayedOut(GameObject cardGo, bool grayedOut)
    {
        if (cardGo == null)
        {
            return;
        }

        CanvasGroup canvasGroup = cardGo.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = cardGo.AddComponent<CanvasGroup>();
        }

        canvasGroup.alpha = grayedOut ? 0.38f : 1f;
        canvasGroup.blocksRaycasts = !grayedOut;

        Button button = cardGo.GetComponent<Button>();
        if (button != null)
        {
            button.interactable = !grayedOut;
        }
    }
}
