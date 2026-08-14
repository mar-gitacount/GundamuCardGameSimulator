using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.IO;
using System;
public class NewDeckMaking : MonoBehaviour
{
    [SerializeField] private GameObject DeckListPanel;
    [SerializeField] private Button NewDeckButton;
    [SerializeField] private GameObject DeckEditPanel;
    [SerializeField] private TextMeshProUGUI NewDeckText;

    [SerializeField] private Button DeckMakeButton;

    [SerializeField] private TMP_InputField DeckTitleInputField;

    [SerializeField] private Button DeckEditButton;
    
    [SerializeField] private Button DeckDeleteButton;

    [SerializeField] private Button DeckCopyButton;


     // 以下押下で、デッキ一覧のどれかをクリックすると、emeryCardData にカードデータを入れる。
    [SerializeField] private Button ButtleButton;

    [SerializeField] private Button OnlineBattleButton;

    [SerializeField] private Button TestPlayButton;

    private TestPlayOpponentSelectPanel _testPlaySelectPanel;
    

    // Start is called before the first frame update
    void Start()
    {
        NewDeckButton.onClick.AddListener(newDeckButtonClicked);
        
        DeckEditButton.onClick.AddListener(EditSelectedDeckClicked);
        DeckSettinObject.Instance.isDeckEditing = false;
        DeckMakeButton.onClick.AddListener(DeckMakeButtonClicked);
        RefreshDeckMakeButtonInteractable();
        DeckDeleteButton.onClick.AddListener(DeleteexecutionJsonFileToUseDeckSeetinObject);
        DeckCopyButton.onClick.AddListener(DeckCopyButtonClicked);
        EnsureOnlineBattleButton();
        if (OnlineBattleButton != null)
        {
            OnlineBattleButton.onClick.AddListener(OnlineBattleButtonClicked);
        }

        EnsureTestPlayButton();
        if (TestPlayButton != null)
        {
            TestPlayButton.onClick.AddListener(TestPlayButtonClicked);
        }

        if (DeckSettinObject.Instance != null)
        {
            DeckSettinObject.Instance.TestPlayOpponentDeckChosen += OnTestPlayOpponentDeckChosen;
        }

        // AIBattle は開発中。クローン生成後に Coming Soon を重ねて無効化する。
        ApplyAiBattleComingSoon();
    }

