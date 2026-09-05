using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>山札→トラッシュ（MillTopToTrash）とチェーン観測コンテキスト。</summary>
public partial class BattleGameMain
{
    private const float MillToTrashCardPreviewWidth = 150f;
    private const float MillToTrashCardPreviewHeight = 210f;

    private EffectChainObservation _effectChainObservation;
    private int _effectChainObservationDepth;
    private bool _effectChainDealtDamage;
    private int _effectChainLastReturnFromTrashCount;
    private int _effectChainReturnFromTrashBatchSize;
    private List<TrashExileCandidate> _effectChainLastMilledTrashCandidates;
    private PlayerType _effectChainLastMilledTrashOwner = PlayerType.Player;

    private bool HasEffectChainObservation => _effectChainObservation != null && _effectChainObservation.HasCards;

    private void BeginEffectChainObservationScope(bool forceNewRoot = false)
    {
        if (forceNewRoot && _effectChainObservationDepth > 0)
        {
            Debug.LogWarning(
                $"[EffectChain] 観測スコープを強制リセット depth:{_effectChainObservationDepth} "
                + $"cards:{_effectChainObservation?.Cards?.Count ?? 0}");
            _effectChainObservationDepth = 0;
            _effectChainObservation?.Clear();
            _effectChainObservation = null;
            _effectChainLastMilledTrashCandidates = null;
            ClearEffectChainLastPickedTargets();
        }

        if (_effectChainObservationDepth++ == 0)
        {
            _effectChainObservation = new EffectChainObservation();
            _effectChainDealtDamage = false;
            _effectChainLastReturnFromTrashCount = 0;
            _effectChainReturnFromTrashBatchSize = 0;
            _effectChainLastMilledTrashCandidates = null;
            ClearEffectChainLastPickedTargets();
        }
    }

    private void EndEffectChainObservationScope()
    {
        if (_effectChainObservationDepth <= 0)
        {
            return;
        }

        if (--_effectChainObservationDepth == 0)
        {
            _effectChainObservation?.Clear();
            _effectChainObservation = null;
            _effectChainLastMilledTrashCandidates = null;
        }
    }

    /// <summary>ターン終了時など、漏れ残ったチェーン観測を破棄する。</summary>
    private void ForceClearEffectChainObservationScope()
    {
        if (_effectChainObservationDepth > 0 || _effectChainObservation != null)
        {
            Debug.Log(
                $"[EffectChain] ForceClear depth:{_effectChainObservationDepth} "
                + $"cards:{_effectChainObservation?.Cards?.Count ?? 0}");
        }

        _effectChainObservationDepth = 0;
        _effectChainObservation?.Clear();
        _effectChainObservation = null;
        _effectChainDealtDamage = false;
        _effectChainLastReturnFromTrashCount = 0;
        _effectChainReturnFromTrashBatchSize = 0;
        _effectChainLastMilledTrashCandidates = null;
        ClearEffectChainLastPickedTargets();
    }

    private void SetEffectChainLastReturnFromTrashCount(int count)
    {
        _effectChainLastReturnFromTrashCount = Mathf.Max(0, count);
    }

    private int GetEffectChainLastReturnFromTrashCount() => _effectChainLastReturnFromTrashCount;

    private void SetEffectChainReturnFromTrashBatchSize(int batchSize)
    {
        _effectChainReturnFromTrashBatchSize = Mathf.Max(0, batchSize);
    }

    private int GetEffectChainReturnFromTrashBatchSize() => _effectChainReturnFromTrashBatchSize;

    private IReadOnlyList<CardData> GetActiveObservedCardsForActivation()
    {
        return _effectChainObservation != null ? _effectChainObservation.Cards : null;
    }

    private void ObserveCardInEffectChain(CardData cardData)
    {
        _effectChainObservation?.Add(cardData);
    }

    private void SetObservedMilledTrashCandidates(PlayerType trashOwner, List<TrashExileCandidate> candidates)
    {
        _effectChainLastMilledTrashOwner = trashOwner;
        _effectChainLastMilledTrashCandidates = candidates != null
            ? new List<TrashExileCandidate>(candidates)
            : null;
    }

