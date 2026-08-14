using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;

public class ScrollBottomChecker : MonoBehaviour
{
    private ScrollRect scrollRect;
    [SerializeField] private GameObject imagePrefab;
    private bool isLoading = false;
    [SerializeField] private RectTransform content;
    [SerializeField] private CardDatabase CardDatabaseObj;

    [SerializeField] private Button SearchButton;

    [SerializeField] private Button SerachFindButton;
    // 検索ワード
    [SerializeField] private TMP_InputField SearchInputField;
    [SerializeField] private Canvas Searchcanvas;
    private int loadCount = 0;

    private List<CardData> allCards = new List<CardData>();

    // カードディスプレイの数
    private int displayCardCount = 0;
    [SerializeField] private IncludedCards IncludedCardsObj;
    [SerializeField] private Button ClearSearchButton;
  



    void Awake()
    {
        SerachFindButton.onClick.AddListener(SerchButtonClickedToFind);
        WireSearchClearButton();
        ApplySearchScreenLocale();
        GameLocale.LanguageChanged += OnSearchLanguageChanged;

        scrollRect = GetComponent<ScrollRect>();

        scrollRect.onValueChanged.AddListener(OnScroll);
        //? データベースインスタンス
        CardData card = CardDatabaseObj.GetComponent<CardDatabase>().GetById(1);
        
        
        CardDatabase db = CardDatabase.Instance;
        // db.LoadAllCards();
        CardData testData = db.GetById(0);

        // 以下すべてのカードデータを取得する例
        allCards = db.GetAllCards();
        Debug.Log("全カードデータの数: " + allCards.Count);
        // 例:40枚のカードデータがある場合
        displayCardCount = allCards.Count;
        SearchButton.onClick.AddListener(SearchButtonClicked);
        

        AddImages(5);
        cardsRemove();
        Debug.Log("取得データ: " + testData.cardName);
        CardData[] cards = Resources.LoadAll<CardData>("Data/Cards");
        Debug.Log("読み込めた数: " + cards.Length);
        // foreach (var card in cards)
        // {
        //     Debug.Log($"カード読み込み: ID={card.id}, 名前={card.cardName}");
            // cardDict[card.id] = card;
        // }
        

    }

    private void OnDestroy()
    {
        GameLocale.LanguageChanged -= OnSearchLanguageChanged;
    }

    private void OnSearchLanguageChanged(GameLanguage _)
    {
        ApplySearchScreenLocale();
    }

    private void SearchButtonClicked()
    {
        // 検索ボタンがクリックされたときの処理
        Debug.Log("Search button clicked!");
        Searchcanvas.gameObject.SetActive(true);
        ApplySearchScreenLocale();
        foreach (Transform child in content)
        {
            Destroy(child.gameObject);
        }
    }

    private void WireSearchClearButton()
    {
        if (ClearSearchButton == null && Searchcanvas != null)
        {
            Button[] buttons = Searchcanvas.GetComponentsInChildren<Button>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                Button button = buttons[i];
                if (button == null)
                {
                    continue;
                }

                TMP_Text tmp = button.GetComponentInChildren<TMP_Text>(true);
                string label = NormalizeUiText(tmp != null ? tmp.text : string.Empty);
                if (label == "クリア" || label.Equals("Clear", System.StringComparison.OrdinalIgnoreCase)
                    || label.Equals("clea", System.StringComparison.OrdinalIgnoreCase))
                {
                    ClearSearchButton = button;
                    break;
                }
            }
        }

        if (ClearSearchButton == null)
        {
            return;
        }

