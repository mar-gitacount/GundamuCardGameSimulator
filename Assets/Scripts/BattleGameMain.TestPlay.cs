using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>TestPlay（ソロ両サイド操作）専用ロジック。AI / Online とは独立。</summary>
public partial class BattleGameMain
{
    private bool IsTestPlayBattle()
    {
        return TestPlayMatchState.HasActiveSession;
    }

    /// <summary>
    /// TestPlay では自動の効果解決を行わない（配備時・誘発・攻撃時など）。
    /// 手動 OnMain / OnRest ボタンやサンドボックス操作は対象外。Online / AI には影響しない。
    /// </summary>
    private bool ShouldSkipAutomaticEffectsInTestPlay()
    {
        return IsTestPlayBattle();
    }

    /// <summary>TestPlay は手番に関係なく両サイドのカードを操作できる。</summary>
    private bool IsActingSideForUi(PlayerType ownerType)
    {
        return IsTestPlayBattle() || ownerType == currentPlayerType;
    }

    /// <summary>TestPlay: 人間が操作するサイドのメイン待ち（AI なし）。</summary>
    public void EnterTestPlayControlledMainPhase(PlayerType side)
    {
        if (!IsTestPlayBattle())
        {
            return;
        }

        currentPlayerType = side;
        ApplyTestPlayBoardPerspective(side);
        UpdateEndTurnButtonVisibility();
        Debug.Log($"[TestPlay] Human-controlled main phase: {side}");
    }

    /// <summary>
    /// 操作中サイドを画面下（非回転）に置き、反対サイドを上（180°）に置く。
    /// </summary>
    private void ApplyTestPlayBoardPerspective(PlayerType activeSide)
    {
        if (!IsTestPlayBattle())
        {
            return;
        }

        RectTransform boardCanvas = ResolveBattleBoardRoot();
        RectTransform playerRt = PlayerFieldPanel != null
            ? PlayerFieldPanel.GetComponent<RectTransform>()
            : null;
        RectTransform enemyRt = EnemyPlayerFieldPanel != null
            ? EnemyPlayerFieldPanel.GetComponent<RectTransform>()
            : null;
        if (boardCanvas == null || playerRt == null || enemyRt == null)
        {
            return;
        }

        // Apply(bottomField, topField): 第1引数が下・非回転、第2が上・180°
        if (activeSide == PlayerType.Player)
        {
            BattleBoardScrollLayout.Apply(boardCanvas, playerRt, enemyRt);
        }
        else
        {
            BattleBoardScrollLayout.Apply(boardCanvas, enemyRt, playerRt);
        }

        if (cardGameRule != null)
        {
            cardGameRule.RefreshResourceBoardFlipState();
        }

        if (enemyCardGameRule != null)
        {
            enemyCardGameRule.RefreshResourceBoardFlipState();
        }

        Canvas.ForceUpdateCanvases();

        if (cardGameRule != null)
        {
            cardGameRule.RefreshResourceBoardFlipState();
        }

        if (enemyCardGameRule != null)
        {
            enemyCardGameRule.RefreshResourceBoardFlipState();
        }

        ConfigureEndTurnButtonForActiveSide(activeSide);
        RefreshTestPlayDeckDrawButtonStates();
    }

    private void ConfigureEndTurnButtonForActiveSide(PlayerType activeSide)
    {
        if (EndTurnButton == null)
        {
            return;
        }

        CardGameRule rule = activeSide == PlayerType.Player ? cardGameRule : enemyCardGameRule;
        RectTransform handPanel = rule != null ? rule.PlayerHandPanel : null;
        if (handPanel == null)
        {
            return;
        }

        RectTransform btnRect = EndTurnButton.GetComponent<RectTransform>();
        if (btnRect == null)
        {
            return;
        }

        EndTurnButton.transform.SetParent(handPanel, false);
        EndTurnButton.transform.SetAsLastSibling();
        EndTurnButton.gameObject.SetActive(true);

        Canvas.ForceUpdateCanvases();
        float handWidth = handPanel.rect.width;
        float minWidthForFiveCards = rule.GetHandMinimumWidthForVisibleCards(5);
        float extraWidth = Mathf.Max(0f, handWidth - minWidthForFiveCards);
        float endTurnAreaWidth = Mathf.Clamp(extraWidth, MinEndTurnAreaWidth, MaxEndTurnAreaWidth);
        if (extraWidth < MinEndTurnAreaWidth)
        {
            endTurnAreaWidth = Mathf.Max(70f, extraWidth);
        }

        btnRect.anchorMin = new Vector2(1f, 0f);
        btnRect.anchorMax = new Vector2(1f, 0f);
        btnRect.pivot = new Vector2(1f, 0f);
        btnRect.anchoredPosition = new Vector2(-10f, 10f);
        btnRect.sizeDelta = new Vector2(Mathf.Max(68f, endTurnAreaWidth - 16f), 44f);
        rule.SetHandScrollRightMargin(endTurnAreaWidth);
    }

    private void ConfigureTestPlayDecks(ref Dictionary<int, int> playerDeck, ref Dictionary<int, int> enemyDeck)
    {
        if (!IsTestPlayBattle())
        {
            return;
        }

        // 両デッキはメニューで個別選択済み。空なら最低限の安全策のみ。
        playerDeck = playerDeck ?? new Dictionary<int, int>();
        enemyDeck = enemyDeck ?? new Dictionary<int, int>();
        if (enemyDeck.Count == 0 && playerDeck.Count > 0)
        {
            Debug.LogWarning("[TestPlay] Enemy deck empty — padding from player deck.");
            enemyDeck = new Dictionary<int, int>(playerDeck);
        }

        Debug.Log(
            $"[TestPlay] Decks ready playerTypes:{playerDeck.Count} enemyTypes:{enemyDeck.Count}");
    }

    /// <summary>
    /// TestPlay: 自分／相手を別 UI で配置。マリガン or キープを押したサイドの UI だけ消す。
    /// 両サイド確定後にシールド配備へ進む。
    /// </summary>
    private IEnumerator RunTestPlayDualMulliganCoroutine(Canvas canvas, int openingHandSize, int exBasePoints)
    {
        if (canvas == null)
        {
            yield return RunTestPlayOpeningWithoutMulliganCoroutine(exBasePoints);
            yield break;
        }

        isMulliganPromptOpen = true;

        GameObject layer = new GameObject(
            "TestPlayMulliganLayer",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        layer.transform.SetParent(canvas.transform, false);
        layer.transform.SetAsLastSibling();
        RectTransform layerRt = layer.GetComponent<RectTransform>();
        layerRt.SetFullSize();
        Image dim = layer.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.62f);
        dim.raycastTarget = true;

        bool playerDecided = false;
        bool enemyDecided = false;

        GameObject enemyUi = null;
        GameObject playerUi = null;

        enemyUi = BuildTestPlayMulliganSideUi(
            layer.transform,
            PlayerType.Enemy,
            seatNumber: 2,
            isTop: true,
            onMulligan: () =>
            {
                if (enemyDecided)
                {
                    return;
                }

                PerformMulligan(enemyCardGameRule, enemyHandCards, openingHandSize, PlayerType.Enemy);
                enemyDecided = true;
                if (enemyUi != null)
                {
                    Destroy(enemyUi);
                    enemyUi = null;
                }

                Debug.Log("[TestPlay][Mulligan] Enemy mulligan → UI closed.");
            },
            onKeep: () =>
            {
                if (enemyDecided)
                {
                    return;
                }

                enemyDecided = true;
                if (enemyUi != null)
                {
                    Destroy(enemyUi);
                    enemyUi = null;
                }

                Debug.Log("[TestPlay][Mulligan] Enemy keep → UI closed.");
            });

        playerUi = BuildTestPlayMulliganSideUi(
            layer.transform,
            PlayerType.Player,
            seatNumber: 1,
            isTop: false,
            onMulligan: () =>
            {
                if (playerDecided)
                {
                    return;
                }

                PerformMulligan(cardGameRule, playerHandCards, openingHandSize, PlayerType.Player);
                playerDecided = true;
                if (playerUi != null)
                {
                    Destroy(playerUi);
                    playerUi = null;
                }

                Debug.Log("[TestPlay][Mulligan] Player mulligan → UI closed.");
            },
            onKeep: () =>
            {
                if (playerDecided)
                {
                    return;
                }

                playerDecided = true;
                if (playerUi != null)
                {
                    Destroy(playerUi);
                    playerUi = null;
                }

                Debug.Log("[TestPlay][Mulligan] Player keep → UI closed.");
            });

        yield return new WaitUntil(() => playerDecided && enemyDecided);

        if (layer != null)
        {
            Destroy(layer);
        }

        isMulliganPromptOpen = false;

        cardGameRule.SetupShieldFromDeckAfterMulligan(
            CardImagePrefab,
            OnCardClicked,
            OpeningShieldCardCount,
            exBasePoints);
        enemyCardGameRule.SetupShieldFromDeckAfterMulligan(
            CardImagePrefab,
            OnCardClicked,
            OpeningShieldCardCount,
            exBasePoints);
        Debug.Log("[TestPlay] Dual mulligan finished; shields set for both sides.");
    }

    /// <summary>旧経路互換：マリガンなしでシールドのみ配備。</summary>
    private IEnumerator RunTestPlayOpeningWithoutMulliganCoroutine(int exBasePoints)
    {
        cardGameRule.SetupShieldFromDeckAfterMulligan(
            CardImagePrefab,
            OnCardClicked,
            OpeningShieldCardCount,
            exBasePoints);
        enemyCardGameRule.SetupShieldFromDeckAfterMulligan(
            CardImagePrefab,
            OnCardClicked,
            OpeningShieldCardCount,
            exBasePoints);
        Debug.Log("[TestPlay] Skipped mulligan; shields set for both sides.");
        yield break;
    }

    private const float TestPlayMulliganPanelWidth = 480f;

