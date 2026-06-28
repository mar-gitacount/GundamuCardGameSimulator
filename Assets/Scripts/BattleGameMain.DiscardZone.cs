using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>トラッシュ／除外ゾーン UI と除外（Exile）効果。</summary>
public partial class BattleGameMain
{
    private GameObject _activeDiscardZoneInspectRoot;

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

        for (int i = 0; i < magnitude; i++)
        {
            if (!deckRule.TryTakeCardAtDeckIndex(0, out int cardId))
            {
                break;
            }

            CardData data = DeckSettinObject.Instance.GetCardDataById(cardId);
            deckRule.AddCardToExile(cardId);
            if (data != null)
            {
                ObserveCardInEffectChain(data);
            }

            Debug.Log(
                $"[Effect] ExileFromDeck {data?.cardName ?? "?"}(id:{cardId}) deck:{deckLabel} "
                + $"by cardId:{sourceCard?.Data?.id}");
        }

        SyncGundamRuleDeckCount(deckOwner, deckRule.GetRemainingCount());
        onComplete?.Invoke();
    }
}
