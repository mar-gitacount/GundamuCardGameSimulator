using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.Linq;
using TMPro;
using System.IO;
using System;
using System.Globalization;
using System.Threading.Tasks;

public class DeckSettinObject : MonoBehaviour
{
    public static DeckSettinObject Instance;

    /// <summary>TestPlay 相手選択中に一覧のデッキが押された。</summary>
    public event Action<DeckSaveData, DeckStorageEntry, string> TestPlayOpponentDeckChosen;
    public bool isDeckEditing;
    private Dictionary<int, int> cardData = new Dictionary<int, int>();
    [SerializeField] private GameObject DeckEditNowpanel;
    // テキストフィールド
    [SerializeField] private TMP_InputField DeckTitleInputField;

    [SerializeField] private Canvas MainCanvas;


    [SerializeField] private TMP_Text NewDeckText;

    // エネミーデッキのカードデータを保存するためのフィールド
    private Dictionary<int, int> enemyCardData = new Dictionary<int, int>();
    

    // 編集中のデッキの文字列
    public string deckPathName;
    // ?デッキデータプレハブ
    [SerializeField] private GameObject DeckDataPrefab;
    public GameObject DeckImagePrefab;  
    [SerializeField] private TextMeshProUGUI CardCountText;
    [SerializeField] private GameObject DeckListPanel;
    [SerializeField] private GameObject DeckinfoPanel;

    private const string DeckTotalCountLabelName = "DeckTotalCountLabel";
    private const string DeckCardCountBadgeName = "DeckCardCountBadge";
    private const string DeckThumbnailFrameName = "ThumbnailFrame";
    private const string DeckListCreatedCountLabelName = "DeckListCreatedCountLabel";
    private const float HomeBoardDesignWidth = 480f;
    private const float HomeBoardDesignHeight = 800f;
    private const float DeckListCellWidth = 140f;
    /// <summary>
    /// サムネ132 + タイトル36 + 日付18 + 曜日18 + VLG余白/間隔 ≈ 232。
    /// 旧228だとセルからはみ出し、末尾行がスクロール範囲外になる。
    /// </summary>
    private const float DeckListCellHeight = 240f;
    private const float DeckListContentBottomExtra = 48f;
    private TextMeshProUGUI _deckTotalCountLabel;
    private GameObject _deckTotalCountLabelRoot;
    private TextMeshProUGUI _deckListCreatedCountLabel;
    private GameObject _deckListCreatedCountLabelRoot;
    /// <summary>一覧再読込の世代。古いコルーチンを無効化するために使う。</summary>
    private int _deckListLoadGeneration;
    private static Sprite _deckCardCountBadgeCircleSprite;
    private static Sprite _uiWhiteSprite;
    private Vector2 _lastHomeBoardParentSize;
    /// <summary>編集中デッキの代表サムネカード ID。0 は未設定（先頭にフォールバック）。</summary>
    private int _thumbnailCardId;
    /// <summary>編集開始時のデッキ内容。Cancel 時の差分判定・復元に使う。</summary>
    private Dictionary<int, int> _editBaselineCards = new Dictionary<int, int>();
    private string _editBaselineTitle = string.Empty;
    private int _editBaselineThumbnailId;
    private string _editBaselinePath = string.Empty;
    // バトルキャンバス
    [SerializeField] private Canvas BattleCanvas;
    // !デッキデータを保存するクラス
    // private DeckSaveData data = JsonUtility.FromJson<DeckSaveData>(json);

    // !カードIDからカードデータを取得するためのテーブル（辞書）
    // private Dictionary<int, CardData> cardTable = Resources.LoadAll<CardData>("Data/Cards").ToDictionary(data => data.id);
    

    // バトルボタン押下時に、他のデッキをエネミーデッキに入れるためのフラグ。
    // デッキ一覧内のデッキを押下した際に、このフラグが立っている場合は、押下されたデッキをエネミーデッキに入れる。そうでない場合は、通常通り編集デッキに入れる。
    private bool BattoleStartFlag = false;

    public void CopyJsonFile()
    {
        deckPathName = "";
        SaveDeckToJson(cardData);
        // デッキリストを空にする
        
    }

    void Awake()
    {
        Instance = this;
        EnsurePlayerAuthServiceExists();
        StartCoroutine(InitializeDeckStorageAndShowListCoroutine());
    }

    private void EnsurePlayerAuthServiceExists()
    {
        if (PlayerAuthService.Instance != null)
        {
            return;
        }

        GameObject go = new GameObject("PlayerAuthService");
        go.AddComponent<PlayerAuthService>();
    }

    private IEnumerator InitializeDeckStorageAndShowListCoroutine()
    {
        Task initTask = PlayerAuthService.Instance.InitializeAsync();
        while (!initTask.IsCompleted)
        {
            yield return null;
        }

        yield return ShowFileListCoroutine();
    }

    public void RefreshDeckListFromStorage()
    {
        StartCoroutine(ShowFileListCoroutine());
    }

    /// <summary>
    /// 現在の一覧件数に合わせて、作成デッキ数表示とスクロール高さを動的更新する。
    /// </summary>
    /// <param name="createdDeckCount">表示件数。null なら現在の子数。</param>
    /// <param name="preciseMeasure">true なら子の実バウンドで高さ補正（完了時向け）。</param>
    public void RefreshDeckListLayoutDynamic(int? createdDeckCount = null, bool preciseMeasure = true)
    {
        if (DeckListPanel == null)
        {
            return;
        }

        EnsureDeckListAreaVisible();

        int count = createdDeckCount
            ?? CountActiveDeckListItems(DeckListPanel.GetComponent<RectTransform>());
        RefreshDeckListCreatedCountLabel(count);

        if (preciseMeasure)
        {
            RefreshDeckListScrollLayout(count);
        }
        else
        {
            EnsureDeckListScrollable();
            ApplyDeckListContentHeight(count);
        }
    }

    /// <summary>専用 ScrollView ごと一覧エリアを表示する（非表示時の高さ計測ミス防止）。</summary>
    private void EnsureDeckListAreaVisible()
    {
        if (DeckListPanel == null)
        {
            return;
        }

        Transform t = DeckListPanel.transform;
        if (t.parent != null
            && t.parent.name == DeckListScrollViewportName
            && t.parent.parent != null
            && t.parent.parent.name == DeckListScrollRootName)
        {
            t.parent.parent.gameObject.SetActive(true);
        }

        if (!DeckListPanel.activeSelf)
        {
            DeckListPanel.SetActive(true);
        }
    }

    public void OnGuestModeActivated()
    {
        deckPathName = string.Empty;
        ClearDeckList();
        RefreshDeckListFromStorage();
    }

    public void OnCloudStorageActivated()
    {
        if (!string.IsNullOrEmpty(deckPathName)
            && !CloudDeckStorageProvider.IsValidCloudKey(CloudDeckStorageProvider.ToCloudKey(deckPathName)))
        {
            deckPathName = string.Empty;
        }

        RefreshDeckListFromStorage();
    }

    private void LateUpdate()
    {
        CenterHomeBoardAndDeckinfoPanel(force: false);
    }
    public void ClearDeckList()
    {
        cardData.Clear();
        _thumbnailCardId = 0;
        deckPathName = string.Empty;
        ClearDeckListItems();
    }

    /// <summary>一覧セルだけ破棄する（編集中の cardData は触らない）。</summary>
    private void ClearDeckListItems()
    {
        if (DeckListPanel == null)
        {
            return;
        }

        Transform root = DeckListPanel.transform;
        for (int i = root.childCount - 1; i >= 0; i--)
        {
            Transform child = root.GetChild(i);
            if (child == null)
            {
                continue;
            }

            // DetachChildren だとシーンに残り、再表示で欠落・高さズレの原因になる
            child.SetParent(null, false);
            Destroy(child.gameObject);
        }
    }

    /// <summary>新規デッキ編集用に、選択中パスとカードを空にする（既存ファイルを上書きしない）。</summary>
    public void BeginNewEmptyDeck()
    {
        cardData.Clear();
        _thumbnailCardId = 0;
        deckPathName = string.Empty;
        _editBaselineCards = new Dictionary<int, int>();
        _editBaselineTitle = string.Empty;
        _editBaselineThumbnailId = 0;
        _editBaselinePath = string.Empty;
    }

    public Dictionary<int, int> LoadDeckReturn()
    {    
        Debug.Log($"デッキデータを返します。カードの種類数: {cardData.Count}");
        return cardData;
    }

    // 敵のデッキデータを返す関数
    public Dictionary<int, int> LoadEnemyDeckReturn()
    {
        return enemyCardData;
    }

    public bool HasSelectedPlayerDeck()
    {
        return cardData != null && cardData.Count > 0;
    }

    /// <summary>カード画像スプライト（デッキ UI 共用）。</summary>
    public static Sprite ResolveDeckCardSprite(int cardId)
    {
        return ResolveCardSprite(cardId);
    }

    /// <summary>現在選択中の自分デッキを TestPlay UI 用に写す。</summary>
    public TestPlayDeckPick CaptureCurrentPlayerDeckPick()
    {
        if (!HasSelectedPlayerDeck())
        {
            return null;
        }

        TestPlayDeckPick pick = new TestPlayDeckPick();
        pick.StorageKey = deckPathName;
        pick.Title = GetSelectedDeckDisplayName();
        pick.Cards = TestPlayDeckPick.CopyCards(cardData);
        pick.ThumbnailId = DeckStorageService.ResolveThumbnailId(pick.Cards, _thumbnailCardId);
        pick.Thumbnail = ResolveCardSprite(pick.ThumbnailId);
        pick.TotalCount = TestPlayDeckPick.CountCards(pick.Cards);
        return pick;
    }

