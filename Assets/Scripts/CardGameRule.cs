using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TMPro; // これを追加！
using UnityEngine.UI;

/// <summary>トラッシュ／除外は同一 UI 位置で切り替え表示する。</summary>
public enum DiscardZoneViewMode
{
    Trash,
    Exile
}

public class CardGameRule
{
    // 実際に「山札」として使うリスト
    private List<int> deckList = new List<int>();
    private List<int> trashList = new List<int>();
    private List<int> exileList = new List<int>();
    private DiscardZoneViewMode _discardZoneViewMode = DiscardZoneViewMode.Trash;
    private int resourcePoints = 0; // プレイヤーのリソースポイントを管理する変数
    private int resourceLevel = 0;
    // Exリソースポイントを管理する変数（必要に応じて使用）
    private int ExtraResourcePoints = 0; 

    private TextMeshProUGUI extraResourcePoint; // Exリソースポイントのクラス（必要に応じて使用）
    
    private TextMeshProUGUI ResourcePointText; // リソースポイント表示用のテキスト
    private TextMeshProUGUI LevelText;

    private TMPro.TextMeshProUGUI levelText;

    private TextMeshProUGUI LvText;

    private TextMeshProUGUI ResourceText;
    private TextMeshProUGUI ExResourceText;
    
    private GameObject LvObj;

    private GameObject fieldPanel; // フィールドのパネルを管理する変数
    private GameObject PlayerMainFieldPanel; // プレイヤーのフィールドパネルを管理する変数
    private GameObject playerDeployPanel;
    private GameObject HandPanel;

    private GameObject ScrollPanel;
    private GameObject deckObjectPanel;
    private GameObject trashAreaPanel;
    private GameObject exileAreaPanel;
    private TextMeshProUGUI deckCountText;
    private TextMeshProUGUI handCountText;
    private TextMeshProUGUI discardZoneLabelText;
    private TextMeshProUGUI discardZoneCountText;
    private TextMeshProUGUI exileZoneLabelText;
    private TextMeshProUGUI exileZoneCountText;
    private Button discardZoneToggleButton;
    private Button discardZoneCountButton;
    private Button exileZoneCountButton;
    private Button deckAreaButton;
    private Button testPlayDeckDrawButton;
    private Button testPlayShieldTokenButton;
    private Button baseSlotAreaButton;

    private RectTransform resourceTokensContent;
    private RectTransform exTokensContent;
    private GameObject resourceZoneRoot;
    private GameObject exZoneRoot;
    private TextMeshProUGUI resourceZoneHeaderText;
    private TextMeshProUGUI exZoneHeaderText;
    private GameObject testPlayResourceCounterRoot;
    private GameObject testPlayExCounterRoot;
    private TextMeshProUGUI testPlayResourceCountText;
    private TextMeshProUGUI testPlayExCountText;
    private Action<bool> testPlayResourceTokenClickHandler;
    private Action<int> testPlayResourceLevelDeltaHandler;
    private Action<int> testPlayExDeltaHandler;
    private TextMeshProUGUI battleAreaLabelText;
    private TextMeshProUGUI baseZoneLabelText;
    private TextMeshProUGUI deckZoneLabelText;
    private static readonly List<CardGameRule> ActiveRules = new List<CardGameRule>();
    private static bool _localeHooked;
    private readonly List<GameObject> resourceTokenObjects = new List<GameObject>();
    private readonly List<GameObject> exTokenObjects = new List<GameObject>();
    private const float ResourceTokenWidth = 28f;
    private const float ResourceTokenHeight = 40f;
    private const float ResourceTokenRestAngleZ = -90f;
    /// <summary>親フィールドが 180° のとき true（相手盤）。レスト角の向き調整に使う。</summary>
    private bool _resourceBoardIsFlipped;

    private GameObject shieldPanelRoot;
    private RectTransform shieldCardsContent;
    private GridLayoutGroup shieldGrid;
    private TextMeshProUGUI exBaseDisplayText;
    private TextMeshProUGUI shieldCountDisplayText;
    private RectTransform baseSlotContent;
    private CardController deployedBase;
    private readonly List<int> shieldCardIds = new List<int>();
    private readonly List<CardController> shieldControllersInDrawOrder = new List<CardController>();
    /// <summary>シールド破壊で切り離したカードの一時退避（ゾーン UI に残さない）。</summary>
    private Transform shieldBreakLimbo;
    /// <summary>
    /// デッキデータを元に、シャッフルされた山札を作成する
    /// </summary>
    /// 
    private void Awake()
    {
        // デッキの初期化やリソースポイントの初期化など、必要なセットアップをここで行うことができます。
        // 例えば、ゲーム開始時にリソースポイントを0に設定するなど。
        // resourcePoints = 0;
        // resourceLevel = 0;
        // ExtraResourcePoints = 0;
        // !フィールドなどを生成する処理

    }
    public void SetUp(GameObject getfieldPanel)
    {
        this.fieldPanel = getfieldPanel;
        ClearGeneratedFieldUi(fieldPanel);
        ClearResourceTokenPools();

        // シーン上の旧 HandPanel が帯を隠すことがあるので無効化
        Transform sceneHand = fieldPanel.transform.Find("HandPanel");
        if (sceneHand != null)
        {
            sceneHand.gameObject.SetActive(false);
        }

        // 下から: 手札 → リソース帯 → バトル行（互いを重ねない）
        // 相手フィールドは親を 180° 回転するため、ここは自分と同じ配置でよい
        const int handHeaderHeight = 18;
        const int handScrollHeight = 88;
        const int handTotalHeight = handHeaderHeight + handScrollHeight; // 106
        const int resourceStripHeight = 72;
        const int gapHandToResource = 8;
        const int gapResourceToBattle = 4;
        const int sideColumnWidth = 70;
        const int battleAreaWidth = 320;
        float resourceBottom = handTotalHeight + gapHandToResource; // リソース帯の下端
        float battleBottom = resourceBottom + resourceStripHeight + gapResourceToBattle;

        // 1) 手札（最下）
        HandPanel = fieldPanel.CreateChildPanelCustom(
            "PlayerHandPanel",
            UIAnchor.BottomStretch,
            0,
            handTotalHeight);
        RectTransform handRt = HandPanel.GetComponent<RectTransform>();
        handRt.anchoredPosition = Vector2.zero;
        BuildHandCountArea(handHeaderHeight);
        ScrollPanel = HandPanel.CreateGridScrollView(600, handScrollHeight, UIAnchor.FullStretch);
        RectTransform scrollRect = ScrollPanel.GetComponent<RectTransform>();
        if (scrollRect != null)
        {
            scrollRect.offsetMin = new Vector2(0f, 0f);
            scrollRect.offsetMax = new Vector2(0f, -handHeaderHeight);
        }

        ScrollPanel.ConfigureGridCellFromViewportHeight(0.75f, 56f);

        // 2) リソース帯（手札の真上・独立パネル）
        GameObject resourceStripRoot = fieldPanel.CreateChildPanelCustom(
            "PlayerResourceAndExStrip",
            UIAnchor.BottomStretch,
            0,
            resourceStripHeight);
        RectTransform resourceStripRt = resourceStripRoot.GetComponent<RectTransform>();
        resourceStripRt.anchorMin = new Vector2(0f, 0f);
        resourceStripRt.anchorMax = new Vector2(1f, 0f);
        resourceStripRt.pivot = new Vector2(0.5f, 0f);
        resourceStripRt.sizeDelta = new Vector2(0f, resourceStripHeight);
        resourceStripRt.anchoredPosition = new Vector2(0f, resourceBottom);
        BuildResourceAndExStripContents(resourceStripRoot, resourceStripHeight);

        // 3) バトル行（リソース帯のさらに上）
        PlayerMainFieldPanel = fieldPanel.CreateChildPanelCustom("PlayerMainField", UIAnchor.FullStretch, 0, 0);
        RectTransform mainRt = PlayerMainFieldPanel.GetComponent<RectTransform>();
        mainRt.anchorMin = Vector2.zero;
        mainRt.anchorMax = Vector2.one;
        mainRt.pivot = new Vector2(0.5f, 0.5f);
        mainRt.offsetMin = new Vector2(0f, battleBottom);
        mainRt.offsetMax = Vector2.zero;
        Image mainBg = PlayerMainFieldPanel.GetComponent<Image>();
        if (mainBg != null)
        {
            mainBg.color = new Color32(30, 42, 58, 40);
            mainBg.raycastTarget = false;
        }

        GameObject battleRow = PlayerMainFieldPanel;
        BuildShieldPanel(battleRow, sideColumnWidth);

        GameObject battleAreaRoot = battleRow.CreateChildPanelCustom(
            "PlayerBattleAreaRoot",
            UIAnchor.TopCenter,
            battleAreaWidth,
            200);
        StretchVerticallyInParent(battleAreaRoot.GetComponent<RectTransform>(), centerHorizontally: true, width: battleAreaWidth);
        Image battleAreaBg = battleAreaRoot.GetComponent<Image>();
        if (battleAreaBg != null)
        {
            battleAreaBg.color = new Color32(48, 42, 72, 90);
        }

        TextMeshProUGUI battleAreaLabel = battleAreaRoot.CreateChildTextCustom(
            "BattleAreaLabel",
            UIAnchor.TopCenter,
            battleAreaWidth - 8,
            20);
        battleAreaLabelText = battleAreaLabel;
        battleAreaLabelText.SetLocalizedText(GameLocale.TKey("zone.battle"));
        battleAreaLabel.fontSize = 14;
        battleAreaLabel.color = new Color(0.85f, 0.9f, 1f, 1f);
        battleAreaLabel.alignment = TextAlignmentOptions.Center;
        battleAreaLabel.raycastTarget = false;
        RectTransform battleLabelRt = battleAreaLabel.GetComponent<RectTransform>();
        battleLabelRt.anchoredPosition = new Vector2(0f, -2f);

        playerDeployPanel = battleAreaRoot.CreateChildPanelCustom(
            "PlayerDeployPanel",
            UIAnchor.FullStretch,
            0,
            0);
        RectTransform deployRt = playerDeployPanel.GetComponent<RectTransform>();
        deployRt.offsetMin = new Vector2(4f, 4f);
        deployRt.offsetMax = new Vector2(-4f, -22f);
        Image deployBg = playerDeployPanel.GetComponent<Image>();
        if (deployBg != null)
        {
            deployBg.color = new Color32(255, 255, 255, 18);
            deployBg.raycastTarget = false;
        }

        var deployGrid = playerDeployPanel.AddComponent<GridLayoutGroup>();
        deployGrid.cellSize = new Vector2(96f, 88f);
        deployGrid.spacing = new Vector2(6f, 6f);
        deployGrid.padding = new RectOffset(6, 6, 4, 4);
        // 親を 180° すると手前側に来るよう、ローカル上側から並べる
        deployGrid.childAlignment = TextAnchor.UpperCenter;
        deployGrid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        deployGrid.startAxis = GridLayoutGroup.Axis.Horizontal;
        deployGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        deployGrid.constraintCount = 3;

        GameObject deckColumn = battleRow.CreateChildPanelCustom(
            "PlayerDeckAndTrashPanel",
            UIAnchor.TopRight,
            sideColumnWidth,
            200);
        StretchVerticallyInParent(deckColumn.GetComponent<RectTransform>(), centerHorizontally: false, width: sideColumnWidth, rightAligned: true);
        CreateDeckAndTrashArea(deckColumn, sideColumnWidth);

        // 描画順: バトル < リソース < 手札（手札をクリック優先）
        PlayerMainFieldPanel.transform.SetSiblingIndex(0);
        resourceStripRoot.transform.SetAsLastSibling();
        HandPanel.transform.SetAsLastSibling();

        RefreshHandCountDisplay();
        RebuildResourceTokenVisuals();
        // スクロールレイアウトで相手フィールドが 180° されたあとに呼び直せるよう公開フック
        RefreshResourceBoardFlipState();
        RegisterForLocaleRefresh();
        RefreshLocalizedZoneLabels();
    }

    private void RegisterForLocaleRefresh()
    {
        if (!ActiveRules.Contains(this))
        {
            ActiveRules.Add(this);
        }

        if (!_localeHooked)
        {
            _localeHooked = true;
            GameLocale.LanguageChanged += OnLocaleLanguageChanged;
        }
    }

    private static void OnLocaleLanguageChanged(GameLanguage _)
    {
        for (int i = ActiveRules.Count - 1; i >= 0; i--)
        {
            CardGameRule rule = ActiveRules[i];
            if (rule == null)
            {
                ActiveRules.RemoveAt(i);
                continue;
            }

            rule.RefreshLocalizedZoneLabels();
        }
    }