    /// <summary>1プレイヤー分の独立マリガン UI（幅480）。確定で Destroy される。</summary>
    private GameObject BuildTestPlayMulliganSideUi(
        Transform parent,
        PlayerType side,
        int seatNumber,
        bool isTop,
        System.Action onMulligan,
        System.Action onKeep)
    {
        GameObject root = new GameObject(
            isTop ? "EnemyMulliganUi" : "PlayerMulliganUi",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        root.transform.SetParent(parent, false);
        RectTransform rootRt = root.GetComponent<RectTransform>();
        rootRt.anchorMin = new Vector2(0.5f, isTop ? 1f : 0f);
        rootRt.anchorMax = new Vector2(0.5f, isTop ? 1f : 0f);
        rootRt.pivot = new Vector2(0.5f, isTop ? 1f : 0f);
        rootRt.sizeDelta = new Vector2(TestPlayMulliganPanelWidth, 300f);
        rootRt.anchoredPosition = new Vector2(0f, isTop ? -18f : 18f);
        Image rootBg = root.GetComponent<Image>();
        rootBg.color = new Color(0.08f, 0.09f, 0.12f, 0.94f);
        rootBg.raycastTarget = true;

        // ヘッダー帯
        GameObject header = new GameObject(
            "Header",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        header.transform.SetParent(root.transform, false);
        RectTransform headerRt = header.GetComponent<RectTransform>();
        headerRt.anchorMin = new Vector2(0f, 1f);
        headerRt.anchorMax = new Vector2(1f, 1f);
        headerRt.pivot = new Vector2(0.5f, 1f);
        headerRt.sizeDelta = new Vector2(0f, 52f);
        headerRt.anchoredPosition = Vector2.zero;
        header.GetComponent<Image>().color = new Color(0.12f, 0.14f, 0.18f, 1f);

        GameObject badge = new GameObject(
            "SeatBadge",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        badge.transform.SetParent(header.transform, false);
        RectTransform badgeRt = badge.GetComponent<RectTransform>();
        badgeRt.anchorMin = new Vector2(0f, 0.5f);
        badgeRt.anchorMax = new Vector2(0f, 0.5f);
        badgeRt.pivot = new Vector2(0f, 0.5f);
        badgeRt.sizeDelta = new Vector2(28f, 28f);
        badgeRt.anchoredPosition = new Vector2(8f, 0f);
        badge.GetComponent<Image>().color = seatNumber == 1
            ? new Color(0.22f, 0.48f, 0.92f, 1f)
            : new Color(0.88f, 0.28f, 0.28f, 1f);

        TextMeshProUGUI badgeText = badge.CreateChildTextCustom("SeatNum", UIAnchor.FullSize, 28, 28);
        badgeText.text = seatNumber.ToString();
        badgeText.fontSize = 18;
        badgeText.fontStyle = FontStyles.Bold;
        badgeText.color = Color.white;
        badgeText.alignment = TextAlignmentOptions.Center;
        RectTransform badgeTextRt = badgeText.GetComponent<RectTransform>();
        badgeTextRt.offsetMin = Vector2.zero;
        badgeTextRt.offsetMax = Vector2.zero;

        bool sideIsFirst = (side == PlayerType.Player) == isFirstPlayer;
        string deckTitle = side == PlayerType.Player
            ? (string.IsNullOrEmpty(TestPlayMatchState.PlayerDeckTitle)
                ? GameLocale.T("プレイヤー", "Player")
                : TestPlayMatchState.PlayerDeckTitle)
            : (string.IsNullOrEmpty(TestPlayMatchState.EnemyDeckTitle)
                ? GameLocale.T("相手", "Opponent")
                : TestPlayMatchState.EnemyDeckTitle);
        string turnLabel = sideIsFirst
            ? GameLocale.TKey("mulligan.first")
            : GameLocale.TKey("mulligan.second");

        TextMeshProUGUI title = header.CreateChildTextCustom("DeckTitle", UIAnchor.TopLeft, 200, 36);
        title.text = $"{deckTitle} ({turnLabel})";
        title.ApplyJapaneseFont();
        title.fontSize = 16;
        title.color = Color.white;
        title.alignment = TextAlignmentOptions.MidlineLeft;
        title.enableWordWrapping = false;
        title.overflowMode = TextOverflowModes.Ellipsis;
        RectTransform titleRt = title.GetComponent<RectTransform>();
        titleRt.anchorMin = new Vector2(0f, 0.5f);
        titleRt.anchorMax = new Vector2(0f, 0.5f);
        titleRt.pivot = new Vector2(0f, 0.5f);
        titleRt.sizeDelta = new Vector2(200f, 36f);
        titleRt.anchoredPosition = new Vector2(42f, 0f);

        const float actionBtnW = 88f;
        Button mulliganBtn = header.CreateChildButton(GameLocale.TKey("mulligan.action"));
        StyleTestPlayMulliganButton(mulliganBtn, new Color(0.55f, 0.32f, 0.78f, 1f));
        RectTransform mulliganRt = mulliganBtn.GetComponent<RectTransform>();
        mulliganRt.anchorMin = new Vector2(1f, 0.5f);
        mulliganRt.anchorMax = new Vector2(1f, 0.5f);
        mulliganRt.pivot = new Vector2(1f, 0.5f);
        mulliganRt.sizeDelta = new Vector2(actionBtnW, 34f);
        mulliganRt.anchoredPosition = new Vector2(-(actionBtnW + 16f), 0f);

        Button keepBtn = header.CreateChildButton(GameLocale.TKey("mulligan.keep"));
        StyleTestPlayMulliganButton(keepBtn, new Color(0.22f, 0.62f, 0.38f, 1f));
        RectTransform keepRt = keepBtn.GetComponent<RectTransform>();
        keepRt.anchorMin = new Vector2(1f, 0.5f);
        keepRt.anchorMax = new Vector2(1f, 0.5f);
        keepRt.pivot = new Vector2(1f, 0.5f);
        keepRt.sizeDelta = new Vector2(actionBtnW, 34f);
        keepRt.anchoredPosition = new Vector2(-8f, 0f);

        GameObject cardsHost = new GameObject(
            "CardsRow",
            typeof(RectTransform),
            typeof(HorizontalLayoutGroup));
        cardsHost.transform.SetParent(root.transform, false);
        RectTransform cardsRt = cardsHost.GetComponent<RectTransform>();
        cardsRt.anchorMin = new Vector2(0.5f, 0f);
        cardsRt.anchorMax = new Vector2(0.5f, 1f);
        cardsRt.pivot = new Vector2(0.5f, 0.5f);
        cardsRt.sizeDelta = new Vector2(TestPlayMulliganPanelWidth - 16f, -64f);
        cardsRt.anchoredPosition = new Vector2(0f, -26f);
        HorizontalLayoutGroup hlg = cardsHost.GetComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.spacing = 4f;
        hlg.padding = new RectOffset(4, 4, 0, 0);
        hlg.childControlWidth = false;
        hlg.childControlHeight = false;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        RefreshTestPlayMulliganCardRow(cardsHost.transform, side);

        mulliganBtn.onClick.AddListener(() => onMulligan?.Invoke());
        keepBtn.onClick.AddListener(() => onKeep?.Invoke());

        return root;
    }

    private void RefreshTestPlayMulliganCardRow(Transform cardsHost, PlayerType side)
    {
        if (cardsHost == null)
        {
            return;
        }

        for (int i = cardsHost.childCount - 1; i >= 0; i--)
        {
            Destroy(cardsHost.GetChild(i).gameObject);
        }

        CardGameRule rule = side == PlayerType.Player ? cardGameRule : enemyCardGameRule;
        List<int> ids = CollectHandCardIdsFromHandContent(rule);
        // 横幅480内に5枚収める（padding+spacing込み）
        const float cardW = 84f;
        const float cardH = 118f;

        for (int i = 0; i < ids.Count; i++)
        {
            CardData data = null;
            if (DeckSettinObject.Instance != null)
            {
                data = DeckSettinObject.Instance.GetCardDataById(ids[i]);
            }

            if (data == null && CardDatabase.Instance != null)
            {
                data = CardDatabase.Instance.GetById(ids[i]);
            }

            if (data == null || CardImagePrefab == null)
            {
                continue;
            }

            GameObject go = Instantiate(CardImagePrefab, cardsHost);
            RectTransform goRt = go.GetComponent<RectTransform>();
            if (goRt != null)
            {
                goRt.sizeDelta = new Vector2(cardW, cardH);
            }

            LayoutElement layout = go.GetComponent<LayoutElement>();
            if (layout == null)
            {
                layout = go.AddComponent<LayoutElement>();
            }

            layout.preferredWidth = cardW;
            layout.preferredHeight = cardH;
            layout.minWidth = cardW;
            layout.minHeight = cardH;

            CardController preview = go.GetComponent<CardController>();
            if (preview != null)
            {
                CardData captured = data;
                preview.SetUp(captured, _ => ShowTestPlayMulliganCardInspect(captured));
            }
        }
    }

    private void ShowTestPlayMulliganCardInspect(CardData data)
    {
        if (data == null || CardImagePrefab == null)
        {
            return;
        }

        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            return;
        }

        GameObject root = new GameObject(
            "TestPlayMulliganInspect",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        root.transform.SetParent(canvas.transform, false);
        root.transform.SetAsLastSibling();
        root.GetComponent<RectTransform>().SetFullSize();
        Image dim = root.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.55f);
        dim.raycastTarget = true;

        GameObject go = Instantiate(CardImagePrefab, root.transform);
        RectTransform goRt = go.GetComponent<RectTransform>();
        goRt.anchorMin = new Vector2(0.5f, 0.5f);
        goRt.anchorMax = new Vector2(0.5f, 0.5f);
        goRt.pivot = new Vector2(0.5f, 0.5f);
        goRt.sizeDelta = new Vector2(220f, 308f);
        goRt.anchoredPosition = new Vector2(0f, 24f);
        CardController preview = go.GetComponent<CardController>();
        preview?.SetUp(data, _ => { });
        Button cardBtn = go.GetComponent<Button>();
        if (cardBtn != null)
        {
            cardBtn.interactable = false;
        }

        Button closeBtn = root.CreateChildButton(GameLocale.T("閉じる", "Close"));
        RectTransform closeRt = closeBtn.GetComponent<RectTransform>();
        closeRt.anchorMin = new Vector2(0.5f, 0f);
        closeRt.anchorMax = new Vector2(0.5f, 0f);
        closeRt.pivot = new Vector2(0.5f, 0f);
        closeRt.sizeDelta = new Vector2(220f, 52f);
        closeRt.anchoredPosition = new Vector2(0f, 36f);
        closeBtn.onClick.AddListener(() => Destroy(root));
    }

    private static void StyleTestPlayMulliganButton(Button button, Color bg)
    {
        if (button == null)
        {
            return;
        }

        Image img = button.GetComponent<Image>();
        if (img != null)
        {
            img.color = bg;
        }

        TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>();
        if (label != null)
        {
            label.color = Color.white;
            label.fontSize = 16;
            label.fontStyle = FontStyles.Bold;
        }
    }

    private void ConfigureTestPlaySandboxToolbar()
    {
        if (!IsTestPlayBattle())
        {
            return;
        }

        BindTestPlayDeckInteractions();
        BindTestPlayBaseSlotInteractions();
        BindTestPlayResourceZoneInteractions();
        BindTestPlayShieldTokenInteractions();
    }

    private void BindTestPlayShieldTokenInteractions()
    {
        if (!IsTestPlayBattle())
        {
            return;
        }

        if (cardGameRule != null)
        {
            cardGameRule.EnsureTestPlayShieldTokenButton(() => OpenTestPlayTokenSelectPanel(PlayerType.Player));
        }

        if (enemyCardGameRule != null)
        {
            enemyCardGameRule.EnsureTestPlayShieldTokenButton(() => OpenTestPlayTokenSelectPanel(PlayerType.Enemy));
        }
    }

    /// <summary>TestPlay: ユニットトークン一覧から選択してバトルゾーンへ出す。</summary>
    private void OpenTestPlayTokenSelectPanel(PlayerType ownerType)
    {
        if (!IsTestPlayBattle())
        {
            return;
        }

        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            return;
        }

        List<CardData> tokens = CollectUnitTokenCardDatas();
        DestroyActiveOnActionPopupIfAny();

        GameObject root = new GameObject(
            "TestPlayTokenSelect",
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

        const int panelWidth = 480;

        TextMeshProUGUI title = root.CreateChildTextCustom("TokenTitle", UIAnchor.TopCenter, panelWidth, 48);
        title.SetLocalizedText(
            ownerType == PlayerType.Player
                ? "トークンを選択（自陣）"
                : "トークンを選択（相手陣）",
            ownerType == PlayerType.Player
                ? "Select Token (Your side)"
                : "Select Token (Opponent side)");
        title.color = Color.white;
        title.fontSize = 22;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -20f);

        TextMeshProUGUI subtitle = root.CreateChildTextCustom("TokenSubtitle", UIAnchor.TopCenter, panelWidth - 24, 40);
        subtitle.SetLocalizedText(
            $"登録トークン: {tokens.Count} 種 — タップで場に出す",
            $"Registered tokens: {tokens.Count} — tap to deploy");
        subtitle.color = new Color(0.9f, 0.9f, 0.9f);
        subtitle.fontSize = 14;
        subtitle.enableWordWrapping = true;
        subtitle.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -62f);

        GameObject scrollGo = root.CreateGridScrollView(panelWidth, 420, UIAnchor.TopCenter);
        RectTransform scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.anchoredPosition = new Vector2(0f, -320f);
        scrollGo.ConfigureGridCellFromViewportHeight(0.78f, 56f);
        ScrollRect sr = scrollGo.GetComponent<ScrollRect>();
        RectTransform content = sr != null ? sr.content : null;
        if (sr != null && sr.content != null)
        {
            GridLayoutGroup grid = sr.content.GetComponent<GridLayoutGroup>();
            if (grid != null)
            {
                // 480幅内に3列で収める
                const int columns = 3;
                float pad = grid.padding.left + grid.padding.right;
                float spacing = grid.spacing.x * (columns - 1);
                float cellW = Mathf.Floor((panelWidth - pad - spacing) / columns);
                float cellH = cellW * 1.45f;
                grid.cellSize = new Vector2(cellW, cellH);
                grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                grid.constraintCount = columns;
                grid.childAlignment = TextAnchor.UpperCenter;
            }
        }

        if (tokens.Count == 0)
        {
            TextMeshProUGUI empty = root.CreateChildTextCustom("EmptyTokens", UIAnchor.TopCenter, panelWidth - 40, 40);
            empty.SetLocalizedText(
                "UnitToken タイプのカードがありません",
                "No UnitToken cards found");
            empty.color = Color.yellow;
            empty.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -200f);
        }
        else if (content != null)
        {
            for (int i = 0; i < tokens.Count; i++)
            {
                CardData tokenData = tokens[i];
                if (tokenData == null)
                {
                    continue;
                }

                if (CardImagePrefab != null)
                {
                    GameObject go = Instantiate(CardImagePrefab, content);
                    CardController cc = go.GetComponent<CardController>();
                    if (cc != null)
                    {
                        cc.SetUp(tokenData, _ => { });
                    }

                    TextMeshProUGUI stat = go.CreateChildTextCustom("TokenStat", UIAnchor.BottomCenter, 100, 22);
                    stat.text = $"AP:{tokenData.power} HP:{tokenData.hp}";
                    stat.fontSize = 11;
                    stat.color = Color.white;
                    stat.alignment = TextAlignmentOptions.Center;

                    TextMeshProUGUI nameTag = go.CreateChildTextCustom("TokenName", UIAnchor.TopCenter, 100, 20);
                    nameTag.text = tokenData.cardName ?? "?";
                    nameTag.fontSize = 10;
                    nameTag.color = Color.white;
                    nameTag.alignment = TextAlignmentOptions.Center;
                    nameTag.enableWordWrapping = false;
                    nameTag.overflowMode = TextOverflowModes.Ellipsis;

                    Button btn = go.GetComponent<Button>() ?? go.AddComponent<Button>();
                    CardData pick = tokenData;
                    btn.onClick.AddListener(() =>
                    {
                        TestPlayDeployTokenToField(ownerType, pick);
                        Destroy(root);
                        activeOnActionPopupRoot = null;
                        isOnActionPopupOpen = false;
                    });
                }
                else
                {
                    Button rowBtn = content.gameObject.CreateChildButton(
                        $"{tokenData.cardName} AP{tokenData.power}/HP{tokenData.hp}");
                    RectTransform rowRt = rowBtn.GetComponent<RectTransform>();
                    rowRt.sizeDelta = new Vector2(panelWidth - 40f, 44f);
                    CardData pick = tokenData;
                    rowBtn.onClick.AddListener(() =>
                    {
                        TestPlayDeployTokenToField(ownerType, pick);
                        Destroy(root);
                        activeOnActionPopupRoot = null;
                        isOnActionPopupOpen = false;
                    });
                }
            }
        }