    /// <summary>選択スナップショットを反映して TestPlay を開始する。</summary>
    public void ApplyTestPlayDecksAndStart(TestPlayDeckPick player, TestPlayDeckPick enemy)
    {
        if (player == null || enemy == null)
        {
            Debug.LogWarning("[TestPlay] 自分と相手のデッキが揃っていません。");
            return;
        }

        cardData.Clear();
        foreach (KeyValuePair<int, int> pair in player.Cards)
        {
            cardData[pair.Key] = pair.Value;
        }

        _thumbnailCardId = player.ThumbnailId;
        if (!string.IsNullOrEmpty(player.StorageKey))
        {
            deckPathName = player.StorageKey;
        }

        if (DeckTitleInputField != null)
        {
            DeckTitleInputField.text = player.Title ?? string.Empty;
        }

        enemyCardData.Clear();
        foreach (KeyValuePair<int, int> pair in enemy.Cards)
        {
            enemyCardData[pair.Key] = pair.Value;
        }

        HideDeckActionButtons();
        TestPlayMatchState.Begin();
        EnterBattleFromMenu();
    }

    /// <summary>TestPlay 用の対戦選択状態を破棄する。</summary>
    public void ClearTestPlayBattleObjects()
    {
        enemyCardData.Clear();
        BattoleStartFlag = false;
        TestPlayMatchState.Clear();
    }

    /// <summary>選択中デッキ内のオンライン不可（notUsedOnline）カード一覧。</summary>
    public List<CardData> CollectNotUsedOnlineCardsInSelectedDeck()
    {
        List<CardData> banned = new List<CardData>();
        if (cardData == null || cardData.Count == 0 || CardDatabase.Instance == null)
        {
            return banned;
        }

        HashSet<int> seen = new HashSet<int>();
        foreach (KeyValuePair<int, int> entry in cardData)
        {
            if (entry.Value <= 0 || !seen.Add(entry.Key))
            {
                continue;
            }

            CardData data = CardDatabase.Instance.FindById(entry.Key);
            if (data != null && data.notUsedOnline)
            {
                banned.Add(data);
            }
        }

        banned.Sort((a, b) => a.id.CompareTo(b.id));
        return banned;
    }

    public bool SelectedDeckContainsNotUsedOnlineCards()
    {
        return CollectNotUsedOnlineCardsInSelectedDeck().Count > 0;
    }

    public string GetSelectedDeckDisplayName()
    {
        if (DeckTitleInputField != null && !string.IsNullOrWhiteSpace(DeckTitleInputField.text))
        {
            return DeckTitleInputField.text.Trim();
        }

        return string.IsNullOrWhiteSpace(deckPathName) ? "Untitled Deck" : deckPathName;
    }

    public void ClearBattleStartFlag()
    {
        BattoleStartFlag = false;
    }

    public bool IsBattleStartFlagActive()
    {
        return BattoleStartFlag;
    }

    /// <summary>現在の編集内容を保存し、完了まで待つ。</summary>
    public IEnumerator SaveCurrentDeckCoroutine()
    {
        yield return SaveDeckCoroutine(cardData);
    }

    // デッキパネル内のカードを保存（ゲスト=ローカル JSON / ログイン=Cloud Save）
    public void SaveDeckToJson(Dictionary<int, int> cardData)
    {
        StartCoroutine(SaveDeckCoroutine(cardData));
    }

    private IEnumerator SaveDeckCoroutine(Dictionary<int, int> sourceCardData)
    {
        string title = DeckTitleInputField != null ? DeckTitleInputField.text : string.Empty;
        DeckSaveData saveData = DeckStorageService.BuildSaveData(title, sourceCardData, _thumbnailCardId);
        _thumbnailCardId = saveData.thumbnailId;
        string storageKey = DeckStorageService.PrepareStorageKeyForSave(deckPathName);

        Task saveTask = DeckStorageService.SaveDeckAsync(storageKey, saveData);
        while (!saveTask.IsCompleted)
        {
            yield return null;
        }

        if (saveTask.IsFaulted)
        {
            Debug.LogError($"[Deck] Save failed: {DeckStorageService.FormatStorageError(saveTask.Exception)}");
            yield break;
        }

        deckPathName = storageKey;
        string mode = DeckStorageService.IsUsingCloudStorage ? "Cloud" : "Local";
        Debug.Log($"[Deck] Save complete ({mode}): {storageKey}");

        // 編集中以外なら件数・高さを即反映。編集中は ReturnToDeckListAfterEdit 側で再読込する。
        if (!isDeckEditing && DeckListPanel != null)
        {
            EnsureDeckListAreaVisible();
            RefreshDeckListFromStorage();
        }
    }

    public int CardCount(int id)
    {
        // int count = cardData[id];
        if (cardData.TryGetValue(id, out int count))
        {
            return count;
        }
        return 0;
    }

    public int GetDeckTotalCardCount()
    {
        int total = 0;
        foreach (KeyValuePair<int, int> pair in cardData)
        {
            if (pair.Value > 0)
            {
                total += pair.Value;
            }
        }

        return total;
    }

    public void RefreshDeckEditCountDisplays()
    {
        RefreshDeckTotalCountLabel();

        if (DeckEditNowpanel == null || !DeckEditNowpanel.activeInHierarchy)
        {
            return;
        }

        RefreshDeckCardCountBadges();
    }

    public void EnsureDeckEditUiVisible()
    {
        // 編集中は操作ボタン一覧パネルを隠し、デッキ・カードへのタップを妨げない
        SetDeckinfoPanelVisible(false);

        if (DeckEditNowpanel != null)
        {
            DeckEditNowpanel.SetActive(true);
            SetDeckEditPanelRaycastBlocking(false);
        }
    }

    public void ShowDeckActionButtons()
    {
        SetDeckinfoPanelVisible(true);
        ConfigureDeckinfoPanelRaycast(false);
        CenterHomeBoardAndDeckinfoPanel(force: true);
    }

    public void HideDeckActionButtons()
    {
        SetDeckinfoPanelVisible(false);
    }

    /// <summary>ホームの 480×800 盤面 Rect。</summary>
    public RectTransform GetHomeBoardRect()
    {
        if (DeckinfoPanel != null)
        {
            RectTransform panelRt = DeckinfoPanel.GetComponent<RectTransform>();
            if (panelRt != null && panelRt.parent is RectTransform boardRt)
            {
                return boardRt;
            }
        }

        return null;
    }

    private void SetDeckinfoPanelVisible(bool visible)
    {
        if (DeckinfoPanel != null)
        {
            DeckinfoPanel.SetActive(visible);
            if (visible)
            {
                CenterHomeBoardAndDeckinfoPanel(force: true);
            }
        }
    }

    /// <summary>
    /// ホームの 480×800 盤面を親キャンバス中央に置き、DeckinfoPanel はその枠いっぱいに合わせる。
    /// ストレッチの SizeDelta 残り（約+622）が上方向にはみ出すのを防ぐ。
    /// </summary>
    private void CenterHomeBoardAndDeckinfoPanel(bool force)
    {
        if (DeckinfoPanel == null)
        {
            return;
        }

        RectTransform panelRt = DeckinfoPanel.GetComponent<RectTransform>();
        if (panelRt == null)
        {
            return;
        }

        RectTransform boardRt = panelRt.parent as RectTransform;
        RectTransform parentRt = boardRt != null ? boardRt.parent as RectTransform : null;
        Vector2 parentSize = parentRt != null ? parentRt.rect.size : new Vector2(HomeBoardDesignWidth, HomeBoardDesignHeight);
        if (parentSize.x < 1f)
        {
            parentSize.x = HomeBoardDesignWidth;
        }

        if (parentSize.y < 1f)
        {
            parentSize.y = HomeBoardDesignHeight;
        }

        if (!force && (parentSize - _lastHomeBoardParentSize).sqrMagnitude < 0.01f)
        {
            return;
        }

        _lastHomeBoardParentSize = parentSize;

        if (boardRt != null)
        {
            float width = Mathf.Min(HomeBoardDesignWidth, parentSize.x);
            float height = Mathf.Min(HomeBoardDesignHeight, parentSize.y);
            boardRt.anchorMin = new Vector2(0.5f, 0.5f);
            boardRt.anchorMax = new Vector2(0.5f, 0.5f);
            boardRt.pivot = new Vector2(0.5f, 0.5f);
            boardRt.sizeDelta = new Vector2(width, height);
            boardRt.anchoredPosition = Vector2.zero;
            boardRt.localScale = Vector3.one;
            boardRt.localRotation = Quaternion.identity;
        }

        panelRt.anchorMin = Vector2.zero;
        panelRt.anchorMax = Vector2.one;
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.offsetMin = Vector2.zero;
        panelRt.offsetMax = Vector2.zero;
        panelRt.anchoredPosition = Vector2.zero;
        panelRt.sizeDelta = Vector2.zero;
        panelRt.localScale = Vector3.one;
        panelRt.localRotation = Quaternion.identity;
    }

    private void ConfigureDeckinfoPanelRaycast(bool blockRaycasts)
    {
        if (DeckinfoPanel == null)
        {
            return;
        }

        Image background = DeckinfoPanel.GetComponent<Image>();
        if (background != null)
        {
            background.raycastTarget = blockRaycasts;
        }
    }

    private void SetDeckEditPanelRaycastBlocking(bool blockRaycasts)
    {
        if (DeckEditNowpanel == null)
        {
            return;
        }

        Image background = DeckEditNowpanel.GetComponent<Image>();
        if (background != null)
        {
            background.raycastTarget = blockRaycasts;
        }
    }

    public void HideDeckEditCountUi()
    {
        if (_deckTotalCountLabelRoot != null)
        {
            _deckTotalCountLabelRoot.SetActive(false);
        }
    }

    public void EnsureCardCountBadge(GameObject cardObject, int count)
    {
        if (cardObject == null)
        {
            return;
        }

        // ユニットトークンはデッキに入れられないため、枚数バッジを出さない。
        int cardId = ResolveCardIdFromDeckPreviewObject(cardObject);
        if (IsUnitTokenCardId(cardId))
        {
            HideCardCountBadge(cardObject);
            return;
        }

        TextMeshProUGUI badge = GetOrCreateCardCountBadge(cardObject);
        if (badge == null)
        {
            return;
        }

        badge.text = count.ToString();
        badge.enabled = true;
        badge.gameObject.SetActive(true);
        ApplyCardCountBadgeLayout(badge, count);
    }

    private static int ResolveCardIdFromDeckPreviewObject(GameObject cardObject)
    {
        if (cardObject == null)
        {
            return 0;
        }

        Card card = cardObject.GetComponent<Card>();
        return card != null ? card.CardId : 0;
    }