        ClearSearchButton.onClick.RemoveListener(ClearSearchUi);
        ClearSearchButton.onClick.AddListener(ClearSearchUi);
    }

    /// <summary>検索画面の固定文言を現行言語へ合わせる。</summary>
    private void ApplySearchScreenLocale()
    {
        if (Searchcanvas == null)
        {
            return;
        }

        TMP_Text[] tmps = Searchcanvas.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < tmps.Length; i++)
        {
            ApplySearchLabel(tmps[i]);
        }

        Text[] legacyTexts = Searchcanvas.GetComponentsInChildren<Text>(true);
        for (int i = 0; i < legacyTexts.Length; i++)
        {
            Text legacy = legacyTexts[i];
            if (legacy == null || !TryMapSearchLabel(NormalizeUiText(legacy.text), out string ja, out string en))
            {
                continue;
            }

            legacy.text = GameLocale.T(ja, en);
        }

        if (SearchInputField != null && SearchInputField.placeholder is TMP_Text placeholder)
        {
            LocalizedTmpText loc = placeholder.GetComponent<LocalizedTmpText>();
            if (loc == null)
            {
                loc = placeholder.gameObject.AddComponent<LocalizedTmpText>();
            }

            loc.SetTexts("カード名を入力", "Enter card name");
        }

        if (IncludedCardsObj != null)
        {
            IncludedCardsObj.RefreshLocalizedLabels();
        }

        ApplyAllIncludedCardLocale();
    }

    private static void ApplySearchLabel(TMP_Text tmp)
    {
        if (tmp == null)
        {
            return;
        }

        LocalizedTmpText loc = tmp.GetComponent<LocalizedTmpText>();
        if (loc != null)
        {
            loc.Refresh();
            return;
        }

        if (!TryMapSearchLabel(NormalizeUiText(tmp.text), out string ja, out string en))
        {
            return;
        }

        loc = tmp.gameObject.AddComponent<LocalizedTmpText>();
        loc.SetTexts(ja, en);
    }

    private static bool TryMapSearchLabel(string normalized, out string japanese, out string english)
    {
        japanese = string.Empty;
        english = string.Empty;
        if (string.IsNullOrEmpty(normalized))
        {
            return false;
        }

        if (normalized == "検索" || normalized.Equals("Search", System.StringComparison.OrdinalIgnoreCase))
        {
            japanese = "検索";
            english = "Search";
            return true;
        }

        if (normalized == "カード名" || normalized.Equals("Card Name", System.StringComparison.OrdinalIgnoreCase))
        {
            japanese = "カード名";
            english = "Card Name";
            return true;
        }

        if (normalized == "収録カード" || normalized.Equals("Included Cards", System.StringComparison.OrdinalIgnoreCase))
        {
            japanese = "収録カード";
            english = "Included Cards";
            return true;
        }

        if (normalized == "フリーワード" || normalized.Equals("Free Word", System.StringComparison.OrdinalIgnoreCase))
        {
            japanese = "フリーワード";
            english = "Free Word";
            return true;
        }

        if (normalized == "詳細検索" || normalized.Equals("Advanced Search", System.StringComparison.OrdinalIgnoreCase))
        {
            japanese = "詳細検索";
            english = "Advanced Search";
            return true;
        }

        if (normalized == "作品検索" || normalized.Equals("Set Search", System.StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("IncludeIn", System.StringComparison.OrdinalIgnoreCase))
        {
            japanese = "作品検索";
            english = "Set Search";
            return true;
        }

        if (normalized == "クリア" || normalized.Equals("Clear", System.StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("clea", System.StringComparison.OrdinalIgnoreCase))
        {
            japanese = "クリア";
            english = "Clear";
            return true;
        }

        return false;
    }

    private static string NormalizeUiText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.Replace("\u200B", string.Empty).Trim();
    }

    /// <summary>入力文字とチェックボックスをすべて解除し、一覧を未フィルタに戻す。</summary>
    private void ClearSearchUi()
    {
        if (SearchInputField != null)
        {
            SearchInputField.text = string.Empty;
        }

        if (IncludedCardsObj != null)
        {
            IncludedCardsObj.ClearAllToggles();
        }

        ClearAllIncludedCardToggles();
        ReloadFullCatalog();
    }

    private void ReloadFullCatalog()
    {
        allCards.Clear();
        if (CardDatabase.Instance != null)
        {
            allCards.AddRange(CardDatabase.Instance.GetAllCards());
        }

        displayCardCount = allCards.Count;
        if (content != null)
        {
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                Destroy(content.GetChild(i).gameObject);
            }
        }

        AddImages(Mathf.Min(5, allCards.Count));
        cardsRemove();
    }

    private List<CardData> ApplyAllIncludedCardFilters(List<CardData> cards)
    {
        IncludedCards[] filters = GetSearchIncludedCards();
        for (int i = 0; i < filters.Length; i++)
        {
            if (filters[i] != null)
            {
                cards = filters[i].GetSelectedCards(cards);
            }
        }

        return cards;
    }

    private void ClearAllIncludedCardToggles()
    {
        IncludedCards[] filters = GetSearchIncludedCards();
        for (int i = 0; i < filters.Length; i++)
        {
            if (filters[i] != null)
            {
                filters[i].ClearAllToggles();
            }
        }
    }

    private void ApplyAllIncludedCardLocale()
    {
        IncludedCards[] filters = GetSearchIncludedCards();
        for (int i = 0; i < filters.Length; i++)
        {
            if (filters[i] != null)
            {
                filters[i].RefreshLocalizedLabels();
            }
        }
    }

    private IncludedCards[] GetSearchIncludedCards()
    {
        if (Searchcanvas != null)
        {
            return Searchcanvas.GetComponentsInChildren<IncludedCards>(true);
        }

        if (IncludedCardsObj != null)
        {
            return new[] { IncludedCardsObj };
        }

        return System.Array.Empty<IncludedCards>();
    }

    void OnEnable()
    {
        scrollRect.onValueChanged.AddListener(OnScroll);
    }

    void OnDisable()
    {
        scrollRect.onValueChanged.RemoveListener(OnScroll);
    }

  

    void cardsRemove()
    {
        if(allCards.Count >= 0)
        {
            if(allCards.Count < displayCardCount)
            {
                 allCards.RemoveRange(0, allCards.Count);
            }    
            else
            {
                allCards.RemoveRange(0, displayCardCount);
            }
                
        }
        else
        {
            Debug.Log("これ以上カードデータがありません");
            return;
        }
    }

    void OnScroll(Vector2 value)
    {
        if (isLoading || content == null || imagePrefab == null)
            return;

        Debug.Log("Scroll Position: " + value);
        if (value.y <= 0.01f)
        {
            AddImages(3);
            if(allCards.Count >= 0)
            {
                if(allCards.Count < displayCardCount)
                {
                    allCards.RemoveRange(0, allCards.Count);
                }

                else
                {
                    allCards.RemoveRange(0, displayCardCount);
                }
                
            }
            else
            {
                Debug.Log("これ以上カードデータがありません");
                return;
            }
        }
        
    }
    
    public void SerchButtonClickedToFind()
    {
        string keyword = SearchInputField != null ? SearchInputField.text : string.Empty;
        List<CardData> results = CardDatabase.Instance.FindByNameContains(keyword);
        results = ApplyAllIncludedCardFilters(results);

        Debug.Log($"検索キーワード: {keyword}, 件数: {results.Count}");

        // 旧リスト／表示を捨てて、検索結果だけで描画し直す
        allCards.Clear();
        allCards.AddRange(results);
        if (content != null)
        {
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                Destroy(content.GetChild(i).gameObject);
            }
        }

        AddImages(Mathf.Min(5, allCards.Count));
        cardsRemove();
    }
    void AddImages(int count)
    {
        // 実行するたびにallcardsからデータを取得して画像を追加、そのあとlistから削除する。

        if (isLoading || content == null || imagePrefab == null)
            return;

        if (allCards.Count == 0)
        {
            Debug.Log("これ以上カードデータがありません");
            return;
        }

        isLoading = true;
        // ここを共通の選択したところから取るようにする。検索窓
        // CardData carddata = db.GetById(1);
        // GameObject obj = Instantiate(imagePrefab,content);
        // Image img = obj.GetComponent<Image>();
        // img.sprite = carddata.image;

        displayCardCount = count;

        foreach (var card in allCards)
        {
            Debug.Log($"カード読み込み: ID={card.id}, 名前={card.cardName}, 画像={card.imageName}");
            if(allCards.Count == 0)
            {
                Debug.Log("これ以上カードデータがありません");
                break;
            }            
        }
        int addCount = Mathf.Min(count, allCards.Count);
        for (int i = 0; i < addCount; i++)
        {
            CardData carddata = allCards[i];
            if (carddata == null)
            {
                Debug.LogWarning($"allCards[{i}] が null のためスキップします");
                continue;
            }

            Debug.Log($"カード追加: ID={carddata.id}, 名前={carddata.cardName}");
            GameObject obj = Instantiate(imagePrefab, content);
            if (obj == null)
                continue;

            Card cardId = obj.GetComponent<Card>();
            if (cardId != null)
                cardId.CardId = carddata.id;

            Image img = obj.GetComponent<Image>();
            if (img != null)
                img.sprite = carddata.imageName;

            Card.EnsureNotUsedOnlineLabel(obj, carddata);

            Button button = obj.GetComponent<Button>();
            if (button != null)
                button.onClick.AddListener(cardclicked);
        }
        // 1フレーム待ってから再度許可（レイアウト更新待ち）
        StartCoroutine(ResetLoading());
    }

    void cardclicked()
    {
        Debug.Log("カードがクリックされました");
    }

    System.Collections.IEnumerator ResetLoading()
    {
        yield return null;
        isLoading = false;
    }

}