    /// <summary>盤面ゾーン名などを現行言語で書き換える。</summary>
    public void RefreshLocalizedZoneLabels()
    {
        if (battleAreaLabelText != null)
        {
            battleAreaLabelText.SetLocalizedText(GameLocale.TKey("zone.battle"));
        }

        if (baseZoneLabelText != null)
        {
            baseZoneLabelText.SetLocalizedText(GameLocale.TKey("zone.base"));
        }

        if (deckZoneLabelText != null)
        {
            deckZoneLabelText.SetLocalizedText(GameLocale.TKey("zone.deck"));
        }

        UpdateDeckAndDiscardZoneTexts();
        RebuildResourceTokenVisuals();
        RefreshHandCountDisplay();

        if (shieldCountDisplayText != null && shieldCountDisplayText.gameObject.activeInHierarchy)
        {
            SetShieldCountDisplay(GetShieldZoneCardCount());
        }
    }

    /// <summary>
    /// 親フィールドの回転を見て、リソーストークンのレスト向きを合わせる。
    /// BattleBoardScrollLayout 適用後に呼ぶこと。
    /// </summary>
    public void RefreshResourceBoardFlipState()
    {
        _resourceBoardIsFlipped = false;
        if (fieldPanel != null)
        {
            float z = fieldPanel.transform.eulerAngles.z;
            _resourceBoardIsFlipped = Mathf.Abs(Mathf.DeltaAngle(z, 180f)) < 45f;
        }

        RebuildResourceTokenVisuals();
    }

    private void ClearResourceTokenPools()
    {
        DestroyTokenPool(resourceTokenObjects);
        DestroyTokenPool(exTokenObjects);
        resourceTokensContent = null;
        exTokensContent = null;
        resourceZoneRoot = null;
        exZoneRoot = null;
        resourceZoneHeaderText = null;
        exZoneHeaderText = null;
        testPlayResourceCounterRoot = null;
        testPlayExCounterRoot = null;
        testPlayResourceCountText = null;
        testPlayExCountText = null;
        testPlayResourceTokenClickHandler = null;
        testPlayResourceLevelDeltaHandler = null;
        testPlayExDeltaHandler = null;
    }

    private static void DestroyTokenPool(List<GameObject> pool)
    {
        if (pool == null)
        {
            return;
        }

        for (int i = 0; i < pool.Count; i++)
        {
            if (pool[i] != null)
            {
                UnityEngine.Object.DestroyImmediate(pool[i]);
            }
        }

        pool.Clear();
    }

    /// <summary>前回 SetUp で生成した UI を破棄する。</summary>
    private static void ClearGeneratedFieldUi(GameObject fieldPanel)
    {
        if (fieldPanel == null)
        {
            return;
        }

        string[] generatedNames =
        {
            "PlayerHandPanel",
            "PlayerResourceAndExStrip",
            "PlayerMainField",
            "BattleRow",
            "PlayerDeployResourcePanel"
        };

        for (int i = fieldPanel.transform.childCount - 1; i >= 0; i--)
        {
            Transform child = fieldPanel.transform.GetChild(i);
            for (int n = 0; n < generatedNames.Length; n++)
            {
                if (child.name == generatedNames[n])
                {
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                    break;
                }
            }
        }
    }

    /// <summary>親の高さに合わせて左右固定幅で縦ストレッチする。</summary>
    private static void StretchVerticallyInParent(
        RectTransform rect,
        bool centerHorizontally,
        float width,
        bool rightAligned = false)
    {
        if (rect == null)
        {
            return;
        }

        if (centerHorizontally)
        {
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, 0f);
            rect.anchoredPosition = Vector2.zero;
            rect.offsetMin = new Vector2(rect.offsetMin.x, 0f);
            rect.offsetMax = new Vector2(rect.offsetMax.x, 0f);
            return;
        }