    private static bool IsUnitTokenCardId(int cardId)
    {
        if (cardId <= 0 || CardDatabase.Instance == null)
        {
            return false;
        }

        CardData data = CardDatabase.Instance.FindById(cardId);
        return data != null && data.IsUnitToken();
    }

    private void HideCardCountBadge(GameObject cardObject)
    {
        if (cardObject == null)
        {
            return;
        }

        Transform existing = cardObject.transform.Find(DeckCardCountBadgeName);
        if (existing != null)
        {
            existing.gameObject.SetActive(false);
        }
    }

    private void RefreshDeckTotalCountLabel()
    {
        TextMeshProUGUI label = EnsureDeckTotalCountLabel();
        if (label == null)
        {
            return;
        }

        ApplyDeckTotalCountLabelLayout();
        _deckTotalCountLabelRoot.SetActive(true);
        label.SetLocalizedText(
            $"デッキ合計: {GetDeckTotalCardCount()}枚",
            $"Deck total: {GetDeckTotalCardCount()} cards");
    }

    private void ApplyDeckTotalCountLabelLayout()
    {
        if (_deckTotalCountLabelRoot == null || DeckEditNowpanel == null)
        {
            return;
        }

        const float labelHeight = 30f;
        const float gapAboveCards = 8f;
        int reservedTopSpace = Mathf.RoundToInt(labelHeight + gapAboveCards);

        RectTransform labelRect = _deckTotalCountLabelRoot.GetComponent<RectTransform>();
        labelRect.SetParent(DeckEditNowpanel.transform, false);
        labelRect.SetAsFirstSibling();

        LayoutElement layoutElement = _deckTotalCountLabelRoot.GetComponent<LayoutElement>();
        if (layoutElement == null)
        {
            layoutElement = _deckTotalCountLabelRoot.AddComponent<LayoutElement>();
        }

        layoutElement.ignoreLayout = true;

        labelRect.anchorMin = new Vector2(0f, 1f);
        labelRect.anchorMax = new Vector2(1f, 1f);
        labelRect.pivot = new Vector2(0.5f, 1f);
        labelRect.anchoredPosition = new Vector2(0f, 0f);
        labelRect.sizeDelta = new Vector2(0f, labelHeight);

        GridLayoutGroup grid = DeckEditNowpanel.GetComponent<GridLayoutGroup>();
        if (grid != null)
        {
            RectOffset padding = grid.padding;
            padding.top = reservedTopSpace;
            grid.padding = padding;
        }
    }

    private void RefreshDeckCardCountBadges()
    {
        if (DeckEditNowpanel == null)
        {
            return;
        }

        Card[] cards = DeckEditNowpanel.GetComponentsInChildren<Card>(true);
        for (int i = 0; i < cards.Length; i++)
        {
            Card card = cards[i];
            if (card == null)
            {
                continue;
            }

            EnsureCardCountBadge(card.gameObject, CardCount(card.CardId));
        }
    }

    private TextMeshProUGUI EnsureDeckTotalCountLabel()
    {
        if (_deckTotalCountLabel != null && _deckTotalCountLabel.gameObject == null)
        {
            _deckTotalCountLabel = null;
            _deckTotalCountLabelRoot = null;
        }

        if (_deckTotalCountLabel != null)
        {
            return _deckTotalCountLabel;
        }

        if (DeckEditNowpanel == null)
        {
            return null;
        }

        RemoveLegacyDeckTotalCountLabels();

        Transform existing = DeckEditNowpanel.transform.Find(DeckTotalCountLabelName);
        if (existing != null)
        {
            _deckTotalCountLabelRoot = existing.gameObject;
            _deckTotalCountLabel = existing.GetComponentInChildren<TextMeshProUGUI>(true);
            return _deckTotalCountLabel;
        }

        GameObject labelObject = new GameObject(DeckTotalCountLabelName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        _deckTotalCountLabelRoot = labelObject;

        Image background = labelObject.GetComponent<Image>();
        background.color = new Color(1f, 1f, 1f, 0.92f);
        background.raycastTarget = false;

        GameObject textObject = new GameObject("TotalCountText", typeof(RectTransform));
        textObject.transform.SetParent(labelObject.transform, false);
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        _deckTotalCountLabel = textObject.AddComponent<TextMeshProUGUI>();
        TMP_FontAsset font = Resources.Load<TMP_FontAsset>("SourceHanSansJP-Regular SDF");
        if (font != null)
        {
            _deckTotalCountLabel.font = font;
        }

        _deckTotalCountLabel.fontSize = 22;
        _deckTotalCountLabel.fontStyle = FontStyles.Bold;
        _deckTotalCountLabel.alignment = TextAlignmentOptions.Center;
        _deckTotalCountLabel.color = Color.black;
        _deckTotalCountLabel.raycastTarget = false;
        return _deckTotalCountLabel;
    }

    private void RemoveLegacyDeckTotalCountLabels()
    {
        if (DeckinfoPanel != null)
        {
            Transform oldOnInfo = DeckinfoPanel.transform.Find(DeckTotalCountLabelName);
            if (oldOnInfo != null)
            {
                Destroy(oldOnInfo.gameObject);
            }
        }

        Transform misplaced = DeckEditNowpanel.transform.Find(DeckTotalCountLabelName);
        if (misplaced != null && misplaced.GetComponentInChildren<TextMeshProUGUI>(true) == null)
        {
            Destroy(misplaced.gameObject);
        }
    }

    private TextMeshProUGUI GetOrCreateCardCountBadge(GameObject cardObject)
    {
        Transform existing = cardObject.transform.Find(DeckCardCountBadgeName);
        if (existing != null && existing.GetComponent<Image>() == null)
        {
            Destroy(existing.gameObject);
            existing = null;
        }

        if (existing != null)
        {
            Transform textTransform = existing.Find("CountText");
            if (textTransform != null)
            {
                return textTransform.GetComponent<TextMeshProUGUI>();
            }

            return existing.GetComponentInChildren<TextMeshProUGUI>(true);
        }

        GameObject badgeRoot = new GameObject(
            DeckCardCountBadgeName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        badgeRoot.transform.SetParent(cardObject.transform, false);
        badgeRoot.transform.SetAsLastSibling();

        RectTransform rect = badgeRoot.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-2f, -2f);
        rect.sizeDelta = new Vector2(20f, 20f);

        Image background = badgeRoot.GetComponent<Image>();
        background.sprite = GetDeckCardCountBadgeCircleSprite();
        background.type = Image.Type.Simple;
        background.preserveAspect = true;
        background.color = Color.black;
        background.raycastTarget = false;

        GameObject textObject = new GameObject("CountText", typeof(RectTransform));
        textObject.transform.SetParent(badgeRoot.transform, false);
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        TMP_FontAsset font = Resources.Load<TMP_FontAsset>("SourceHanSansJP-Regular SDF");
        if (font != null)
        {
            text.font = font;
        }

        text.fontSize = 13;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    private static void ApplyCardCountBadgeLayout(TextMeshProUGUI badge, int count)
    {
        if (badge == null)
        {
            return;
        }

        RectTransform badgeRect = badge.transform.parent as RectTransform;
        if (badgeRect == null)
        {
            return;
        }

        float size = count >= 10 ? 24f : 20f;
        badgeRect.sizeDelta = new Vector2(size, size);
        badge.fontSize = count >= 10 ? 12f : 13f;
    }

    private static Sprite GetDeckCardCountBadgeCircleSprite()
    {
        if (_deckCardCountBadgeCircleSprite != null)
        {
            return _deckCardCountBadgeCircleSprite;
        }

        const int textureSize = 32;
        Texture2D texture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false)
        {
            name = "DeckCardCountBadgeCircle",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };

        Color32 transparent = new Color32(0, 0, 0, 0);
        Color32 black = new Color32(0, 0, 0, 255);
        float center = (textureSize - 1) * 0.5f;
        float radius = textureSize * 0.5f - 1.5f;
        float radiusSquared = radius * radius;

        for (int y = 0; y < textureSize; y++)
        {
            for (int x = 0; x < textureSize; x++)
            {
                float dx = x - center;
                float dy = y - center;
                texture.SetPixel(x, y, dx * dx + dy * dy <= radiusSquared ? black : transparent);
            }
        }

        texture.Apply();
        _deckCardCountBadgeCircleSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, textureSize, textureSize),
            new Vector2(0.5f, 0.5f),
            100f);
        _deckCardCountBadgeCircleSprite.name = "DeckCardCountBadgeCircleSprite";
        return _deckCardCountBadgeCircleSprite;
    }

    public void ShowAllCanvasChildren(GameObject canvasObj)
    {
        canvasObj.gameObject.SetActive(true);
        return;
        foreach (Transform child in canvasObj.transform)
       {
        child.gameObject.SetActive(true);
       }
    }


    public void HideAllCanvasChildren(GameObject canvasObj)
    {
        canvasObj.gameObject.SetActive(false);
        return;
    // canvasObjの子要素（Transform）を一つずつループ
       foreach (Transform child in canvasObj.transform)
      {
        child.gameObject.SetActive(false);
      }
    }

    // 編集中のデッキ→カードをクリックしてデッキに入れる処理
    public void Deckedit(int id , int count)
    {
        // Debug.Log($"デッキデータ{cardData[id]}枚");
        Debug.Log($"デッキデータ{id}のカードを{count}枚入れました。");
        if (cardData.ContainsKey(id))
        {
           Debug.Log($"デッキデータ{cardData[id]}枚");
        }

        if (count <= 0)
        {
            cardData.Remove(id);
        }
        else
        {
            cardData[id] = count;
        }

        Debug.Log($"デッキデータ{id}の枚数: {count}");
        DeckEditNowpanel.SetActive(true);
        OnDeckCompositionChanged();
    }
    public void RemoveCardById(int targetId)
{
    // DeckEditNowpanel の子から CardId が一致するものを検索
    // (true) を入れることで、コンポーネントが OFF になっているオブジェクトも対象にする
    Card targetCard = DeckEditNowpanel.GetComponentsInChildren<Card>(true).FirstOrDefault(c => c.CardId == targetId);
    
    
    if (targetCard != null)
    {
        // GameObject を削除
        Destroy(targetCard.gameObject);
        // カードデータからも削除
        cardData.Remove(targetId);
        Debug.Log($"CardId: {targetId} のオブジェクトを削除しました。");
    }
    else
    {
        Debug.LogWarning($"CardId: {targetId} は見つかりませんでした。");
    }

    RefreshDeckEditCountDisplays();
    EnsureThumbnailCardId();
    RefreshThumbnailFrames();
}
public GameObject FindCardById(int targetId)
{
    // 1. 子要素からすべての Card コンポーネントを取得 (非アクティブも含む場合は true)
    Card[] allCards = DeckEditNowpanel.GetComponentsInChildren<Card>(true);

    // 2. LINQで CardId が一致する最初のものを探す
    Card targetCard = allCards.FirstOrDefault(c => c.CardId == targetId);
    foreach (Card card in allCards)
    {
        // ログにオブジェクト名とIDを表示
        Debug.Log($"[IDログ] Name: {card.gameObject.name}, CardId: {card.CardId}, Enabled: {card.enabled}");

        // if (card.CardId == targetId)
        // {
        //     Debug.Log($"<color=green>一致するIDを発見しました: {targetId}</color>");
        //     return card.gameObject;
        // }
    }
    if (targetCard != null)
    {
        Debug.Log($"カード発見: {targetCard.gameObject.name} カードid{targetId}");
        return targetCard.gameObject;
    }

    Debug.LogWarning($"CardId: {targetId} は見つかりませんでした。");
    return null;
}