    private void OnDestroy()
    {
        if (DeckSettinObject.Instance != null)
        {
            DeckSettinObject.Instance.TestPlayOpponentDeckChosen -= OnTestPlayOpponentDeckChosen;
        }

        CloseTestPlayOpponentSelectUi();
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    private void ButtleButtonClicked()
    {
        // AIBattle は開発中のため未使用。有効化時に再接続する。
        DeckSettinObject.Instance.battleStart();
    }

    /// <summary>Battle を AIBattle 表示にし、Coming Soon 透かしで押下不可にする。</summary>
    private void ApplyAiBattleComingSoon()
    {
        if (ButtleButton == null)
        {
            return;
        }

        ButtleButton.gameObject.name = "AIBattleButton";
        ButtleButton.interactable = false;
        ButtleButton.onClick.RemoveAllListeners();

        TextMeshProUGUI label = ButtleButton.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null && label.transform.parent == ButtleButton.transform)
        {
            label.SetLocalizedText("AIBattle", "AIBattle");
        }

        Transform existing = ButtleButton.transform.Find("ComingSoonOverlay");
        if (existing != null)
        {
            existing.gameObject.SetActive(true);
            return;
        }

        GameObject overlay = new GameObject(
            "ComingSoonOverlay",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        overlay.transform.SetParent(ButtleButton.transform, false);
        overlay.SetFullSize();
        overlay.transform.SetAsLastSibling();

        Image dim = overlay.GetComponent<Image>();
        dim.color = new Color(0.08f, 0.08f, 0.1f, 0.55f);
        dim.raycastTarget = true;

        TextMeshProUGUI watermark = overlay.CreateChildTextCustom(
            "ComingSoonLabel",
            UIAnchor.FullStretch,
            0,
            0);
        watermark.raycastTarget = false;
        watermark.alignment = TextAlignmentOptions.Center;
        watermark.fontStyle = FontStyles.Bold;
        watermark.fontSize = 18f;
        watermark.color = new Color(1f, 1f, 1f, 0.82f);
        watermark.enableWordWrapping = false;
        watermark.overflowMode = TextOverflowModes.Overflow;
        watermark.SetLocalizedText("Coming Soon", "Coming Soon");

        RectTransform markRt = watermark.rectTransform;
        markRt.offsetMin = Vector2.zero;
        markRt.offsetMax = Vector2.zero;
        markRt.localEulerAngles = new Vector3(0f, 0f, -22f);
    }

    private void OnlineBattleButtonClicked()
    {
        DeckSettinObject deckSettings = DeckSettinObject.Instance;
        if (deckSettings == null)
        {
            Debug.LogWarning("[Online] DeckSettinObject not found.");
            return;
        }

        if (!deckSettings.HasSelectedPlayerDeck())
        {
            Debug.LogWarning("[Online] Select a deck from the list before Online Battle.");
            return;
        }

        List<CardData> bannedCards = deckSettings.CollectNotUsedOnlineCardsInSelectedDeck();
        if (bannedCards != null && bannedCards.Count > 0)
        {
            ShowNotUsedOnlineAlert(bannedCards);
            return;
        }

        TestPlayMatchState.Clear();
        deckSettings.ClearBattleStartFlag();
        EosOnlinePlaytestController.OpenPanel();
    }

    private void TestPlayButtonClicked()
    {
        DeckSettinObject deckSettings = DeckSettinObject.Instance;
        if (deckSettings == null)
        {
            Debug.LogWarning("[TestPlay] DeckSettinObject not found.");
            return;
        }

        if (!deckSettings.HasSelectedPlayerDeck())
        {
            Debug.LogWarning("[TestPlay] Select your deck from the list first, then press TestPlay, then select the enemy deck.");
            return;
        }

        // AI バトルの BattoleStartFlag とは別。敵デッキ選択待ちにする。
        deckSettings.ClearBattleStartFlag();
        deckSettings.HideDeckActionButtons();
        TestPlayMatchState.BeginEnemyDeckPick();
        ShowTestPlayOpponentSelectUi();
    }

    private void ShowTestPlayOpponentSelectUi()
    {
        DeckSettinObject deckSettings = DeckSettinObject.Instance;
        if (deckSettings == null)
        {
            return;
        }

        RectTransform board = ResolveTestPlaySelectBoard();
        if (board == null)
        {
            Debug.LogWarning("[TestPlay] 選択 UI の親盤面が見つかりません。");
            return;
        }

        if (_testPlaySelectPanel == null)
        {
            _testPlaySelectPanel = new TestPlayOpponentSelectPanel();
        }

        _testPlaySelectPanel.Show(
            board,
            deckSettings.CaptureCurrentPlayerDeckPick(),
            OnTestPlaySelectOk,
            OnTestPlaySelectCancel);
    }

    private RectTransform ResolveTestPlaySelectBoard()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            canvas = canvas.rootCanvas;
        }

        if (canvas == null)
        {
            canvas = FindObjectOfType<Canvas>();
        }