        if (rightAligned)
        {
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.offsetMin = new Vector2(-width, 0f);
            rect.offsetMax = new Vector2(0f, 0f);
            return;
        }

        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.offsetMin = new Vector2(0f, 0f);
        rect.offsetMax = new Vector2(width, 0f);
    }
    public void CreateField(GameObject targetPanel )
    {
        
    }
    public void CreateShuffledDeck(Dictionary<int, int> cardData)
    {
        CreateShuffledDeck(cardData, null);
    }

    public void CreateShuffledDeck(Dictionary<int, int> cardData, int? seed)
    {
        deckList.Clear();
        trashList.Clear();
        exileList.Clear();
        _discardZoneViewMode = DiscardZoneViewMode.Trash;

        Debug.Log($"デッキの数: {cardData.Count}枚");

        foreach (var pair in cardData)
        {
            for (int i = 0; i < pair.Value; i++)
            {
                deckList.Add(pair.Key);
            }
        }

        if (seed.HasValue)
        {
            System.Random rng = new System.Random(seed.Value);
            deckList = deckList.OrderBy(_ => rng.Next()).ToList();
        }
        else
        {
            deckList = deckList.OrderBy(x => System.Guid.NewGuid()).ToList();
        }

        Debug.Log($"山札を生成しました。枚数: {deckList.Count}");
        UpdateDeckAndTrashTexts();
    }

    public void ResourcAndLevelTextGet(TextMeshProUGUI resourceText, TextMeshProUGUI levelText, TextMeshProUGUI extraResourceText)
    {
        ResourcePointText = resourceText;
        LevelText = levelText;
        extraResourcePoint = extraResourceText;
        if (ResourcePointText != null) ResourcePointText.color = Color.black;
        if (LevelText != null) LevelText.color = Color.black;
        if (extraResourcePoint != null) extraResourcePoint.color = Color.black;

    }

    // 一応山札をシャッフルする関数も用意しておく
    public void ShuffleDeck()
    {
        deckList = deckList.OrderBy(x => System.Guid.NewGuid()).ToList();
        Debug.Log("山札をシャッフルしました。");
    }

    /// <summary>
    /// マリガン：手札として持っていたカードIDを山札に戻し、シャッフルする（ルール上は手札を山札に戻して再構築）。
    /// </summary>
    public void ReturnCardIdsToDeckAndShuffle(IReadOnlyList<int> cardIds)
    {
        if (cardIds == null || cardIds.Count == 0)
        {
            ShuffleDeck();
            UpdateDeckAndTrashTexts();
            return;
        }

        for (int i = 0; i < cardIds.Count; i++)
        {
            deckList.Add(cardIds[i]);
        }

        ShuffleDeck();
        UpdateDeckAndTrashTexts();
    }

    // デッキの内容を返す
    public List<int> GetDeckList() => deckList;

    /// <summary>山札の上から最大 count 枚のカード ID を返す（山札は変更しない）。</summary>
    public List<int> PeekTopCardIds(int count)
    {
        List<int> result = new List<int>();
        if (count <= 0 || deckList == null || deckList.Count == 0)
        {
            return result;
        }

        int take = Mathf.Min(count, deckList.Count);
        for (int i = 0; i < take; i++)
        {
            result.Add(deckList[i]);
        }

        return result;
    }

    /// <summary>指定インデックスのカードを山札から取り除き ID を返す。</summary>
    public bool TryTakeCardAtDeckIndex(int index, out int cardId)
    {
        cardId = -1;
        if (deckList == null || index < 0 || index >= deckList.Count)
        {
            return false;
        }

        cardId = deckList[index];
        deckList.RemoveAt(index);
        UpdateDeckAndTrashTexts();
        return true;
    }

    /// <summary>指定 ID のカードを山札から1枚取り除く（同 ID が複数あれば先頭の1枚）。</summary>
    public bool TryTakeCardById(int cardId, out int removedAtIndex)
    {
        removedAtIndex = -1;
        if (deckList == null || cardId < 0)
        {
            return false;
        }

        for (int i = 0; i < deckList.Count; i++)
        {
            if (deckList[i] != cardId)
            {
                continue;
            }

            removedAtIndex = i;
            deckList.RemoveAt(i);
            UpdateDeckAndTrashTexts();
            return true;
        }

        return false;
    }

    /// <summary>山札の上に、リスト先頭が一番上になるよう順番どおり挿入する。</summary>
    public void PrependCardsToTopInOrder(IReadOnlyList<int> cardIdsTopFirst)
    {
        if (deckList == null || cardIdsTopFirst == null || cardIdsTopFirst.Count == 0)
        {
            return;
        }

        for (int i = cardIdsTopFirst.Count - 1; i >= 0; i--)
        {
            deckList.Insert(0, cardIdsTopFirst[i]);
        }

        UpdateDeckAndTrashTexts();
    }

    /// <summary>山札の下にカードを追加する。</summary>
    public void AppendCardsToBottom(IReadOnlyList<int> cardIds)
    {
        if (deckList == null || cardIds == null || cardIds.Count == 0)
        {
            return;
        }

        for (int i = 0; i < cardIds.Count; i++)
        {
            deckList.Add(cardIds[i]);
        }

        UpdateDeckAndTrashTexts();
    }

    public bool ContainsCardId(int cardId)
    {
        if (deckList == null || cardId < 0)
        {
            return false;
        }

        return deckList.Contains(cardId);
    }

    /// <summary>
    /// 山札の一番上からカードを1枚引く
    /// </summary>
    public int Draw()
    {
        // オンラインミラー用パディングを先頭から捨てる
        while (deckList.Count > 0 && deckList[0] == OnlineDeckCountPaddingId)
        {
            deckList.RemoveAt(0);
        }

        if (deckList.Count == 0)
        {
            Debug.LogWarning("山札が空です！");
            UpdateDeckAndTrashTexts();
            return -1; // デッキ切れの合図
        }

        // 一番上のカードを取得して、リストから消す
        int topCardId = deckList[0];
        deckList.RemoveAt(0);
        UpdateDeckAndTrashTexts();

        return topCardId;
    }
    public void StartTurn()
    {
        // ターン開始時の処理をここに書きます。
        // 例えば、リソースポイントのリセットやカードのドローなど。
        // RefreshResourcePoints(); // ターン開始時にリソースポイントをリセット
        int drawnCardId = Draw(); // ターン開始時にカードを1枚引く（必要に応じて枚数を増やすこともできます）
        CardData drawnCardData = DeckSettinObject.Instance.GetCardDataById(drawnCardId);
        Debug.Log($"ターン開始！引いたカードID: {drawnCardId}, カード名: {drawnCardData.cardName}");
    }

    // リソースポイントを増やす関数
    // デフォルトでは1ポイント増やすようにしていますが、引数で任意の値を指定できます。
    public void AddResourcePoints(int amount=1)
    {
        resourceLevel += amount;
        if (LvText != null)
        {
            LvText.text = "LV:"+resourceLevel.ToString();
        }

        RebuildResourceTokenVisuals();
        Debug.Log($"リソースレベルが{amount}増加しました。現在のレベル: {resourceLevel}");
    }

   public RectTransform PlayerFieldPanel => fieldPanel.GetComponent<RectTransform>();
   public RectTransform PlayerDeployPanel => playerDeployPanel != null ? playerDeployPanel.GetComponent<RectTransform>() : fieldPanel.GetComponent<RectTransform>();
   public RectTransform PlayerHandPanel => HandPanel.GetComponent<RectTransform>();
   public RectTransform HandScrollContent => ScrollPanel.GetComponent<ScrollRect>().content;
    public RectTransform ShieldCardsContent => shieldCardsContent;
    public RectTransform BaseSlotContent => baseSlotContent;
    public CardController DeployedBase => deployedBase;

    /// <summary>
    /// マリガン完了後：EXベース表示を更新し、山札上から指定枚数をシールドエリアに並べる（手札には加えない）。
    /// </summary>
    public void SetupShieldFromDeckAfterMulligan(GameObject cardPrefab, System.Action<CardController> onShieldCardClicked, int shieldCardCount, int exBasePoints)
    {
        shieldCardIds.Clear();
        shieldControllersInDrawOrder.Clear();
        if (shieldCardsContent == null || cardPrefab == null)
        {
            Debug.LogWarning("シールド設置: コンテナまたはカードプレハブがありません。");
            return;
        }

        for (int i = shieldCardsContent.childCount - 1; i >= 0; i--)
        {
            UnityEngine.Object.Destroy(shieldCardsContent.GetChild(i).gameObject);
        }

        SetExBaseDisplay(exBasePoints);

        for (int i = 0; i < shieldCardCount; i++)
        {
            int id = Draw();
            if (id < 0)
            {
                Debug.LogWarning("シールド設置: 山札が不足しました。");
                break;
            }
            shieldCardIds.Add(id);
            CardData data = DeckSettinObject.Instance.GetCardDataById(id);
            GameObject go = UnityEngine.Object.Instantiate(cardPrefab, shieldCardsContent);
            CardController cc = go.GetComponent<CardController>();
            if (cc != null)
            {
                cc.SetUp(data, onShieldCardClicked);
                ApplyShieldCardLayout(go);
                shieldControllersInDrawOrder.Add(cc);
                cc.SetShieldFaceHidden(true);
            }
        }
        SetShieldCountDisplay(shieldControllersInDrawOrder.Count);
    }

    /// <summary>シールド枠のカードサイズをグリッドに合わせる。</summary>
    private void ApplyShieldCardLayout(GameObject cardObject)
    {
        if (cardObject == null)
        {
            return;
        }

        RectTransform cardRect = cardObject.GetComponent<RectTransform>();
        Vector2 cell = shieldGrid != null ? shieldGrid.cellSize : new Vector2(48f, 26f);
        if (cardRect != null)
        {
            cardRect.localScale = Vector3.one;
            cardRect.localRotation = Quaternion.identity;
            cardRect.sizeDelta = cell;
        }

        LayoutElement layout = cardObject.GetComponent<LayoutElement>();
        if (layout == null)
        {
            layout = cardObject.AddComponent<LayoutElement>();
        }

        layout.preferredWidth = cell.x;
        layout.preferredHeight = cell.y;
        layout.minWidth = cell.x;
        layout.minHeight = cell.y;
        layout.ignoreLayout = false;
    }

    /// <summary>
    /// オンライン対戦：相手クライアントから受け取ったシールドカード ID でゾーンを構築する。
    /// </summary>
    public void SetupShieldFromCardIds(
        GameObject cardPrefab,
        System.Action<CardController> onShieldCardClicked,
        IReadOnlyList<int> cardIds,
        int exBasePoints)
    {
        shieldCardIds.Clear();
        shieldControllersInDrawOrder.Clear();
        if (shieldCardsContent == null || cardPrefab == null)
        {
            Debug.LogWarning("シールド設置(同期): コンテナまたはカードプレハブがありません。");
            return;
        }

        for (int i = shieldCardsContent.childCount - 1; i >= 0; i--)
        {
            UnityEngine.Object.Destroy(shieldCardsContent.GetChild(i).gameObject);
        }

        SetExBaseDisplay(exBasePoints);

        if (cardIds == null)
        {
            return;
        }

        for (int i = 0; i < cardIds.Count; i++)
        {
            int id = cardIds[i];
            if (id < 0)
            {
                continue;
            }

            TryTakeCardById(id, out _);
            shieldCardIds.Add(id);
            CardData data = DeckSettinObject.Instance.GetCardDataById(id);
            if (data == null)
            {
                Debug.LogWarning($"シールド設置(同期): 不明なカード ID {id}");
                continue;
            }

            GameObject go = UnityEngine.Object.Instantiate(cardPrefab, shieldCardsContent);
            CardController cc = go.GetComponent<CardController>();
            if (cc != null)
            {
                cc.SetUp(data, onShieldCardClicked);
                ApplyShieldCardLayout(go);
                shieldControllersInDrawOrder.Add(cc);
                cc.SetShieldFaceHidden(true);
            }
        }

        SetShieldCountDisplay(shieldControllersInDrawOrder.Count);
        UpdateDeckAndTrashTexts();
    }

    /// <summary>
    /// オンライン同期：ベース枠は触らずシールドゾーンのみ指定 ID 列で再構築する。
    /// </summary>
    public void ApplyShieldZoneSnapshotFromCardIds(
        GameObject cardPrefab,
        System.Action<CardController> onShieldCardClicked,
        IReadOnlyList<int> cardIds)
    {
        shieldCardIds.Clear();
        shieldControllersInDrawOrder.Clear();
        if (shieldCardsContent == null || cardPrefab == null)
        {
            Debug.LogWarning("シールド同期: コンテナまたはカードプレハブがありません。");
            return;
        }

        for (int i = shieldCardsContent.childCount - 1; i >= 0; i--)
        {
            UnityEngine.Object.Destroy(shieldCardsContent.GetChild(i).gameObject);
        }

        if (cardIds == null)
        {
            return;
        }

        for (int i = 0; i < cardIds.Count; i++)
        {
            int id = cardIds[i];
            if (id < 0)
            {
                continue;
            }

            shieldCardIds.Add(id);
            CardData data = DeckSettinObject.Instance.GetCardDataById(id);
            if (data == null)
            {
                Debug.LogWarning($"シールド同期: 不明なカード ID {id}");
                continue;
            }

            GameObject go = UnityEngine.Object.Instantiate(cardPrefab, shieldCardsContent);
            CardController cc = go.GetComponent<CardController>();
            if (cc != null)
            {
                cc.SetUp(data, onShieldCardClicked);
                ApplyShieldCardLayout(go);
                shieldControllersInDrawOrder.Add(cc);
                cc.SetShieldFaceHidden(true);
            }
        }

        SetShieldCountDisplay(shieldControllersInDrawOrder.Count);
    }

    /// <summary>オンラインミラー用：実カードが無いときの山札枚数パディング ID。</summary>
    public const int OnlineDeckCountPaddingId = -999;

    /// <summary>オンライン：相手の山札残数に合わせる（シールド ID 除去後に余剰を削る）。</summary>
    public void TrimDeckToRemainingCount(int targetRemainCount)
    {
        SetDeckRemainCount(targetRemainCount);
    }

    /// <summary>
    /// 山札残数を権威値へ完全同期する。少ないときは末尾を削り、多いときはパディングして UI 枚数を合わせる。
    /// </summary>
    public void SetDeckRemainCount(int targetRemainCount)
    {
        if (targetRemainCount < 0)
        {
            targetRemainCount = 0;
        }

        while (deckList.Count > targetRemainCount)
        {
            deckList.RemoveAt(deckList.Count - 1);
        }

        while (deckList.Count < targetRemainCount)
        {
            deckList.Add(OnlineDeckCountPaddingId);
        }

        UpdateDeckAndTrashTexts();
    }

    /// <summary>指定 ID のシールドをゾーンから切り離す（同期用）。</summary>
    public bool TryDetachShieldCardById(int cardId, out ShieldBreakTaken taken, bool revealFace = true)
    {
        taken = default;
        for (int i = 0; i < shieldCardIds.Count; i++)
        {
            if (shieldCardIds[i] == cardId)
            {
                return TryDetachShieldCardAtZoneIndex(i, out taken, revealFace);
            }
        }

        return false;
    }

    /// <summary>
    /// シールドが破壊された枚数ぶん、先頭からカードをトラッシュへ送る（バースト UI なしの旧経路）。
    /// </summary>
    public void MoveTopShieldCardsToTrash(int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (!TryTakeTopShieldCardForBreak(out ShieldBreakTaken taken))
            {
                break;
            }

            CommitShieldCardToTrash(taken);
        }
    }

    private void EnsureShieldBreakLimbo()
    {
        if (shieldBreakLimbo != null)
        {
            return;
        }

        Transform limboParent = shieldPanelRoot != null
            ? shieldPanelRoot.transform
            : (fieldPanel != null ? fieldPanel.transform : null);
        GameObject limbo = new GameObject("ShieldBreakLimbo");
        if (limboParent != null)
        {
            limbo.transform.SetParent(limboParent, false);
        }

        limbo.SetActive(false);
        shieldBreakLimbo = limbo.transform;
    }

    /// <summary>破壊される先頭シールド1枚をリストから切り離し、表面を公開する。</summary>
    public bool TryTakeTopShieldCardForBreak(out ShieldBreakTaken taken)
    {
        return TryDetachShieldCardAtZoneIndex(0, out taken, revealFace: true);
    }

    /// <summary>シールドゾーンに残っている実カード枚数（一覧のゾンビエントリを除去してから数える）。</summary>
    public int GetShieldZoneCardCount()
    {
        PruneStaleShieldZoneEntries();
        RebuildShieldZoneListFromContentIfDesynced();
        return Mathf.Min(shieldControllersInDrawOrder.Count, shieldCardIds.Count);
    }

    /// <summary>
    /// 子 Transform にカードがあるのに登録リストが空、などの不整合を子から再構築する。
    /// </summary>
    private void RebuildShieldZoneListFromContentIfDesynced()
    {
        if (shieldCardsContent == null)
        {
            return;
        }

        int childCardCount = 0;
        for (int i = 0; i < shieldCardsContent.childCount; i++)
        {
            if (shieldCardsContent.GetChild(i).GetComponent<CardController>() != null)
            {
                childCardCount++;
            }
        }

        if (childCardCount <= 0 || shieldControllersInDrawOrder.Count == childCardCount)
        {
            return;
        }

        shieldControllersInDrawOrder.Clear();
        shieldCardIds.Clear();
        for (int i = 0; i < shieldCardsContent.childCount; i++)
        {
            CardController cc = shieldCardsContent.GetChild(i).GetComponent<CardController>();
            if (cc == null || cc.Data == null)
            {
                continue;
            }

            shieldControllersInDrawOrder.Add(cc);
            shieldCardIds.Add(cc.Data.id);
        }

        Debug.Log(
            $"[ShieldZone] Rebuilt registry from content children count:{shieldControllersInDrawOrder.Count}");
    }

    /// <summary>破棄・ベース昇格などでゾーンを離れたが一覧に残っているエントリを除去する。</summary>
    private void PruneStaleShieldZoneEntries()
    {
        for (int i = shieldControllersInDrawOrder.Count - 1; i >= 0; i--)
        {
            CardController cc = shieldControllersInDrawOrder[i];
            if (cc == null
                || shieldCardsContent == null
                || !cc.transform.IsChildOf(shieldCardsContent))
            {
                shieldControllersInDrawOrder.RemoveAt(i);
                if (i < shieldCardIds.Count)
                {
                    shieldCardIds.RemoveAt(i);
                }
            }
        }

        while (shieldCardIds.Count > shieldControllersInDrawOrder.Count)
        {
            shieldCardIds.RemoveAt(shieldCardIds.Count - 1);
        }

        while (shieldControllersInDrawOrder.Count > shieldCardIds.Count)
        {
            shieldControllersInDrawOrder.RemoveAt(shieldControllersInDrawOrder.Count - 1);
        }
    }

    /// <summary>ゾーン内インデックスのカード情報（制圧選択 UI 用）。</summary>
    public bool TryGetShieldZoneCardAt(int zoneIndex, out ShieldBreakTaken taken)
    {
        taken = default;
        if (zoneIndex < 0 || zoneIndex >= shieldControllersInDrawOrder.Count || zoneIndex >= shieldCardIds.Count)
        {
            return false;
        }

        CardController cc = shieldControllersInDrawOrder[zoneIndex];
        int id = shieldCardIds[zoneIndex];
        taken = new ShieldBreakTaken
        {
            Controller = cc,
            CardId = id,
            Data = cc != null && cc.Data != null ? cc.Data : DeckSettinObject.Instance.GetCardDataById(id),
        };
        return taken.Data != null;
    }

    /// <summary>ゾーン内インデックスのカードを切り離す（制圧で順番確定後）。</summary>
    public bool TryDetachShieldCardAtZoneIndex(int zoneIndex, out ShieldBreakTaken taken, bool revealFace = true)
    {
        taken = default;
        if (zoneIndex < 0 || zoneIndex >= shieldControllersInDrawOrder.Count || zoneIndex >= shieldCardIds.Count)
        {
            return false;
        }

        CardController cc = shieldControllersInDrawOrder[zoneIndex];
        int id = shieldCardIds[zoneIndex];
        shieldControllersInDrawOrder.RemoveAt(zoneIndex);
        shieldCardIds.RemoveAt(zoneIndex);

        if (cc != null)
        {
            if (revealFace)
            {
                cc.RevealShieldFace();
            }

            // ゾーン UI から外し、コミット／バースト配備まで一時退避（残像バグ防止）
            EnsureShieldBreakLimbo();
            cc.transform.SetParent(shieldBreakLimbo, false);
            cc.gameObject.SetActive(false);
        }

        taken = new ShieldBreakTaken
        {
            Controller = cc,
            CardId = id,
            Data = cc != null && cc.Data != null ? cc.Data : DeckSettinObject.Instance.GetCardDataById(id),
        };
        return true;
    }

    /// <summary>公開・バースト処理後にトラッシュへ送る。</summary>
    public void CommitShieldCardToTrash(ShieldBreakTaken taken)
    {
        if (taken.CardId > 0)
        {
            AddCardToTrash(taken.CardId);
        }

        if (taken.Controller != null)
        {
            UnityEngine.Object.Destroy(taken.Controller.gameObject);
        }
    }

    /// <summary>手札の CardController をシールドゾーン末尾に追加する（face down）。</summary>
    public bool TryAttachShieldCardFromHand(CardController cc)
    {
        if (cc == null || cc.Data == null || shieldCardsContent == null)
        {
            return false;
        }

        if (cc.Data.IsUnitLike() || cc.Data.IsPilot() || cc.Data.type == Type.Base)
        {
            Debug.LogWarning(
                $"[ShieldDeploy] ユニット/パイロット/ベースはシールドゾーンへ配備できません: {cc.Data.cardName}(type:{cc.Data.type})");
            return false;
        }

        shieldCardIds.Add(cc.Data.id);
        shieldControllersInDrawOrder.Add(cc);
        cc.gameObject.SetActive(true);
        cc.transform.SetParent(shieldCardsContent, false);
        RectTransform cardRect = cc.GetComponent<RectTransform>();
        if (cardRect != null && shieldGrid != null)
        {
            cardRect.localScale = Vector3.one;
            cardRect.sizeDelta = shieldGrid.cellSize;
        }

        cc.SetShieldFaceHidden(true);
        cc.SetEligibleForShieldZoneDeploy(false);
        return true;
    }

    /// <summary>TestPlay: 種類制限なしでシールドゾーンへ載せる（ベース含む）。</summary>
    public bool TryForceAttachShieldCard(CardController cc)
    {
        if (cc == null || cc.Data == null || shieldCardsContent == null)
        {
            return false;
        }

        shieldCardIds.Add(cc.Data.id);
        shieldControllersInDrawOrder.Add(cc);
        cc.gameObject.SetActive(true);
        cc.transform.SetParent(shieldCardsContent, false);
        ApplyShieldCardLayout(cc.gameObject);
        cc.SetShieldFaceHidden(true);
        cc.SetEligibleForShieldZoneDeploy(false);
        SetShieldCountDisplay(shieldControllersInDrawOrder.Count);
        return true;
    }

    public bool HasShieldCardInZone => GetShieldZoneCardCount() > 0;

    /// <summary>シールドゾーンの登録リストに載っているか（親 Transform だけ残っているゾンビは false）。</summary>
    public bool IsRegisteredInShieldZone(CardController cc)
    {
        return cc != null && shieldControllersInDrawOrder.Contains(cc);
    }

    /// <summary>
    /// 破壊切り離し後も UI 親がシールドゾーンのままのカードを、ゾーン登録に戻す
    /// （DeploySelfToShield バースト用）。
    /// </summary>
    public bool TryReregisterDetachedShieldCard(CardController cc)
    {
        if (cc == null || cc.Data == null || shieldCardsContent == null)
        {
            return false;
        }

        if (IsRegisteredInShieldZone(cc))
        {
            return true;
        }

        bool underShield = shieldCardsContent != null && cc.transform.IsChildOf(shieldCardsContent);
        bool underLimbo = shieldBreakLimbo != null && cc.transform.IsChildOf(shieldBreakLimbo);
        if (!underShield && !underLimbo)
        {
            return false;
        }

        shieldCardIds.Add(cc.Data.id);
        shieldControllersInDrawOrder.Add(cc);
        cc.gameObject.SetActive(true);
        cc.transform.SetParent(shieldCardsContent, false);
        cc.SetShieldFaceHidden(true);
        cc.SetEligibleForShieldZoneDeploy(false);
        RectTransform cardRect = cc.GetComponent<RectTransform>();
        if (cardRect != null && shieldGrid != null)
        {
            cardRect.localScale = Vector3.one;
            cardRect.sizeDelta = shieldGrid.cellSize;
        }

        return true;
    }

    /// <summary>
    /// 登録リストに無いのにシールドゾーン配下に残っているカード UI を破棄する。
    /// 破壊時にリストだけ外れて GameObject が残る不整合の掃除。
    /// </summary>
    public void DestroyUnregisteredShieldZoneVisuals()
    {
        if (shieldCardsContent == null)
        {
            return;
        }

        PruneStaleShieldZoneEntries();
        HashSet<CardController> registered = new HashSet<CardController>(shieldControllersInDrawOrder);
        for (int i = shieldCardsContent.childCount - 1; i >= 0; i--)
        {
            CardController cc = shieldCardsContent.GetChild(i).GetComponent<CardController>();
            if (cc == null || registered.Contains(cc))
            {
                continue;
            }

            UnityEngine.Object.Destroy(cc.gameObject);
        }
    }

    /// <summary>シールドゾーン登録から外す（ベース配備などでゾーンを離れるとき）。</summary>
    public bool TryUnregisterShieldZoneCard(CardController cc)
    {
        if (cc == null)
        {
            return false;
        }

        int index = shieldControllersInDrawOrder.IndexOf(cc);
        if (index < 0 || index >= shieldCardIds.Count)
        {
            return false;
        }

        shieldControllersInDrawOrder.RemoveAt(index);
        shieldCardIds.RemoveAt(index);
        return true;
    }

    /// <summary>ベース枠にカードが残っているか（参照切れ対策）。</summary>
    public bool HasOccupantInBaseSlot()
    {
        if (baseSlotContent == null)
        {
            return deployedBase != null;
        }

        for (int i = 0; i < baseSlotContent.childCount; i++)
        {
            if (baseSlotContent.GetChild(i).GetComponent<CardController>() != null)
            {
                return true;
            }
        }

        return deployedBase != null;
    }

    /// <summary>指定シールドカードを手札へ移す（破壊 UI なし）。</summary>
    public bool TryMoveShieldCardToHand(CardController shieldCard, RectTransform handContent)
    {
        if (shieldCard == null || handContent == null)
        {
            return false;
        }

        if (!TryUnregisterShieldZoneCard(shieldCard))
        {
            return false;
        }

        shieldCard.RevealShieldFace();
        shieldCard.gameObject.SetActive(true);
        shieldCard.transform.SetParent(handContent, false);
        RectTransform cardRect = shieldCard.GetComponent<RectTransform>();
        if (cardRect != null)
        {
            cardRect.localScale = Vector3.one;
        }

        ApplyHandZoneLayoutToCard(shieldCard);
        return true;
    }

    /// <summary>シールドゾーン先頭1枚を手札へ移す（破壊・バースト UI なし）。</summary>
    public bool TryMoveTopShieldCardToHand(RectTransform handContent, out CardController movedCard)
    {
        movedCard = null;
        if (handContent == null || !HasShieldCardInZone)
        {
            return false;
        }

        CardController cc = shieldControllersInDrawOrder[0];
        shieldControllersInDrawOrder.RemoveAt(0);
        shieldCardIds.RemoveAt(0);
        if (cc == null)
        {
            return false;
        }

        cc.RevealShieldFace();
        cc.gameObject.SetActive(true);
        cc.transform.SetParent(handContent, false);
        RectTransform cardRect = cc.GetComponent<RectTransform>();
        if (cardRect != null)
        {
            cardRect.localScale = Vector3.one;
        }

        movedCard = cc;
        return true;
    }

    public IReadOnlyList<int> GetShieldCardIds() => shieldCardIds;

    public IReadOnlyList<int> GetTrashCardIds() => trashList;

    public IReadOnlyList<int> GetExileCardIds() => exileList;

    public DiscardZoneViewMode DiscardZoneViewMode => _discardZoneViewMode;

    public DiscardZoneViewMode ToggleDiscardZoneView()
    {
        _discardZoneViewMode = _discardZoneViewMode == DiscardZoneViewMode.Trash
            ? DiscardZoneViewMode.Exile
            : DiscardZoneViewMode.Trash;
        UpdateDeckAndDiscardZoneTexts();
        return _discardZoneViewMode;
    }

    public void SetDiscardZoneViewMode(DiscardZoneViewMode mode)
    {
        _discardZoneViewMode = mode;
        UpdateDeckAndDiscardZoneTexts();
    }

    /// <summary>旧UI互換: ラベル押下で TRASH↔EXILE 切替。</summary>
    public void BindDiscardZoneToggleClick(Action onClick)
    {
        if (discardZoneToggleButton == null || onClick == null)
        {
            return;
        }

        discardZoneToggleButton.onClick.RemoveAllListeners();
        discardZoneToggleButton.onClick.AddListener(() => onClick());
    }

    /// <summary>トラッシュ領域押下。</summary>
    public void BindDiscardZoneCountClick(Action onClick)
    {
        if (discardZoneCountButton == null || onClick == null)
        {
            return;
        }

        discardZoneCountButton.onClick.RemoveAllListeners();
        discardZoneCountButton.onClick.AddListener(() => onClick());
    }

    /// <summary>除外領域押下。</summary>
    public void BindExileZoneCountClick(Action onClick)
    {
        if (exileZoneCountButton == null || onClick == null)
        {
            return;
        }

        exileZoneCountButton.onClick.RemoveAllListeners();
        exileZoneCountButton.onClick.AddListener(() => onClick());
    }

    /// <summary>山札ゾーン押下。</summary>
    public void BindDeckAreaClick(Action onClick)
    {
        if (deckAreaButton == null || onClick == null)
        {
            return;
        }

        deckAreaButton.onClick.RemoveAllListeners();
        deckAreaButton.onClick.AddListener(() => onClick());
    }

    /// <summary>ベース枠（EXベース表示含む）押下。配備ベースカード自体のクリックは CardController 側。</summary>
    public void BindBaseSlotAreaClick(Action onClick)
    {
        if (onClick == null)
        {
            return;
        }

        void EnsureButton(GameObject go, Graphic graphic)
        {
            if (go == null)
            {
                return;
            }

            if (graphic != null)
            {
                graphic.raycastTarget = true;
            }

            Button btn = go.GetComponent<Button>();
            if (btn == null)
            {
                btn = go.AddComponent<Button>();
            }

            if (graphic != null)
            {
                btn.targetGraphic = graphic;
            }

            btn.transition = Selectable.Transition.None;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => onClick());
        }

        if (baseSlotContent != null)
        {
            Image slotBg = baseSlotContent.GetComponent<Image>();
            EnsureButton(baseSlotContent.gameObject, slotBg);
            baseSlotAreaButton = baseSlotContent.GetComponent<Button>();
        }

        if (exBaseDisplayText != null)
        {
            EnsureButton(exBaseDisplayText.gameObject, exBaseDisplayText);
        }
    }

    /// <summary>TestPlay: リソース／EXゾーン上の +/- カウンターとトークン押下。</summary>
    public void BindTestPlayResourceZoneControls(
        Action<int> onResourceLevelDelta,
        Action<int> onExDelta,
        Action<bool> onResourceTokenClicked)
    {
        testPlayResourceLevelDeltaHandler = onResourceLevelDelta;
        testPlayExDeltaHandler = onExDelta;
        testPlayResourceTokenClickHandler = onResourceTokenClicked;
        EnsureTestPlayZoneCounter(
            resourceZoneRoot,
            ref testPlayResourceCounterRoot,
            ref testPlayResourceCountText,
            "TestPlayResCounter",
            () => testPlayResourceLevelDeltaHandler?.Invoke(-1),
            () => testPlayResourceLevelDeltaHandler?.Invoke(1));
        EnsureTestPlayZoneCounter(
            exZoneRoot,
            ref testPlayExCounterRoot,
            ref testPlayExCountText,
            "TestPlayExCounter",
            () => testPlayExDeltaHandler?.Invoke(-1),
            () => testPlayExDeltaHandler?.Invoke(1));

        if (resourceZoneHeaderText != null)
        {
            RectTransform headerRt = resourceZoneHeaderText.GetComponent<RectTransform>();
            headerRt.sizeDelta = new Vector2(160f, 16f);
        }

        if (exZoneHeaderText != null)
        {
            RectTransform headerRt = exZoneHeaderText.GetComponent<RectTransform>();
            headerRt.sizeDelta = new Vector2(36f, 16f);
        }

        RebuildResourceTokenVisuals();
    }

    private void EnsureTestPlayZoneCounter(
        GameObject zone,
        ref GameObject root,
        ref TextMeshProUGUI countText,
        string name,
        Action onMinus,
        Action onPlus)
    {
        if (zone == null)
        {
            return;
        }

        if (root != null)
        {
            return;
        }

        root = new GameObject(name, typeof(RectTransform));
        root.transform.SetParent(zone.transform, false);
        RectTransform rt = root.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.sizeDelta = new Vector2(84f, 18f);
        rt.anchoredPosition = new Vector2(-2f, -1f);

        Button minusBtn = root.CreateChildButton("-");
        RectTransform minusRt = minusBtn.GetComponent<RectTransform>();
        minusRt.anchorMin = new Vector2(0f, 0.5f);
        minusRt.anchorMax = new Vector2(0f, 0.5f);
        minusRt.pivot = new Vector2(0f, 0.5f);
        minusRt.sizeDelta = new Vector2(22f, 16f);
        minusRt.anchoredPosition = Vector2.zero;
        TextMeshProUGUI minusLabel = minusBtn.GetComponentInChildren<TextMeshProUGUI>();
        if (minusLabel != null)
        {
            minusLabel.fontSize = 14;
            minusLabel.color = Color.black;
        }

        minusBtn.onClick.RemoveAllListeners();
        minusBtn.onClick.AddListener(() => onMinus?.Invoke());

        countText = root.CreateChildTextCustom("Count", UIAnchor.TopCenter, 36, 16);
        countText.text = "0";
        countText.fontSize = 12;
        countText.fontStyle = FontStyles.Bold;
        countText.color = Color.white;
        countText.alignment = TextAlignmentOptions.Center;
        countText.raycastTarget = false;

        Button plusBtn = root.CreateChildButton("+");
        RectTransform plusRt = plusBtn.GetComponent<RectTransform>();
        plusRt.anchorMin = new Vector2(1f, 0.5f);
        plusRt.anchorMax = new Vector2(1f, 0.5f);
        plusRt.pivot = new Vector2(1f, 0.5f);
        plusRt.sizeDelta = new Vector2(22f, 16f);
        plusRt.anchoredPosition = Vector2.zero;
        TextMeshProUGUI plusLabel = plusBtn.GetComponentInChildren<TextMeshProUGUI>();
        if (plusLabel != null)
        {
            plusLabel.fontSize = 14;
            plusLabel.color = Color.black;
        }

        plusBtn.onClick.RemoveAllListeners();
        plusBtn.onClick.AddListener(() => onPlus?.Invoke());
    }

    /// <summary>TestPlay: 山札ゾーン下に Draw ボタンを用意する。</summary>
    public void EnsureTestPlayDeckDrawButton(Action onDraw)
    {
        if (deckObjectPanel == null || onDraw == null)
        {
            return;
        }

        if (testPlayDeckDrawButton == null)
        {
            Button btn = deckObjectPanel.CreateChildButton("Draw");
            testPlayDeckDrawButton = btn;
            RectTransform btnRt = btn.GetComponent<RectTransform>();
            btnRt.anchorMin = new Vector2(0.5f, 0f);
            btnRt.anchorMax = new Vector2(0.5f, 0f);
            btnRt.pivot = new Vector2(0.5f, 0f);
            btnRt.sizeDelta = new Vector2(52f, 22f);
            btnRt.anchoredPosition = new Vector2(0f, 2f);
            TextMeshProUGUI label = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
            {
                label.SetLocalizedText("ドロー", "Draw");
                label.fontSize = 12;
                label.color = Color.black;
            }

            // 枚数表示を Draw の上へずらす
            if (deckCountText != null)
            {
                RectTransform countRt = deckCountText.GetComponent<RectTransform>();
                countRt.anchoredPosition = new Vector2(0f, 24f);
            }
        }

        testPlayDeckDrawButton.gameObject.SetActive(true);
        testPlayDeckDrawButton.onClick.RemoveAllListeners();
        testPlayDeckDrawButton.onClick.AddListener(() => onDraw());
    }

    public void SetTestPlayDeckDrawButtonInteractable(bool interactable)
    {
        if (testPlayDeckDrawButton != null)
        {
            testPlayDeckDrawButton.interactable = interactable;
        }
    }

    /// <summary>TestPlay: シールドゾーン下にトークン選択ボタンを用意する。</summary>
    public void EnsureTestPlayShieldTokenButton(Action onOpen)
    {
        if (shieldPanelRoot == null || onOpen == null)
        {
            return;
        }

        if (testPlayShieldTokenButton == null)
        {
            Button btn = shieldPanelRoot.CreateChildButton("Token");
            testPlayShieldTokenButton = btn;
            RectTransform btnRt = btn.GetComponent<RectTransform>();
            btnRt.anchorMin = new Vector2(0.5f, 0f);
            btnRt.anchorMax = new Vector2(0.5f, 0f);
            btnRt.pivot = new Vector2(0.5f, 0f);
            btnRt.sizeDelta = new Vector2(58f, 22f);
            btnRt.anchoredPosition = new Vector2(0f, 2f);
            TextMeshProUGUI label = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
            {
                label.SetLocalizedText("トークン", "Token");
                label.fontSize = 11;
                label.color = Color.black;
            }

            // シールドカード行の下端をボタン分あける
            if (shieldCardsContent != null)
            {
                RectTransform rowRt = shieldCardsContent;
                rowRt.offsetMin = new Vector2(rowRt.offsetMin.x, 26f);
            }
        }

        testPlayShieldTokenButton.gameObject.SetActive(true);
        testPlayShieldTokenButton.onClick.RemoveAllListeners();
        testPlayShieldTokenButton.onClick.AddListener(() => onOpen());
    }

    public void SetTestPlayShieldTokenButtonInteractable(bool interactable)
    {
        if (testPlayShieldTokenButton != null)
        {
            testPlayShieldTokenButton.interactable = interactable;
        }
    }

    /// <summary>山札の上から最大 count 枚を取り出す（先頭＝一番上）。</summary>
    public List<int> TakeTopCardIds(int count)
    {
        List<int> taken = new List<int>();
        if (count <= 0 || deckList == null)
        {
            return taken;
        }

        int remain = count;
        while (remain > 0 && deckList.Count > 0)
        {
            while (deckList.Count > 0 && deckList[0] == OnlineDeckCountPaddingId)
            {
                deckList.RemoveAt(0);
            }

            if (deckList.Count == 0)
            {
                break;
            }

            taken.Add(deckList[0]);
            deckList.RemoveAt(0);
            remain--;
        }

        UpdateDeckAndTrashTexts();
        return taken;
    }

    /// <summary>互換エイリアス。<see cref="BindDiscardZoneCountClick"/> を使用してください。</summary>
    public void BindTrashAreaClick(Action onClick) => BindDiscardZoneCountClick(onClick);

    public void SetExBaseDisplay(int points)
    {
        if (exBaseDisplayText == null)
        {
            return;
        }

        if (deployedBase != null)
        {
            return;
        }

        exBaseDisplayText.gameObject.SetActive(true);
        exBaseDisplayText.text = $"EX Base:{points}";
    }

    public void SetDeployedBaseHeader(string text)
    {
        if (exBaseDisplayText == null)
        {
            return;
        }

        exBaseDisplayText.gameObject.SetActive(true);
        exBaseDisplayText.text = text;
    }

    /// <summary>ルール上の残りシールド枚数をシールドゾーン付近に表示する。</summary>
    public void SetShieldCountDisplay(int count)
    {
        if (shieldCountDisplayText == null)
        {
            return;
        }

        shieldCountDisplayText.gameObject.SetActive(true);
        shieldCountDisplayText.SetLocalizedText(
            $"{GameLocale.TKey("zone.shield")} ({Mathf.Max(0, count)})");
        shieldCountDisplayText.fontStyle = FontStyles.Bold;
        shieldCountDisplayText.color = new Color(1f, 0.95f, 0.55f, 1f);
    }

    /// <summary>EX ベース枠にベースカードを配置する。旧ベースは呼び出し側でトラッシュすること。</summary>
    public void AttachDeployedBaseCard(CardController baseCard)
    {
        deployedBase = baseCard;
        if (baseCard == null || baseSlotContent == null)
        {
            return;
        }

        baseCard.gameObject.SetActive(true);
        baseCard.transform.SetParent(baseSlotContent, false);
        RectTransform rt = baseCard.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(58f, 82f);
            rt.localScale = Vector3.one;
            // レスト中に再アタッチした場合も横倒しを維持
            if (baseCard.IsRestState)
            {
                baseCard.SetUnitRestVisual(true);
            }
            else
            {
                rt.localRotation = Quaternion.identity;
            }
        }

        if (exBaseDisplayText != null)
        {
            exBaseDisplayText.gameObject.SetActive(false);
        }
    }

    public void ClearDeployedBaseCard()
    {
        deployedBase = null;
        if (exBaseDisplayText != null)
        {
            exBaseDisplayText.gameObject.SetActive(true);
        }
    }

    private void BuildShieldPanel(GameObject parent, int width)
    {
        shieldPanelRoot = parent.CreateChildPanelCustom("PlayerShieldPanel", UIAnchor.TopLeft, width, 200);
        StretchVerticallyInParent(shieldPanelRoot.GetComponent<RectTransform>(), centerHorizontally: false, width: width);
        Image shieldBg = shieldPanelRoot.GetComponent<Image>();
        if (shieldBg != null)
        {
            // 回転フィールドでも視認できるよう少し濃くする
            shieldBg.color = new Color32(40, 52, 68, 200);
            shieldBg.raycastTarget = false;
        }

        // 親が 180° 回転すると RectMask2D が子を全て消すことがあるため使わない

        baseZoneLabelText = shieldPanelRoot.CreateChildTextCustom("BaseLabel", UIAnchor.TopCenter, width - 4, 18);
        baseZoneLabelText.SetLocalizedText(GameLocale.TKey("zone.base"));
        baseZoneLabelText.fontSize = 13;
        baseZoneLabelText.color = new Color(0.9f, 0.92f, 1f, 1f);
        baseZoneLabelText.raycastTarget = false;
        RectTransform baseLabelRt = baseZoneLabelText.GetComponent<RectTransform>();
        baseLabelRt.anchoredPosition = new Vector2(0f, -2f);

        // 旧表示互換（非表示）
        exBaseDisplayText = shieldPanelRoot.CreateChildTextCustom("ExBaseText", UIAnchor.TopCenter, width - 4, 16);
        exBaseDisplayText.text = string.Empty;
        exBaseDisplayText.gameObject.SetActive(false);

        const float baseHeaderHeight = 100f;
        GameObject baseSlot = shieldPanelRoot.CreateChildPanelCustom("BaseSlot", UIAnchor.TopCenter, width - 6, 72);
        baseSlotContent = baseSlot.GetComponent<RectTransform>();
        baseSlotContent.anchoredPosition = new Vector2(0f, -20f);
        Image baseSlotBg = baseSlot.GetComponent<Image>();
        if (baseSlotBg != null)
        {
            baseSlotBg.color = new Color32(255, 255, 255, 28);
        }

        shieldCountDisplayText = shieldPanelRoot.CreateChildTextCustom("ShieldCountText", UIAnchor.TopCenter, width - 4, 16);
        shieldCountDisplayText.SetLocalizedText($"{GameLocale.TKey("zone.shield")} (0)");
        shieldCountDisplayText.color = new Color(1f, 0.95f, 0.55f, 1f);
        shieldCountDisplayText.fontSize = 12;
        shieldCountDisplayText.fontStyle = FontStyles.Bold;
        shieldCountDisplayText.alignment = TextAlignmentOptions.Center;
        RectTransform shieldCountRt = shieldCountDisplayText.GetComponent<RectTransform>();
        shieldCountRt.anchoredPosition = new Vector2(0f, -94f);

        // 残り高さだけをシールド枠に使う（RectMask2D は回転親で消えるので付けない）
        GameObject shieldRow = shieldPanelRoot.CreateChildPanelCustom("ShieldCardsRow", UIAnchor.FullStretch, width, 0);
        RectTransform shieldRowRt = shieldRow.GetComponent<RectTransform>();
        shieldRowRt.anchorMin = new Vector2(0f, 0f);
        shieldRowRt.anchorMax = new Vector2(1f, 1f);
        shieldRowRt.offsetMin = new Vector2(2f, 2f);
        shieldRowRt.offsetMax = new Vector2(-2f, -baseHeaderHeight);

        Image shieldRowBg = shieldRow.GetComponent<Image>();
        if (shieldRowBg != null)
        {
            shieldRowBg.color = new Color32(20, 60, 120, 100);
            shieldRowBg.raycastTarget = false;
        }

        shieldCardsContent = shieldRow.GetComponent<RectTransform>();
        shieldGrid = shieldRow.AddComponent<GridLayoutGroup>();
        shieldGrid.cellSize = new Vector2(54f, 34f);
        shieldGrid.spacing = new Vector2(0f, 3f);
        shieldGrid.padding = new RectOffset(4, 4, 2, 2);
        shieldGrid.childAlignment = TextAnchor.UpperCenter;
        shieldGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        shieldGrid.constraintCount = 1;
        shieldGrid.startAxis = GridLayoutGroup.Axis.Vertical;
        shieldGrid.startCorner = GridLayoutGroup.Corner.UpperLeft;
    }

    public void SetHandScrollRightPadding(int rightPadding)
    {
        if (ScrollPanel == null)
        {
            return;
        }

        ScrollRect scrollRect = ScrollPanel.GetComponent<ScrollRect>();
        if (scrollRect == null || scrollRect.content == null)
        {
            return;
        }

        GridLayoutGroup grid = scrollRect.content.GetComponent<GridLayoutGroup>();
        if (grid == null)
        {
            return;
        }

        int safePadding = Mathf.Max(0, rightPadding);
        grid.padding = new RectOffset(grid.padding.left, safePadding, grid.padding.top, grid.padding.bottom);
    }

    public void SetHandScrollRightMargin(float rightMargin)
    {
        if (ScrollPanel == null)
        {
            return;
        }

        RectTransform scrollRect = ScrollPanel.GetComponent<RectTransform>();
        if (scrollRect == null)
        {
            return;
        }

        float safeMargin = Mathf.Max(0f, rightMargin);
        Vector2 offsetMax = scrollRect.offsetMax;
        offsetMax.x = -safeMargin;
        scrollRect.offsetMax = offsetMax;
    }

    public float GetHandMinimumWidthForVisibleCards(int visibleCardCount)
    {
        if (visibleCardCount <= 0 || ScrollPanel == null)
        {
            return 0f;
        }

        ScrollRect scrollRect = ScrollPanel.GetComponent<ScrollRect>();
        if (scrollRect == null || scrollRect.content == null)
        {
            return 0f;
        }

        GridLayoutGroup grid = scrollRect.content.GetComponent<GridLayoutGroup>();
        if (grid == null)
        {
            return 0f;
        }

        float cellWidth = grid.cellSize.x;
        float spacingX = grid.spacing.x;
        return grid.padding.left + grid.padding.right + (cellWidth * visibleCardCount) + (spacingX * (visibleCardCount - 1));
    }

    public void AddExtraResourcePoints(int amount)
    {
        ExtraResourcePoints += amount;
        if (extraResourcePoint != null)
        {
            extraResourcePoint.text = ExtraResourcePoints.ToString();
        }

        // Exポイントの増加に応じてリソースレベルも増加させる
        AddResourcePoints(amount);
        Debug.Log($"Exリソースポイントが{amount}増加しました。現在のExポイント: {ExtraResourcePoints}");
    }

    // リソースレベルを取得する関数
    public int GetResourcePoints()
    {
        return resourceLevel;
    }

    // リソースレベルを代入してリセットする。デフォではレベルに応じたポイントをリセットするようにしています。
    // シンアスカやスレッタの効果の場合は引数に1を入れて呼び出す予定。
    public void RefreshResourcePoints()
    {
        resourcePoints = resourceLevel; // レベルに応じたポイントをリセット
        if (ResourcePointText != null)
        {
            ResourcePointText.text = resourcePoints.ToString();
        }

        if (ResourceText != null)
        {
            ResourceText.text = $"Resource:{resourcePoints}";
        }

        RebuildResourceTokenVisuals();
        Debug.Log("リソースポイントがリセットされました。");
    }

    public bool UseResourcePoints(int amount)
    {
        if (amount > resourcePoints)
        {
            Debug.LogWarning($"リソースポイントが足りません！カードのコスト: {amount}現在のポイント: {resourcePoints}");
            return false; // 使用失敗
        }

        // resourcePoints -= amount;
        // ResourcePointText.text = resourcePoints.ToString(); // リソースポイントテキストを更新
        // Debug.Log($"{amount}ポイント使用しました。残りのポイント: {resourcePoints}");
        return true;
    }

    public void UseResourcePointsWithoutCheck(int amount)
    {
        resourcePoints -= amount;
        if (ResourcePointText != null)
        {
            ResourcePointText.text = resourcePoints.ToString();
        }

        RebuildResourceTokenVisuals();
        Debug.Log($"{amount}ポイント使用しました。残りのポイント: {resourcePoints}");
    }

    public int returnResourcePoints() => resourcePoints;

    public void AddCardToTrash(int cardId)
    {
        if (cardId < 0)
        {
            return;
        }

        trashList.Add(cardId);
        UpdateDeckAndTrashTexts();
        OnCardAddedToTrash?.Invoke(cardId);
    }

    /// <summary>トラッシュから指定 ID のカードを1枚除去（同 ID が複数あれば先頭1枚）。</summary>
    public bool TryRemoveCardFromTrash(int cardId, out int removedCardId)
    {
        removedCardId = -1;
        if (cardId < 0)
        {
            return false;
        }

        int index = trashList.IndexOf(cardId);
        if (index < 0)
        {
            return false;
        }

        removedCardId = trashList[index];
        trashList.RemoveAt(index);
        UpdateDeckAndTrashTexts();
        return true;
    }

    /// <summary>トラッシュの指定位置のカードを1枚除去。</summary>
    public bool TryRemoveCardFromTrashAt(int index, out int removedCardId)
    {
        removedCardId = -1;
        if (index < 0 || index >= trashList.Count)
        {
            return false;
        }

        removedCardId = trashList[index];
        trashList.RemoveAt(index);
        UpdateDeckAndTrashTexts();
        return true;
    }

    public void AddCardToExile(int cardId)
    {
        if (cardId < 0)
        {
            return;
        }

        exileList.Add(cardId);
        UpdateDeckAndTrashTexts();
        OnCardAddedToExile?.Invoke(cardId);
    }

    /// <summary>除外ゾーンの指定位置のカードを1枚除去。</summary>
    public bool TryRemoveCardFromExileAt(int index, out int removedCardId)
    {
        removedCardId = -1;
        if (index < 0 || index >= exileList.Count)
        {
            return false;
        }

        removedCardId = exileList[index];
        exileList.RemoveAt(index);
        UpdateDeckAndTrashTexts();
        return true;
    }

    /// <summary>カードがトラッシュに追加されたとき（cardId）。プレイヤー側 AI 観測用。</summary>
    public event Action<int> OnCardAddedToTrash;

    /// <summary>カードが除外ゾーンに追加されたとき（cardId）。</summary>
    public event Action<int> OnCardAddedToExile;

    /// <summary>
    /// 外部のルールエンジンで確定したレベル/リソースを、このクラスの表示値へ同期する。
    /// </summary>
    public void ApplyExternalResourceState(int level, int resource, int exResource)
    {
        resourceLevel = Mathf.Max(0, level);
        resourcePoints = Mathf.Max(0, resource);
        ExtraResourcePoints = Mathf.Max(0, exResource);

        if (LvText != null)
        {
            LvText.text = $"LV:{resourceLevel}";
            LvText.color = Color.black;
        }

        if (ResourceText != null)
        {
            ResourceText.text = $"Resource:{resourcePoints}";
            ResourceText.color = Color.black;
        }

        if (ExResourceText != null)
        {
            ExResourceText.text = $"EX:{ExtraResourcePoints}";
            ExResourceText.color = Color.black;
        }

        if (ResourcePointText != null)
        {
            ResourcePointText.text = resourcePoints.ToString();
            ResourcePointText.color = Color.black;
        }

        if (LevelText != null)
        {
            LevelText.text = $"LV:{resourceLevel}";
            LevelText.color = Color.black;
        }

        if (extraResourcePoint != null)
        {
            extraResourcePoint.text = ExtraResourcePoints.ToString();
            extraResourcePoint.color = Color.black;
        }

        if (fieldPanel != null)
        {
            float z = fieldPanel.transform.eulerAngles.z;
            _resourceBoardIsFlipped = Mathf.Abs(Mathf.DeltaAngle(z, 180f)) < 45f;
        }

        RebuildResourceTokenVisuals();
    }

    // 現在の残り枚数を知りたい場合に便利
    public int GetRemainingCount() => deckList.Count;
    public int GetTrashCount() => trashList.Count;

    public int GetExileCount() => exileList.Count;

    // リソース関数もここに追加していく予定

    private void BuildResourceAndExStripContents(GameObject strip, int height)
    {
        Image stripBg = strip.GetComponent<Image>();
        if (stripBg != null)
        {
            stripBg.color = new Color32(28, 72, 52, 230);
            stripBg.raycastTarget = false;
        }

        GameObject resourceZone = strip.CreateChildPanelCustom("ResourceZone", UIAnchor.TopLeft, 340, height - 4);
        resourceZoneRoot = resourceZone;
        RectTransform resourceZoneRt = resourceZone.GetComponent<RectTransform>();
        resourceZoneRt.anchorMin = new Vector2(0f, 0f);
        resourceZoneRt.anchorMax = new Vector2(0.72f, 1f);
        resourceZoneRt.offsetMin = new Vector2(4f, 4f);
        resourceZoneRt.offsetMax = new Vector2(-2f, -4f);
        Image resourceZoneBg = resourceZone.GetComponent<Image>();
        if (resourceZoneBg != null)
        {
            resourceZoneBg.color = new Color32(255, 255, 255, 18);
            resourceZoneBg.raycastTarget = false;
        }

        resourceZoneHeaderText = resourceZone.CreateChildTextCustom("ResourceHeader", UIAnchor.TopLeft, 200, 16);
        resourceZoneHeaderText.SetLocalizedText($"{GameLocale.TKey("zone.resource")} (0/0)");
        resourceZoneHeaderText.fontSize = 12;
        resourceZoneHeaderText.color = new Color(0.9f, 1f, 0.9f, 1f);
        resourceZoneHeaderText.alignment = TextAlignmentOptions.MidlineLeft;
        resourceZoneHeaderText.raycastTarget = false;
        RectTransform resourceHeaderRt = resourceZoneHeaderText.GetComponent<RectTransform>();
        resourceHeaderRt.anchoredPosition = new Vector2(6f, -2f);

        // 互換テキスト（非表示）
        LvText = resourceZone.CreateChildTextCustom("LevelText", UIAnchor.TopRight, 60, 16);
        LvText.gameObject.SetActive(false);
        ResourceText = resourceZone.CreateChildTextCustom("ResourceText", UIAnchor.TopRight, 60, 16);
        ResourceText.gameObject.SetActive(false);

        resourceTokensContent = CreateHorizontalTokenRow(resourceZone.transform, "ResourceTokens", 18f);

        GameObject exZone = strip.CreateChildPanelCustom("ExZone", UIAnchor.TopRight, 120, height - 4);
        exZoneRoot = exZone;
        RectTransform exZoneRt = exZone.GetComponent<RectTransform>();
        exZoneRt.anchorMin = new Vector2(0.72f, 0f);
        exZoneRt.anchorMax = new Vector2(1f, 1f);
        exZoneRt.offsetMin = new Vector2(2f, 4f);
        exZoneRt.offsetMax = new Vector2(-4f, -4f);
        Image exZoneBg = exZone.GetComponent<Image>();
        if (exZoneBg != null)
        {
            exZoneBg.color = new Color32(90, 70, 28, 120);
            exZoneBg.raycastTarget = false;
        }

        exZoneHeaderText = exZone.CreateChildTextCustom("ExHeader", UIAnchor.TopLeft, 100, 16);
        exZoneHeaderText.SetLocalizedText($"{GameLocale.TKey("zone.ex")} (0)");
        exZoneHeaderText.fontSize = 12;
        exZoneHeaderText.color = new Color(1f, 0.95f, 0.75f, 1f);
        exZoneHeaderText.alignment = TextAlignmentOptions.MidlineLeft;
        exZoneHeaderText.raycastTarget = false;
        RectTransform exHeaderRt = exZoneHeaderText.GetComponent<RectTransform>();
        exHeaderRt.anchoredPosition = new Vector2(6f, -2f);

        ExResourceText = exZone.CreateChildTextCustom("ExResourceText", UIAnchor.TopRight, 40, 16);
        ExResourceText.gameObject.SetActive(false);

        exTokensContent = CreateHorizontalTokenRow(exZone.transform, "ExTokens", 18f);
    }

    private static RectTransform CreateHorizontalTokenRow(Transform parent, string name, float topInset)
    {
        GameObject row = new GameObject(name, typeof(RectTransform));
        row.transform.SetParent(parent, false);
        RectTransform rowRt = row.GetComponent<RectTransform>();
        rowRt.anchorMin = new Vector2(0f, 0f);
        rowRt.anchorMax = new Vector2(1f, 1f);
        rowRt.offsetMin = new Vector2(4f, 2f);
        rowRt.offsetMax = new Vector2(-4f, -topInset);

        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.spacing = 4f;
        layout.padding = new RectOffset(2, 2, 0, 0);
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        return rowRt;
    }

    private void RebuildResourceTokenVisuals()
    {
        // ルール値: available = resourcePoints / 全体(通常) = TotalLevel - EX
        int totalLevel = Mathf.Max(0, resourceLevel);
        int exCount = Mathf.Clamp(ExtraResourcePoints, 0, totalLevel);
        int normalCount = Mathf.Max(0, totalLevel - exCount);
        // 利用可能数。使った分 = normalCount - activeNormal を横向きにする
        int activeNormal = Mathf.Clamp(resourcePoints, 0, normalCount);
        int restedNormal = Mathf.Max(0, normalCount - activeNormal);

        if (resourceZoneHeaderText != null)
        {
            resourceZoneHeaderText.SetLocalizedText(
                $"{GameLocale.TKey("zone.resource")} ({activeNormal}/{normalCount})");
        }

        if (exZoneHeaderText != null)
        {
            exZoneHeaderText.SetLocalizedText(
                $"{GameLocale.TKey("zone.ex")} ({exCount})");
        }

        EnsureTokenObjectCount(
            resourceTokensContent,
            resourceTokenObjects,
            normalCount,
            new Color32(42, 88, 140, 255),
            new Color32(180, 210, 255, 255));

        for (int i = 0; i < resourceTokenObjects.Count; i++)
        {
            // 相手盤は描画順が反転して見えるため、使った分を手前側に寄せて横にする
            bool rested;
            if (_resourceBoardIsFlipped)
            {
                // 先頭側をレスト（使用済）にする
                rested = i < restedNormal;
            }
            else
            {
                // 先頭からアクティブ、以降レスト
                rested = i >= activeNormal;
            }

            SetResourceTokenRested(resourceTokenObjects[i], rested);
            BindResourceTokenClick(resourceTokenObjects[i], rested);
        }

        EnsureTokenObjectCount(
            exTokensContent,
            exTokenObjects,
            exCount,
            new Color32(150, 110, 40, 255),
            new Color32(255, 220, 140, 255));

        for (int i = 0; i < exTokenObjects.Count; i++)
        {
            SetResourceTokenRested(exTokenObjects[i], false);
        }

        if (testPlayResourceCountText != null)
        {
            testPlayResourceCountText.text = normalCount.ToString();
        }

        if (testPlayExCountText != null)
        {
            testPlayExCountText.text = exCount.ToString();
        }

        if (resourceTokensContent != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(resourceTokensContent);
        }

        if (exTokensContent != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(exTokensContent);
        }
    }

    private void EnsureTokenObjectCount(
        RectTransform content,
        List<GameObject> pool,
        int count,
        Color32 faceColor,
        Color32 borderColor)
    {
        if (content == null || pool == null)
        {
            return;
        }

        // 破棄済み参照を掃除
        for (int i = pool.Count - 1; i >= 0; i--)
        {
            if (pool[i] == null)
            {
                pool.RemoveAt(i);
            }
        }

        while (pool.Count < count)
        {
            GameObject created = CreateResourceTokenCard(content, faceColor, borderColor);
            PlayResourceTokenAppear(created, (pool.Count) * 0.05f);
            pool.Add(created);
        }

        while (pool.Count > count)
        {
            int last = pool.Count - 1;
            GameObject go = pool[last];
            pool.RemoveAt(last);
            if (go != null)
            {
                UnityEngine.Object.Destroy(go);
            }
        }

        for (int i = 0; i < pool.Count; i++)
        {
            GameObject go = pool[i];
            if (go == null)
            {
                pool[i] = CreateResourceTokenCard(content, faceColor, borderColor);
                go = pool[i];
                PlayResourceTokenAppear(go, 0f);
            }

            go.SetActive(true);
            if (go.transform.parent != content)
            {
                go.transform.SetParent(content, false);
            }

            go.transform.SetSiblingIndex(i);
        }
    }

    private static void PlayResourceTokenAppear(GameObject token, float delaySeconds)
    {
        if (token == null)
        {
            return;
        }

        ResourceTokenAppearAnim anim = token.GetComponent<ResourceTokenAppearAnim>();
        if (anim == null)
        {
            anim = token.AddComponent<ResourceTokenAppearAnim>();
        }

        anim.Play(delaySeconds);
    }

    private GameObject CreateResourceTokenCard(Transform parent, Color32 faceColor, Color32 borderColor)
    {
        // Layout 用ルートは回転させない（180° 盤面でも LayoutGroup が向きを潰さない）
        GameObject token = new GameObject("ResourceToken", typeof(RectTransform), typeof(LayoutElement));
        token.transform.SetParent(parent, false);

        RectTransform rt = token.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(ResourceTokenWidth, ResourceTokenHeight);

        LayoutElement layout = token.GetComponent<LayoutElement>();
        layout.preferredWidth = ResourceTokenWidth;
        layout.preferredHeight = ResourceTokenHeight;
        layout.minWidth = ResourceTokenWidth;
        layout.minHeight = ResourceTokenHeight;
        layout.flexibleWidth = 0f;
        layout.flexibleHeight = 0f;

        GameObject visual = new GameObject("Visual", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        visual.transform.SetParent(token.transform, false);
        RectTransform visualRt = visual.GetComponent<RectTransform>();
        visualRt.anchorMin = new Vector2(0.5f, 0.5f);
        visualRt.anchorMax = new Vector2(0.5f, 0.5f);
        visualRt.pivot = new Vector2(0.5f, 0.5f);
        visualRt.sizeDelta = new Vector2(ResourceTokenWidth, ResourceTokenHeight);
        visualRt.anchoredPosition = Vector2.zero;
        Image face = visual.GetComponent<Image>();
        face.color = faceColor;
        face.raycastTarget = false;

        GameObject border = new GameObject("Border", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        border.transform.SetParent(visual.transform, false);
        RectTransform borderRt = border.GetComponent<RectTransform>();
        borderRt.anchorMin = Vector2.zero;
        borderRt.anchorMax = Vector2.one;
        borderRt.offsetMin = new Vector2(1f, 1f);
        borderRt.offsetMax = new Vector2(-1f, -1f);
        Image borderImage = border.GetComponent<Image>();
        borderImage.color = borderColor;
        borderImage.raycastTarget = false;

        GameObject inner = new GameObject("Inner", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        inner.transform.SetParent(border.transform, false);
        RectTransform innerRt = inner.GetComponent<RectTransform>();
        innerRt.anchorMin = Vector2.zero;
        innerRt.anchorMax = Vector2.one;
        innerRt.offsetMin = new Vector2(2f, 2f);
        innerRt.offsetMax = new Vector2(-2f, -2f);
        Image innerImage = inner.GetComponent<Image>();
        innerImage.color = faceColor;
        innerImage.raycastTarget = false;

        Image hit = token.GetComponent<Image>();
        if (hit == null)
        {
            hit = token.AddComponent<Image>();
        }

        hit.color = new Color(1f, 1f, 1f, 0.01f);
        hit.raycastTarget = true;

        Button btn = token.GetComponent<Button>();
        if (btn == null)
        {
            btn = token.AddComponent<Button>();
        }

        btn.targetGraphic = hit;
        btn.transition = Selectable.Transition.None;

        return token;
    }

    private void BindResourceTokenClick(GameObject token, bool rested)
    {
        if (token == null)
        {
            return;
        }

        Button btn = token.GetComponent<Button>();
        if (btn == null)
        {
            return;
        }

        btn.onClick.RemoveAllListeners();
        if (testPlayResourceTokenClickHandler == null)
        {
            return;
        }

        bool capturedRested = rested;
        btn.onClick.AddListener(() => testPlayResourceTokenClickHandler.Invoke(capturedRested));
    }

    private void SetResourceTokenRested(GameObject token, bool rested)
    {
        if (token == null)
        {
            return;
        }

        RectTransform rt = token.GetComponent<RectTransform>();
        LayoutElement layout = token.GetComponent<LayoutElement>();
        Transform visual = token.transform.Find("Visual");
        RectTransform visualRt = visual != null ? visual.GetComponent<RectTransform>() : null;
        Image faceImage = visual != null ? visual.GetComponent<Image>() : null;

        float layoutW = rested ? ResourceTokenHeight : ResourceTokenWidth;
        float layoutH = rested ? ResourceTokenWidth : ResourceTokenHeight;

        if (rt != null)
        {
            rt.localRotation = Quaternion.identity;
            rt.sizeDelta = new Vector2(layoutW, layoutH);
        }

        if (layout != null)
        {
            layout.preferredWidth = layoutW;
            layout.preferredHeight = layoutH;
            layout.minWidth = layoutW;
            layout.minHeight = layoutH;
        }

        if (visualRt != null)
        {
            visualRt.sizeDelta = new Vector2(ResourceTokenWidth, ResourceTokenHeight);
            visualRt.anchoredPosition = Vector2.zero;
            // ワールド角で固定（親 180° でも画面上の縦/横が確実に切り替わる）
            float worldZ;
            if (_resourceBoardIsFlipped)
            {
                worldZ = rested ? 90f : 180f;
            }
            else
            {
                worldZ = rested ? ResourceTokenRestAngleZ : 0f;
            }

            visualRt.rotation = Quaternion.Euler(0f, 0f, worldZ);
        }

        if (faceImage != null)
        {
            bool isExToken = token.transform.parent != null
                && token.transform.parent.name == "ExTokens";
            Color32 activeColor = isExToken
                ? new Color32(150, 110, 40, 255)
                : new Color32(42, 88, 140, 255);
            Color32 restedColor = isExToken
                ? new Color32(90, 70, 40, 255)
                : new Color32(90, 90, 110, 255);
            faceImage.color = rested ? restedColor : activeColor;
        }
    }

    private void CreateDeckAndTrashArea(GameObject deckAndTrashPanel, int width)
    {
        Image columnBg = deckAndTrashPanel.GetComponent<Image>();
        if (columnBg != null)
        {
            columnBg.color = new Color32(40, 52, 68, 110);
        }

        // 上1/3 デッキ・中1/3 除外・下1/3 トラッシュ
        deckObjectPanel = deckAndTrashPanel.CreateChildPanelCustom("DeckObjectPanel", UIAnchor.FullStretch, width - 4, 0);
        RectTransform deckRt = deckObjectPanel.GetComponent<RectTransform>();
        deckRt.anchorMin = new Vector2(0f, 0.67f);
        deckRt.anchorMax = new Vector2(1f, 1f);
        deckRt.offsetMin = new Vector2(2f, 2f);
        deckRt.offsetMax = new Vector2(-2f, -2f);
        Image deckBg = deckObjectPanel.GetComponent<Image>();
        if (deckBg != null)
        {
            deckBg.color = new Color32(255, 255, 255, 20);
            deckBg.raycastTarget = false;
        }

        deckZoneLabelText = deckObjectPanel.CreateChildTextCustom("DeckLabel", UIAnchor.TopCenter, width - 8, 16);
        deckZoneLabelText.SetLocalizedText(GameLocale.TKey("zone.deck"));
        deckZoneLabelText.fontSize = 12;
        deckZoneLabelText.color = new Color(0.9f, 0.92f, 1f, 1f);

        GameObject deckCardPlaceholder = deckObjectPanel.CreateChildPanelCustom("DeckCardPlaceholder", UIAnchor.TopCenter, 40, 54);
        RectTransform deckCardRt = deckCardPlaceholder.GetComponent<RectTransform>();
        deckCardRt.anchoredPosition = new Vector2(0f, -16f);
        Image deckCardImage = deckCardPlaceholder.GetComponent<Image>();
        if (deckCardImage != null)
        {
            deckCardImage.color = new Color32(35, 55, 95, 255);
        }

        deckCountText = deckObjectPanel.CreateChildTextCustom("DeckCountText", UIAnchor.BottomCenter, width - 8, 16);
        deckCountText.text = "0";
        deckCountText.fontSize = 14;
        deckCountText.color = Color.white;
        deckCountText.raycastTarget = false;

        // デッキ領域タップ用（TestPlay 等）。通常時はリスナーなし。
        if (deckBg != null)
        {
            deckBg.raycastTarget = true;
        }

        deckAreaButton = deckObjectPanel.GetComponent<Button>();
        if (deckAreaButton == null)
        {
            deckAreaButton = deckObjectPanel.AddComponent<Button>();
        }

        deckAreaButton.targetGraphic = deckBg;
        deckAreaButton.transition = Selectable.Transition.None;
        ApplyTextButtonColors(deckAreaButton);

        exileAreaPanel = deckAndTrashPanel.CreateChildPanelCustom("ExileZonePanel", UIAnchor.FullStretch, width - 4, 0);
        RectTransform exileRt = exileAreaPanel.GetComponent<RectTransform>();
        exileRt.anchorMin = new Vector2(0f, 0.34f);
        exileRt.anchorMax = new Vector2(1f, 0.67f);
        exileRt.offsetMin = new Vector2(2f, 2f);
        exileRt.offsetMax = new Vector2(-2f, -2f);
        Image exileBg = exileAreaPanel.GetComponent<Image>();
        if (exileBg != null)
        {
            exileBg.color = new Color32(70, 50, 95, 120);
        }

        exileZoneLabelText = exileAreaPanel.CreateChildTextCustom("ExileZoneLabel", UIAnchor.TopCenter, width - 8, 16);
        exileZoneLabelText.SetLocalizedText(GameLocale.TKey("zone.exile"));
        exileZoneLabelText.fontSize = 12;
        exileZoneLabelText.color = new Color(0.9f, 0.85f, 1f, 1f);
        exileZoneLabelText.raycastTarget = true;

        exileZoneCountText = exileAreaPanel.CreateChildTextCustom("ExileZoneCountText", UIAnchor.BottomCenter, width - 8, 18);
        exileZoneCountText.text = "0";
        exileZoneCountText.fontSize = 14;
        exileZoneCountText.color = Color.white;
        exileZoneCountText.raycastTarget = true;
        exileZoneCountButton = exileZoneCountText.gameObject.GetComponent<Button>();
        if (exileZoneCountButton == null)
        {
            exileZoneCountButton = exileZoneCountText.gameObject.AddComponent<Button>();
        }

        exileZoneCountButton.targetGraphic = exileZoneCountText;
        ApplyTextButtonColors(exileZoneCountButton);

        trashAreaPanel = deckAndTrashPanel.CreateChildPanelCustom("TrashZonePanel", UIAnchor.FullStretch, width - 4, 0);
        RectTransform trashRt = trashAreaPanel.GetComponent<RectTransform>();
        trashRt.anchorMin = new Vector2(0f, 0f);
        trashRt.anchorMax = new Vector2(1f, 0.34f);
        trashRt.offsetMin = new Vector2(2f, 2f);
        trashRt.offsetMax = new Vector2(-2f, -2f);
        Image trashBg = trashAreaPanel.GetComponent<Image>();
        if (trashBg != null)
        {
            trashBg.color = new Color32(60, 45, 45, 140);
        }

        discardZoneLabelText = trashAreaPanel.CreateChildTextCustom("TrashZoneLabel", UIAnchor.TopCenter, width - 8, 16);
        discardZoneLabelText.SetLocalizedText(GameLocale.TKey("zone.trash"));
        discardZoneLabelText.fontSize = 12;
        discardZoneLabelText.color = new Color(1f, 0.9f, 0.9f, 1f);
        discardZoneLabelText.raycastTarget = true;
        discardZoneToggleButton = discardZoneLabelText.gameObject.GetComponent<Button>();
        if (discardZoneToggleButton == null)
        {
            discardZoneToggleButton = discardZoneLabelText.gameObject.AddComponent<Button>();
        }

        discardZoneToggleButton.targetGraphic = discardZoneLabelText;
        ApplyTextButtonColors(discardZoneToggleButton);

        discardZoneCountText = trashAreaPanel.CreateChildTextCustom("TrashZoneCountText", UIAnchor.BottomCenter, width - 8, 18);
        discardZoneCountText.text = "0";
        discardZoneCountText.fontSize = 14;
        discardZoneCountText.color = Color.white;
        discardZoneCountText.raycastTarget = true;
        discardZoneCountButton = discardZoneCountText.gameObject.GetComponent<Button>();
        if (discardZoneCountButton == null)
        {
            discardZoneCountButton = discardZoneCountText.gameObject.AddComponent<Button>();
        }

        discardZoneCountButton.targetGraphic = discardZoneCountText;
        ApplyTextButtonColors(discardZoneCountButton);
        UpdateDeckAndDiscardZoneTexts();
    }

    private void BuildHandCountArea(int headerHeight)
    {
        if (HandPanel == null)
        {
            return;
        }

        GameObject handHeader = HandPanel.CreateChildPanelTop("HandCountHeader", headerHeight, UIAnchor.TopStretch);
        Image headerBg = handHeader.GetComponent<Image>();
        if (headerBg != null)
        {
            headerBg.color = new Color32(245, 245, 250, 230);
            headerBg.raycastTarget = false;
        }

        handCountText = handHeader.CreateChildTextCustom("HandCountText", UIAnchor.TopLeft, 200, headerHeight);
        handCountText.GetComponent<RectTransform>().SetFullSize();
        handCountText.SetLocalizedText($"{GameLocale.TKey("zone.hand")} (0)");
        handCountText.color = Color.black;
        handCountText.fontSize = 16;
        handCountText.fontStyle = FontStyles.Bold;
        handCountText.alignment = TextAlignmentOptions.MidlineLeft;
        handCountText.margin = new Vector4(12f, 0f, 0f, 0f);
        handCountText.raycastTarget = false;
        handCountText.transform.SetAsLastSibling();
    }

    /// <summary>手札ゾーン内のカード枚数表示を、実際の手札 UI 枚数に合わせて更新する。</summary>
    public void RefreshHandCountDisplay()
    {
        RefreshHandCountDisplay(CountHandZoneCards());
    }

    /// <summary>指定枚数で手札ヘッダーを更新する（破棄待ちカードを含めない集計結果を渡す）。</summary>
    public void RefreshHandCountDisplay(int displayedCount)
    {
        if (handCountText == null)
        {
            return;
        }

        int count = Mathf.Max(0, displayedCount);
        handCountText.SetLocalizedText($"{GameLocale.TKey("zone.hand")} ({count})");
        handCountText.color = Color.black;
    }

    /// <summary>
    /// オンライン相手手札ミラー：合計枚数に合わせて伏せカードを増減する。
    /// 既に表向き／既知の手札カードはそのまま残し、不足分だけプレースホルダを足す。
    /// </summary>
    public void SetOnlineOpponentHandTotalCount(int totalCount, GameObject cardPrefab)
    {
        totalCount = Mathf.Max(0, totalCount);
        if (HandScrollContent == null || cardPrefab == null)
        {
            RefreshHandCountDisplay();
            return;
        }

        List<CardController> known = new List<CardController>();
        List<CardController> placeholders = new List<CardController>();
        CollectHandControllers(known, placeholders);

        // 既知枚数が目標を超える場合は余剰プレースホルダ優先で削り、それでも超過なら末尾の既知を削除
        while (known.Count + placeholders.Count > totalCount && placeholders.Count > 0)
        {
            CardController remove = placeholders[placeholders.Count - 1];
            placeholders.RemoveAt(placeholders.Count - 1);
            if (remove != null)
            {
                UnityEngine.Object.Destroy(remove.gameObject);
            }
        }

        while (known.Count > totalCount)
        {
            CardController remove = known[known.Count - 1];
            known.RemoveAt(known.Count - 1);
            if (remove != null)
            {
                UnityEngine.Object.Destroy(remove.gameObject);
            }
        }

        int needPlaceholders = totalCount - known.Count;
        while (placeholders.Count > needPlaceholders)
        {
            CardController remove = placeholders[placeholders.Count - 1];
            placeholders.RemoveAt(placeholders.Count - 1);
            if (remove != null)
            {
                UnityEngine.Object.Destroy(remove.gameObject);
            }
        }

        while (placeholders.Count < needPlaceholders)
        {
            GameObject go = UnityEngine.Object.Instantiate(cardPrefab, HandScrollContent);
            CardController cc = go.GetComponent<CardController>();
            if (cc == null)
            {
                UnityEngine.Object.Destroy(go);
                break;
            }

            cc.ConfigureAsOnlineOpponentHandPlaceholder();
            ApplyHandZoneLayoutToCard(cc);
            placeholders.Add(cc);
        }

        RefreshHandCountDisplay();
    }

    /// <summary>伏せプレースホルダ1枚を公開カードへ差し替える（無い場合は新規追加）。</summary>
    public CardController PromoteOrAddOnlineOpponentHandCard(
        CardData data,
        GameObject cardPrefab,
        System.Action<CardController> onClick,
        bool revealFace)
    {
        if (data == null || HandScrollContent == null || cardPrefab == null)
        {
            return null;
        }

        List<CardController> known = new List<CardController>();
        List<CardController> placeholders = new List<CardController>();
        CollectHandControllers(known, placeholders);

        CardController target;
        if (placeholders.Count > 0)
        {
            target = placeholders[0];
            target.ConvertOnlineOpponentHandPlaceholderToKnownCard(data, onClick);
        }
        else
        {
            GameObject go = UnityEngine.Object.Instantiate(cardPrefab, HandScrollContent);
            target = go.GetComponent<CardController>();
            if (target == null)
            {
                UnityEngine.Object.Destroy(go);
                return null;
            }

            target.SetUp(data, onClick);
        }

        if (revealFace)
        {
            target.RevealShieldFace();
        }
        else
        {
            target.SetShieldFaceHidden(true);
        }

        ApplyHandZoneLayoutToCard(target);
        RefreshHandCountDisplay();
        return target;
    }

    private void CollectHandControllers(List<CardController> known, List<CardController> placeholders)
    {
        known.Clear();
        placeholders.Clear();
        if (HandScrollContent == null)
        {
            return;
        }

        for (int i = 0; i < HandScrollContent.childCount; i++)
        {
            CardController cc = HandScrollContent.GetChild(i).GetComponent<CardController>();
            if (cc == null)
            {
                continue;
            }

            if (cc.IsOnlineOpponentHandPlaceholder)
            {
                placeholders.Add(cc);
            }
            else
            {
                known.Add(cc);
            }
        }
    }

    /// <summary>手札スクロール内の生存カード枚数（オンライン伏せトークン含む。Destroy 済みは除く）。</summary>
    public int CountHandZoneCards()
    {
        if (HandScrollContent == null)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < HandScrollContent.childCount; i++)
        {
            CardController cc = HandScrollContent.GetChild(i).GetComponent<CardController>();
            if (cc == null)
            {
                continue;
            }

            count++;
        }

        return count;
    }

    /// <summary>シールドゾーン等から手札へ移したカードを手札グリッドに合わせる。</summary>
    public void ApplyHandZoneLayoutToCard(CardController card)
    {
        if (card == null || ScrollPanel == null)
        {
            return;
        }

        RectTransform cardRect = card.GetComponent<RectTransform>();
        if (cardRect == null)
        {
            return;
        }

        ScrollRect scrollRect = ScrollPanel.GetComponent<ScrollRect>();
        GridLayoutGroup grid = scrollRect != null && scrollRect.content != null
            ? scrollRect.content.GetComponent<GridLayoutGroup>()
            : null;
        cardRect.localScale = Vector3.one;
        if (grid != null)
        {
            cardRect.sizeDelta = grid.cellSize;
        }

        LayoutElement layoutElement = card.GetComponent<LayoutElement>();
        if (layoutElement == null)
        {
            layoutElement = card.gameObject.AddComponent<LayoutElement>();
        }

        layoutElement.ignoreLayout = false;
    }

    private static void ApplyTextButtonColors(Button button)
    {
        if (button == null)
        {
            return;
        }

        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.85f, 0.9f, 1f, 1f);
        colors.pressedColor = new Color(0.75f, 0.82f, 0.95f, 1f);
        colors.selectedColor = colors.highlightedColor;
        button.colors = colors;
    }

    private void UpdateDeckAndDiscardZoneTexts()
    {
        if (discardZoneLabelText != null)
        {
            discardZoneLabelText.SetLocalizedText(GameLocale.TKey("zone.trash"));
            discardZoneLabelText.color = new Color(1f, 0.9f, 0.9f, 1f);
        }

        if (discardZoneCountText != null)
        {
            discardZoneCountText.text = trashList.Count.ToString();
            discardZoneCountText.color = Color.white;
            GameLocale.ApplyFont(discardZoneCountText);
        }

        if (exileZoneLabelText != null)
        {
            exileZoneLabelText.SetLocalizedText(GameLocale.TKey("zone.exile"));
            exileZoneLabelText.color = new Color(0.9f, 0.85f, 1f, 1f);
        }

        if (exileZoneCountText != null)
        {
            exileZoneCountText.text = exileList.Count.ToString();
            exileZoneCountText.color = Color.white;
            GameLocale.ApplyFont(exileZoneCountText);
        }
    }

    private void UpdateDeckAndTrashTexts()
    {
        if (deckCountText != null)
        {
            deckCountText.text = deckList.Count.ToString();
            // デッキ帯は暗めの背景のため白字で視認性を確保
            deckCountText.color = Color.white;
            GameLocale.ApplyFont(deckCountText);
        }

        UpdateDeckAndDiscardZoneTexts();
    }
}