// カードをクリックしたときにデッキ編集パネルにカードオブジェクトを追加する処理。
public void cardObj(GameObject obj)
{
    if (obj == null)
    {
        return;
    }

    Card card = obj.GetComponent<Card>();
    if (card == null)
    {
        return;
    }

    cardObj(card.CardId, obj);
}

public void cardObj(int cardId, GameObject preferredTemplate = null)
{
    if (cardId <= 0)
    {
        return;
    }

    Debug.Log($"サムネ追加 cardId:{cardId}");

    if (!cardData.TryGetValue(cardId, out int count))
    {
        count = 0;
    }

    GameObject existing = FindCardById(cardId);
    if (existing != null)
    {
        if (count == 0)
        {
            Debug.Log($"カードID {cardId} の枚数が0になったため、オブジェクトを削除します。");
            RemoveCardById(cardId);
            return;
        }

        EnsureCardCountBadge(existing, count);
        CardData existingData = CardDatabase.Instance != null
            ? CardDatabase.Instance.FindById(cardId)
            : null;
        Card.EnsureNotUsedOnlineLabel(existing, existingData);
        OnDeckCompositionChanged();
        return;
    }

    if (count == 0)
    {
        return;
    }

    GameObject template = ResolveDeckEditCardTemplate(cardId, preferredTemplate);
    if (template == null)
    {
        Debug.LogWarning($"[Deck] カードID {cardId} のテンプレートが見つからずプレビューを追加できません。");
        return;
    }

    GameObject copy = Instantiate(template, DeckEditNowpanel.transform);
    Card copyCard = copy.GetComponent<Card>();
    if (copyCard != null)
    {
        copyCard.CardId = cardId;
        copyCard.ClearDeckEditSession();
    }

    CardData cardDataAsset = CardDatabase.Instance != null
        ? CardDatabase.Instance.FindById(cardId)
        : null;
    if (cardDataAsset != null)
    {
        Image cardImage = copy.GetComponent<Image>();
        if (cardImage != null)
        {
            CardSpriteLoader.ApplyToImage(cardImage, cardDataAsset);
        }
    }

    EnsureCardCountBadge(copy, count);
    Card.EnsureNotUsedOnlineLabel(copy, cardDataAsset);
    Debug.Log($"サムネid{cardId}");
    RectTransform rect = copy.GetComponent<RectTransform>();
    rect.anchoredPosition = Vector2.zero;
    rect.anchorMin = new Vector2(0.5f, 0.5f);
    rect.anchorMax = new Vector2(0.5f, 0.5f);
    rect.pivot = new Vector2(0.5f, 0.5f);
    rect.sizeDelta = new Vector2(40, 60);

    Image img = copy.GetComponentInChildren<Image>();

    if (img == null)
    {
        Debug.Log("Imageない");
    }
    else
    {
        Debug.Log("Sprite: " + img.sprite);
    }

    OnDeckCompositionChanged();
}

private GameObject ResolveDeckEditCardTemplate(int cardId, GameObject preferredTemplate)
{
    if (preferredTemplate != null)
    {
        Card preferredCard = preferredTemplate.GetComponent<Card>();
        if (preferredCard != null && preferredCard.CardId == cardId)
        {
            return preferredTemplate;
        }
    }

    if (DeckListPanel != null)
    {
        Card[] listCards = DeckListPanel.GetComponentsInChildren<Card>(true);
        for (int i = 0; i < listCards.Length; i++)
        {
            Card listCard = listCards[i];
            if (listCard != null && listCard.CardId == cardId)
            {
                return listCard.gameObject;
            }
        }
    }

    Card[] allCards = FindObjectsByType<Card>(FindObjectsInactive.Include, FindObjectsSortMode.None);
    for (int i = 0; i < allCards.Length; i++)
    {
        Card candidate = allCards[i];
        if (candidate == null || candidate.CardId != cardId)
        {
            continue;
        }

        if (DeckEditNowpanel != null && candidate.transform.IsChildOf(DeckEditNowpanel.transform))
        {
            continue;
        }

        return candidate.gameObject;
    }

    return DeckDataPrefab;
}
 public void CardDataToJson()
    {
        // !以下はあとで編集デッキの場合は名前を検知して保存するようにする。
        SaveDeckToJson(cardData);
    }

// 全ての保存されたデッキの一覧を取得する
public List<string> GetSaveFileNames()
{
        List<string> fileList = new List<string>();
        Task<List<DeckStorageEntry>> listTask = DeckStorageService.ListDecksAsync();
        listTask.Wait();
        List<DeckStorageEntry> entries = listTask.Result;
        for (int i = 0; i < entries.Count; i++)
        {
            fileList.Add(entries[i].StorageKey);
        }

        return fileList;
    }

    private DeckSaveData LoadDeckSaveData(string storageKey)
    {
        Task<DeckSaveData> loadTask = DeckStorageService.LoadDeckAsync(storageKey);
        loadTask.Wait();
        return loadTask.Result;
    }

public void battleStart()
{
    // 現在の値が true なら false、false なら true に入れ替える
    TestPlayMatchState.Clear();
    BattoleStartFlag = !BattoleStartFlag;
    
    Debug.Log($"バトル開始フラグ:{BattoleStartFlag}");
}

    public bool IsBattleCanvasVisible()
    {
        return BattleCanvas != null && BattleCanvas.gameObject.activeSelf;
    }

    private BattleGameMain ResolveBattleMain()
    {
        if (BattleCanvas == null)
        {
            return UnityEngine.Object.FindObjectOfType<BattleGameMain>();
        }

        BattleGameMain battle = BattleCanvas.GetComponentInChildren<BattleGameMain>(true);
        return battle != null ? battle : UnityEngine.Object.FindObjectOfType<BattleGameMain>();
    }

    /// <summary>バトル画面を表示し、前回の対戦状態を破棄して最初からセットアップする。</summary>
    public void EnterBattleFromMenu()
    {
        if (MainCanvas != null)
        {
            HideAllCanvasChildren(MainCanvas.gameObject);
        }

        if (BattleCanvas != null)
        {
            ShowAllCanvasChildren(BattleCanvas.gameObject);
        }

        BattleGameMain battle = ResolveBattleMain();
        if (battle != null)
        {
            battle.RestartBattleFromBeginning();
        }
        else
        {
            Debug.LogWarning("[Battle] BattleGameMain が見つからないため、新規セットアップをスキップしました。");
        }
    }

    /// <summary>バトルキャンバスを閉じ、デッキ編集などのメイン UI を表示する。</summary>
    public void ReturnToMainMenuFromBattle()
    {
        BattoleStartFlag = false;
        TestPlayMatchState.Clear();
        EosOnlineMatchState.Clear();
        BattleGameMain battle = ResolveBattleMain();
        if (battle != null)
        {
            battle.TeardownBattleSessionForMainMenu();
        }

        if (BattleCanvas != null)
        {
            HideAllCanvasChildren(BattleCanvas.gameObject);
        }

        if (MainCanvas != null)
        {
            ShowAllCanvasChildren(MainCanvas.gameObject);
        }

        Debug.Log("[Battle] Returned to main menu (canvas switch).");
    }


public CardData GetCardDataById(int id)
{
    CardFeatureRegistry.EnsureLoaded();
    var cardTable = Resources.LoadAll<CardData>("Data/Cards").ToDictionary(data => data.id);
    if (cardTable.TryGetValue(id, out CardData card))
    {
        card?.EnsureFeaturesResolved();
        Debug.Log($"ID:{id} のカードデータを取得しました。カード名: {card.cardName}");
        return card;
    }
    else
    {
        Debug.LogError($"ID {id} のカードデータが見つかりません！");
        return null;
    }
}
public void DeleteJsonFile()
{
        if (string.IsNullOrEmpty(deckPathName))
        {
            Debug.LogWarning("[Deck] 削除対象のデッキが選択されていません。");
            return;
        }

        StartCoroutine(DeleteDeckCoroutine(deckPathName));
    }

    private IEnumerator DeleteDeckCoroutine(string storageKey)
    {
        Task deleteTask = DeckStorageService.DeleteDeckAsync(storageKey);
        while (!deleteTask.IsCompleted)
        {
            yield return null;
        }

        if (deleteTask.IsFaulted)
        {
            Debug.LogError($"[Deck] 削除失敗: {deleteTask.Exception?.GetBaseException().Message}");
            yield break;
        }

        Debug.Log($"[Deck] 削除完了: {storageKey}");
        deckPathName = string.Empty;
        HideDeckActionButtons();
        ClearDeckList();
        yield return ShowFileListCoroutine();
    }

