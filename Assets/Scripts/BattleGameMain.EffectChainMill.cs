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

    private bool HasEffectChainObservation => _effectChainObservation != null && _effectChainObservation.HasCards;

    private void BeginEffectChainObservationScope()
    {
        if (_effectChainObservationDepth++ == 0)
        {
            _effectChainObservation = new EffectChainObservation();
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
        }
    }

    private IReadOnlyList<CardData> GetActiveObservedCardsForActivation()
    {
        return _effectChainObservation != null ? _effectChainObservation.Cards : null;
    }

    private void ObserveCardInEffectChain(CardData cardData)
    {
        _effectChainObservation?.Add(cardData);
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

        if (effect.HasEffectActivationConditions()
            && !EffectActivationEvaluator.AreAllConditionsMet(effect.effectActivationConditions, activationContext))
        {
            Debug.Log($"[{logTag}] 効果スキップ: effectActivationConditions 未達 ({effect.type})");
            return false;
        }

        return true;
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
            isOnActionPopupOpen = false;
            activeOnActionPopupRoot = null;
            Destroy(root);
        });

        yield return new WaitUntil(() => acknowledged);
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
        title.text = milledCards.Count == 1
            ? "This card to trash"
            : "These cards to trash";
        title.fontSize = 26;
        title.fontStyle = FontStyles.Bold;
        title.color = Color.white;
        title.alignment = TextAlignmentOptions.Center;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -18f);

        TextMeshProUGUI subtitle = root.CreateChildTextCustom("MillSubtitle", UIAnchor.TopCenter, 760, 36);
        subtitle.text = milledCards.Count == 1
            ? $"このカードをトラッシュに置きました（{deckLabel}の山札）"
            : $"{milledCards.Count}枚をトラッシュに置きました（{deckLabel}の山札）";
        subtitle.fontSize = 18;
        subtitle.color = new Color(0.88f, 0.92f, 1f, 1f);
        subtitle.alignment = TextAlignmentOptions.Center;
        subtitle.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -56f);

        TextMeshProUGUI hint = root.CreateChildTextCustom("MillHint", UIAnchor.TopCenter, 760, 28);
        hint.text = "カード内容を確認して OK で続行";
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

        string statLine = data.type == Type.Command
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
        sb.Append("Lv.").Append(data.level)
            .Append("  COST ").Append(data.cost);
        if (data.type != Type.Command)
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
            return "（なし）";
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