    private List<TrashExileCandidate> GetObservedMilledTrashCandidates(PlayerType trashOwner)
    {
        if (_effectChainLastMilledTrashCandidates == null || _effectChainLastMilledTrashOwner != trashOwner)
        {
            return new List<TrashExileCandidate>();
        }

        return new List<TrashExileCandidate>(_effectChainLastMilledTrashCandidates);
    }

    private bool CanRunTimedBlockAtChainTime(TimedEffectData timed, EffectActivationContext activationContext, string logTag)
    {
        if (timed == null)
        {
            return false;
        }

        if (timed.requireChainObservationContext && !HasEffectChainObservation)
        {
            Debug.Log($"[{logTag}] チェーン観測なしのためスキップ (requireChainObservationContext)");
            return false;
        }

        if (!EffectActivationEvaluator.AreTimedConditionsMet(timed, activationContext))
        {
            Debug.Log($"[{logTag}] 発動条件未達のためスキップ");
            return false;
        }

        return true;
    }

    private bool ShouldApplyChainedEffect(
        EffectData effect,
        EffectActivationContext activationContext,
        string logTag)
    {
        if (effect == null)
        {
            return false;
        }

        if (!effect.HasEffectActivationConditions() && !effect.requireChainObservationContext)
        {
            return true;
        }

        if (effect.requireChainObservationContext && !HasEffectChainObservation)
        {
            Debug.Log($"[{logTag}] 効果スキップ: チェーン観測なし ({effect.type})");
            return false;
        }

        // 手動ユニット選択は ResolveSelectableEffectTargets で候補ごとに effectActivationConditions を評価する。
        // ここで効果元（コマンド等）に対して評価すると SourceUnitIsLinked 等が誤って false になる。
        if (EffectRequiresManualUnitSelection(effect))
        {
            return true;
        }

        EffectActivationContext contextForConditions = activationContext;
        if (activationContext != null
            && effect.HasEffectActivationConditions()
            && EffectActivationEvaluator.ContainsObservedCardCondition(effect.effectActivationConditions))
        {
            // OnMain 等はチェーン開始時コンテキストを使い回すため、途中で観測したカードをここで反映する
            contextForConditions = activationContext
                .WithObservedCards(GetActiveObservedCardsForActivation())
                .WithPriorChainDealtDamage(GetEffectChainDealtDamage());
        }

        if (activationContext != null
            && effect.HasEffectActivationConditions()
            && EffectActivationEvaluator.ContainsPriorChainPickedCondition(effect.effectActivationConditions))
        {
            contextForConditions = (contextForConditions ?? activationContext)
                .WithPriorChainPickedUnits(GetAliveEffectChainLastPickedTargets());
        }

        if (effect.HasEffectActivationConditions()
            && !EffectActivationEvaluator.AreAllConditionsMet(
                effect.effectActivationConditions,
                contextForConditions))
        {
            Debug.Log($"[{logTag}] 効果スキップ: effectActivationConditions 未達 ({effect.type})");
            return false;
        }

        return true;
    }

    private void MarkEffectChainDealtDamage()
    {
        _effectChainDealtDamage = true;
    }

    private bool GetEffectChainDealtDamage() => _effectChainDealtDamage;

    private static bool IsFieldWideUnitDamageEffect(EffectData effect)
    {
        return effect != null
            && effect.type == EffectType.Damage
            && (effect.target == TargetType.AllyAllUnits || effect.target == TargetType.EnemyAllUnits);
    }

    private void TryApplyFieldWideDamageWithPreviewAsync(
        CardController sourceCard,
        PlayerType ownerType,
        EffectData effect,
        System.Action onContinue)
    {
        if (ownerType != PlayerType.Player)
        {
            ApplyEffectRespectingLookAsync(sourceCard, ownerType, effect, onContinue);
            return;
        }

        List<CardController> previewTargets = ResolveEffectTargets(sourceCard, ownerType, effect);
        int magnitude = ResolveEffectMagnitude(effect, ownerType, sourceCard);
        StartCoroutine(ShowFieldDamagePreviewAndApplyCoroutine(
            sourceCard,
            ownerType,
            effect,
            previewTargets,
            magnitude,
            onContinue));
    }

