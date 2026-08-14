using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Draw 効果で引いたカードをプレイヤーに公開する UI。</summary>
public partial class BattleGameMain
{
    private GameObject _activeDrawRevealRoot;

    private static bool ShouldRevealDrawnCards(EffectData effect, PlayerType ownerType)
    {
        return effect != null
            && effect.type == EffectType.Draw
            && effect.revealDrawnToPlayer
            && ownerType == PlayerType.Player;
    }

    private IEnumerator ApplyDrawEffectWithRevealCoroutine(
        CardController sourceCard,
        PlayerType ownerType,
        EffectData effect,
        System.Action onChainContinue)
    {
        int magnitude = ResolveEffectMagnitude(effect, ownerType, sourceCard);
        if (magnitude <= 0)
        {
            onChainContinue?.Invoke();
            yield break;
        }

        CardGameRule rule = cardGameRule;
        List<CardData> drawnCards = new List<CardData>(magnitude);
        BeginOnlineEffectSyncBatch(ownerType);
        for (int i = 0; i < magnitude; i++)
        {
            CardController drawn = CardAddtoHandAndReturn(rule, ownerType);
            if (drawn == null || drawn.Data == null)
            {
                break;
            }

            drawnCards.Add(drawn.Data);
        }

        FlushOnlineEffectSyncBatch();
        Debug.Log($"[Effect] Draw x{drawnCards.Count} (reveal) by cardId:{sourceCard?.Data?.id}");

        if (drawnCards.Count == 0)
        {
            onChainContinue?.Invoke();
            yield break;
        }

        yield return ShowDrawnCardsRevealPanelCoroutine(drawnCards);
        onChainContinue?.Invoke();
    }

    private IEnumerator ShowDrawnCardsRevealPanelCoroutine(IReadOnlyList<CardData> drawnCards)
    {
        if (drawnCards == null || drawnCards.Count == 0)
        {
            yield break;
        }

        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null || CardImagePrefab == null)
        {
            yield break;
        }

        CloseDrawRevealPanelIfAny();
        DestroyActiveOnActionPopupIfAny();
        GameObject root = new GameObject(
            "DrawReveal",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        _activeDrawRevealRoot = root;
        activeOnActionPopupRoot = root;
        isOnActionPopupOpen = true;
        root.transform.SetParent(canvas.transform, false);
        root.transform.SetAsLastSibling();
        root.SetFullSize();
        Image dim = root.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.6f);
        dim.raycastTarget = true;

        TextMeshProUGUI title = root.CreateChildTextCustom("DrawRevealTitle", UIAnchor.TopCenter, 720, 48);
        if (drawnCards.Count == 1)
        {
            title.SetLocalizedText("引いたカード", "Drawn card");
        }
        else
        {
            title.SetLocalizedText(
                $"引いたカード（{drawnCards.Count}枚）",
                $"Drawn cards ({drawnCards.Count})");
        }

        title.color = Color.white;
        title.fontSize = 24;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -24f);

        TextMeshProUGUI hint = root.CreateChildTextCustom("DrawRevealHint", UIAnchor.TopCenter, 720, 32);
        hint.SetLocalizedText("確認したら OK を押してください", "Press OK when ready");
        hint.color = new Color(0.85f, 0.9f, 1f);
        hint.fontSize = 18;
        hint.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -62f);

        GameObject scrollGo = root.CreateGridScrollView(700, 360, UIAnchor.TopCenter);
        RectTransform scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.anchoredPosition = new Vector2(0f, -88f);
        scrollGo.ConfigureGridCellFromViewportHeight(0.78f, 56f);
        ScrollRect sr = scrollGo.GetComponent<ScrollRect>();
        RectTransform content = sr != null ? sr.content : null;

        if (content != null)
        {
            for (int i = 0; i < drawnCards.Count; i++)
            {
                CardData data = drawnCards[i];
                if (data == null)
                {
                    continue;
                }

                GameObject cardGo = Instantiate(CardImagePrefab, content);
                CardController preview = cardGo.GetComponent<CardController>();
                preview?.SetUp(data, _ => { });
                Button blocker = cardGo.GetComponent<Button>();
                if (blocker != null)
                {
                    blocker.interactable = false;
                }
            }
        }

        bool acknowledged = false;
        Button ok = root.CreateChildButton("DrawRevealOk");
        RectTransform okRt = ok.GetComponent<RectTransform>();
        okRt.sizeDelta = new Vector2(180f, 46f);
        okRt.anchoredPosition = new Vector2(0f, 52f);
        TextMeshProUGUI okLabel = ok.GetComponentInChildren<TextMeshProUGUI>();
        if (okLabel != null)
        {
            okLabel.text = "OK";
        }

        ok.onClick.AddListener(() => { acknowledged = true; });
        yield return new WaitUntil(() => acknowledged);
        CloseDrawRevealPanelIfAny();
    }

    private void CloseDrawRevealPanelIfAny()
    {
        if (_activeDrawRevealRoot == null)
        {
            return;
        }

        if (activeOnActionPopupRoot == _activeDrawRevealRoot)
        {
            activeOnActionPopupRoot = null;
        }

        Destroy(_activeDrawRevealRoot);
        _activeDrawRevealRoot = null;

        if (activeOnActionPopupRoot == null)
        {
            isOnActionPopupOpen = false;
        }
    }
}
