using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro; // これを追加！

public class BattleGameMain : MonoBehaviour
{
    // Start is called before the first frame update
    public bool isFirstPlayer;

    // 現在のバトルフェイズを管理する列挙型
    public enum BattlePhase
{
    StartTurn,   // ターン開始（ドローなど）
    ActivePhase,  // アクティブフェイズ（リソースの獲得、カードのドローなど）
    DrawPhase,        // ドローフェイズ（カードを引く）
    ResourcePhase, // リソースフェイズ（リソースの獲得や管理）
    MainPhase,    // メイン（カードを出す、攻撃する）
    EndTurn,     // ターン終了処理
    OpponentTurn // 相手のターン
}
    public BattlePhase currentPhase;
    // プレイヤーとエネミーのデッキデータを取得する。
    private Dictionary<int,int> playerDeckData =  new Dictionary<int, int>();
    private Dictionary<int,int> enemyDeckData = new Dictionary<int, int>();
    private CardGameRule cardGameRule = new CardGameRule();
    private CardGameRule enemyCardGameRule = new CardGameRule();

    //true = プレイヤー false =    エネミー
    public bool currentPlayer;

    [SerializeField]private Button EndTurnButton;


    // プレイヤーのフィールドパネル→これをCardGameRuleに渡して、子要素などを生成する。
    [SerializeField] private GameObject PlayerFieldPanel;
    [SerializeField] private GameObject EnemyPlayerFieldPanel;

    //! カード山札のパネル
    [SerializeField] private GameObject CardImagePrefab;

    [SerializeField] private Transform playerHandTransform;
    [SerializeField] private Transform PlayerBattleZoneTransform;

    [SerializeField] private GameObject FilterPanelPrefab;
    [SerializeField] private Canvas Filtercanvas;

    [SerializeField] private TextMeshProUGUI PlayerresourcePointText; // リソースポイント表示用のテキスト
    [SerializeField] private TextMeshProUGUI PlayerlevelText; // レベル表示用のテキスト
    [SerializeField] private TextMeshProUGUI ExresourcePointText; // エネミーのリソースポイント表示用のテキスト
    private Canvas FilterSetParentanvas;

   
    
    public enum PlayerType{Player,Enemy}

    public PlayerType currentPlayerType;
    // バトルゾーンのカードを管理するリスト
    private List<CardController> playerBattleZoneCards = new List<CardController>();
    // プレイヤーの手札を管理するリスト
    private List<CardData> playerHandCards = new List<CardData>();
    // エネミーのバトルゾーンのカードを管理するリスト
    private List<CardController> enemyBattleZoneCards = new List<CardController>();

    private CardController copyCardController;
     void Start()
    {
        Debug.Log("バトルゲームのメインシーン");
        // 先攻後攻を決める。
        isFirstPlayer = DecideTurnOrder();

        playerDeckData = DeckSettinObject.Instance.LoadDeckReturn();
        enemyDeckData = DeckSettinObject.Instance.LoadEnemyDeckReturn();

        //! プレイヤーが先行、後攻の場合、なにがしかのアラートを後ほど実装。

        //! マリガンの後ほど実装

        // cardGameRule.CreateField(PlayerFieldPanel);

        // UI作成も自動でやる

        // プレイヤーとエネミーの山札を作成する。
        cardGameRule.SetUp(PlayerFieldPanel);
        cardGameRule.CreateShuffledDeck(playerDeckData);
        //! 以下もsetupの中でやる予定。
        cardGameRule.ResourcAndLevelTextGet(PlayerresourcePointText,PlayerlevelText,ExresourcePointText);
        // int playerCardId = cardGameRule.Draw();
        // Debug.Log($"プレイヤーが引いたカードID: {playerCardId}");
        enemyCardGameRule.SetUp(EnemyPlayerFieldPanel);
        enemyCardGameRule.CreateShuffledDeck(enemyDeckData);


        
       for(int i = 0; i < 5; i++)
        {
            // ランダムで引いたカードのidを取得する。
            // int playerCardId = cardGameRule.Draw();
            // ランダムで引いてきたカードをプレイヤーの手札に追加する。
            CardAddtoHand();
            // cardImage.GetComponent<Image>().sprite = cardSprite;
            // Debug.Log($"プレイヤーが引いたカードID: {playerCardId}");

            int enemyCardId = enemyCardGameRule.Draw();
            Debug.Log($"エネミーが引いたカードID: {enemyCardId}");
        }
        
        // int enemyCardId = enemyCardGameRule.Draw();
        // Debug.Log($"エネミーが引いたカードID: {enemyCardId}");

        //  ?playerDeckData = cardGameRule.GetDeckList().GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());

        // ターン開始フェイズに移行する。
        ChangePhase(BattlePhase.StartTurn);

        //! エンドフェイズはボタンを押下したときに呼び出すようにする予定。
        EndTurnButton.onClick.AddListener(() => ChangePhase(BattlePhase.EndTurn));

        
    }