// 保存されたデッキ一覧を表示する
public void ShowFileList()
{
        StartCoroutine(ShowFileListCoroutine());
    }

    /// <summary>編集開始時点のデッキを記録する。</summary>
    public void CaptureEditBaseline(string title)
    {
        _editBaselineCards = new Dictionary<int, int>();
        foreach (KeyValuePair<int, int> pair in cardData)
        {
            if (pair.Value > 0)
            {
                _editBaselineCards[pair.Key] = pair.Value;
            }
        }

        _editBaselineTitle = title ?? string.Empty;
        _editBaselineThumbnailId = _thumbnailCardId;
        _editBaselinePath = deckPathName ?? string.Empty;
    }

    /// <summary>編集開始時からカード・タイトル・サムネに差分があるか。</summary>
    public bool HasChangesFromEditBaseline(string currentTitle)
    {
        string nowTitle = currentTitle ?? string.Empty;
        if (!string.Equals(_editBaselineTitle.Trim(), nowTitle.Trim(), StringComparison.Ordinal))
        {
            return true;
        }

        if (_thumbnailCardId != _editBaselineThumbnailId)
        {
            return true;
        }

        Dictionary<int, int> current = new Dictionary<int, int>();
        foreach (KeyValuePair<int, int> pair in cardData)
        {
            if (pair.Value > 0)
            {
                current[pair.Key] = pair.Value;
            }
        }

        if (current.Count != _editBaselineCards.Count)
        {
            return true;
        }

        foreach (KeyValuePair<int, int> pair in _editBaselineCards)
        {
            if (!current.TryGetValue(pair.Key, out int count) || count != pair.Value)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>編集開始時の内容に戻す。復元後のタイトルを返す。</summary>
    public string RestoreEditBaseline()
    {
        cardData.Clear();
        foreach (KeyValuePair<int, int> pair in _editBaselineCards)
        {
            if (pair.Value > 0)
            {
                cardData[pair.Key] = pair.Value;
            }
        }

        _thumbnailCardId = _editBaselineThumbnailId;
        deckPathName = _editBaselinePath ?? string.Empty;
        EnsureThumbnailCardId();
        return _editBaselineTitle ?? string.Empty;
    }

    /// <summary>編集開始時に既存ファイルがあったか（新規作成ではなく既存編集）。</summary>
    public bool HasEditBaselineStorageKey()
    {
        return !string.IsNullOrEmpty(_editBaselinePath);
    }

    /// <summary>編集中デッキのサムネカード ID。未設定なら先頭。</summary>
    public int GetThumbnailCardId()
    {
        EnsureThumbnailCardId();
        return _thumbnailCardId;
    }

    /// <summary>デッキ内カードをサムネに設定する（1枚以上ある場合のみ）。</summary>
    public void SetThumbnailCardId(int cardId)
    {
        if (cardId <= 0 || CardCount(cardId) <= 0)
        {
            Debug.LogWarning($"[Deck] サムネにできないカード id:{cardId}");
            return;
        }

        _thumbnailCardId = cardId;
        RefreshThumbnailFrames();
        Debug.Log($"[Deck] サムネ設定: {_thumbnailCardId}");
    }

    private void OnDeckCompositionChanged()
    {
        RefreshDeckEditCountDisplays();
        EnsureThumbnailCardId();
        RefreshThumbnailFrames();
    }

    private void EnsureThumbnailCardId()
    {
        if (_thumbnailCardId > 0 && CardCount(_thumbnailCardId) > 0)
        {
            return;
        }

        _thumbnailCardId = 0;
        if (DeckEditNowpanel != null)
        {
            Card[] cards = DeckEditNowpanel.GetComponentsInChildren<Card>(true);
            for (int i = 0; i < cards.Length; i++)
            {
                Card card = cards[i];
                if (card == null || card.CardId <= 0)
                {
                    continue;
                }

                if (CardCount(card.CardId) > 0)
                {
                    _thumbnailCardId = card.CardId;
                    return;
                }
            }
        }

        foreach (KeyValuePair<int, int> pair in cardData)
        {
            if (pair.Value > 0)
            {
                _thumbnailCardId = pair.Key;
                return;
            }
        }
    }

    /// <summary>デッキ編集プレビューにサムネ枠を付ける／外す。</summary>
    public void RefreshThumbnailFrames()
    {
        if (DeckEditNowpanel == null)
        {
            return;
        }

        EnsureThumbnailCardId();
        Card[] cards = DeckEditNowpanel.GetComponentsInChildren<Card>(true);
        for (int i = 0; i < cards.Length; i++)
        {
            Card card = cards[i];
            if (card == null)
            {
                continue;
            }

            bool isThumb = card.CardId == _thumbnailCardId && CardCount(card.CardId) > 0;
            ApplyThumbnailFrame(card.gameObject, isThumb);
        }
    }

    private static void ApplyThumbnailFrame(GameObject cardObject, bool enabled)
    {
        if (cardObject == null)
        {
            return;
        }

        Outline outline = cardObject.GetComponent<Outline>();
        if (!enabled)
        {
            if (outline != null)
            {
                outline.enabled = false;
            }

            Transform legacy = cardObject.transform.Find(DeckThumbnailFrameName);
            if (legacy != null)
            {
                legacy.gameObject.SetActive(false);
            }

            return;
        }

        if (outline == null)
        {
            outline = cardObject.AddComponent<Outline>();
        }

        outline.enabled = true;
        outline.effectColor = new Color(1f, 0.84f, 0.2f, 1f);
        outline.effectDistance = new Vector2(4f, 4f);
        outline.useGraphicAlpha = true;
    }

    private const string DeckListScrollRootName = "DeckListScrollView";
    private const string DeckListScrollViewportName = "Viewport";

    private void ConfigureDeckListGridLayout()
    {
        if (DeckListPanel == null)
        {
            return;
        }

        DeckListPanel.transform.localScale = Vector3.one;

        GridLayoutGroup grid = DeckListPanel.GetComponent<GridLayoutGroup>();
        if (grid == null)
        {
            grid = DeckListPanel.AddComponent<GridLayoutGroup>();
        }

        grid.padding = new RectOffset(12, 12, 8, 16);
        grid.spacing = new Vector2(8f, 12f);
        grid.cellSize = new Vector2(DeckListCellWidth, DeckListCellHeight);
        grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;
        grid.childAlignment = TextAnchor.UpperCenter;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3;

        ContentSizeFitter listFitter = DeckListPanel.GetComponent<ContentSizeFitter>();
        if (listFitter != null)
        {
            // 高さは ApplyDeckListContentHeight で明示設定する（Fitter は末尾行欠けの原因）
            listFitter.enabled = false;
        }

        Image panelImage = DeckListPanel.GetComponent<Image>();
        if (panelImage != null)
        {
            panelImage.color = new Color(0f, 0f, 0f, 0.2f);
            if (panelImage.sprite == null)
            {
                panelImage.sprite = GetUiWhiteSprite();
            }
        }

        EnsureDeckListScrollable();
    }

    /// <summary>
    /// 赤枠のデッキグリッドだけを縦スクロール可能にする。
    /// NewDeck 等の上部ボタンは固定し、DeckListPanel 専用 ScrollRect で包む。
    /// </summary>
    private void EnsureDeckListScrollable()
    {
        if (DeckListPanel == null)
        {
            return;
        }

        RectTransform listRt = DeckListPanel.GetComponent<RectTransform>();
        bool createdNow = false;
        ScrollRect dedicated = FindDedicatedDeckListScroll(listRt);
        if (dedicated == null)
        {
            dedicated = CreateDedicatedDeckListScroll(listRt);
            createdNow = dedicated != null;
        }

        if (dedicated == null)
        {
            return;
        }

        dedicated.horizontal = false;
        dedicated.vertical = true;
        dedicated.scrollSensitivity = 40f;
        dedicated.inertia = true;
        dedicated.decelerationRate = 0.135f;
        dedicated.movementType = ScrollRect.MovementType.Clamped;
        dedicated.content = listRt;
        if (dedicated.viewport == null && dedicated.transform.childCount > 0)
        {
            dedicated.viewport = dedicated.transform.GetChild(0) as RectTransform;
        }

        if (listRt.parent != dedicated.viewport)
        {
            listRt.SetParent(dedicated.viewport, false);
        }

        // 上基準・横ストレッチ。高さは Refresh でアイテム数から明示設定する。
        listRt.anchorMin = new Vector2(0f, 1f);
        listRt.anchorMax = new Vector2(1f, 1f);
        listRt.pivot = new Vector2(0.5f, 1f);
        listRt.sizeDelta = new Vector2(0f, Mathf.Max(listRt.sizeDelta.y, 1f));
        if (createdNow)
        {
            listRt.anchoredPosition = Vector2.zero;
        }

        // ContentSizeFitter は高さ不足／レイアウト競合の原因になるため無効化
        ContentSizeFitter listFitter = DeckListPanel.GetComponent<ContentSizeFitter>();
        if (listFitter != null)
        {
            listFitter.enabled = false;
        }

        LayoutElement listLayout = DeckListPanel.GetComponent<LayoutElement>();
        if (listLayout != null)
        {
            listLayout.ignoreLayout = true;
        }

        // 外側の ScrollRect（ボタン込み）はスクロールさせない
        ScrollRect[] scrolls = DeckListPanel.GetComponentsInParent<ScrollRect>(true);
        for (int i = 0; i < scrolls.Length; i++)
        {
            if (scrolls[i] == null || scrolls[i] == dedicated)
            {
                continue;
            }

            scrolls[i].horizontal = false;
            scrolls[i].vertical = false;
            scrolls[i].enabled = true;

            if (scrolls[i].content != null)
            {
                ConfigureOuterDeckFilterContent(scrolls[i].content, dedicated);
            }
        }
    }

    private static ScrollRect FindDedicatedDeckListScroll(RectTransform listRt)
    {
        if (listRt == null)
        {
            return null;
        }

        Transform parent = listRt.parent;
        if (parent != null && parent.name == DeckListScrollViewportName)
        {
            Transform root = parent.parent;
            if (root != null && root.name == DeckListScrollRootName)
            {
                return root.GetComponent<ScrollRect>();
            }
        }

        Transform siblingParent = listRt.parent;
        if (siblingParent != null)
        {
            Transform existing = siblingParent.Find(DeckListScrollRootName);
            if (existing != null)
            {
                return existing.GetComponent<ScrollRect>();
            }
        }

        return null;
    }

    private ScrollRect CreateDedicatedDeckListScroll(RectTransform listRt)
    {
        Transform originalParent = listRt.parent;
        if (originalParent == null)
        {
            return null;
        }

        int siblingIndex = listRt.GetSiblingIndex();

        GameObject scrollRoot = new GameObject(
            DeckListScrollRootName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(ScrollRect),
            typeof(LayoutElement));
        scrollRoot.transform.SetParent(originalParent, false);
        scrollRoot.transform.SetSiblingIndex(siblingIndex);

        RectTransform scrollRt = scrollRoot.GetComponent<RectTransform>();
        // VerticalLayoutGroup 配下なので stretch 全面ではなく、レイアウトに高さを任せる
        scrollRt.anchorMin = new Vector2(0f, 1f);
        scrollRt.anchorMax = new Vector2(1f, 1f);
        scrollRt.pivot = new Vector2(0.5f, 1f);
        scrollRt.anchoredPosition = Vector2.zero;
        scrollRt.sizeDelta = new Vector2(0f, 400f);

        Image scrollImage = scrollRoot.GetComponent<Image>();
        scrollImage.color = new Color(0f, 0f, 0f, 0.01f);
        scrollImage.raycastTarget = true;
        if (scrollImage.sprite == null)
        {
            scrollImage.sprite = GetUiWhiteSprite();
        }

        LayoutElement scrollLayout = scrollRoot.GetComponent<LayoutElement>();
        scrollLayout.minHeight = 200f;
        scrollLayout.preferredHeight = -1f;
        scrollLayout.flexibleHeight = 1f;
        scrollLayout.flexibleWidth = 1f;

        GameObject viewport = new GameObject(
            DeckListScrollViewportName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(RectMask2D));
        viewport.transform.SetParent(scrollRoot.transform, false);

        RectTransform viewportRt = viewport.GetComponent<RectTransform>();
        viewportRt.anchorMin = Vector2.zero;
        viewportRt.anchorMax = Vector2.one;
        viewportRt.offsetMin = Vector2.zero;
        viewportRt.offsetMax = Vector2.zero;
        viewportRt.pivot = new Vector2(0.5f, 1f);

        Image viewportImage = viewport.GetComponent<Image>();
        viewportImage.color = new Color(1f, 1f, 1f, 0.001f);
        viewportImage.raycastTarget = true;
        if (viewportImage.sprite == null)
        {
            viewportImage.sprite = GetUiWhiteSprite();
        }

        ScrollRect scroll = scrollRoot.GetComponent<ScrollRect>();
        scroll.viewport = viewportRt;
        scroll.content = listRt;
        scroll.horizontal = false;
        scroll.vertical = true;

        listRt.SetParent(viewportRt, false);
        return scroll;
    }

    private const string DeckListHeaderRowName = "DeckListHeaderRow";

    /// <summary>外側 Content: 上部ボタンは横並び固定 + 下の専用 Scroll が残り高さを使う。</summary>
    private void ConfigureOuterDeckFilterContent(RectTransform content, ScrollRect dedicatedScroll)
    {
        if (content == null || dedicatedScroll == null)
        {
            return;
        }

        content.anchorMin = new Vector2(0f, 0f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.offsetMin = Vector2.zero;
        content.offsetMax = Vector2.zero;
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;

        VerticalLayoutGroup layout = content.GetComponent<VerticalLayoutGroup>();
        if (layout == null)
        {
            layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        }

        layout.padding = new RectOffset(4, 4, 4, 4);
        layout.spacing = 6f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter outerFitter = content.GetComponent<ContentSizeFitter>();
        if (outerFitter != null)
        {
            outerFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            outerFitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
            outerFitter.enabled = false;
        }

        RectTransform headerRow = EnsureDeckListHeaderRow(content);
        RectTransform countLabelRt = EnsureDeckListCreatedCountLabel(content);
        RectTransform dedicatedRt = dedicatedScroll.GetComponent<RectTransform>();

        // 並び: ヘッダー横並び → 作成デッキ数 → デッキ一覧スクロール
        if (headerRow != null)
        {
            headerRow.SetSiblingIndex(0);
        }

        if (countLabelRt != null)
        {
            countLabelRt.SetSiblingIndex(1);
        }

        if (dedicatedRt != null)
        {
            dedicatedRt.SetSiblingIndex(2);
        }

        for (int i = 0; i < content.childCount; i++)
        {
            RectTransform child = content.GetChild(i) as RectTransform;
            if (child == null)
            {
                continue;
            }

            LayoutElement element = child.GetComponent<LayoutElement>();
            if (element == null)
            {
                element = child.gameObject.AddComponent<LayoutElement>();
            }

            if (headerRow != null && child == headerRow)
            {
                element.minHeight = 30f;
                element.preferredHeight = 30f;
                element.flexibleHeight = 0f;
                element.flexibleWidth = 1f;
                element.ignoreLayout = false;
                continue;
            }

            if (countLabelRt != null && child == countLabelRt)
            {
                element.minHeight = 24f;
                element.preferredHeight = 24f;
                element.flexibleHeight = 0f;
                element.flexibleWidth = 1f;
                element.ignoreLayout = false;
                continue;
            }

            if (dedicatedRt != null && child == dedicatedRt)
            {
                element.minHeight = 200f;
                element.preferredHeight = -1f;
                element.flexibleHeight = 1f;
                element.ignoreLayout = false;
                continue;
            }

            // 編集用パネル／タイトル入力は一覧レイアウト外
            string childName = child.name ?? string.Empty;
            if (childName.IndexOf("DeckEdit", StringComparison.OrdinalIgnoreCase) >= 0
                || childName.IndexOf("DeckTitle", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                element.ignoreLayout = true;
                continue;
            }

            // ヘッダーへ移したボタンが残っていれば無視
            if (IsDeckListHeaderButtonName(childName))
            {
                element.ignoreLayout = true;
                continue;
            }

            element.ignoreLayout = !child.gameObject.activeSelf;
        }
    }

    /// <summary>small / NewDeck / DeckMake を横一列のヘッダーにまとめる。</summary>
    private RectTransform EnsureDeckListHeaderRow(RectTransform content)
    {
        if (content == null)
        {
            return null;
        }

        Transform existing = content.Find(DeckListHeaderRowName);
        GameObject rowGo;
        if (existing != null)
        {
            rowGo = existing.gameObject;
        }
        else
        {
            rowGo = new GameObject(
                DeckListHeaderRowName,
                typeof(RectTransform),
                typeof(HorizontalLayoutGroup),
                typeof(LayoutElement));
            rowGo.transform.SetParent(content, false);
        }

        RectTransform rowRt = rowGo.GetComponent<RectTransform>();
        rowRt.anchorMin = new Vector2(0f, 1f);
        rowRt.anchorMax = new Vector2(1f, 1f);
        rowRt.pivot = new Vector2(0.5f, 1f);
        rowRt.sizeDelta = new Vector2(0f, 30f);

        HorizontalLayoutGroup hlg = rowGo.GetComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(4, 4, 0, 0);
        hlg.spacing = 8f;
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = true;

        // Content 直下のヘッダーボタンを横並び行へ移動（順序固定）
        MoveHeaderButtonIntoRow(content, rowRt, "AllViewButton", 0);
        MoveHeaderButtonIntoRow(content, rowRt, "NewDeckMakeingButton", 1);
        MoveHeaderButtonIntoRow(content, rowRt, "DeckMakeButton", 2);

        for (int i = 0; i < rowRt.childCount; i++)
        {
            RectTransform btnRt = rowRt.GetChild(i) as RectTransform;
            if (btnRt == null)
            {
                continue;
            }

            LayoutElement btnLayout = btnRt.GetComponent<LayoutElement>();
            if (btnLayout == null)
            {
                btnLayout = btnRt.gameObject.AddComponent<LayoutElement>();
            }

            btnLayout.minWidth = 100f;
            btnLayout.preferredWidth = 160f;
            btnLayout.flexibleWidth = 1f;
            btnLayout.minHeight = 30f;
            btnLayout.preferredHeight = 30f;
            btnLayout.ignoreLayout = false;

            btnRt.sizeDelta = new Vector2(160f, 30f);
        }

        return rowRt;
    }

    private static void MoveHeaderButtonIntoRow(
        RectTransform content,
        RectTransform row,
        string buttonNamePrefix,
        int siblingIndex)
    {
        if (content == null || row == null || string.IsNullOrEmpty(buttonNamePrefix))
        {
            return;
        }

        // 既に行内にあれば順序だけ整える
        for (int i = 0; i < row.childCount; i++)
        {
            Transform child = row.GetChild(i);
            if (child != null && IsDeckListHeaderButtonName(child.name)
                && child.name.IndexOf(buttonNamePrefix, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                child.SetSiblingIndex(Mathf.Clamp(siblingIndex, 0, row.childCount - 1));
                return;
            }
        }

        Transform found = null;
        for (int i = 0; i < content.childCount; i++)
        {
            Transform child = content.GetChild(i);
            if (child == null)
            {
                continue;
            }

            string name = child.name ?? string.Empty;
            if (name.IndexOf(buttonNamePrefix, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                found = child;
                break;
            }
        }

        if (found == null)
        {
            return;
        }

        found.SetParent(row, false);
        found.SetSiblingIndex(Mathf.Clamp(siblingIndex, 0, Mathf.Max(0, row.childCount - 1)));
    }

    private static bool IsDeckListHeaderButtonName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        return name.IndexOf("AllViewButton", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("NewDeckMakeingButton", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("DeckMakeButton", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>ヘッダーボタン直下の「作成デッキ数」ラベルを用意する。</summary>
    private RectTransform EnsureDeckListCreatedCountLabel(RectTransform content)
    {
        if (content == null)
        {
            return null;
        }

        if (_deckListCreatedCountLabelRoot != null && _deckListCreatedCountLabelRoot.gameObject == null)
        {
            _deckListCreatedCountLabelRoot = null;
            _deckListCreatedCountLabel = null;
        }

        if (_deckListCreatedCountLabelRoot == null)
        {
            Transform existing = content.Find(DeckListCreatedCountLabelName);
            if (existing != null)
            {
                _deckListCreatedCountLabelRoot = existing.gameObject;
                _deckListCreatedCountLabel = existing.GetComponentInChildren<TextMeshProUGUI>(true);
            }
        }

        if (_deckListCreatedCountLabelRoot == null)
        {
            GameObject root = new GameObject(
                DeckListCreatedCountLabelName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(LayoutElement));
            root.transform.SetParent(content, false);
            _deckListCreatedCountLabelRoot = root;

            GameObject textGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer));
            textGo.transform.SetParent(root.transform, false);
            RectTransform textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;

            _deckListCreatedCountLabel = textGo.AddComponent<TextMeshProUGUI>();
            TMP_FontAsset font = Resources.Load<TMP_FontAsset>("SourceHanSansJP-Regular SDF");
            if (font != null)
            {
                _deckListCreatedCountLabel.font = font;
            }

            _deckListCreatedCountLabel.fontSize = 16f;
            _deckListCreatedCountLabel.fontStyle = FontStyles.Bold;
            _deckListCreatedCountLabel.alignment = TextAlignmentOptions.Center;
            _deckListCreatedCountLabel.color = new Color(0.1f, 0.1f, 0.12f, 1f);
            _deckListCreatedCountLabel.raycastTarget = false;
        }

        if (_deckListCreatedCountLabelRoot.transform.parent != content)
        {
            _deckListCreatedCountLabelRoot.transform.SetParent(content, false);
        }

        RectTransform rootRt = _deckListCreatedCountLabelRoot.GetComponent<RectTransform>();
        rootRt.anchorMin = new Vector2(0f, 1f);
        rootRt.anchorMax = new Vector2(1f, 1f);
        rootRt.pivot = new Vector2(0.5f, 1f);
        rootRt.sizeDelta = new Vector2(0f, 24f);
        _deckListCreatedCountLabelRoot.SetActive(true);
        return rootRt;
    }

    /// <summary>作成済みデッキ数をヘッダー直下に表示する。</summary>
    private void RefreshDeckListCreatedCountLabel(int createdDeckCount)
    {
        if (DeckListPanel == null)
        {
            return;
        }

        ScrollRect dedicated = FindDedicatedDeckListScroll(DeckListPanel.GetComponent<RectTransform>());
        RectTransform content = null;
        if (dedicated != null)
        {
            ScrollRect[] scrolls = DeckListPanel.GetComponentsInParent<ScrollRect>(true);
            for (int i = 0; i < scrolls.Length; i++)
            {
                if (scrolls[i] != null && scrolls[i] != dedicated && scrolls[i].content != null)
                {
                    content = scrolls[i].content;
                    break;
                }
            }
        }

        if (content == null && dedicated != null && dedicated.transform.parent is RectTransform parentRt)
        {
            content = parentRt;
        }

        if (content == null)
        {
            return;
        }

        EnsureDeckListCreatedCountLabel(content);
        if (_deckListCreatedCountLabel == null)
        {
            return;
        }

        int count = Mathf.Max(0, createdDeckCount);
        _deckListCreatedCountLabel.SetLocalizedText(
            $"作成デッキ数: {count}",
            $"Decks created: {count}");
    }

    /// <summary>一覧生成後にグリッド高さを実測し、末尾行までスクロールできるようにする。</summary>
    private void RefreshDeckListScrollLayout(int? itemCountOverride = null)
    {
        if (DeckListPanel == null)
        {
            return;
        }

        EnsureDeckListAreaVisible();
        EnsureDeckListScrollable();

        RectTransform listRt = DeckListPanel.GetComponent<RectTransform>();
        int itemCount = itemCountOverride
            ?? CountActiveDeckListItems(listRt);
        ScrollRect dedicated = FindDedicatedDeckListScroll(listRt);

        // 外側レイアウトを先に確定させ、Viewport 高さを安定させる
        ScrollRect outer = null;
        ScrollRect[] scrolls = DeckListPanel.GetComponentsInParent<ScrollRect>(true);
        for (int i = 0; i < scrolls.Length; i++)
        {
            if (scrolls[i] != null && scrolls[i] != dedicated)
            {
                outer = scrolls[i];
                break;
            }
        }

        if (outer != null && outer.content != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(outer.content);
        }

        if (dedicated != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(dedicated.GetComponent<RectTransform>());
            if (dedicated.viewport != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(dedicated.viewport);
            }
        }

        ApplyDeckListContentHeight(itemCount);
        LayoutRebuilder.ForceRebuildLayoutImmediate(listRt);
        Canvas.ForceUpdateCanvases();
        // 子の実バウンドで再補正（セル想定よりはみ出す場合に必要）。式計算未満には落とさない。
        ApplyDeckListContentHeightFromChildren(itemCount);

        if (dedicated != null)
        {
            dedicated.StopMovement();
            dedicated.verticalNormalizedPosition = 1f;
            dedicated.velocity = Vector2.zero;
        }
    }

    /// <summary>件数から Grid Content の必要高さを計算する。</summary>
    private float CalculateDeckListContentHeight(int itemCount)
    {
        if (DeckListPanel == null)
        {
            return 1f;
        }

        GridLayoutGroup grid = DeckListPanel.GetComponent<GridLayoutGroup>();
        int columns = 3;
        float cellH = DeckListCellHeight;
        float spaceY = 12f;
        float padTop = 8f;
        float padBottom = 16f;
        if (grid != null)
        {
            columns = Mathf.Max(1, grid.constraintCount);
            if (Mathf.Abs(grid.cellSize.y - DeckListCellHeight) > 0.01f)
            {
                grid.cellSize = new Vector2(grid.cellSize.x, DeckListCellHeight);
            }

            cellH = grid.cellSize.y;
            spaceY = grid.spacing.y;
            padTop = grid.padding.top;
            padBottom = grid.padding.bottom;
        }

        int rows = itemCount <= 0 ? 0 : Mathf.CeilToInt(itemCount / (float)columns);
        float height = padTop + padBottom;
        if (rows > 0)
        {
            height += rows * cellH + (rows - 1) * spaceY;
        }

        return height + DeckListContentBottomExtra;
    }

    /// <summary>
    /// Grid の行数から Content 高さを決める。
    /// </summary>
    private void ApplyDeckListContentHeight(int? itemCountOverride = null)
    {
        if (DeckListPanel == null)
        {
            return;
        }

        RectTransform listRt = DeckListPanel.GetComponent<RectTransform>();
        int itemCount = itemCountOverride ?? CountActiveDeckListItems(listRt);
        SetDeckListContentHeight(listRt, CalculateDeckListContentHeight(itemCount));
    }

    /// <summary>子 Rect の実バウンドから Content 高さを補正する。</summary>
    private void ApplyDeckListContentHeightFromChildren(int? itemCountOverride = null)
    {
        if (DeckListPanel == null)
        {
            return;
        }

        RectTransform listRt = DeckListPanel.GetComponent<RectTransform>();
        int itemCount = itemCountOverride ?? CountActiveDeckListItems(listRt);
        float formulaHeight = CalculateDeckListContentHeight(itemCount);
        if (itemCount <= 0)
        {
            SetDeckListContentHeight(listRt, 1f);
            return;
        }

        // 親サイズを一時的に十分大きくしてからバウンド計測（狭いと子が潰れて測れない）
        float provisional = Mathf.Max(formulaHeight, listRt.rect.height, 8000f);
        SetDeckListContentHeight(listRt, provisional);
        LayoutRebuilder.ForceRebuildLayoutImmediate(listRt);
        Canvas.ForceUpdateCanvases();

        float lowest = 0f;
        for (int i = 0; i < listRt.childCount; i++)
        {
            RectTransform child = listRt.GetChild(i) as RectTransform;
            if (child == null || !child.gameObject.activeSelf)
            {
                continue;
            }

            // 上ピボット親ローカルで、子の下端（より小さい y）
            Vector3[] corners = new Vector3[4];
            child.GetWorldCorners(corners);
            for (int c = 0; c < 4; c++)
            {
                Vector3 local = listRt.InverseTransformPoint(corners[c]);
                if (local.y < lowest)
                {
                    lowest = local.y;
                }
            }
        }

        // pivot=top なので高さは上端0から最下端までの距離。式計算未満には落とさない。
        float measured = Mathf.Max(1f, -lowest + DeckListContentBottomExtra);
        SetDeckListContentHeight(listRt, Mathf.Max(formulaHeight, measured));
        LayoutRebuilder.ForceRebuildLayoutImmediate(listRt);
    }

    private static int CountActiveDeckListItems(RectTransform listRt)
    {
        if (listRt == null)
        {
            return 0;
        }

        int itemCount = 0;
        for (int i = 0; i < listRt.childCount; i++)
        {
            if (listRt.GetChild(i).gameObject.activeSelf)
            {
                itemCount++;
            }
        }

        return itemCount;
    }

    private static void SetDeckListContentHeight(RectTransform listRt, float height)
    {
        if (listRt == null)
        {
            return;
        }

        listRt.anchorMin = new Vector2(0f, 1f);
        listRt.anchorMax = new Vector2(1f, 1f);
        listRt.pivot = new Vector2(0.5f, 1f);
        listRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(height, 1f));
        listRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 0f);
    }

    private GameObject CreateDeckListItem(DeckSaveData data, DeckStorageEntry entry)
    {
        GameObject cardObj = Instantiate(DeckDataPrefab, DeckListPanel.transform);
        cardObj.name = string.IsNullOrEmpty(data.title) ? entry.DisplayName : data.title;
        cardObj.transform.localScale = Vector3.one;

        Image panelImage = cardObj.GetComponent<Image>();
        if (panelImage != null)
        {
            panelImage.sprite = GetUiWhiteSprite();
            panelImage.type = Image.Type.Sliced;
            panelImage.color = new Color(0.12f, 0.12f, 0.14f, 1f);
            panelImage.raycastTarget = true;
        }

        VerticalLayoutGroup layout = cardObj.GetComponent<VerticalLayoutGroup>();
        if (layout == null)
        {
            layout = cardObj.AddComponent<VerticalLayoutGroup>();
        }

        layout.padding = new RectOffset(8, 8, 8, 8);
        layout.spacing = 4f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        int thumbId = DeckStorageService.ResolveThumbnailId(
            BuildCountMap(data),
            data.thumbnailId);
        GameObject thumbGo = new GameObject(
            "Thumbnail",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(LayoutElement));
        thumbGo.transform.SetParent(cardObj.transform, false);
        LayoutElement thumbLayout = thumbGo.GetComponent<LayoutElement>();
        thumbLayout.preferredHeight = 132f;
        thumbLayout.minHeight = 120f;
        Image thumbImage = thumbGo.GetComponent<Image>();
        CardSpriteLoader.ApplyToImage(thumbImage, thumbId);
        thumbImage.preserveAspect = true;
        thumbImage.raycastTarget = false;
        thumbImage.color = Color.white;

        string title = string.IsNullOrEmpty(data.title) ? entry.DisplayName : data.title;
        TextMeshProUGUI titleText = CreateDeckListLabel(cardObj, "DeckTitle", title, 16f, FontStyles.Bold, 36f);
        titleText.alignment = TextAlignmentOptions.TopLeft;
        titleText.enableWordWrapping = true;
        titleText.overflowMode = TextOverflowModes.Ellipsis;

        DateTime stamp = entry.LastWriteTime;
        if (stamp == DateTime.MinValue && data.updatedAtUnix > 0)
        {
            stamp = DateTimeOffset.FromUnixTimeSeconds(data.updatedAtUnix).LocalDateTime;
        }

        string dateLine = stamp == DateTime.MinValue
            ? string.Empty
            : FormatDeckListDate(stamp);
        string weekLine = stamp == DateTime.MinValue
            ? string.Empty
            : $"({FormatDeckListWeekday(stamp.DayOfWeek)})";

        TextMeshProUGUI dateText = CreateDeckListLabel(cardObj, "DeckDate", dateLine, 12f, FontStyles.Normal, 18f);
        dateText.alignment = TextAlignmentOptions.TopLeft;
        TextMeshProUGUI weekText = CreateDeckListLabel(cardObj, "DeckWeekday", weekLine, 12f, FontStyles.Normal, 18f);
        weekText.alignment = TextAlignmentOptions.TopLeft;

        return cardObj;
    }

    private static TextMeshProUGUI CreateDeckListLabel(
        GameObject parent,
        string name,
        string text,
        float fontSize,
        FontStyles style,
        float preferredHeight)
    {
        GameObject go = new GameObject(
            name,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI),
            typeof(LayoutElement));
        go.transform.SetParent(parent.transform, false);
        LayoutElement layout = go.GetComponent<LayoutElement>();
        layout.preferredHeight = preferredHeight;
        layout.minHeight = preferredHeight;

        TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.ApplyJapaneseFont();
        tmp.text = text ?? string.Empty;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static Dictionary<int, int> BuildCountMap(DeckSaveData data)
    {
        Dictionary<int, int> map = new Dictionary<int, int>();
        if (data == null || data.cards == null)
        {
            return map;
        }

        for (int i = 0; i < data.cards.Count; i++)
        {
            CardSlot slot = data.cards[i];
            if (slot == null || slot.id <= 0 || slot.count <= 0)
            {
                continue;
            }

            map[slot.id] = slot.count;
        }

        return map;
    }

    private static Sprite ResolveCardSprite(int cardId)
    {
        return CardSpriteLoader.ResolveEmbeddedSpriteByCardId(cardId);
    }

    private static string FormatDeckListDate(DateTime stamp)
    {
        if (GameLocale.IsEnglish)
        {
            return stamp.ToString("MMM dd, yyyy", CultureInfo.GetCultureInfo("en-US"));
        }

        return stamp.ToString("yyyy年MM月dd日");
    }

    private static string FormatDeckListWeekday(DayOfWeek day)
    {
        if (GameLocale.IsEnglish)
        {
            switch (day)
            {
                case DayOfWeek.Sunday: return "Sun";
                case DayOfWeek.Monday: return "Mon";
                case DayOfWeek.Tuesday: return "Tue";
                case DayOfWeek.Wednesday: return "Wed";
                case DayOfWeek.Thursday: return "Thu";
                case DayOfWeek.Friday: return "Fri";
                case DayOfWeek.Saturday: return "Sat";
                default: return string.Empty;
            }
        }

        switch (day)
        {
            case DayOfWeek.Sunday: return "日";
            case DayOfWeek.Monday: return "月";
            case DayOfWeek.Tuesday: return "火";
            case DayOfWeek.Wednesday: return "水";
            case DayOfWeek.Thursday: return "木";
            case DayOfWeek.Friday: return "金";
            case DayOfWeek.Saturday: return "土";
            default: return string.Empty;
        }
    }

    private static Sprite GetUiWhiteSprite()
    {
        if (_uiWhiteSprite != null)
        {
            return _uiWhiteSprite;
        }

        Texture2D texture = new Texture2D(8, 8, TextureFormat.RGBA32, false)
        {
            name = "DeckListUiWhite",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        Color32 white = new Color32(255, 255, 255, 255);
        Color32[] pixels = new Color32[64];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = white;
        }

        texture.SetPixels32(pixels);
        texture.Apply();
        _uiWhiteSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, 8f, 8f),
            new Vector2(0.5f, 0.5f),
            100f,
            0u,
            SpriteMeshType.FullRect,
            new Vector4(2f, 2f, 2f, 2f));
        _uiWhiteSprite.name = "DeckListUiWhiteSprite";
        return _uiWhiteSprite;
    }

    private IEnumerator ShowFileListCoroutine()
    {
        int loadGeneration = ++_deckListLoadGeneration;

        // 非表示のまま計測すると高さ不足で約18件までしか見えない
        EnsureDeckListAreaVisible();

        ClearDeckListItems();
        // Destroy はフレーム末尾なので、破棄完了を待ってから再生成する
        yield return null;
        if (loadGeneration != _deckListLoadGeneration)
        {
            yield break;
        }

        EnsureDeckListAreaVisible();
        ConfigureDeckListGridLayout();
        RefreshDeckListLayoutDynamic(0);

        Task<List<DeckStorageEntry>> listTask = DeckStorageService.ListDecksAsync();
        while (!listTask.IsCompleted)
        {
            yield return null;
        }

        if (loadGeneration != _deckListLoadGeneration)
        {
            yield break;
        }

        if (listTask.IsFaulted)
        {
            Debug.LogError($"[Deck] 一覧取得失敗: {listTask.Exception?.GetBaseException().Message}");
            RefreshDeckListLayoutDynamic(0);
            yield break;
        }

        List<DeckStorageEntry> entries = listTask.Result ?? new List<DeckStorageEntry>();
        Debug.Log(
            $"保存されたデッキ数: {entries.Count} "
            + $"({(DeckStorageService.IsUsingCloudStorage ? "Cloud" : "Local")})");
        RefreshDeckListCreatedCountLabel(entries.Count);

        int createdVisible = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            if (loadGeneration != _deckListLoadGeneration)
            {
                yield break;
            }

            DeckStorageEntry entry = entries[i];
            string storageKey = entry.StorageKey;
            string captureKey = storageKey;

            Task<DeckSaveData> loadTask = DeckStorageService.LoadDeckAsync(storageKey);
            while (!loadTask.IsCompleted)
            {
                yield return null;
            }

            if (loadGeneration != _deckListLoadGeneration)
            {
                yield break;
            }

            if (loadTask.IsFaulted)
            {
                Debug.LogWarning(
                    $"[Deck] 読込スキップ {storageKey}: {loadTask.Exception?.GetBaseException().Message}");
                continue;
            }

            DeckSaveData data = loadTask.Result;
            if (data == null)
            {
                continue;
            }

            GameObject cardObj = CreateDeckListItem(data, entry);
            createdVisible++;

            Button btn = cardObj.GetComponentInChildren<Button>();
            if (btn == null)
            {
                btn = cardObj.GetComponent<Button>();
            }

            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() =>
                {
                    Debug.Log(cardObj.name + " がクリックされました！");
                    Debug.Log(
                        $"エネミーフラグ:{BattoleStartFlag} "
                        + $"testPlayPick:{TestPlayMatchState.IsAwaitingEnemyDeckPick}");

                    if (TestPlayMatchState.IsAwaitingEnemyDeckPick)
                    {
                        Debug.Log("[TestPlay] 選択デッキを相手候補として UI に渡します。");
                        TestPlayOpponentDeckChosen?.Invoke(data, entry, captureKey);
                        return;
                    }

                    deckPathName = captureKey;

                    if (BattoleStartFlag)
                    {
                        Debug.Log(
                            "バトル開始フラグが立っているため、クリックされたデッキをエネミーデッキに入れます。");
                        enemyCardData.Clear();
                        foreach (var card in data.cards)
                        {
                            Debug.Log($"エネミーデッキに入れるカードID: {card.id}, 枚数: {card.count}");
                            enemyCardData[card.id] = card.count;
                        }

                        EnterBattleFromMenu();
                        return;
                    }

                    cardData.Clear();
                    _thumbnailCardId = data.thumbnailId;
                    ShowDeckActionButtons();
                    if (DeckTitleInputField != null)
                    {
                        DeckTitleInputField.text = data.title;
                    }

                    foreach (var card in data.cards)
                    {
                        Debug.Log($"クリックされたデッキのカードID: {card.id}, 枚数: {card.count}");
                        cardData[card.id] = card.count;
                    }

                    EnsureThumbnailCardId();
                });
            }

            // 行が増えるたびに高さと件数を更新（作成の都度スクロール範囲を伸ばす）
            if (createdVisible % 3 == 0 || i == entries.Count - 1)
            {
                RefreshDeckListLayoutDynamic(createdVisible, preciseMeasure: false);
            }
        }

        if (loadGeneration != _deckListLoadGeneration)
        {
            yield break;
        }

        // 最終件数でラベル・高さを確定（表示件数ベース）
        RefreshDeckListLayoutDynamic(createdVisible, preciseMeasure: true);
        yield return null;
        if (loadGeneration != _deckListLoadGeneration)
        {
            yield break;
        }

        EnsureDeckListAreaVisible();
        RefreshDeckListLayoutDynamic(createdVisible, preciseMeasure: true);
        // レイアウト確定後もう1フレーム（親 VLG の flexibleHeight 確定待ち）
        yield return null;
        if (loadGeneration != _deckListLoadGeneration)
        {
            yield break;
        }

        RefreshDeckListLayoutDynamic(createdVisible, preciseMeasure: true);
    }
}
