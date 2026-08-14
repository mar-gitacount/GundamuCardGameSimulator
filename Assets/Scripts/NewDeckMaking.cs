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
    

    // Start is called before the first frame update
    void Start()
    {
        NewDeckButton.onClick.AddListener(newDeckButtonClicked);
        
        DeckEditButton.onClick.AddListener(newDeckButtonClicked);
        DeckSettinObject.Instance.isDeckEditing = false;
        DeckMakeButton.onClick.AddListener(DeckMakeButtonClicked);
        DeckDeleteButton.onClick.AddListener(DeleteexecutionJsonFileToUseDeckSeetinObject);
        DeckCopyButton.onClick.AddListener(DeckCopyButtonClicked);
        ButtleButton.onClick.AddListener(ButtleButtonClicked);
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
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    private void ButtleButtonClicked()
    {
        DeckSettinObject.Instance.battleStart();
        // Debug.Log($"バトル開始フラグ:{DeckSettinObject.Instance.BattleStartFlag}");
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
        TestPlayMatchState.BeginEnemyDeckPick();
    }

    private GameObject _notUsedOnlineAlertRoot;

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
        if (OnlineBattleButton != null || ButtleButton == null)
        {
            return;
        }

        GameObject clone = Instantiate(ButtleButton.gameObject, ButtleButton.transform.parent);
        clone.name = "OnlineBattleButton";

        OnlineBattleButton = clone.GetComponent<Button>();
        if (OnlineBattleButton == null)
        {
            OnlineBattleButton = clone.AddComponent<Button>();
        }

        RectTransform cloneRect = clone.GetComponent<RectTransform>();
        RectTransform battleRect = ButtleButton.GetComponent<RectTransform>();
        if (cloneRect != null && battleRect != null)
        {
            cloneRect.anchoredPosition = battleRect.anchoredPosition + new Vector2(0f, -56f);
        }

        TextMeshProUGUI label = clone.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null)
        {
            label.text = "Online Battle";
        }
    }

    private void EnsureTestPlayButton()
    {
        if (TestPlayButton != null || ButtleButton == null)
        {
            return;
        }

        Button source = OnlineBattleButton != null ? OnlineBattleButton : ButtleButton;
        GameObject clone = Instantiate(source.gameObject, ButtleButton.transform.parent);
        clone.name = "TestPlayButton";

        TestPlayButton = clone.GetComponent<Button>();
        if (TestPlayButton == null)
        {
            TestPlayButton = clone.AddComponent<Button>();
        }

        RectTransform cloneRect = clone.GetComponent<RectTransform>();
        RectTransform sourceRect = source.GetComponent<RectTransform>();
        if (cloneRect != null && sourceRect != null)
        {
            cloneRect.anchoredPosition = sourceRect.anchoredPosition + new Vector2(0f, -56f);
        }

        TextMeshProUGUI label = clone.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null)
        {
            label.SetLocalizedText("TestPlay", "TestPlay");
        }
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
        
        if(DeckSettinObject.Instance.isDeckEditing)
        {
            DeckSettinObject.Instance.isDeckEditing = false;
            DeckEditPanel.gameObject.SetActive(false);
            DeckListPanel.gameObject.SetActive(true);
           
            // DeckSettinObject.Instance.ShowFileList();
            Debug.Log("デッキ編集モードを終了してデッキリストに戻ります。");
            NewDeckText.text = "NewDeck";

            // デッキリストを空にする
            DeckSettinObject.Instance.ClearDeckList();
            // デッキリストを再表示する
            DeckSettinObject.Instance.ShowFileList();
            DeckSettinObject.Instance.HideDeckEditCountUi();
            DeckSettinObject.Instance.HideDeckActionButtons();
            DeckTitleInputField.text = "";
            DeckTitleInputField.gameObject.SetActive(false);
        }
        else
        {
            DeckSettinObject.Instance.isDeckEditing = true;
            NewDeckText.text = "Editing Now ..";
            DeckListPanel.gameObject.SetActive(false);
            DeckTitleInputField.gameObject.SetActive(true);
            DeckSettinObject.Instance.EnsureDeckEditUiVisible();

            DeckEditPanel.gameObject.SetActive(true);
            DeckEditPanel editPanel = DeckEditPanel.GetComponent<DeckEditPanel>();
            editPanel.LoadDeckToEditPanel();
            DeckSettinObject.Instance.RefreshDeckEditCountDisplays();
            return;


        }
        Debug.Log($"ボタン:{DeckSettinObject.Instance.isDeckEditing}");
        
        
        // DeckListPanel.gameObject.SetActive(false);
    }


    private void DeckMakeButtonClicked()
    {
        DeckSettinObject.Instance.CardDataToJson();
        return;
        Debug.Log("デッキ作成ボタンがクリックされました。");
        string path = Application.persistentDataPath;
        // ここでデッキデータをJSONに変換して保存する処理を実装します。
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

        // 2. ファイル名を作成
        string fileName = "Deck_" + timestamp + ".json";

        // 3. 保存先のフルパスを作成
        string fullPath = Path.Combine(Application.persistentDataPath, fileName);

        // 確認用ログ
        Debug.Log("保存パス: " + fullPath);
    }
}