    private IEnumerator ShowFieldDamagePreviewAndApplyCoroutine(
        CardController sourceCard,
        PlayerType ownerType,
        EffectData effect,
        List<CardController> targets,
        int magnitude,
        System.Action onContinue)
    {
        string sideLabel = effect.target == TargetType.EnemyAllUnits
            ? GameLocale.T("相手", "Opponent")
            : GameLocale.T("自分", "Your");
        GameObject root = BuildFieldDamagePreviewPanel(sideLabel, targets, magnitude, sourceCard);
        if (root == null)
        {
            ApplyEffect(sourceCard, ownerType, effect);
            onContinue?.Invoke();
            yield break;
        }

        bool acknowledged = false;
        Button okBtn = root.CreateChildButton("OK");
        RectTransform okRt = okBtn.GetComponent<RectTransform>();
        okRt.sizeDelta = new Vector2(220f, 52f);
        okRt.anchorMin = new Vector2(0.5f, 0f);
        okRt.anchorMax = new Vector2(0.5f, 0f);
        okRt.pivot = new Vector2(0.5f, 0f);
        okRt.anchoredPosition = new Vector2(0f, 36f);
        TextMeshProUGUI okLabel = okBtn.GetComponentInChildren<TextMeshProUGUI>();
        if (okLabel != null)
        {
            okLabel.text = "OK";
        }

        okBtn.onClick.AddListener(() =>
        {
            acknowledged = true;
            isOnActionPopupOpen = false;
            activeOnActionPopupRoot = null;
            Destroy(root);
        });

        yield return new WaitUntil(() => acknowledged);
        ApplyEffect(sourceCard, ownerType, effect);
        onContinue?.Invoke();
    }

    private GameObject BuildFieldDamagePreviewPanel(
        string sideLabel,
        List<CardController> targets,
        int magnitude,
        CardController sourceCard)
    {
        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            return null;
        }

        DestroyActiveOnActionPopupIfAny();
        GameObject root = new GameObject(
            "FieldDamagePreview",
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

        TextMeshProUGUI title = root.CreateChildTextCustom("FieldDamageTitle", UIAnchor.TopCenter, 760, 48);
        if (targets != null && targets.Count > 0)
        {
            title.SetLocalizedText(
                $"{sideLabel}フィールドへ {magnitude} ダメージ",
                $"{magnitude} damage to {sideLabel} field");
        }
        else
        {
            title.SetLocalizedText(
                $"{sideLabel}フィールドにダメージ対象なし",
                $"No damage targets on {sideLabel} field");
        }

        title.fontSize = 26;
        title.fontStyle = FontStyles.Bold;
        title.color = Color.white;
        title.alignment = TextAlignmentOptions.Center;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -18f);

        string sourceName = sourceCard?.Data?.cardName ?? "?";
        TextMeshProUGUI subtitle = root.CreateChildTextCustom("FieldDamageSubtitle", UIAnchor.TopCenter, 760, 36);
        if (targets != null && targets.Count > 0)
        {
            subtitle.SetLocalizedText(
                $"{sourceName} の効果 — 対象 {targets.Count} 体（各 {magnitude} ダメージ）",
                $"{sourceName}'s effect — {targets.Count} target(s) ({magnitude} damage each)");
        }
        else
        {
            subtitle.SetLocalizedText(
                $"{sourceName} の効果 — ユニットがいないためダメージは入りません",
                $"{sourceName}'s effect — no Units, so no damage is dealt");
        }

