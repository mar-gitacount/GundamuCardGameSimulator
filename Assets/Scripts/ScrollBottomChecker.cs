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
  



    void Awake()
    {
        SerachFindButton.onClick.AddListener(SerchButtonClickedToFind);

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

    private void SearchButtonClicked()
    {
        // 検索ボタンがクリックされたときの処理
        Debug.Log("Search button clicked!");
        // ここに検索処理を追加
        // 一旦全てのカードをクリアして再読み込み
        Searchcanvas.gameObject.SetActive(true);
        foreach (Transform child in content)
        {
            Destroy(child.gameObject);
        }
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
        if (IncludedCardsObj != null)
        {
            results = IncludedCardsObj.GetSelectedCards(results);
        }

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