    private void OnCardClicked(CardController cardController)
    {
        // Debug.Log($"カードがクリックされました。カード名前: {cardController.Data.cardName}");
        // カードを手札→バトルゾーンに移動する処理。
        int level = cardController.Data.level;
        int cost = cardController.Data.cost;

        Sprite cardSprite = cardController.cardSprite;
       
        // クリック時にフィルターパネルを表示する処理
        FilterSetParentanvas = GetComponentInParent<Canvas>().rootCanvas;

        GameObject FilterPanel = Instantiate(FilterPanelPrefab, FilterSetParentanvas.transform);
        
        FilterPanel.SetFullSize(); // UIを親要素いっぱいに広げる（Stretch設定）

        // GameObject imageOnlyObj = new GameObject("CopyImage", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        // imageOnlyObj.transform.SetParent(FilterPanel.transform, false);

        // UnityEngine.UI.Image sourceImg = cardController.GetComponent<UnityEngine.UI.Image>();
        // UnityEngine.UI.Image targetImg = imageOnlyObj.GetComponent<UnityEngine.UI.Image>();
        
        
        // targetImg.sprite = sourceImg.sprite; 
        GameObject copy = FilterPanel.CreateChildImageFrom(cardController.gameObject);
        FilterPanel.SetActive(true);
    
        if(cardGameRule.GetResourcePoints() < level)
        {
            Debug.Log("レベルが足りません！カードのレベル: " + level);
            return;
        }
        if(cardGameRule.UseResourcePoints(cost))
        {
            var myButton = FilterPanel.CreateChildButton("put on the field");
            // ボタンのサイズを整える
            RectTransform btnRect = myButton.GetComponent<RectTransform>();
            btnRect.sizeDelta = new Vector2(160, 50);
           
             // ボタンが押された時の処理
            myButton.onClick.AddListener(() => {
              Debug.Log("ボタンが押されました");
             cardController.transform.SetParent(PlayerBattleZoneTransform,false);
             cardGameRule.UseResourcePointsWithoutCheck(cost);
            Destroy(FilterPanel);
            });
        }
        else
        {
            Debug.Log("リソースポイントが足りません！");
            return;
        }
        
        // Instantiate(CardImagePrefab, playerHandTransform);
    }
    //! 以下の関数もCardGameRuleに移す予定。
    void CardAddtoHand()
    {

        int cardId = cardGameRule.Draw();
        CardData playerCardData = DeckSettinObject.Instance.GetCardDataById(cardId);

        Debug.Log($"イメージ: {playerCardData.imageName.name}");
        // 以下分岐してエネミーの手札にカードを追加する処理も書く。→後で
        GameObject cardImage = Instantiate(CardImagePrefab, playerHandTransform);
        // フィールドのゲームオブジェクトも渡す。
        cardImage.GetComponent<CardController>().SetUp(playerCardData,OnCardClicked);
        //! カードのデータをプレイヤーの手札のリストに追加する処理も書く。→後で
        // !AIの処理をバックグラウンドで走らせるため、毎ターン更新する。バックグラウンド処理用
        playerHandCards.Add(cardImage.GetComponent<CardController>().Data);
    }
    public bool DecideTurnOrder()
    {
        // 0か1をランダムに取得 (Rangeの第2引数は未満なので、0か1が出る)
        int result = Random.Range(0, 2);

        isFirstPlayer = (result == 0);
        // 先攻後攻をランダムに決める
        currentPlayerType = (Random.value < 0.5f) ? PlayerType.Player : PlayerType.Enemy;
        
        
        // !以下不要
        if (isFirstPlayer)
        {
            
            Debug.Log("あなたが先攻です。");
            currentPlayer = true; // プレイヤーが先攻の場合はtrue
            return true;
        }
        else
        {
            currentPlayer = false; // エネミーが先攻の場合はfalse
            Debug.Log("相手が先攻です。");
            return false;
        }
    }

