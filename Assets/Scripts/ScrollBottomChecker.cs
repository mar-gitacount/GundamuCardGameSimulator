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
        // 検索ボタンがクリックされたときの処理
        var keyword = SearchInputField.text;
      
        var results = CardDatabase.Instance.FindByNameContains(keyword);
        // results = IncludedCards.Instance.GetSelectedCards(results);

        // ?トグルで選択されたカードセットの条件で絞り込む
        results = IncludedCardsObj.GetSelectedCards(results);
        List<Toggle> onToggles =  IncludedCardsObj.GetOnToggles();
        Debug.Log($"ONトグル数: {onToggles.Count}");
        foreach (var toggle in onToggles)
        {
            Debug.Log($"ONトグル: {toggle.GetComponent<ToggleDatail>().id}");
        }
        // CardDatabase db = CardDatabase.Instance;
        CardDatabase db = CardDatabase.Instance;
         // db.LoadAllCards();
         // CardData testData = db.GetById(0);
         Debug.Log("検索キーワード: " + keyword);
         
         foreach (var card in results)
        {
            Debug.Log($"キーワード検索結果: ID={card.id}, 名前={card.cardName}, 画像={card.imageName}, version={card.version}, sourceType={card.sourceType}");
            // ここでカードデータをallCardsに追加するなどの処理を行うことができます。
            // ここでカードデータをallCardsに追加するなどの処理を行うことができます。
            // allCards.Add(db.GetById(card.id));
            allCards.Add(card);
        }
        // db.LoadAllCards();
        // CardData testData = db.GetById(0);
        Debug.Log("検索結果の数: " + results.Count);
        Debug.Log("検索後の検索キーワード: " + SearchInputField.text);
        // 以下すべてのカードデータを取得する例
        // allCards = db.GetAllCards();
        Debug.Log("検索後の全カードデータの数:" + allCards.Count);
        
    }
    void AddImages(int count)
    {
        // 実行するたびにallcardsからデータを取得して画像を追加、そのあとlistから削除する。

        isLoading = true;

        CardDatabase db = CardDatabase.Instance;
        if(allCards.Count == 0)
        {
            Debug.Log("これ以上カードデータがありません");
            return;
        }
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

            // コンテンツに画像を追加
            // CardData carddata = db.GetById(allCards[i].id);
            CardData carddata = db.FindById(allCards[i].id);
            // jsonデータから画像情報を取得して表示する。
            Debug.Log("プレハブの有無: " + (imagePrefab != null));
            Debug.Log("コンテンツの有無: " + (content != null));
            GameObject obj = Instantiate(imagePrefab,content);

            Card cardId = obj.GetComponent<Card>(); 
            cardId.CardId = allCards[i].id;
           
            // Debug.Log("画像の名前: " + carddata.imageName+", カードの名前: " + carddata.cardName+", カードのID: " + carddata.id+", カードのバージョン: " + carddata.version+", カードのコスト: " + carddata.cost +", カードのレベル: " + carddata.level+", カードのパワー: " + carddata.power+", カードのHP: " + carddata.hp+", カードデータのカードのソースタイプ: " + carddata.sourceType);
            Debug.Log("カードの名前: " + carddata.cardName);
            Image img = obj.GetComponent<Image>();
            img.sprite = carddata.imageName;
            // ?カードがクリックされたときの処理を追加テスト
            obj.GetComponent<Button>().onClick.AddListener(cardclicked);
            // Instantiate(imagePrefab, content);
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
