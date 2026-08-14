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
    private const float HomeBoardDesignWidth = 480f;
    private const float HomeBoardDesignHeight = 800f;
    private const float DeckListCellWidth = 140f;
    private const float DeckListCellHeight = 228f;
    private TextMeshProUGUI _deckTotalCountLabel;
    private GameObject _deckTotalCountLabelRoot;
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
        if (DeckListPanel != null)
        {
            DeckListPanel.transform.DetachChildren(); // デッキリストの子オブジェクトを全て削除
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
        if (cardImage != null && cardDataAsset.imageName != null)
        {
            cardImage.sprite = cardDataAsset.imageName;
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

        Image panelImage = DeckListPanel.GetComponent<Image>();
        if (panelImage != null)
        {
            panelImage.color = new Color(0f, 0f, 0f, 0.2f);
            if (panelImage.sprite == null)
            {
                panelImage.sprite = GetUiWhiteSprite();
            }
        }
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
        Sprite thumbSprite = ResolveCardSprite(thumbId);

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
        thumbImage.sprite = thumbSprite;
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
        if (cardId <= 0)
        {
            return null;
        }

        if (CardDatabase.Instance != null)
        {
            CardData data = CardDatabase.Instance.FindById(cardId);
            if (data != null && data.imageName != null)
            {
                return data.imageName;
            }
        }

        CardData[] all = Resources.LoadAll<CardData>("Data/Cards");
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].id == cardId && all[i].imageName != null)
            {
                return all[i].imageName;
            }
        }

        return null;
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
    if (DeckListPanel != null)
    {
        DeckListPanel.transform.DetachChildren();
    }

    ConfigureDeckListGridLayout();

    Task<List<DeckStorageEntry>> listTask = DeckStorageService.ListDecksAsync();
    while (!listTask.IsCompleted)
    {
        yield return null;
    }

    if (listTask.IsFaulted)
    {
        Debug.LogError($"[Deck] 一覧取得失敗: {listTask.Exception?.GetBaseException().Message}");
        yield break;
    }

    List<DeckStorageEntry> entries = listTask.Result;
    Debug.Log($"保存されたデッキ数: {entries.Count} ({(DeckStorageService.IsUsingCloudStorage ? "Cloud" : "Local")})");

    for (int i = 0; i < entries.Count; i++)
    {
        DeckStorageEntry entry = entries[i];
        string storageKey = entry.StorageKey;
        string captureKey = storageKey;

        Task<DeckSaveData> loadTask = DeckStorageService.LoadDeckAsync(storageKey);
        while (!loadTask.IsCompleted)
        {
            yield return null;
        }

        if (loadTask.IsFaulted)
        {
            Debug.LogWarning($"[Deck] 読込スキップ {storageKey}: {loadTask.Exception?.GetBaseException().Message}");
            continue;
        }

        DeckSaveData data = loadTask.Result;
        if (data == null)
        {
            continue;
        }

    GameObject cardObj = CreateDeckListItem(data, entry);

    Button btn = cardObj.GetComponentInChildren<Button>();
    if (btn == null)
    {
        btn = cardObj.GetComponent<Button>();
    }

    if (btn != null)
    {
    btn.onClick.RemoveAllListeners();
    btn.onClick.AddListener(() => {
        Debug.Log(cardObj.name + " がクリックされました！");
        Debug.Log($"エネミーフラグ:{BattoleStartFlag} testPlayPick:{TestPlayMatchState.IsAwaitingEnemyDeckPick}");

        // TestPlay: 一覧タップは開始せず、選択 UI へ渡す
        if (TestPlayMatchState.IsAwaitingEnemyDeckPick)
        {
            Debug.Log("[TestPlay] 選択デッキを相手候補として UI に渡します。");
            TestPlayOpponentDeckChosen?.Invoke(data, entry, captureKey);
            return;
        }

        deckPathName = captureKey;

        if(BattoleStartFlag)
        {
            Debug.Log("バトル開始フラグが立っているため、クリックされたデッキをエネミーデッキに入れます。");
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
        DeckTitleInputField.text = data.title;
        foreach (var card in data.cards)
        {
            Debug.Log($"クリックされたデッキのカードID: {card.id}, 枚数: {card.count}");
            cardData[card.id] = card.count;
        }

        EnsureThumbnailCardId();
    });
    }
    }
}
}