        return canvas != null ? canvas.GetComponent<RectTransform>() : null;
    }

    private void OnTestPlayOpponentDeckChosen(DeckSaveData data, DeckStorageEntry entry, string storageKey)
    {
        if (_testPlaySelectPanel == null || !_testPlaySelectPanel.IsOpen)
        {
            ShowTestPlayOpponentSelectUi();
        }

        if (_testPlaySelectPanel == null)
        {
            return;
        }

        _testPlaySelectPanel.AssignDeckFromList(TestPlayDeckPick.FromSaveData(data, entry, storageKey));
    }

    private void OnTestPlaySelectOk()
    {
        DeckSettinObject deckSettings = DeckSettinObject.Instance;
        if (deckSettings == null || _testPlaySelectPanel == null)
        {
            return;
        }

        TestPlayDeckPick player = _testPlaySelectPanel.PlayerPick;
        TestPlayDeckPick enemy = _testPlaySelectPanel.EnemyPick;
        CloseTestPlayOpponentSelectUi();
        deckSettings.ApplyTestPlayDecksAndStart(player, enemy);
    }

    private void OnTestPlaySelectCancel()
    {
        if (DeckSettinObject.Instance != null)
        {
            DeckSettinObject.Instance.ClearTestPlayBattleObjects();
        }

        CloseTestPlayOpponentSelectUi();
    }

    private void CloseTestPlayOpponentSelectUi()
    {
        if (_testPlaySelectPanel != null)
        {
            _testPlaySelectPanel.Close();
            _testPlaySelectPanel = null;
        }
    }

    private GameObject _notUsedOnlineAlertRoot;
    private GameObject _saveConfirmRoot;

    private void DeckMakeButtonClicked()
    {
        if (DeckSettinObject.Instance == null || !DeckSettinObject.Instance.isDeckEditing)
        {
            return;
        }

        ShowSaveEditsConfirm();
    }

    /// <summary>Deck Make は編集中だけ押せる。</summary>
    private void RefreshDeckMakeButtonInteractable()
    {
        if (DeckMakeButton == null)
        {
            return;
        }

        bool editing = DeckSettinObject.Instance != null && DeckSettinObject.Instance.isDeckEditing;
        DeckMakeButton.interactable = editing;
    }

    /// <summary>編集を保存するか確認し、OK なら保存してデッキ一覧へ戻る。</summary>
    private void ShowSaveEditsConfirm()
    {
        ShowTwoButtonConfirm(
            "編集を保存しますか？",
            "Save your edits?",
            "OK",
            "OK",
            "キャンセル",
            "Cancel",
            () => StartCoroutine(SaveEditsAndReturnToListCoroutine()),
            null);
    }

    /// <summary>Cancel 押下。差分があれば適用確認、なければ一覧へ戻る。</summary>
    private void TryCancelDeckEdit()
    {
        string currentTitle = DeckTitleInputField != null ? DeckTitleInputField.text : string.Empty;
        if (DeckSettinObject.Instance != null
            && DeckSettinObject.Instance.HasChangesFromEditBaseline(currentTitle))
        {
            ShowTwoButtonConfirm(
                "変更がありますが、変更しますか？",
                "You have changes. Apply them?",
                "OK",
                "OK",
                "No",
                "No",
                () => StartCoroutine(SaveEditsAndReturnToListCoroutine()),
                () => StartCoroutine(DiscardEditsAndReturnToListCoroutine()));
            return;
        }

        ReturnToDeckListAfterEdit();
    }

    private void ShowTwoButtonConfirm(
        string promptJa,
        string promptEn,
        string okJa,
        string okEn,
        string noJa,
        string noEn,
        UnityEngine.Events.UnityAction onOk,
        UnityEngine.Events.UnityAction onNo)
    {
        CloseSaveEditsConfirm();

        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null)
        {
            canvas = FindObjectOfType<Canvas>();
        }

        if (canvas == null)
        {
            Debug.LogWarning("[Deck] Canvas not found for confirm.");
            return;
        }

        _saveConfirmRoot = new GameObject(
            "DeckEditConfirm",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        _saveConfirmRoot.transform.SetParent(canvas.transform, false);
        _saveConfirmRoot.transform.SetAsLastSibling();
        _saveConfirmRoot.SetFullSize();

        Image dim = _saveConfirmRoot.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.65f);
        dim.raycastTarget = true;

        GameObject panel = _saveConfirmRoot.CreateChildPanelCustom("ConfirmPanel", UIAnchor.FullSize, 360, 200);
        Image panelImage = panel.GetComponent<Image>();
        if (panelImage != null)
        {
            panelImage.color = new Color(0.16f, 0.16f, 0.18f, 0.96f);
        }

        RectTransform panelRt = panel.GetComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0.5f, 0.5f);
        panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(360f, 220f);
        panelRt.anchoredPosition = Vector2.zero;

        TextMeshProUGUI prompt = panel.CreateChildTextCustom("ConfirmPrompt", UIAnchor.TopCenter, 320, 90);
        prompt.SetLocalizedText(promptJa, promptEn);
        prompt.fontSize = 20;
        prompt.fontStyle = FontStyles.Bold;
        prompt.alignment = TextAlignmentOptions.Center;
        prompt.enableWordWrapping = true;
        prompt.color = Color.white;
        RectTransform promptRt = prompt.GetComponent<RectTransform>();
        promptRt.anchoredPosition = new Vector2(0f, -24f);

        Button okBtn = panel.CreateChildButton(GameLocale.T(okJa, okEn));
        RectTransform okRt = okBtn.GetComponent<RectTransform>();
        okRt.sizeDelta = new Vector2(130f, 44f);
        okRt.anchorMin = new Vector2(0.5f, 0f);
        okRt.anchorMax = new Vector2(0.5f, 0f);
        okRt.pivot = new Vector2(0.5f, 0f);
        okRt.anchoredPosition = new Vector2(-80f, 24f);
        TextMeshProUGUI okLabel = okBtn.GetComponentInChildren<TextMeshProUGUI>(true);
        if (okLabel != null)
        {
            okLabel.SetLocalizedText(okJa, okEn);
        }

        okBtn.onClick.AddListener(() =>
        {
            CloseSaveEditsConfirm();
            onOk?.Invoke();
        });

        Button noBtn = panel.CreateChildButton(GameLocale.T(noJa, noEn));
        RectTransform noRt = noBtn.GetComponent<RectTransform>();
        noRt.sizeDelta = new Vector2(130f, 44f);
        noRt.anchorMin = new Vector2(0.5f, 0f);
        noRt.anchorMax = new Vector2(0.5f, 0f);
        noRt.pivot = new Vector2(0.5f, 0f);
        noRt.anchoredPosition = new Vector2(80f, 24f);
        TextMeshProUGUI noLabel = noBtn.GetComponentInChildren<TextMeshProUGUI>(true);
        if (noLabel != null)
        {
            noLabel.SetLocalizedText(noJa, noEn);
        }

        noBtn.onClick.AddListener(() =>
        {
            CloseSaveEditsConfirm();
            if (onNo != null)
            {
                onNo.Invoke();
            }
        });
    }

    private void CloseSaveEditsConfirm()
    {
        if (_saveConfirmRoot != null)
        {
            Destroy(_saveConfirmRoot);
            _saveConfirmRoot = null;
        }
    }

    private IEnumerator SaveEditsAndReturnToListCoroutine()
    {
        if (DeckSettinObject.Instance != null)
        {
            yield return DeckSettinObject.Instance.SaveCurrentDeckCoroutine();
        }

        ReturnToDeckListAfterEdit();
    }

    private IEnumerator DiscardEditsAndReturnToListCoroutine()
    {
        if (DeckSettinObject.Instance != null)
        {
            string restoredTitle = DeckSettinObject.Instance.RestoreEditBaseline();
            if (DeckTitleInputField != null)
            {
                DeckTitleInputField.text = restoredTitle;
            }

            if (DeckSettinObject.Instance.HasEditBaselineStorageKey())
            {
                yield return DeckSettinObject.Instance.SaveCurrentDeckCoroutine();
            }
        }

        ReturnToDeckListAfterEdit();
    }

    /// <summary>編集画面を閉じてデッキ一覧へ戻す。</summary>
    private void ReturnToDeckListAfterEdit()
    {
        if (DeckSettinObject.Instance != null)
        {
            DeckSettinObject.Instance.isDeckEditing = false;
            DeckSettinObject.Instance.HideDeckEditCountUi();
            DeckSettinObject.Instance.HideDeckActionButtons();
            DeckSettinObject.Instance.ClearDeckList();
            DeckSettinObject.Instance.ShowFileList();
        }

        if (DeckEditPanel != null)
        {
            DeckEditPanel.gameObject.SetActive(false);
        }

        if (DeckListPanel != null)
        {
            DeckListPanel.gameObject.SetActive(true);
        }

        if (NewDeckText != null)
        {
            NewDeckText.SetLocalizedText("NewDeck", "New Deck");
        }

        if (DeckTitleInputField != null)
        {
            DeckTitleInputField.text = "";
            DeckTitleInputField.gameObject.SetActive(false);
        }

        Debug.Log("デッキを保存し、一覧画面に戻りました。");
        RefreshDeckMakeButtonInteractable();
    }

    private void ShowNotUsedOnlineAlert(List<CardData> bannedCards)
    {
        CloseNotUsedOnlineAlert();

        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null)
        {
            canvas = FindObjectOfType<Canvas>();
        }

        if (canvas == null)
        {
            Debug.LogWarning("[Online] Canvas not found for Not Used Online alert.");
            return;
        }

        _notUsedOnlineAlertRoot = new GameObject(
            "NotUsedOnlineAlert",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        _notUsedOnlineAlertRoot.transform.SetParent(canvas.transform, false);
        _notUsedOnlineAlertRoot.transform.SetAsLastSibling();
        _notUsedOnlineAlertRoot.SetFullSize();

        Image dim = _notUsedOnlineAlertRoot.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.65f);
        dim.raycastTarget = true;

        TextMeshProUGUI title = _notUsedOnlineAlertRoot.CreateChildTextCustom(
            "NotUsedOnlineTitle",
            UIAnchor.TopCenter,
            720,
            48);
        title.text = "Not used card online";
        title.fontSize = 30;
        title.fontStyle = FontStyles.Bold;
        title.alignment = TextAlignmentOptions.Center;
        title.color = Color.white;
        RectTransform titleRt = title.GetComponent<RectTransform>();
        titleRt.anchoredPosition = new Vector2(0f, -36f);

        TextMeshProUGUI subtitle = _notUsedOnlineAlertRoot.CreateChildTextCustom(
            "NotUsedOnlineSubtitle",
            UIAnchor.TopCenter,
            720,
            36);
        subtitle.text = "These cards cannot be used in Online Battle.";
        subtitle.fontSize = 18;
        subtitle.alignment = TextAlignmentOptions.Center;
        subtitle.color = new Color(0.9f, 0.9f, 0.9f);
        RectTransform subtitleRt = subtitle.GetComponent<RectTransform>();
        subtitleRt.anchoredPosition = new Vector2(0f, -78f);

        GameObject scrollGo = _notUsedOnlineAlertRoot.CreateGridScrollView(680, 360, UIAnchor.TopCenter);
        RectTransform scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.anchoredPosition = new Vector2(0f, -300f);
        scrollGo.ConfigureGridCellFromViewportHeight(0.78f, 56f);
        ScrollRect sr = scrollGo.GetComponent<ScrollRect>();
        RectTransform content = sr != null ? sr.content : null;

        for (int i = 0; i < bannedCards.Count; i++)
        {
            CardData data = bannedCards[i];
            if (data == null || content == null)
            {
                continue;
            }

            GameObject cardGo = new GameObject(
                $"BannedCard_{data.id}",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(LayoutElement));
            cardGo.transform.SetParent(content, false);
            Image img = cardGo.GetComponent<Image>();
            img.sprite = data.imageName != null ? data.imageName : data.image;
            img.preserveAspect = true;
            LayoutElement layout = cardGo.GetComponent<LayoutElement>();
            layout.preferredWidth = 100f;
            layout.preferredHeight = 140f;

            Card.EnsureNotUsedOnlineLabel(cardGo, data);
        }

        Button okBtn = _notUsedOnlineAlertRoot.CreateChildButton("OK");
        RectTransform okRt = okBtn.GetComponent<RectTransform>();
        okRt.sizeDelta = new Vector2(160f, 48f);
        okRt.anchorMin = new Vector2(0.5f, 0f);
        okRt.anchorMax = new Vector2(0.5f, 0f);
        okRt.pivot = new Vector2(0.5f, 0f);
        okRt.anchoredPosition = new Vector2(0f, 36f);
        okBtn.onClick.AddListener(CloseNotUsedOnlineAlert);
    }

    private void CloseNotUsedOnlineAlert()
    {
        if (_notUsedOnlineAlertRoot != null)
        {
            Destroy(_notUsedOnlineAlertRoot);
            _notUsedOnlineAlertRoot = null;
        }
    }

    private void EnsureOnlineBattleButton()
    {
        if (ButtleButton == null)
        {
            return;
        }

        if (OnlineBattleButton == null)
        {
            GameObject clone = Instantiate(ButtleButton.gameObject, ButtleButton.transform.parent);
            clone.name = "OnlineBattleButton";
            OnlineBattleButton = clone.GetComponent<Button>();
            if (OnlineBattleButton == null)
            {
                OnlineBattleButton = clone.AddComponent<Button>();
            }

            TextMeshProUGUI label = clone.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null)
            {
                label.text = "Online Battle";
            }
        }

        // Battle(-450) の直下 → -550
        PlaceDeckMenuButtonAtY(OnlineBattleButton, ButtleButton, -550f);
    }

    private void EnsureTestPlayButton()
    {
        if (ButtleButton == null)
        {
            return;
        }

        if (TestPlayButton == null)
        {
            GameObject clone = Instantiate(ButtleButton.gameObject, ButtleButton.transform.parent);
            clone.name = "TestPlayButton";
            TestPlayButton = clone.GetComponent<Button>();
            if (TestPlayButton == null)
            {
                TestPlayButton = clone.AddComponent<Button>();
            }

            TextMeshProUGUI label = clone.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null)
            {
                label.SetLocalizedText("TestPlay", "TestPlay");
            }
        }

        // Online Battle(-550) の直下 → -650
        PlaceDeckMenuButtonAtY(TestPlayButton, ButtleButton, -650f);
    }

    /// <summary>デッキ操作ボタンを Battle と同じアンカーで指定 Y に置く。</summary>
    private static void PlaceDeckMenuButtonAtY(Button target, Button battleButton, float anchoredY)
    {
        if (target == null || battleButton == null)
        {
            return;
        }

        RectTransform targetRt = target.GetComponent<RectTransform>();
        RectTransform battleRt = battleButton.GetComponent<RectTransform>();
        if (targetRt == null || battleRt == null)
        {
            return;
        }

        targetRt.anchorMin = battleRt.anchorMin;
        targetRt.anchorMax = battleRt.anchorMax;
        targetRt.pivot = battleRt.pivot;
        targetRt.sizeDelta = battleRt.sizeDelta;
        targetRt.anchoredPosition = new Vector2(battleRt.anchoredPosition.x, anchoredY);
        targetRt.localScale = battleRt.localScale;
    }

    private void DeleteexecutionJsonFileToUseDeckSeetinObject()
    {
        // 1. ファイルが存在するか確認
        DeckSettinObject.Instance.DeleteJsonFile();
        
    }
    private void DeckCopyButtonClicked()
    {
        // 1. ファイルが存在するか確認
        DeckSettinObject.Instance.CopyJsonFile();
        DeckSettinObject.Instance.ClearDeckList();
        // デッキリストを再表示する
        DeckSettinObject.Instance.ShowFileList();
    }
    private void newDeckButtonClicked()
    {
        if (DeckSettinObject.Instance.isDeckEditing)
        {
            TryCancelDeckEdit();
            return;
        }

        // 新規作成。選択中の既存デッキパスを残すと保存時に上書きしてしまう。
        BeginDeckEdit(asNewDeck: true);
    }

    private void EditSelectedDeckClicked()
    {
        if (DeckSettinObject.Instance != null && DeckSettinObject.Instance.isDeckEditing)
        {
            return;
        }

        BeginDeckEdit(asNewDeck: false);
    }

    private void BeginDeckEdit(bool asNewDeck)
    {
        if (DeckSettinObject.Instance == null)
        {
            return;
        }

        if (asNewDeck)
        {
            DeckSettinObject.Instance.BeginNewEmptyDeck();
            if (DeckTitleInputField != null)
            {
                DeckTitleInputField.text = string.Empty;
            }
        }

        DeckSettinObject.Instance.isDeckEditing = true;
        if (NewDeckText != null)
        {
            NewDeckText.SetLocalizedText("キャンセル", "Cancel");
        }

        if (DeckListPanel != null)
        {
            DeckListPanel.gameObject.SetActive(false);
        }

        if (DeckTitleInputField != null)
        {
            DeckTitleInputField.gameObject.SetActive(true);
        }

        DeckSettinObject.Instance.EnsureDeckEditUiVisible();

        if (DeckEditPanel != null)
        {
            DeckEditPanel.gameObject.SetActive(true);
            DeckEditPanel editPanel = DeckEditPanel.GetComponent<DeckEditPanel>();
            if (editPanel != null)
            {
                editPanel.LoadDeckToEditPanel();
            }
        }

        DeckSettinObject.Instance.RefreshDeckEditCountDisplays();
        DeckSettinObject.Instance.RefreshThumbnailFrames();
        string startTitle = DeckTitleInputField != null ? DeckTitleInputField.text : string.Empty;
        DeckSettinObject.Instance.CaptureEditBaseline(startTitle);
        RefreshDeckMakeButtonInteractable();
        Debug.Log($"ボタン:{DeckSettinObject.Instance.isDeckEditing} newDeck:{asNewDeck}");
    }
}