        subtitle.fontSize = 18;
        subtitle.color = new Color(0.88f, 0.92f, 1f, 1f);
        subtitle.alignment = TextAlignmentOptions.Center;
        subtitle.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -56f);

        TextMeshProUGUI hint = root.CreateChildTextCustom("FieldDamageHint", UIAnchor.TopCenter, 760, 28);
        hint.SetLocalizedText("対象カードを確認して OK でダメージを適用", "Review targets, then OK to apply damage");
        hint.fontSize = 15;
        hint.color = new Color(0.75f, 0.8f, 0.85f, 1f);
        hint.alignment = TextAlignmentOptions.Center;
        hint.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -86f);

        if (targets == null || targets.Count == 0 || CardImagePrefab == null)
        {
            return root;
        }

        GameObject scrollGo = root.CreateGridScrollView(760, 420, UIAnchor.TopCenter);
        RectTransform scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.anchoredPosition = new Vector2(0f, -118f);
        scrollGo.ConfigureGridCellFromViewportHeight(0.82f, 72f);
        ScrollRect sr = scrollGo.GetComponent<ScrollRect>();
        RectTransform content = sr != null ? sr.content : null;
        if (content != null)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                CardController unit = targets[i];
                if (unit?.Data == null)
                {
                    continue;
                }

                int resolvedDamage = ResolveEffectDamageAmount(magnitude, unit);
                AppendFieldDamagePreviewEntry(content, unit.Data, i + 1, unit.CurrentHp, resolvedDamage);
            }
        }

        return root;
    }

    private void AppendFieldDamagePreviewEntry(
        RectTransform content,
        CardData data,
        int orderIndex,
        int currentHp,
        int damageAmount)
    {
        GameObject cell = new GameObject(
            $"FieldDamageCard_{orderIndex}",
            typeof(RectTransform),
            typeof(LayoutElement));
        cell.transform.SetParent(content, false);
        LayoutElement layout = cell.GetComponent<LayoutElement>();
        layout.minHeight = 250f;
        layout.preferredHeight = 250f;
        layout.minWidth = 700f;
        layout.preferredWidth = 700f;

        RectTransform cellRt = cell.GetComponent<RectTransform>();
        cellRt.sizeDelta = new Vector2(700f, 250f);

        GameObject cardGo = Instantiate(CardImagePrefab, cell.transform);
        RectTransform cardRt = cardGo.GetComponent<RectTransform>();
        cardRt.anchorMin = new Vector2(0f, 0.5f);
        cardRt.anchorMax = new Vector2(0f, 0.5f);
        cardRt.pivot = new Vector2(0f, 0.5f);
        cardRt.sizeDelta = new Vector2(MillToTrashCardPreviewWidth, MillToTrashCardPreviewHeight);
        cardRt.anchoredPosition = new Vector2(8f, 0f);

        CardController preview = cardGo.GetComponent<CardController>();
        preview?.SetUp(data, _ => { });
        Button cardBtn = cardGo.GetComponent<Button>();
        if (cardBtn != null)
        {
            cardBtn.interactable = false;
        }

        AppendCardDataStatOverlay(cardGo, data);

        TextMeshProUGUI detail = cell.CreateChildTextCustom("CardDetail", UIAnchor.TopLeft, 500, 220);
        RectTransform detailRt = detail.GetComponent<RectTransform>();
        detailRt.anchorMin = new Vector2(0f, 0.5f);
        detailRt.anchorMax = new Vector2(0f, 0.5f);
        detailRt.pivot = new Vector2(0f, 0.5f);
        detailRt.anchoredPosition = new Vector2(MillToTrashCardPreviewWidth + 24f, 0f);
        int hpAfter = Mathf.Max(0, currentHp - damageAmount);
        detail.SetLocalizedText(
            FormatMillToTrashCardDetailText(data, orderIndex)
            + $"\n現在HP {currentHp} → {hpAfter}（-{damageAmount}）",
            FormatMillToTrashCardDetailText(data, orderIndex)
            + $"\nCurrent HP {currentHp} → {hpAfter} (-{damageAmount})");
        detail.fontSize = 16;
        detail.color = Color.white;
        detail.alignment = TextAlignmentOptions.TopLeft;
        detail.enableWordWrapping = true;
    }

    private void ApplyMillTopToTrashEffect(
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
        List<CardData> milledCards = new List<CardData>(magnitude);
        List<int> milledCardIds = new List<int>(magnitude);
        List<TrashExileCandidate> observedTrashCandidates = new List<TrashExileCandidate>(magnitude);

        WithZoneSyncSuppressed(() =>
        {
            for (int i = 0; i < magnitude; i++)
            {
                if (!deckRule.TryTakeCardAtDeckIndex(0, out int cardId))
                {
                    break;
                }

                CardData data = DeckSettinObject.Instance.GetCardDataById(cardId);
                deckRule.AddCardToTrash(cardId);
                milledCardIds.Add(cardId);
                observedTrashCandidates.Add(new TrashExileCandidate(
                    deckRule.GetTrashCardIds().Count - 1,
                    cardId,
                    data));
                if (data != null)
                {
                    milledCards.Add(data);
                    ObserveCardInEffectChain(data);
                }

                Debug.Log(
                    $"[Effect] MillTopToTrash {data?.cardName ?? "?"}(id:{cardId}) deck:{deckLabel} "
                    + $"by cardId:{sourceCard?.Data?.id}");
            }
        });

        if (milledCardIds.Count > 0)
        {
            int deckRemain = deckRule.GetRemainingCount();
            SyncGundamRuleDeckCount(deckOwner, deckRemain);
            NotifyLocalZoneDeckToTrash(deckOwner, milledCardIds, deckRemain);
        }

        SetObservedMilledTrashCandidates(deckOwner, observedTrashCandidates);

        if (ownerType == PlayerType.Player && milledCards.Count > 0)
        {
            StartCoroutine(ShowMillToTrashAcknowledgementCoroutine(deckLabel, milledCards, onComplete));
            return;
        }

        onComplete?.Invoke();
    }

    private void SyncGundamRuleDeckCount(PlayerType deckOwner, int deckRemain)
    {
        if (gundamRule == null)
        {
            return;
        }

        Gundam2024RuleScript.PlayerState state = deckOwner == PlayerType.Player
            ? gundamRule.Player
            : gundamRule.Enemy;
        if (state != null)
        {
            state.deckCount = Mathf.Max(0, deckRemain);
        }
    }

    private IEnumerator ShowMillToTrashAcknowledgementCoroutine(
        string deckLabel,
        List<CardData> milledCards,
        Action onComplete)
    {
        GameObject root = BuildMillToTrashAcknowledgementPanel(deckLabel, milledCards);
        if (root == null)
        {
            onComplete?.Invoke();
            yield break;
        }

        bool acknowledged = false;
        Button okBtn = root.CreateChildButton("OK");
        RectTransform okRt = okBtn.GetComponent<RectTransform>();
        okRt.sizeDelta = new Vector2(220f, 52f);
        okRt.anchorMin = new Vector2(0.5f, 0f);
        okRt.anchorMax = new Vector2(0.5f, 0f);
        okRt.pivot = new Vector2(0.5f, 0f);
        okRt.anchoredPosition = new Vector2(0f, 36f);
        TextMeshProUGUI okLabel = okBtn.GetComponentInChildren<TextMeshProUGUI>();
        if (okLabel != null)
        {
            okLabel.text = "OK";
        }

        okBtn.onClick.AddListener(() =>
        {
            acknowledged = true;
            Destroy(root);
            if (activeOnActionPopupRoot == root)
            {
                activeOnActionPopupRoot = null;
            }

            isOnActionPopupOpen = activeOnActionPopupRoot != null
                || _activeLookDeckPopupRoot != null
                || _isActionStepCommandResolving
                || _activeResourcePaymentOverlay != null;
        });

        // 他効果 UI が DestroyActiveOnActionPopupIfAny でこのパネルを潰しても待機解除する
        // （潰れたままだと OnDestroyed 完了通知が送られず、破壊側 effectthink が残る）
        yield return new WaitUntil(() => acknowledged || root == null);
        if (!acknowledged && activeOnActionPopupRoot == root)
        {
            activeOnActionPopupRoot = null;
        }

        isOnActionPopupOpen = activeOnActionPopupRoot != null
            || _activeLookDeckPopupRoot != null
            || _isActionStepCommandResolving
            || _activeResourcePaymentOverlay != null;

        // Mill 了承／パネル消滅時点で破壊側 effectthink を解除（Look OK と同様・非 OnDestroyed 時は no-op）
        NotifyOnlineOnDestroyedPlayerAcknowledged();
        onComplete?.Invoke();
    }

    private GameObject BuildMillToTrashAcknowledgementPanel(string deckLabel, List<CardData> milledCards)
    {
        if (milledCards == null || milledCards.Count == 0 || CardImagePrefab == null)
        {
            return null;
        }

        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            return null;
        }

        DestroyActiveOnActionPopupIfAny();
        GameObject root = new GameObject(
            "MillToTrashReveal",
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

        TextMeshProUGUI title = root.CreateChildTextCustom("MillTitle", UIAnchor.TopCenter, 760, 48);
        if (milledCards.Count == 1)
        {
            title.SetLocalizedText("このカードをトラッシュへ", "This card to trash");
        }
        else
        {
            title.SetLocalizedText("これらのカードをトラッシュへ", "These cards to trash");
        }

        title.fontSize = 26;
        title.fontStyle = FontStyles.Bold;
        title.color = Color.white;
        title.alignment = TextAlignmentOptions.Center;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -18f);

        TextMeshProUGUI subtitle = root.CreateChildTextCustom("MillSubtitle", UIAnchor.TopCenter, 760, 36);
        if (milledCards.Count == 1)
        {
            subtitle.SetLocalizedText(
                $"このカードをトラッシュに置きました（{deckLabel}の山札）",
                $"Sent this card to trash ({deckLabel}'s deck)");
        }
        else
        {
            subtitle.SetLocalizedText(
                $"{milledCards.Count}枚をトラッシュに置きました（{deckLabel}の山札）",
                $"Sent {milledCards.Count} cards to trash ({deckLabel}'s deck)");
        }

        subtitle.fontSize = 18;
        subtitle.color = new Color(0.88f, 0.92f, 1f, 1f);
        subtitle.alignment = TextAlignmentOptions.Center;
        subtitle.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -56f);

        TextMeshProUGUI hint = root.CreateChildTextCustom("MillHint", UIAnchor.TopCenter, 760, 28);
        hint.SetLocalizedText("カード内容を確認して OK で続行", "Review the cards, then OK to continue");
        hint.fontSize = 15;
        hint.color = new Color(0.75f, 0.8f, 0.85f, 1f);
        hint.alignment = TextAlignmentOptions.Center;
        hint.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -86f);

        GameObject scrollGo = root.CreateGridScrollView(760, 420, UIAnchor.TopCenter);
        RectTransform scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.anchoredPosition = new Vector2(0f, -118f);
        scrollGo.ConfigureGridCellFromViewportHeight(0.82f, 72f);
        ScrollRect sr = scrollGo.GetComponent<ScrollRect>();
        RectTransform content = sr != null ? sr.content : null;
        if (content != null)
        {
            for (int i = 0; i < milledCards.Count; i++)
            {
                CardData data = milledCards[i];
                if (data == null)
                {
                    continue;
                }

                AppendMillToTrashCardDetailEntry(content, data, i + 1);
            }
        }

        return root;
    }

    private void AppendMillToTrashCardDetailEntry(RectTransform content, CardData data, int orderIndex)
    {
        GameObject cell = new GameObject(
            $"MillCard_{orderIndex}",
            typeof(RectTransform),
            typeof(LayoutElement));
        cell.transform.SetParent(content, false);
        LayoutElement layout = cell.GetComponent<LayoutElement>();
        layout.minHeight = 250f;
        layout.preferredHeight = 250f;
        layout.minWidth = 700f;
        layout.preferredWidth = 700f;

        RectTransform cellRt = cell.GetComponent<RectTransform>();
        cellRt.sizeDelta = new Vector2(700f, 250f);

        GameObject cardGo = Instantiate(CardImagePrefab, cell.transform);
        RectTransform cardRt = cardGo.GetComponent<RectTransform>();
        cardRt.anchorMin = new Vector2(0f, 0.5f);
        cardRt.anchorMax = new Vector2(0f, 0.5f);
        cardRt.pivot = new Vector2(0f, 0.5f);
        cardRt.sizeDelta = new Vector2(MillToTrashCardPreviewWidth, MillToTrashCardPreviewHeight);
        cardRt.anchoredPosition = new Vector2(8f, 0f);

        CardController preview = cardGo.GetComponent<CardController>();
        preview?.SetUp(data, _ => { });
        Button cardBtn = cardGo.GetComponent<Button>();
        if (cardBtn != null)
        {
            cardBtn.interactable = false;
        }

        AppendCardDataStatOverlay(cardGo, data);

        TextMeshProUGUI detail = cell.CreateChildTextCustom("CardDetail", UIAnchor.TopLeft, 500, 220);
        RectTransform detailRt = detail.GetComponent<RectTransform>();
        detailRt.anchorMin = new Vector2(0f, 0.5f);
        detailRt.anchorMax = new Vector2(0f, 0.5f);
        detailRt.pivot = new Vector2(0f, 0.5f);
        detailRt.anchoredPosition = new Vector2(MillToTrashCardPreviewWidth + 24f, 0f);
        detail.text = FormatMillToTrashCardDetailText(data, orderIndex);
        detail.fontSize = 16;
        detail.color = Color.white;
        detail.alignment = TextAlignmentOptions.TopLeft;
        detail.enableWordWrapping = true;
    }

    private static void AppendCardDataStatOverlay(GameObject cardGo, CardData data)
    {
        if (cardGo == null || data == null)
        {
            return;
        }

        GameObject statBg = new GameObject("StatBg", typeof(RectTransform), typeof(Image));
        statBg.transform.SetParent(cardGo.transform, false);
        RectTransform statBgRt = statBg.GetComponent<RectTransform>();
        statBgRt.anchorMin = new Vector2(0f, 0f);
        statBgRt.anchorMax = new Vector2(1f, 0f);
        statBgRt.pivot = new Vector2(0.5f, 0f);
        statBgRt.sizeDelta = new Vector2(0f, 34f);
        statBgRt.anchoredPosition = Vector2.zero;
        Image statBgImg = statBg.GetComponent<Image>();
        statBgImg.color = new Color(0f, 0f, 0f, 0.65f);
        statBgImg.raycastTarget = false;

        string statLine = data.IsCommand()
            ? $"COST {data.cost}"
            : $"AP {data.power}  HP {data.hp}";

        TextMeshProUGUI statText = statBg.CreateChildTextCustom("StatText", UIAnchor.FullSize, 140, 32);
        statText.text = statLine;
        statText.fontSize = 13;
        statText.color = Color.white;
        statText.alignment = TextAlignmentOptions.Center;
    }

    private static string FormatMillToTrashCardDetailText(CardData data, int orderIndex)
    {
        if (data == null)
        {
            return string.Empty;
        }

        StringBuilder sb = new StringBuilder();
        if (orderIndex > 0)
        {
            sb.Append("#").Append(orderIndex).Append("  ");
        }

        sb.AppendLine(data.cardName);
        sb.Append("ID: ").Append(data.id).Append("  ");
        sb.Append(CardTypeExtensions.GetDisplayName(data.type)).AppendLine();
        for (int i = 0; i < data.features.Count; i++)
        {
            CardFeatureData feature = data.features[i];
            if (feature == null)
            {
                continue;
            }
            sb.Append("Feature: ").Append(feature.displayName).AppendLine();
        }

        sb.Append("Lv.").Append(data.level)
            .Append("  COST ").Append(data.cost);
        if (!data.IsCommand())
        {
            sb.Append("  AP ").Append(data.power)
                .Append("  HP ").Append(data.hp);
        }

        sb.AppendLine();
        sb.Append("Feature: ").Append(FormatCardFeatureListLabel(data));
        return sb.ToString();
    }

    private static string FormatCardFeatureListLabel(CardData data)
    {
        if (data?.features == null || data.features.Count == 0)
        {
            return GameLocale.T("（なし）", "(none)");
        }

        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < data.features.Count; i++)
        {
            CardFeatureData feature = data.features[i];
            if (feature == null)
            {
                continue;
            }

            if (sb.Length > 0)
            {
                sb.Append(", ");
            }

            sb.Append(string.IsNullOrWhiteSpace(feature.displayName) ? feature.featureKey : feature.displayName);
        }

        return sb.Length > 0 ? sb.ToString() : "（なし）";
    }
}