    public void ChangePhase(BattlePhase nextPhase)
    {
        currentPhase = nextPhase;
        switch (currentPhase)
        {
            case BattlePhase.StartTurn:
                Debug.Log("ターン開始フェイズに入りました。");
                // ターン開始フェイズの処理をここに書く
                ExcuteStartTurn();
                break;
            case BattlePhase.ActivePhase:
                Debug.Log("アクティブフェイズに入りました。");
                // アクティブフェイズの処理をここに書く
                break;
            case BattlePhase.DrawPhase:
                Debug.Log("ドローフェイズに入りました。");
                // ドローフェイズの処理をここに書く
                break;
            case BattlePhase.ResourcePhase:
                Debug.Log("リソースフェイズに入りました。");
                // リソースフェイズの処理をここに書く
                break;
            case BattlePhase.MainPhase:
                Debug.Log("メインフェイズに入りました。");
                // メインフェイズの処理をここに書く
                ExcuteMainPhase();
                break;
            case BattlePhase.EndTurn:
                Debug.Log("エンドフェイズに入りました。");
                // エンドフェイズの処理をここに書く
                // currentPlayer = !currentPlayer; // ターン終了時にプレイヤーを切り替える
                ExcueteEndTurn();
                break;
            case BattlePhase.OpponentTurn:
                Debug.Log("相手のターンに入りました。");
                break;
        }
        
    }
    void ExcuteStartTurn()
    {
        Debug.Log("ターン開始フェイズの処理を実行します。");
        // ターン開始フェイズの具体的な処理をここに書く

        if(currentPlayerType == PlayerType.Player)
        {
            Debug.Log("プレイヤーのターン開始処理を実行します。");
            // プレイヤーのターン開始処理をここに書く
            // 例: プレイヤーの手札を引く、リソースを獲得するなど
            // int playerCardId = cardGameRule.Draw();
            CardAddtoHand();
            // リソースポイントを増やす
            cardGameRule.AddResourcePoints(); // 1ポイント増やす例
            cardGameRule.RefreshResourcePoints(); // ターン開始時にリソースポイントをリセット
            Debug.Log($"プレイヤーの現在のリソースポイント: {cardGameRule.GetResourcePoints()}");
            PlayerresourcePointText.text = cardGameRule.GetResourcePoints().ToString();
            // PlayerlevelText.text = $"Level: {cardGameRule.GetResourcePoints()}";
            
        }
        else
        {
            Debug.Log("エネミーのターン開始処理を実行します。");
            // エネミーのターン開始処理をここに書く
            // 例: エネミーの手札を引く、リソースを獲得するなど
            int enemyCardId = enemyCardGameRule.Draw();
            Debug.Log($"エネミーが引いたカードID: {enemyCardId}");
        }
        // メインフェイズに移行する
        ChangePhase(BattlePhase.MainPhase);
    }

    void ExcuteMainPhase()
    {
        Debug.Log("メインフェイズの処理を実行します。");
        // メインフェイズの具体的な処理をここに書く
        // 例: プレイヤーがカードを出す、攻撃するなど

     
        if(currentPlayerType == PlayerType.Player)
        {
            Debug.Log("プレイヤーのメインフェイズの処理を実行します。");

            // エンドフェイズに移行する
            // ChangePhase(BattlePhase.EndTurn);


            // プレイヤーのメインフェイズの処理をここに書く
            // 例: プレイヤーがカードを出す、攻撃するなど
        }
        else
        {
            Debug.Log("エネミーのメインフェイズの処理を実行します。");
            // エネミーのメインフェイズの処理をここに書く
            // 例: エネミーがカードを出す、攻撃するなど
            StartCoroutine(EnemyActionCoroutine());
        }


    }
    IEnumerator EnemyActionCoroutine()
    {

        Debug.Log("エネミーの行動を開始します。");
        // エネミーの行動をシミュレートするために、数秒待つ
        yield return new WaitForSeconds(2f);
        Debug.Log("エネミーの行動が終了しました。");
        // エンドフェイズに移行する
        ChangePhase(BattlePhase.EndTurn);
    }

    void ExcueteEndTurn()
    {
        // プレイヤーとエネミーのターンを切り替える
        currentPlayerType = (currentPlayerType == PlayerType.Player) ? PlayerType.Enemy : PlayerType.Player;

        Debug.Log("エンドフェイズの処理を実行します。");
        ChangePhase(BattlePhase.StartTurn);

    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