        Button closeBtn = root.CreateChildButton(GameLocale.T("閉じる", "Close"));
        RectTransform closeRt = closeBtn.GetComponent<RectTransform>();
        closeRt.sizeDelta = new Vector2(160f, 44f);
        closeRt.anchorMin = new Vector2(0.5f, 0f);
        closeRt.anchorMax = new Vector2(0.5f, 0f);
        closeRt.pivot = new Vector2(0.5f, 0f);
        closeRt.anchoredPosition = new Vector2(0f, 28f);
        closeBtn.onClick.AddListener(() =>
        {
            Destroy(root);
            activeOnActionPopupRoot = null;
            isOnActionPopupOpen = false;
        });

        Debug.Log($"[TestPlay] Token list opened ({tokens.Count}): " + FormatTokenListForLog(tokens));
    }

    private static List<CardData> CollectUnitTokenCardDatas()
    {
        var list = new List<CardData>();
        CardData[] all = Resources.LoadAll<CardData>("Data/Cards");
        if (all == null)
        {
            return list;
        }

        for (int i = 0; i < all.Length; i++)
        {
            CardData card = all[i];
            if (card != null && card.IsUnitToken())
            {
                list.Add(card);
            }
        }

        list.Sort((a, b) => a.id.CompareTo(b.id));
        return list;
    }

    private static string FormatTokenListForLog(List<CardData> tokens)
    {
        if (tokens == null || tokens.Count == 0)
        {
            return "(none)";
        }

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < tokens.Count; i++)
        {
            CardData t = tokens[i];
            if (t == null)
            {
                continue;
            }

            if (sb.Length > 0)
            {
                sb.Append(", ");
            }

            sb.Append(t.id).Append(':').Append(t.cardName)
                .Append(" AP").Append(t.power).Append("/HP").Append(t.hp);
        }

        return sb.ToString();
    }

    private void TestPlayDeployTokenToField(PlayerType ownerType, CardData tokenData)
    {
        if (!IsTestPlayBattle() || tokenData == null || !tokenData.IsUnitToken())
        {
            return;
        }

        CardGameRule rule = ownerType == PlayerType.Player ? cardGameRule : enemyCardGameRule;
        if (rule == null || rule.PlayerDeployPanel == null || CardImagePrefab == null)
        {
            Debug.LogWarning("[TestPlay] Token deploy failed: missing rule/prefab.");
            return;
        }

        CardController spawned = InstantiateBattleUnit(tokenData, rule.PlayerDeployPanel);
        if (spawned == null)
        {
            Debug.LogWarning($"[TestPlay] Token instantiate failed: {tokenData.cardName}");
            return;
        }

        DeployToBattleZoneWithCapGate(
            ownerType,
            spawned,
            () =>
            {
                if (!DeployUnitToBattleZone(
                        spawned,
                        ownerType,
                        rule,
                        triggerOnPlayed: false,
                        fromHand: false,
                        deployAsRested: false,
                        bypassBattleZoneCap: true))
                {
                    Destroy(spawned.gameObject);
                    Debug.LogWarning($"[TestPlay] Token deploy to battle zone failed: {tokenData.cardName}");
                    return;
                }

                // TestPlay サンドボックス: 出した直後から攻撃操作できるようにする
                spawned.SetAttackFlg(AttackFlg.True);
                spawned.SetUnitRestVisual(false);
                spawned.SetBattleStatOverlayVisible(true);

                Debug.Log(
                    $"[TestPlay] Token → Field {tokenData.cardName}(id:{tokenData.id}) "
                    + $"AP:{spawned.CurrentPower} HP:{spawned.CurrentHp} side:{ownerType}");
            },
            () => Destroy(spawned.gameObject));
    }

    private void BindTestPlayResourceZoneInteractions()
    {
        if (!IsTestPlayBattle())
        {
            return;
        }

        if (cardGameRule != null)
        {
            cardGameRule.BindTestPlayResourceZoneControls(
                delta => TestPlayAdjustResourceLevel(PlayerType.Player, delta),
                delta => TestPlayAdjustExForSide(PlayerType.Player, delta),
                rested => TestPlayToggleResourceToken(PlayerType.Player, rested));
        }

        if (enemyCardGameRule != null)
        {
            enemyCardGameRule.BindTestPlayResourceZoneControls(
                delta => TestPlayAdjustResourceLevel(PlayerType.Enemy, delta),
                delta => TestPlayAdjustExForSide(PlayerType.Enemy, delta),
                rested => TestPlayToggleResourceToken(PlayerType.Enemy, rested));
        }
    }

    private void BindTestPlayBaseSlotInteractions()
    {
        if (!IsTestPlayBattle())
        {
            return;
        }

        if (cardGameRule != null)
        {
            cardGameRule.BindBaseSlotAreaClick(() => OpenTestPlayExBaseHpCounterMenu(PlayerType.Player));
        }

        if (enemyCardGameRule != null)
        {
            enemyCardGameRule.BindBaseSlotAreaClick(() => OpenTestPlayExBaseHpCounterMenu(PlayerType.Enemy));
        }
    }

    /// <summary>
    /// TestPlay: 場のユニット／ベースに数値カウンター（ユニット: AP永続・HP・APターン / ベース: HPのみ）。
    /// </summary>
    private float EmbedTestPlayFieldStatCounters(
        GameObject filterPanel,
        CardController card,
        bool isInBaseSlot,
        float startY)
    {
        if (!IsTestPlayBattle() || filterPanel == null || card == null || card.Data == null)
        {
            return startY;
        }

        bool isUnit = card.Data.IsUnitLike();
        bool isBase = card.Data.type == Type.Base || isInBaseSlot;
        if (!isUnit && !isBase)
        {
            return startY;
        }

        float y = startY;

        TextMeshProUGUI section = filterPanel.CreateChildTextCustom(
            "TestPlayStatSection",
            UIAnchor.TopCenter,
            360,
            28);
        section.SetLocalizedText("数値カウンター", "Stat Counters");
        section.fontSize = 18;
        section.fontStyle = FontStyles.Bold;
        section.color = new Color(0.15f, 0.15f, 0.2f, 1f);
        section.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, y);
        y -= 36f;

        if (isUnit)
        {
            y = AddTestPlayLiveStatCounterRow(
                filterPanel,
                y,
                GameLocale.T("AP（永続）", "AP (permanent)"),
                () => card.GetPowerModifierSumByDuration(EffectDuration.Permanent),
                delta =>
                {
                    card.AddEffectStatBonus(
                        powerDelta: delta,
                        hpDelta: 0,
                        costDelta: 0,
                        levelDelta: 0,
                        duration: EffectDuration.Permanent,
                        statModifierSourceKey: "TestPlay:ApPermanent");
                    RefreshTestPlayFilterBattleStatText(filterPanel, card);
                    Debug.Log(
                        $"[TestPlay] AP永続 Δ{delta} → 補正:{card.GetPowerModifierSumByDuration(EffectDuration.Permanent)} 総AP:{card.CurrentPower}");
                },
                () => GameLocale.T($"総AP {card.CurrentPower}", $"Total AP {card.CurrentPower}"),
                new Color(0.1f, 0.1f, 0.15f, 1f));
        }

        y = AddTestPlayLiveStatCounterRow(
            filterPanel,
            y,
            "HP",
            () => card.CurrentHp,
            delta =>
            {
                int next = Mathf.Max(0, card.CurrentHp + delta);
                card.SetCurrentHpForSync(next);
                if (isBase)
                {
                    RefreshDeployedBaseHpOverlay(card);
                    SyncBaseZoneHeaderDisplay(ToRuleSide(ResolveOwnerTypeOfCard(card)));
                }

                RefreshTestPlayFilterBattleStatText(filterPanel, card);
                Debug.Log($"[TestPlay] HP → {card.CurrentHp} ({card.Data.cardName})");
            },
            null,
            new Color(0.1f, 0.1f, 0.15f, 1f));

        if (isUnit)
        {
            y = AddTestPlayLiveStatCounterRow(
                filterPanel,
                y,
                GameLocale.T("AP（ターン）", "AP (this turn)"),
                () => card.GetPowerModifierSumByDuration(EffectDuration.UntilEndOfTurn),
                delta =>
                {
                    card.AddEffectStatBonus(
                        powerDelta: delta,
                        hpDelta: 0,
                        costDelta: 0,
                        levelDelta: 0,
                        duration: EffectDuration.UntilEndOfTurn,
                        statModifierSourceKey: "TestPlay:ApUntilEndOfTurn");
                    RefreshTestPlayFilterBattleStatText(filterPanel, card);
                    Debug.Log(
                        $"[TestPlay] APターン Δ{delta} → 補正:{card.GetPowerModifierSumByDuration(EffectDuration.UntilEndOfTurn)} 総AP:{card.CurrentPower}");
                },
                () => GameLocale.T($"総AP {card.CurrentPower}", $"Total AP {card.CurrentPower}"),
                new Color(0.1f, 0.1f, 0.15f, 1f));
        }

        return y - 8f;
    }

    private static void RefreshTestPlayFilterBattleStatText(GameObject filterPanel, CardController card)
    {
        if (filterPanel == null || card == null)
        {
            return;
        }

        Transform found = filterPanel.transform.Find("BattleStatText");
        TextMeshProUGUI battleStatText = found != null
            ? found.GetComponent<TextMeshProUGUI>()
            : null;
        if (battleStatText == null)
        {
            TextMeshProUGUI[] texts = filterPanel.GetComponentsInChildren<TextMeshProUGUI>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] != null && texts[i].gameObject.name == "BattleStatText")
                {
                    battleStatText = texts[i];
                    break;
                }
            }
        }

        if (battleStatText != null)
        {
            battleStatText.text = $"AP:{card.CurrentPower}  HP:{card.CurrentHp}";
        }
    }

    /// <summary>TestPlay: EXベース（配備ベースが無い枠）の HP カウンター。</summary>
    private void OpenTestPlayExBaseHpCounterMenu(PlayerType ownerType)
    {
        if (!IsTestPlayBattle() || gundamRule == null)
        {
            return;
        }

        CardGameRule rule = ownerType == PlayerType.Player ? cardGameRule : enemyCardGameRule;
        if (rule == null)
        {
            return;
        }

        // 配備ベースがある場合はカード側のカウンターを使う
        if (rule.DeployedBase != null)
        {
            return;
        }

        Gundam2024RuleScript.PlayerSide side = ToRuleSide(ownerType);
        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            return;
        }

        DestroyActiveOnActionPopupIfAny();
        GameObject root = new GameObject(
            "TestPlayExBaseHpMenu",
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

        TextMeshProUGUI title = root.CreateChildTextCustom("ExBaseHpTitle", UIAnchor.TopCenter, 640, 48);
        title.SetLocalizedText(
            $"{ownerType} EXベース HP",
            $"{ownerType} EX Base HP");
        title.fontSize = 26;
        title.fontStyle = FontStyles.Bold;
        title.color = Color.white;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -100f);

        float y = -180f;
        AddTestPlayLiveStatCounterRow(
            root,
            y,
            "HP",
            () =>
            {
                Gundam2024RuleScript.PlayerState state = side == Gundam2024RuleScript.PlayerSide.Player
                    ? gundamRule.Player
                    : gundamRule.Enemy;
                return state != null ? state.exBase : 0;
            },
            delta =>
            {
                Gundam2024RuleScript.PlayerState state = side == Gundam2024RuleScript.PlayerSide.Player
                    ? gundamRule.Player
                    : gundamRule.Enemy;
                int current = state != null ? state.exBase : 0;
                int next = Mathf.Max(0, current + delta);
                gundamRule.SetExBasePoints(side, next);
                SyncBaseZoneHeaderDisplay(side);
                Debug.Log($"[TestPlay] EX Base HP {side} → {next}");
            },
            null,
            Color.white);

        Button closeBtn = root.CreateChildButton(GameLocale.T("閉じる", "Close"));
        RectTransform closeRt = closeBtn.GetComponent<RectTransform>();
        closeRt.sizeDelta = new Vector2(200f, 48f);
        closeRt.anchorMin = new Vector2(0.5f, 0f);
        closeRt.anchorMax = new Vector2(0.5f, 0f);
        closeRt.pivot = new Vector2(0.5f, 0f);
        closeRt.anchoredPosition = new Vector2(0f, 80f);
        closeBtn.onClick.AddListener(() =>
        {
            Destroy(root);
            ReleaseOnActionPopupState(root);
        });
    }

    private float AddTestPlayLiveStatCounterRow(
        GameObject parent,
        float anchoredY,
        string label,
        System.Func<int> getValue,
        System.Action<int> applyDelta,
        System.Func<string> getHint,
        Color valueColor)
    {
        bool stacked = parent != null && parent.GetComponent<VerticalLayoutGroup>() != null;
        GameObject row = new GameObject(
            "StatCounterRow",
            typeof(RectTransform),
            typeof(LayoutElement),
            typeof(HorizontalLayoutGroup));
        row.transform.SetParent(parent.transform, false);
        RectTransform rowRt = row.GetComponent<RectTransform>();
        rowRt.sizeDelta = new Vector2(420f, 48f);
        LayoutElement rowLayout = row.GetComponent<LayoutElement>();
        rowLayout.preferredWidth = 420f;
        rowLayout.preferredHeight = 48f;
        rowLayout.minHeight = 48f;

        HorizontalLayoutGroup h = row.GetComponent<HorizontalLayoutGroup>();
        h.spacing = 8f;
        h.childAlignment = TextAnchor.MiddleCenter;
        h.childControlWidth = false;
        h.childControlHeight = false;
        h.childForceExpandWidth = false;
        h.childForceExpandHeight = false;
        h.padding = new RectOffset(0, 0, 0, 0);

        if (!stacked)
        {
            rowRt.anchorMin = new Vector2(0.5f, 1f);
            rowRt.anchorMax = new Vector2(0.5f, 1f);
            rowRt.pivot = new Vector2(0.5f, 1f);
            rowRt.anchoredPosition = new Vector2(0f, anchoredY);
        }

        Button minusBtn = row.CreateChildButton("-");
        RectTransform minusRt = minusBtn.GetComponent<RectTransform>();
        minusRt.sizeDelta = new Vector2(72f, 44f);

        TextMeshProUGUI valueText = row.CreateChildTextCustom(
            "StatCounterValue",
            UIAnchor.TopCenter,
            250,
            40);
        valueText.fontSize = 20;
        valueText.fontStyle = FontStyles.Bold;
        valueText.color = valueColor;
        valueText.alignment = TextAlignmentOptions.Center;

        Button plusBtn = row.CreateChildButton("+");
        RectTransform plusRt = plusBtn.GetComponent<RectTransform>();
        plusRt.sizeDelta = new Vector2(72f, 44f);

        void Refresh()
        {
            int v = getValue != null ? getValue() : 0;
            string hint = getHint != null ? getHint() : null;
            if (!string.IsNullOrEmpty(hint))
            {
                valueText.text = $"{label}: {v}  ({hint})";
            }
            else
            {
                valueText.text = $"{label}: {v}";
            }
        }

        Refresh();
        minusBtn.onClick.AddListener(() =>
        {
            applyDelta?.Invoke(-1);
            Refresh();
        });
        plusBtn.onClick.AddListener(() =>
        {
            applyDelta?.Invoke(1);
            Refresh();
        });

        return anchoredY - 56f;
    }

    private PlayerType ResolveOwnerTypeOfCard(CardController card)
    {
        if (card == null)
        {
            return PlayerType.Player;
        }

        if (enemyCardGameRule != null
            && (card.transform.IsChildOf(enemyCardGameRule.PlayerDeployPanel)
                || (enemyCardGameRule.BaseSlotContent != null
                    && card.transform.IsChildOf(enemyCardGameRule.BaseSlotContent))))
        {
            return PlayerType.Enemy;
        }

        return PlayerType.Player;
    }

    private void BindTestPlayDeckInteractions()
    {
        if (!IsTestPlayBattle())
        {
            return;
        }

        if (cardGameRule != null)
        {
            cardGameRule.BindDeckAreaClick(() => OpenTestPlayDeckMenu(PlayerType.Player));
            cardGameRule.EnsureTestPlayDeckDrawButton(() => TestPlayDrawOneForSide(PlayerType.Player));
        }

        if (enemyCardGameRule != null)
        {
            enemyCardGameRule.BindDeckAreaClick(() => OpenTestPlayDeckMenu(PlayerType.Enemy));
            enemyCardGameRule.EnsureTestPlayDeckDrawButton(() => TestPlayDrawOneForSide(PlayerType.Enemy));
        }

        RefreshTestPlayDeckDrawButtonStates();
    }

    private void RefreshTestPlayDeckDrawButtonStates()
    {
        if (!IsTestPlayBattle())
        {
            return;
        }

        if (cardGameRule != null)
        {
            cardGameRule.SetTestPlayDeckDrawButtonInteractable(true);
        }

        if (enemyCardGameRule != null)
        {
            enemyCardGameRule.SetTestPlayDeckDrawButtonInteractable(true);
        }
    }

    private void OpenTestPlayDeckMenu(PlayerType ownerType)
    {
        if (!IsTestPlayBattle())
        {
            return;
        }

        CardGameRule rule = ownerType == PlayerType.Player ? cardGameRule : enemyCardGameRule;
        if (rule == null)
        {
            return;
        }

        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            return;
        }

        DestroyActiveOnActionPopupIfAny();
        GameObject root = new GameObject(
            "TestPlayDeckMenu",
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

        int deckCount = rule.GetRemainingCount();
        int lookCount = Mathf.Clamp(1, 0, Mathf.Max(0, deckCount));
        if (deckCount > 0)
        {
            lookCount = 1;
        }

        TextMeshProUGUI title = root.CreateChildTextCustom("DeckMenuTitle", UIAnchor.TopCenter, 720, 48);
        title.SetLocalizedText("山札メニュー", "Deck Menu");
        title.fontSize = 26;
        title.fontStyle = FontStyles.Bold;
        title.color = Color.white;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -80f);

        TextMeshProUGUI countLabel = root.CreateChildTextCustom("LookCountLabel", UIAnchor.TopCenter, 420, 40);
        void RefreshCountLabel()
        {
            countLabel.SetLocalizedText(
                $"上から見る枚数: {lookCount} / {deckCount}",
                $"Look from top: {lookCount} / {deckCount}");
        }

        RefreshCountLabel();
        countLabel.fontSize = 20;
        countLabel.color = new Color(0.9f, 0.95f, 1f, 1f);
        countLabel.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -150f);

        Button minusBtn = root.CreateChildButton("-");
        RectTransform minusRt = minusBtn.GetComponent<RectTransform>();
        minusRt.sizeDelta = new Vector2(80f, 48f);
        minusRt.anchorMin = new Vector2(0.5f, 1f);
        minusRt.anchorMax = new Vector2(0.5f, 1f);
        minusRt.pivot = new Vector2(0.5f, 1f);
        minusRt.anchoredPosition = new Vector2(-140f, -210f);
        minusBtn.onClick.AddListener(() =>
        {
            lookCount = Mathf.Max(0, lookCount - 1);
            RefreshCountLabel();
        });

        Button plusBtn = root.CreateChildButton("+");
        RectTransform plusRt = plusBtn.GetComponent<RectTransform>();
        plusRt.sizeDelta = new Vector2(80f, 48f);
        plusRt.anchorMin = new Vector2(0.5f, 1f);
        plusRt.anchorMax = new Vector2(0.5f, 1f);
        plusRt.pivot = new Vector2(0.5f, 1f);
        plusRt.anchoredPosition = new Vector2(140f, -210f);
        plusBtn.onClick.AddListener(() =>
        {
            lookCount = Mathf.Min(deckCount, lookCount + 1);
            RefreshCountLabel();
        });

        Button lookBtn = root.CreateChildButton(GameLocale.T("カードを見る", "Look at cards"));
        RectTransform lookRt = lookBtn.GetComponent<RectTransform>();
        lookRt.sizeDelta = new Vector2(320f, 52f);
        lookRt.anchorMin = new Vector2(0.5f, 1f);
        lookRt.anchorMax = new Vector2(0.5f, 1f);
        lookRt.pivot = new Vector2(0.5f, 1f);
        lookRt.anchoredPosition = new Vector2(0f, -290f);
        lookBtn.interactable = deckCount > 0;
        lookBtn.onClick.AddListener(() =>
        {
            int take = Mathf.Clamp(lookCount, 0, rule.GetRemainingCount());
            Destroy(root);
            ReleaseOnActionPopupState(root);
            if (take <= 0)
            {
                return;
            }

            OpenTestPlayDeckLookResult(ownerType, rule, take);
        });

        Button closeBtn = root.CreateChildButton(GameLocale.T("閉じる", "Close"));
        RectTransform closeRt = closeBtn.GetComponent<RectTransform>();
        closeRt.sizeDelta = new Vector2(200f, 48f);
        closeRt.anchorMin = new Vector2(0.5f, 0f);
        closeRt.anchorMax = new Vector2(0.5f, 0f);
        closeRt.pivot = new Vector2(0.5f, 0f);
        closeRt.anchoredPosition = new Vector2(0f, 80f);
        closeBtn.onClick.AddListener(() =>
        {
            Destroy(root);
            ReleaseOnActionPopupState(root);
        });
    }

    private void OpenTestPlayDeckLookResult(PlayerType ownerType, CardGameRule rule, int takeCount)
    {
        if (rule == null || takeCount <= 0 || CardImagePrefab == null)
        {
            return;
        }

        List<int> takenIds = rule.TakeTopCardIds(takeCount);
        if (takenIds.Count == 0)
        {
            return;
        }

        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            rule.PrependCardsToTopInOrder(takenIds);
            return;
        }

        DestroyActiveOnActionPopupIfAny();
        GameObject root = new GameObject(
            "TestPlayDeckLookResult",
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

        TextMeshProUGUI title = root.CreateChildTextCustom("LookResultTitle", UIAnchor.TopCenter, 760, 48);
        title.SetLocalizedText(
            $"山札の上から {takenIds.Count} 枚",
            $"Top {takenIds.Count} from deck");
        title.fontSize = 24;
        title.fontStyle = FontStyles.Bold;
        title.color = Color.white;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -24f);

        TextMeshProUGUI hint = root.CreateChildTextCustom("LookResultHint", UIAnchor.TopCenter, 760, 36);
        hint.SetLocalizedText(
            "確認後、山札の上へ戻すか、下へランダムで送ってください",
            "Return to top in order, or send to bottom in random order");
        hint.fontSize = 16;
        hint.color = new Color(0.85f, 0.9f, 1f, 1f);
        hint.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -64f);

        GameObject scrollGo = root.CreateGridScrollView(700, 360, UIAnchor.TopCenter);
        RectTransform scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.anchoredPosition = new Vector2(0f, -100f);
        scrollGo.ConfigureGridCellFromViewportHeight(0.78f, 56f);
        ScrollRect sr = scrollGo.GetComponent<ScrollRect>();
        RectTransform content = sr != null ? sr.content : null;
        if (content != null)
        {
            for (int i = 0; i < takenIds.Count; i++)
            {
                CardData data = DeckSettinObject.Instance != null
                    ? DeckSettinObject.Instance.GetCardDataById(takenIds[i])
                    : null;
                if (data == null)
                {
                    continue;
                }

                GameObject go = Instantiate(CardImagePrefab, content);
                CardController preview = go.GetComponent<CardController>();
                if (preview != null)
                {
                    preview.SetUp(data, _ => { });
                }

                Button blocker = go.GetComponent<Button>();
                if (blocker != null)
                {
                    blocker.interactable = false;
                }
            }
        }

        void Finish(bool toBottomRandom)
        {
            if (toBottomRandom)
            {
                for (int i = takenIds.Count - 1; i > 0; i--)
                {
                    int j = UnityEngine.Random.Range(0, i + 1);
                    int tmp = takenIds[i];
                    takenIds[i] = takenIds[j];
                    takenIds[j] = tmp;
                }

                rule.AppendCardsToBottom(takenIds);
                Debug.Log($"[TestPlay] Deck look → bottom (random) count:{takenIds.Count} side:{ownerType}");
            }
            else
            {
                rule.PrependCardsToTopInOrder(takenIds);
                Debug.Log($"[TestPlay] Deck look → top (same order) count:{takenIds.Count} side:{ownerType}");
            }

            Destroy(root);
            ReleaseOnActionPopupState(root);
            SyncAllResourceViewsFromRule();
        }

        Button topBtn = root.CreateChildButton(GameLocale.T("山札の上に戻す", "Return to top"));
        RectTransform topRt = topBtn.GetComponent<RectTransform>();
        topRt.sizeDelta = new Vector2(280f, 50f);
        topRt.anchorMin = new Vector2(0.5f, 0f);
        topRt.anchorMax = new Vector2(0.5f, 0f);
        topRt.pivot = new Vector2(0.5f, 0f);
        topRt.anchoredPosition = new Vector2(-160f, 70f);
        topBtn.onClick.AddListener(() => Finish(false));

        Button bottomBtn = root.CreateChildButton(
            GameLocale.T("山札の下へ（ランダム）", "Bottom (random)"));
        RectTransform bottomRt = bottomBtn.GetComponent<RectTransform>();
        bottomRt.sizeDelta = new Vector2(280f, 50f);
        bottomRt.anchorMin = new Vector2(0.5f, 0f);
        bottomRt.anchorMax = new Vector2(0.5f, 0f);
        bottomRt.pivot = new Vector2(0.5f, 0f);
        bottomRt.anchoredPosition = new Vector2(160f, 70f);
        bottomBtn.onClick.AddListener(() => Finish(true));
    }

    private void TestPlayDrawOneForSide(PlayerType side)
    {
        if (!IsTestPlayBattle() || gundamRule == null)
        {
            return;
        }

        CardGameRule rule = side == PlayerType.Player ? cardGameRule : enemyCardGameRule;
        CardAddtoHand(rule, side);
        SyncAllResourceViewsFromRule();
        Debug.Log($"[TestPlay] Draw 1 for {side}");
    }

    /// <summary>通常リソース枚数（レベル）を増減。上限はルールの maxLevel。</summary>
    private void TestPlayAdjustResourceLevel(PlayerType ownerType, int delta)
    {
        if (!IsTestPlayBattle() || gundamRule == null || delta == 0)
        {
            return;
        }

        Gundam2024RuleScript.PlayerSide side = ToRuleSide(ownerType);
        Gundam2024RuleScript.PlayerState state = side == Gundam2024RuleScript.PlayerSide.Player
            ? gundamRule.Player
            : gundamRule.Enemy;
        int maxLevel = gundamRule.MaxLevel;
        if (delta > 0)
        {
            if (state.level >= maxLevel)
            {
                Debug.Log($"[TestPlay] Resource level already at max ({maxLevel}).");
                return;
            }

            state.level += 1;
            state.resource = Mathf.Min(state.resource + 1, state.level);
        }
        else
        {
            if (state.level <= 0)
            {
                return;
            }

            state.level -= 1;
            state.resource = Mathf.Clamp(state.resource, 0, state.level);
        }

        SyncResourceViewsFromRule(side);
        Debug.Log($"[TestPlay] Resource level {side} -> {state.level} (active:{state.resource} max:{maxLevel})");
    }

    private void TestPlayAdjustExForSide(PlayerType ownerType, int delta)
    {
        if (!IsTestPlayBattle() || gundamRule == null || delta == 0)
        {
            return;
        }

        Gundam2024RuleScript.PlayerSide side = ToRuleSide(ownerType);
        Gundam2024RuleScript.PlayerState state = side == Gundam2024RuleScript.PlayerSide.Player
            ? gundamRule.Player
            : gundamRule.Enemy;
        if (delta > 0 && state.exResource >= gundamRule.MaxExResource)
        {
            Debug.Log($"[TestPlay] EX already at max ({gundamRule.MaxExResource}).");
            return;
        }

        gundamRule.AddExResource(side, delta);
        SyncResourceViewsFromRule(side);
        Debug.Log($"[TestPlay] EX {side} -> {state.exResource}/{gundamRule.MaxExResource}");
    }

    /// <summary>リソーストークン押下: 横向き（レスト）⇔ 起き。</summary>
    private void TestPlayToggleResourceToken(PlayerType ownerType, bool currentlyRested)
    {
        if (!IsTestPlayBattle() || gundamRule == null)
        {
            return;
        }

        Gundam2024RuleScript.PlayerSide side = ToRuleSide(ownerType);
        Gundam2024RuleScript.PlayerState state = side == Gundam2024RuleScript.PlayerSide.Player
            ? gundamRule.Player
            : gundamRule.Enemy;
        if (currentlyRested)
        {
            if (state.resource >= state.level)
            {
                return;
            }

            state.resource += 1;
        }
        else
        {
            if (state.resource <= 0)
            {
                return;
            }

            state.resource -= 1;
        }

        SyncResourceViewsFromRule(side);
        Debug.Log(
            $"[TestPlay] Resource token {(currentlyRested ? "activate" : "rest")} "
            + $"side:{side} active:{state.resource}/{state.level}");
    }

    /// <summary>TestPlay: コスト無視で手札から配備するボタン群。</summary>
    private bool TryEmbedTestPlayFreeHandDeployUi(
        GameObject filterPanel,
        CardController cardController,
        PlayerType ownerType,
        CardGameRule ownerRule,
        Gundam2024RuleScript.PlayerSide ownerSide,
        RectTransform closeBtnRect,
        float handActionY)
    {
        if (!IsTestPlayBattle() || filterPanel == null || cardController == null || cardController.Data == null)
        {
            return false;
        }

        float y = handActionY;

        Button trashBtn = filterPanel.CreateChildButton(
            GameLocale.T("手札から墓地へ", "Hand → Trash"));
        RectTransform trashRt = trashBtn.GetComponent<RectTransform>();
        trashRt.sizeDelta = new Vector2(280f, 44f);
        trashRt.anchoredPosition = new Vector2(0f, y);
        trashBtn.onClick.AddListener(() =>
        {
            SendCardToTrash(cardController, ownerType);
            DestroyCardFilterOverlay(filterPanel);
        });
        y -= 52f;

        Button exileBtn = filterPanel.CreateChildButton(
            GameLocale.T("除外する", "Exile"));
        RectTransform exileRt = exileBtn.GetComponent<RectTransform>();
        exileRt.sizeDelta = new Vector2(280f, 44f);
        exileRt.anchoredPosition = new Vector2(0f, y);
        exileBtn.onClick.AddListener(() =>
        {
            TestPlayExileCardInstance(cardController, ownerType);
            DestroyCardFilterOverlay(filterPanel);
        });
        y -= 52f;

        if (cardController.Data.IsPilot())
        {
            bool isCommandPilot = cardController.Data.IsCommand();
            List<CardController> mountTargets = GetMountableUnits(ownerType);
            if (mountTargets.Count > 0)
            {
                Button mountBtn = filterPanel.CreateChildButton(
                    GameLocale.T("搭乗する（無料）", "Mount (free)"));
                RectTransform mountRt = mountBtn.GetComponent<RectTransform>();
                mountRt.sizeDelta = new Vector2(280f, 44f);
                mountRt.anchoredPosition = new Vector2(0f, y);
                mountBtn.onClick.AddListener(() =>
                {
                    ShowPilotMountTargetButtons(
                        filterPanel,
                        cardController,
                        ownerType,
                        ownerSide,
                        cost: 0,
                        exToUse: 0);
                });
                y -= 52f;
            }

            if (!isCommandPilot)
            {
                PinFilterCloseButton(closeBtnRect);
                return true;
            }
        }

        if (cardController.Data.type == Type.Base)
        {
            Button baseBtn = filterPanel.CreateChildButton(
                GameLocale.T("ベース配備（無料）", "Deploy Base (free)"));
            RectTransform baseRt = baseBtn.GetComponent<RectTransform>();
            baseRt.sizeDelta = new Vector2(280f, 44f);
            baseRt.anchoredPosition = new Vector2(0f, y);
            baseBtn.onClick.AddListener(() =>
            {
                BeginDeployBaseFromHand(cardController, ownerType, ownerRule);
                DestroyCardFilterOverlay(filterPanel);
            });
            y -= 52f;
        }
        else if (cardController.Data.IsUnitLike())
        {
            Button deployBtn = filterPanel.CreateChildButton(
                GameLocale.T("盤面へ出す（無料）", "Play to field (free)"));
            RectTransform deployRt = deployBtn.GetComponent<RectTransform>();
            deployRt.sizeDelta = new Vector2(280f, 44f);
            deployRt.anchoredPosition = new Vector2(0f, y);
            deployBtn.onClick.AddListener(() =>
            {
                DeployToBattleZoneWithCapGate(
                    ownerType,
                    cardController,
                    () =>
                    {
                        SendCardToField(cardController, ownerType, ownerRule);
                        DestroyCardFilterOverlay(filterPanel);
                    },
                    () => DestroyCardFilterOverlay(filterPanel));
            });
            y -= 52f;
        }

        PinFilterCloseButton(closeBtnRect);
        return true;
    }

    /// <summary>TestPlay: シールドカード表示＋トラッシュ／手札／ベース配備。</summary>
    private bool TryEmbedTestPlayShieldMenu(
        GameObject filterPanel,
        CardController shieldCard,
        PlayerType ownerType,
        CardGameRule ownerRule,
        RectTransform closeBtnRect)
    {
        if (!IsTestPlayBattle()
            || filterPanel == null
            || shieldCard == null
            || shieldCard.Data == null
            || ownerRule == null)
        {
            return false;
        }

        shieldCard.RevealShieldFace();
        float y = -10f;

        TextMeshProUGUI name = filterPanel.CreateChildTextCustom("ShieldCardName", UIAnchor.TopCenter, 420, 36);
        name.text = shieldCard.Data.cardName ?? string.Empty;
        name.fontSize = 20;
        name.fontStyle = FontStyles.Bold;
        name.color = Color.black;
        name.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, y);
        y -= 48f;

        Button trashBtn = filterPanel.CreateChildButton(
            GameLocale.T("トラッシュへ送る", "Send to Trash"));
        RectTransform trashRt = trashBtn.GetComponent<RectTransform>();
        trashRt.sizeDelta = new Vector2(300f, 44f);
        trashRt.anchoredPosition = new Vector2(0f, y);
        trashBtn.onClick.AddListener(() =>
        {
            TestPlaySendShieldToTrash(shieldCard, ownerType, ownerRule);
            DestroyCardFilterOverlay(filterPanel);
        });
        y -= 52f;

        Button exileBtn = filterPanel.CreateChildButton(
            GameLocale.T("除外する", "Exile"));
        RectTransform exileRt = exileBtn.GetComponent<RectTransform>();
        exileRt.sizeDelta = new Vector2(300f, 44f);
        exileRt.anchoredPosition = new Vector2(0f, y);
        exileBtn.onClick.AddListener(() =>
        {
            TestPlayExileCardInstance(shieldCard, ownerType);
            DestroyCardFilterOverlay(filterPanel);
        });
        y -= 52f;

        Button handBtn = filterPanel.CreateChildButton(
            GameLocale.T("手札に加える", "Add to Hand"));
        RectTransform handRt = handBtn.GetComponent<RectTransform>();
        handRt.sizeDelta = new Vector2(300f, 44f);
        handRt.anchoredPosition = new Vector2(0f, y);
        handBtn.onClick.AddListener(() =>
        {
            TestPlayMoveShieldToHand(shieldCard, ownerType, ownerRule);
            DestroyCardFilterOverlay(filterPanel);
        });
        y -= 52f;

        if (shieldCard.Data.type == Type.Base)
        {
            Button deployBtn = filterPanel.CreateChildButton(
                GameLocale.T("ベースを配備する", "Deploy Base"));
            RectTransform deployRt = deployBtn.GetComponent<RectTransform>();
            deployRt.sizeDelta = new Vector2(300f, 44f);
            deployRt.anchoredPosition = new Vector2(0f, y);
            deployBtn.onClick.AddListener(() =>
            {
                TestPlayDeployBaseFromShield(shieldCard, ownerType, ownerRule);
                DestroyCardFilterOverlay(filterPanel);
            });
            y -= 52f;
        }

        PinFilterCloseButton(closeBtnRect);
        return true;
    }

    private void TestPlayReduceShieldCountAfterRemove(PlayerType ownerType, CardGameRule ownerRule)
    {
        Gundam2024RuleScript.PlayerSide ruleSide = ToRuleSide(ownerType);
        if (gundamRule == null || ownerRule == null)
        {
            return;
        }

        if (!gundamRule.TryReduceShieldCountForHandMove(ruleSide, 1))
        {
            int zoneCount = ownerRule.GetShieldZoneCardCount();
            gundamRule.SyncShieldCountFromZone(ruleSide, zoneCount);
            gundamRule.TryReduceShieldCountForHandMove(ruleSide, 1);
        }

        SyncResourceViewsFromRule(ruleSide);
    }

    private void TestPlayMoveShieldToHand(
        CardController shieldCard,
        PlayerType ownerType,
        CardGameRule ownerRule)
    {
        if (shieldCard == null || ownerRule == null || ownerRule.HandScrollContent == null)
        {
            return;
        }

        if (!ownerRule.TryMoveShieldCardToHand(shieldCard, ownerRule.HandScrollContent))
        {
            Debug.LogWarning("[TestPlay] Failed to move shield card to hand.");
            return;
        }

        TestPlayReduceShieldCountAfterRemove(ownerType, ownerRule);
        shieldCard.SetEligibleForShieldZoneDeploy(shieldCard.Data != null && shieldCard.Data.type != Type.Base);
        RegisterCardInHandLists(shieldCard, ownerType);
        TriggerOnHandAutoEffects(shieldCard, ownerType, skipHandZoneCheck: true);
        ownerRule.RefreshHandCountDisplay();
        Debug.Log($"[TestPlay] Shield → Hand: {shieldCard.Data?.cardName} side:{ownerType}");
    }

    private void TestPlaySendShieldToTrash(
        CardController shieldCard,
        PlayerType ownerType,
        CardGameRule ownerRule)
    {
        if (shieldCard == null || ownerRule == null)
        {
            return;
        }

        if (!ownerRule.TryUnregisterShieldZoneCard(shieldCard))
        {
            Debug.LogWarning("[TestPlay] Shield card was not registered in zone.");
        }

        TestPlayReduceShieldCountAfterRemove(ownerType, ownerRule);
        shieldCard.RevealShieldFace();
        SendCardToTrash(shieldCard, ownerType);
        Debug.Log($"[TestPlay] Shield → Trash: {shieldCard.Data?.cardName} side:{ownerType}");
    }

    private void TestPlayDeployBaseFromShield(
        CardController shieldCard,
        PlayerType ownerType,
        CardGameRule ownerRule)
    {
        if (shieldCard == null || shieldCard.Data == null || shieldCard.Data.type != Type.Base)
        {
            return;
        }

        if (DeployCardToBaseZone(shieldCard, ownerType, ownerRule, triggerOnPlayed: true))
        {
            Debug.Log($"[TestPlay] Shield Base deployed: {shieldCard.Data.cardName} side:{ownerType}");
        }
        else
        {
            Debug.LogWarning("[TestPlay] Failed to deploy Base from Shield.");
        }
    }

    /// <summary>TestPlay: トラッシュのカードを手札／山札／配備へ移すメニュー。</summary>
    private void OpenTestPlayTrashCardMenu(
        CardGameRule rule,
        PlayerType ownerType,
        int trashIndex,
        int cardId,
        CardData data)
    {
        if (!IsTestPlayBattle() || rule == null || data == null)
        {
            return;
        }

        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            return;
        }

        DestroyActiveOnActionPopupIfAny();
        GameObject root = new GameObject(
            "TestPlayTrashCardMenu",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        activeOnActionPopupRoot = root;
        isOnActionPopupOpen = true;
        root.transform.SetParent(canvas.transform, false);
        root.transform.SetAsLastSibling();
        root.SetFullSize();
        Image dim = root.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.72f);
        dim.raycastTarget = true;

        TextMeshProUGUI title = root.CreateChildTextCustom("TrashCardTitle", UIAnchor.TopCenter, 720, 48);
        title.SetLocalizedText(
            $"トラッシュ：{data.cardName}",
            $"Trash: {data.cardName}");
        title.fontSize = 24;
        title.fontStyle = FontStyles.Bold;
        title.color = Color.white;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -80f);

        bool isBase = data.type == Type.Base;
        bool isCommand = data.IsCommand();
        bool isUnit = data.IsUnitLike();
        float y = -160f;

        y = AddTestPlayTrashMenuButton(
            root,
            y,
            isCommand
                ? GameLocale.T("手札に加える", "Add to Hand")
                : GameLocale.T("手札に戻す", "Return to Hand"),
            () => TestPlayMoveTrashCardToHand(rule, ownerType, trashIndex, cardId));

        y = AddTestPlayTrashMenuButton(
            root,
            y,
            GameLocale.T("山札の上に戻す", "Return to deck top"),
            () => TestPlayMoveTrashCardToDeck(rule, ownerType, trashIndex, cardId, toBottom: false));

        y = AddTestPlayTrashMenuButton(
            root,
            y,
            GameLocale.T("山札の下に戻す", "Return to deck bottom"),
            () => TestPlayMoveTrashCardToDeck(rule, ownerType, trashIndex, cardId, toBottom: true));

        y = AddTestPlayTrashMenuButton(
            root,
            y,
            GameLocale.T("除外する", "Exile"),
            () => TestPlayExileTrashCardAt(rule, ownerType, trashIndex, cardId));

        if (isBase)
        {
            y = AddTestPlayTrashMenuButton(
                root,
                y,
                GameLocale.T("シールドゾーンに配備する", "Deploy to Shield Zone"),
                () => TestPlayDeployTrashCardToShield(rule, ownerType, trashIndex, cardId, data));
        }
        else if (isUnit && !isCommand)
        {
            y = AddTestPlayTrashMenuButton(
                root,
                y,
                GameLocale.T("配備する", "Deploy"),
                () => TestPlayDeployTrashCardToField(rule, ownerType, trashIndex, cardId, data));
        }

        Button closeBtn = root.CreateChildButton(GameLocale.T("閉じる", "Close"));
        RectTransform closeRt = closeBtn.GetComponent<RectTransform>();
        closeRt.sizeDelta = new Vector2(200f, 48f);
        closeRt.anchorMin = new Vector2(0.5f, 1f);
        closeRt.anchorMax = new Vector2(0.5f, 1f);
        closeRt.pivot = new Vector2(0.5f, 1f);
        closeRt.anchoredPosition = new Vector2(0f, y - 20f);
        closeBtn.onClick.AddListener(() =>
        {
            Destroy(root);
            ReleaseOnActionPopupState(root);
        });
    }

    private float AddTestPlayTrashMenuButton(
        GameObject parent,
        float anchoredY,
        string label,
        System.Action onClick)
    {
        Button btn = parent.CreateChildButton(label);
        RectTransform btnRt = btn.GetComponent<RectTransform>();
        btnRt.sizeDelta = new Vector2(360f, 48f);
        btnRt.anchorMin = new Vector2(0.5f, 1f);
        btnRt.anchorMax = new Vector2(0.5f, 1f);
        btnRt.pivot = new Vector2(0.5f, 1f);
        btnRt.anchoredPosition = new Vector2(0f, anchoredY);
        btn.onClick.AddListener(() =>
        {
            onClick?.Invoke();
            if (parent != null)
            {
                Destroy(parent);
                ReleaseOnActionPopupState(parent);
            }
        });
        return anchoredY - 56f;
    }

    private void CloseDiscardZoneInspectionIfAny()
    {
        if (_activeDiscardZoneInspectRoot != null)
        {
            Destroy(_activeDiscardZoneInspectRoot);
            _activeDiscardZoneInspectRoot = null;
        }
    }

    private bool TryTakeTestPlayTrashCard(
        CardGameRule rule,
        int trashIndex,
        int expectedCardId,
        out int removedId)
    {
        removedId = -1;
        if (rule == null || expectedCardId < 0)
        {
            return false;
        }

        if (!rule.TryRemoveCardFromTrashAt(trashIndex, out removedId))
        {
            return false;
        }

        if (removedId != expectedCardId)
        {
            rule.AddCardToTrash(removedId);
            removedId = -1;
            return false;
        }

        return true;
    }

    private void TestPlayMoveTrashCardToHand(
        CardGameRule rule,
        PlayerType ownerType,
        int trashIndex,
        int cardId)
    {
        if (!TryTakeTestPlayTrashCard(rule, trashIndex, cardId, out int removedId))
        {
            Debug.LogWarning("[TestPlay] トラッシュから手札へ戻せませんでした。");
            return;
        }

        AddCardIdToHand(rule, ownerType, removedId);
        CloseDiscardZoneInspectionIfAny();
        Debug.Log($"[TestPlay] Trash → Hand id:{removedId} side:{ownerType}");
    }

    private void TestPlayMoveTrashCardToDeck(
        CardGameRule rule,
        PlayerType ownerType,
        int trashIndex,
        int cardId,
        bool toBottom)
    {
        if (!TryTakeTestPlayTrashCard(rule, trashIndex, cardId, out int removedId))
        {
            Debug.LogWarning("[TestPlay] トラッシュから山札へ戻せませんでした。");
            return;
        }

        if (toBottom)
        {
            rule.AppendCardsToBottom(new[] { removedId });
        }
        else
        {
            rule.PrependCardsToTopInOrder(new[] { removedId });
        }

        SyncGundamRuleDeckCount(ownerType, rule.GetRemainingCount());
        CloseDiscardZoneInspectionIfAny();
        Debug.Log(
            $"[TestPlay] Trash → Deck {(toBottom ? "bottom" : "top")} id:{removedId} side:{ownerType}");
    }

    private void TestPlayDeployTrashCardToField(
        CardGameRule rule,
        PlayerType ownerType,
        int trashIndex,
        int cardId,
        CardData data)
    {
        if (data == null || !data.IsUnitLike() || CardImagePrefab == null)
        {
            return;
        }

        if (!TryTakeTestPlayTrashCard(rule, trashIndex, cardId, out int removedId))
        {
            Debug.LogWarning("[TestPlay] トラッシュから配備できませんでした。");
            return;
        }

        CardData resolved = DeckSettinObject.Instance != null
            ? DeckSettinObject.Instance.GetCardDataById(removedId)
            : data;
        if (resolved == null)
        {
            rule.AddCardToTrash(removedId);
            return;
        }

        GameObject go = Instantiate(CardImagePrefab);
        CardController spawned = go.GetComponent<CardController>();
        if (spawned == null)
        {
            Destroy(go);
            rule.AddCardToTrash(removedId);
            return;
        }

        spawned.SetUp(resolved, OnCardClicked);
        SendCardToField(spawned, ownerType, rule);
        CloseDiscardZoneInspectionIfAny();
        Debug.Log($"[TestPlay] Trash → Field {resolved.cardName} side:{ownerType}");
    }

    private void TestPlayDeployTrashCardToShield(
        CardGameRule rule,
        PlayerType ownerType,
        int trashIndex,
        int cardId,
        CardData data)
    {
        if (data == null || data.type != Type.Base || CardImagePrefab == null || gundamRule == null)
        {
            return;
        }

        if (!TryTakeTestPlayTrashCard(rule, trashIndex, cardId, out int removedId))
        {
            Debug.LogWarning("[TestPlay] トラッシュからシールド配備できませんでした。");
            return;
        }

        CardData resolved = DeckSettinObject.Instance != null
            ? DeckSettinObject.Instance.GetCardDataById(removedId)
            : data;
        if (resolved == null)
        {
            rule.AddCardToTrash(removedId);
            return;
        }

        GameObject go = Instantiate(CardImagePrefab);
        CardController spawned = go.GetComponent<CardController>();
        if (spawned == null)
        {
            Destroy(go);
            rule.AddCardToTrash(removedId);
            return;
        }

        spawned.SetUp(resolved, OnCardClicked);
        if (!rule.TryForceAttachShieldCard(spawned))
        {
            Destroy(go);
            rule.AddCardToTrash(removedId);
            Debug.LogWarning("[TestPlay] シールドゾーンへの配備に失敗しました。");
            return;
        }

        gundamRule.AddShieldCount(ToRuleSide(ownerType), 1);
        SyncResourceViewsFromRule(ToRuleSide(ownerType));
        CloseDiscardZoneInspectionIfAny();
        Debug.Log($"[TestPlay] Trash → Shield {resolved.cardName} side:{ownerType}");
    }

    /// <summary>TestPlay: 場のユニットに手札戻し／山札下送り／レスト切替を追加する。</summary>
    private float EmbedTestPlayFieldUnitExtraActions(
        GameObject filterPanel,
        CardController card,
        PlayerType ownerType,
        float startY)
    {
        if (!IsTestPlayBattle() || filterPanel == null || card == null || card.Data == null)
        {
            return startY;
        }

        if (!card.Data.IsUnitLike())
        {
            return startY;
        }

        float y = startY;

        if (!card.Data.IsUnitToken())
        {
            Button returnHandBtn = filterPanel.CreateChildButton(
                GameLocale.T("手札に戻す", "Return to hand"));
            RectTransform returnHandRt = returnHandBtn.GetComponent<RectTransform>();
            returnHandRt.sizeDelta = new Vector2(280f, 50f);
            returnHandRt.anchoredPosition = new Vector2(0f, y);
            returnHandBtn.onClick.AddListener(() =>
            {
                TestPlayReturnUnitToHand(card, ownerType);
                DestroyCardFilterOverlay(filterPanel);
            });
            y -= 60f;

            Button deckBottomBtn = filterPanel.CreateChildButton(
                GameLocale.T("山札の下に送る", "Send to deck bottom"));
            RectTransform deckBottomRt = deckBottomBtn.GetComponent<RectTransform>();
            deckBottomRt.sizeDelta = new Vector2(280f, 50f);
            deckBottomRt.anchoredPosition = new Vector2(0f, y);
            deckBottomBtn.onClick.AddListener(() =>
            {
                TestPlaySendUnitToDeckBottom(card, ownerType);
                DestroyCardFilterOverlay(filterPanel);
            });
            y -= 60f;
        }

        bool isRest = card.IsRestState;
        Button restBtn = filterPanel.CreateChildButton(
            isRest
                ? GameLocale.T("アクティブにする", "Set Active")
                : GameLocale.T("レストする", "Rest"));
        RectTransform restRt = restBtn.GetComponent<RectTransform>();
        restRt.sizeDelta = new Vector2(280f, 50f);
        restRt.anchoredPosition = new Vector2(0f, y);
        restBtn.onClick.AddListener(() =>
        {
            TestPlayToggleUnitRest(card);
            DestroyCardFilterOverlay(filterPanel);
        });
        y -= 60f;

        // トークンは山札に戻さず消滅させる方が自然
        if (card.Data.IsUnitToken())
        {
            Button vanishBtn = filterPanel.CreateChildButton(
                GameLocale.T("トークンを消滅", "Vanish token"));
            RectTransform vanishRt = vanishBtn.GetComponent<RectTransform>();
            vanishRt.sizeDelta = new Vector2(280f, 50f);
            vanishRt.anchoredPosition = new Vector2(0f, y);
            vanishBtn.onClick.AddListener(() =>
            {
                SendCardToTrash(card, ownerType);
                DestroyCardFilterOverlay(filterPanel);
            });
            y -= 60f;
        }

        return y;
    }

    private void TestPlayReturnUnitToHand(CardController unit, PlayerType ownerType)
    {
        if (unit == null)
        {
            return;
        }

        string unitName = unit.Data != null ? unit.Data.cardName : "?";
        if (!TryReturnBattleUnitToHand(unit))
        {
            Debug.LogWarning($"[TestPlay] 手札へ戻せませんでした: {unitName}");
            return;
        }

        Debug.Log($"[TestPlay] Unit → Hand: {unitName} side:{ownerType}");
    }

    private void TestPlaySendUnitToDeckBottom(CardController unit, PlayerType ownerType)
    {
        if (unit == null)
        {
            return;
        }

        string unitName = unit.Data != null ? unit.Data.cardName : "?";
        if (!TryReturnBattleUnitToDeckBottom(unit))
        {
            Debug.LogWarning($"[TestPlay] 山札下へ送れませんでした: {unitName}");
            return;
        }

        CardGameRule rule = ownerType == PlayerType.Player ? cardGameRule : enemyCardGameRule;
        if (rule != null)
        {
            SyncGundamRuleDeckCount(ownerType, rule.GetRemainingCount());
        }

        Debug.Log($"[TestPlay] Unit → Deck bottom: {unitName} side:{ownerType}");
    }

    private void TestPlayToggleUnitRest(CardController unit)
    {
        if (unit == null || unit.Data == null || !unit.Data.IsUnitLike())
        {
            return;
        }

        if (unit.IsRestState)
        {
            unit.SetUnitRestVisual(false);
            unit.SetAttackFlg(AttackFlg.True);
            Debug.Log($"[TestPlay] Active: {unit.Data.cardName}");
            return;
        }

        if (TryApplyRestToUnit(unit))
        {
            Debug.Log($"[TestPlay] Rest: {unit.Data.cardName}");
        }
    }

    /// <summary>TestPlay: 手札／盤面／シールド上のカードを除外ゾーンへ送る。</summary>
    private void TestPlayExileCardInstance(CardController card, PlayerType ownerType)
    {
        if (!IsTestPlayBattle() || card == null || card.Data == null)
        {
            return;
        }

        CardGameRule rule = ownerType == PlayerType.Player ? cardGameRule : enemyCardGameRule;
        if (rule == null)
        {
            return;
        }

        string cardName = card.Data.cardName ?? "?";
        int cardId = card.Data.id;

        if (card.Data.LeavesPlayWithoutZone())
        {
            PruneObservedUnitWatchesOnCardRemoved(card);
            FinalizeRemoveCardFromPlay(card, ownerType, sendToTrashZone: false);
            Debug.Log($"[TestPlay] Exile (token vanish): {cardName}");
            return;
        }

        if (card.Data.IsUnitLike() && card.MountedPilot != null)
        {
            CardController pilot = card.DetachMountedPilotWithoutDestroy();
            if (pilot != null)
            {
                TestPlayExileCardInstance(pilot, ownerType);
            }
        }

        PruneObservedUnitWatchesOnCardRemoved(card);
        rule.AddCardToExile(cardId);
        FinalizeRemoveCardFromPlay(card, ownerType, sendToTrashZone: false);
        Debug.Log($"[TestPlay] Exile: {cardName} id:{cardId} side:{ownerType}");
    }

    /// <summary>TestPlay: トラッシュのカードを除外ゾーンへ送る。</summary>
    private void TestPlayExileTrashCardAt(
        CardGameRule rule,
        PlayerType ownerType,
        int trashIndex,
        int cardId)
    {
        if (!TryTakeTestPlayTrashCard(rule, trashIndex, cardId, out int removedId))
        {
            Debug.LogWarning("[TestPlay] トラッシュから除外できませんでした。");
            return;
        }

        rule.AddCardToExile(removedId);
        CloseDiscardZoneInspectionIfAny();
        Debug.Log($"[TestPlay] Trash → Exile id:{removedId} side:{ownerType}");
    }

    /// <summary>TestPlay: 除外ゾーンのカードを手札／山札／トラッシュ／配備へ移すメニュー。</summary>
    private void OpenTestPlayExileCardMenu(
        CardGameRule rule,
        PlayerType ownerType,
        int exileIndex,
        int cardId,
        CardData data)
    {
        if (!IsTestPlayBattle() || rule == null || data == null)
        {
            return;
        }

        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            return;
        }

        DestroyActiveOnActionPopupIfAny();
        GameObject root = new GameObject(
            "TestPlayExileCardMenu",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        activeOnActionPopupRoot = root;
        isOnActionPopupOpen = true;
        root.transform.SetParent(canvas.transform, false);
        root.transform.SetAsLastSibling();
        root.SetFullSize();
        Image dim = root.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.72f);
        dim.raycastTarget = true;

        TextMeshProUGUI title = root.CreateChildTextCustom("ExileCardTitle", UIAnchor.TopCenter, 720, 48);
        title.SetLocalizedText(
            $"除外：{data.cardName}",
            $"Exile: {data.cardName}");
        title.fontSize = 24;
        title.fontStyle = FontStyles.Bold;
        title.color = Color.white;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -80f);

        bool isBase = data.type == Type.Base;
        bool isCommand = data.IsCommand();
        bool isUnit = data.IsUnitLike();
        float y = -160f;

        y = AddTestPlayTrashMenuButton(
            root,
            y,
            isCommand
                ? GameLocale.T("手札に加える", "Add to Hand")
                : GameLocale.T("手札に戻す", "Return to Hand"),
            () => TestPlayMoveExileCardToHand(rule, ownerType, exileIndex, cardId));

        y = AddTestPlayTrashMenuButton(
            root,
            y,
            GameLocale.T("山札の上に戻す", "Return to deck top"),
            () => TestPlayMoveExileCardToDeck(rule, ownerType, exileIndex, cardId, toBottom: false));

        y = AddTestPlayTrashMenuButton(
            root,
            y,
            GameLocale.T("山札の下に戻す", "Return to deck bottom"),
            () => TestPlayMoveExileCardToDeck(rule, ownerType, exileIndex, cardId, toBottom: true));

        y = AddTestPlayTrashMenuButton(
            root,
            y,
            GameLocale.T("トラッシュに送る", "Send to Trash"),
            () => TestPlayMoveExileCardToTrash(rule, ownerType, exileIndex, cardId));

        if (isBase)
        {
            y = AddTestPlayTrashMenuButton(
                root,
                y,
                GameLocale.T("シールドゾーンに配備する", "Deploy to Shield Zone"),
                () => TestPlayDeployExileCardToShield(rule, ownerType, exileIndex, cardId, data));
        }
        else if (isUnit && !isCommand)
        {
            y = AddTestPlayTrashMenuButton(
                root,
                y,
                GameLocale.T("配備する", "Deploy"),
                () => TestPlayDeployExileCardToField(rule, ownerType, exileIndex, cardId, data));
        }

        Button closeBtn = root.CreateChildButton(GameLocale.T("閉じる", "Close"));
        RectTransform closeRt = closeBtn.GetComponent<RectTransform>();
        closeRt.sizeDelta = new Vector2(200f, 48f);
        closeRt.anchorMin = new Vector2(0.5f, 1f);
        closeRt.anchorMax = new Vector2(0.5f, 1f);
        closeRt.pivot = new Vector2(0.5f, 1f);
        closeRt.anchoredPosition = new Vector2(0f, y - 20f);
        closeBtn.onClick.AddListener(() =>
        {
            Destroy(root);
            ReleaseOnActionPopupState(root);
        });
    }

    private bool TryTakeTestPlayExileCard(
        CardGameRule rule,
        int exileIndex,
        int expectedCardId,
        out int removedId)
    {
        removedId = -1;
        if (rule == null || expectedCardId < 0)
        {
            return false;
        }

        if (!rule.TryRemoveCardFromExileAt(exileIndex, out removedId))
        {
            return false;
        }

        if (removedId != expectedCardId)
        {
            rule.AddCardToExile(removedId);
            removedId = -1;
            return false;
        }

        return true;
    }

    private void TestPlayMoveExileCardToHand(
        CardGameRule rule,
        PlayerType ownerType,
        int exileIndex,
        int cardId)
    {
        if (!TryTakeTestPlayExileCard(rule, exileIndex, cardId, out int removedId))
        {
            Debug.LogWarning("[TestPlay] 除外から手札へ戻せませんでした。");
            return;
        }

        AddCardIdToHand(rule, ownerType, removedId);
        CloseDiscardZoneInspectionIfAny();
        Debug.Log($"[TestPlay] Exile → Hand id:{removedId} side:{ownerType}");
    }

    private void TestPlayMoveExileCardToDeck(
        CardGameRule rule,
        PlayerType ownerType,
        int exileIndex,
        int cardId,
        bool toBottom)
    {
        if (!TryTakeTestPlayExileCard(rule, exileIndex, cardId, out int removedId))
        {
            Debug.LogWarning("[TestPlay] 除外から山札へ戻せませんでした。");
            return;
        }

        if (toBottom)
        {
            rule.AppendCardsToBottom(new[] { removedId });
        }
        else
        {
            rule.PrependCardsToTopInOrder(new[] { removedId });
        }

        SyncGundamRuleDeckCount(ownerType, rule.GetRemainingCount());
        CloseDiscardZoneInspectionIfAny();
        Debug.Log(
            $"[TestPlay] Exile → Deck {(toBottom ? "bottom" : "top")} id:{removedId} side:{ownerType}");
    }

    private void TestPlayMoveExileCardToTrash(
        CardGameRule rule,
        PlayerType ownerType,
        int exileIndex,
        int cardId)
    {
        if (!TryTakeTestPlayExileCard(rule, exileIndex, cardId, out int removedId))
        {
            Debug.LogWarning("[TestPlay] 除外からトラッシュへ送れませんでした。");
            return;
        }

        rule.AddCardToTrash(removedId);
        CloseDiscardZoneInspectionIfAny();
        Debug.Log($"[TestPlay] Exile → Trash id:{removedId} side:{ownerType}");
    }

    private void TestPlayDeployExileCardToField(
        CardGameRule rule,
        PlayerType ownerType,
        int exileIndex,
        int cardId,
        CardData data)
    {
        if (data == null || !data.IsUnitLike() || CardImagePrefab == null)
        {
            return;
        }

        if (!TryTakeTestPlayExileCard(rule, exileIndex, cardId, out int removedId))
        {
            Debug.LogWarning("[TestPlay] 除外から配備できませんでした。");
            return;
        }

        CardData resolved = DeckSettinObject.Instance != null
            ? DeckSettinObject.Instance.GetCardDataById(removedId)
            : data;
        if (resolved == null)
        {
            rule.AddCardToExile(removedId);
            return;
        }

        GameObject go = Instantiate(CardImagePrefab);
        CardController spawned = go.GetComponent<CardController>();
        if (spawned == null)
        {
            Destroy(go);
            rule.AddCardToExile(removedId);
            return;
        }

        spawned.SetUp(resolved, OnCardClicked);
        SendCardToField(spawned, ownerType, rule);
        CloseDiscardZoneInspectionIfAny();
        Debug.Log($"[TestPlay] Exile → Field {resolved.cardName} side:{ownerType}");
    }

    private void TestPlayDeployExileCardToShield(
        CardGameRule rule,
        PlayerType ownerType,
        int exileIndex,
        int cardId,
        CardData data)
    {
        if (data == null || data.type != Type.Base || CardImagePrefab == null || gundamRule == null)
        {
            return;
        }

        if (!TryTakeTestPlayExileCard(rule, exileIndex, cardId, out int removedId))
        {
            Debug.LogWarning("[TestPlay] 除外からシールド配備できませんでした。");
            return;
        }

        CardData resolved = DeckSettinObject.Instance != null
            ? DeckSettinObject.Instance.GetCardDataById(removedId)
            : data;
        if (resolved == null)
        {
            rule.AddCardToExile(removedId);
            return;
        }

        GameObject go = Instantiate(CardImagePrefab);
        CardController spawned = go.GetComponent<CardController>();
        if (spawned == null)
        {
            Destroy(go);
            rule.AddCardToExile(removedId);
            return;
        }

        spawned.SetUp(resolved, OnCardClicked);
        if (!rule.TryForceAttachShieldCard(spawned))
        {
            Destroy(go);
            rule.AddCardToExile(removedId);
            Debug.LogWarning("[TestPlay] シールドゾーンへの配備に失敗しました。");
            return;
        }

        gundamRule.AddShieldCount(ToRuleSide(ownerType), 1);
        SyncResourceViewsFromRule(ToRuleSide(ownerType));
        CloseDiscardZoneInspectionIfAny();
        Debug.Log($"[TestPlay] Exile → Shield {resolved.cardName} side:{ownerType}");
    }
}
