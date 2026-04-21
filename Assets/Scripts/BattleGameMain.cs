using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro; // これを追加！

public class BattleGameMain : MonoBehaviour
{
    private const float MinEndTurnAreaWidth = 90f;
    private const float MaxEndTurnAreaWidth = 180f;
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
    private Gundam2024RuleScript gundamRule = new Gundam2024RuleScript();

    private CardGameRule CurrentPlayerCardGameRule
    {
        get
        {
            if (currentPlayerType == PlayerType.Player)
            {
                return cardGameRule;
            }
            else
            {
                return enemyCardGameRule;
            }
        }
    }

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
    // エネミーの手札を管理するリスト
    private List<CardData> enemyHandCards = new List<CardData>();
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
        enemyCardGameRule.PlayerFieldPanel.SetRotation(180f);
        enemyCardGameRule.CreateShuffledDeck(enemyDeckData);

        // 2024ルールエンジン初期化
        gundamRule.InitializeGame(
            cardGameRule.GetRemainingCount(),
            enemyCardGameRule.GetRemainingCount(),
            ToRuleSide(currentPlayerType));
        SyncAllResourceViewsFromRule();


        
       for(int i = 0; i < 5; i++)
        {
            // ランダムで引いたカードのidを取得する。
            // int playerCardId = cardGameRule.Draw();
            // ランダムで引いてきたカードをプレイヤーの手札に追加する。
            CardAddtoHand(CurrentPlayerCardGameRule, currentPlayerType);
            // ?ターンを交代するあとでリファクト予定。
            currentPlayerType = (currentPlayerType == PlayerType.Player) ? PlayerType.Enemy : PlayerType.Player;
            CardAddtoHand(CurrentPlayerCardGameRule, currentPlayerType);
            // cardImage.GetComponent<Image>().sprite = cardSprite;
            // Debug.Log($"プレイヤーが引いたカードID: {playerCardId}");


          
        }
        
        // int enemyCardId = enemyCardGameRule.Draw();
        // Debug.Log($"エネミーが引いたカードID: {enemyCardId}");

        //  ?playerDeckData = cardGameRule.GetDeckList().GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());

        // ターン開始フェイズに移行する。
        ChangePhase(BattlePhase.StartTurn);

        //! エンドフェイズはボタンを押下したときに呼び出すようにする予定。
        ConfigureEndTurnButtonInHandPanel();
        if (EndTurnButton != null)
        {
            EndTurnButton.onClick.RemoveAllListeners();
            EndTurnButton.onClick.AddListener(() => ChangePhase(BattlePhase.EndTurn));
        }
        UpdateEndTurnButtonVisibility();

        
    }

    private void OnCardClicked(CardController cardController)
    {
        if (cardController == null || cardController.Data == null)
        {
            return;
        }

        PlayerType ownerType = ResolveCardOwner(cardController.transform);
        CardGameRule ownerRule = ownerType == PlayerType.Player ? cardGameRule : enemyCardGameRule;
        Gundam2024RuleScript.PlayerSide ownerSide = ToRuleSide(ownerType);
        bool isInHand = cardController.transform.IsChildOf(ownerRule.HandScrollContent);
        bool isOnField = cardController.transform.IsChildOf(ownerRule.PlayerDeployPanel);
       
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

        // どのケースでも閉じられるようにする
        var closeButton = FilterPanel.CreateChildButton("close");
        RectTransform closeBtnRect = closeButton.GetComponent<RectTransform>();
        closeBtnRect.sizeDelta = new Vector2(140, 44);
        closeBtnRect.anchoredPosition = new Vector2(0, -130);
        closeButton.onClick.AddListener(() => Destroy(FilterPanel));
    
        // 場のカードはトラッシュ送り操作を可能にする。
        if (isOnField)
        {
            var trashButton = FilterPanel.CreateChildButton("send to trash");
            RectTransform trashBtnRect = trashButton.GetComponent<RectTransform>();
            trashBtnRect.sizeDelta = new Vector2(180, 50);
            trashBtnRect.anchoredPosition = new Vector2(0, -70);

            trashButton.onClick.AddListener(() =>
            {
                SendCardToTrash(cardController, ownerType);
                Destroy(FilterPanel);
            });
            return;
        }

        // 手札以外(不明な位置)は処理しない。
        if (!isInHand)
        {
            Debug.Log("このカードは操作対象外のエリアにあります。");
            Destroy(FilterPanel);
            return;
        }

        // 相手ターン中に相手手札を操作させない。
        if (ownerType != currentPlayerType)
        {
            Debug.Log("現在のターンプレイヤーの手札ではありません。");
            Destroy(FilterPanel);
            return;
        }

        int cost = cardController.Data.cost;
        int currentLevel = ownerSide == Gundam2024RuleScript.PlayerSide.Player ? gundamRule.Player.level : gundamRule.Enemy.level;
        int currentResource = ownerSide == Gundam2024RuleScript.PlayerSide.Player ? gundamRule.Player.resource : gundamRule.Enemy.resource;

        if (currentLevel < cardController.Data.level)
        {
            Debug.Log("レベルが足りません。");
            Destroy(FilterPanel);
            return;
        }

        if (currentResource < cost)
        {
            Debug.Log("リソースポイントが足りません！");
            // パネルは表示したままにして、内容確認できるようにする
            return;
        }

        var playButton = FilterPanel.CreateChildButton("send to field");
        RectTransform btnRect = playButton.GetComponent<RectTransform>();
        btnRect.sizeDelta = new Vector2(180, 50);
        btnRect.anchoredPosition = new Vector2(0, -10);

        playButton.onClick.AddListener(() =>
        {
            if (!gundamRule.TryConsumeResource(ownerSide, cost))
            {
                Debug.Log("リソースポイントが足りません！");
                return;
            }

            SendCardToField(cardController, ownerType, ownerRule);

            SyncResourceViewsFromRule(ownerSide);
            Destroy(FilterPanel);
        });
        
        // Instantiate(CardImagePrefab, playerHandTransform);
    }
    //! 以下の関数もCardGameRuleに移す予定。
    void CardAddtoHand(CardGameRule targetRule, PlayerType targetType)
    {
        int cardId = targetRule.Draw();
        if (cardId < 0)
        {
            Debug.LogWarning("山札切れでドローできませんでした。");
            return;
        }
        //?テスト 以下のコードで、列挙型を変更することで、敵味方関係なくカードIDからカードデータを取得できるようにする。
        // CurrentPlayerCardGameRule.StartTurn(); // これで、プレイヤーとエネミーのターン開始処理を共通化できるはず。
      
        CardData drawCardData = DeckSettinObject.Instance.GetCardDataById(cardId);

        // 以下分岐してエネミーの手札にカードを追加する処理も書く。→後で
        // GameObject cardImage = Instantiate(CardImagePrefab, playerHandTransform);
        GameObject cardImage = Instantiate(CardImagePrefab, targetRule.HandScrollContent);
        // フィールドのゲームオブジェクトも渡す。
        cardImage.GetComponent<CardController>().SetUp(drawCardData,OnCardClicked);
        //! カードのデータをプレイヤーの手札のリストに追加する処理も書く。→後で
        // !AIの処理をバックグラウンドで走らせるため、毎ターン更新する。バックグラウンド処理用
        if (targetType == PlayerType.Player)
        {
            playerHandCards.Add(cardImage.GetComponent<CardController>().Data);
        }
        else
        {
            enemyHandCards.Add(cardImage.GetComponent<CardController>().Data);
        }
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
        UpdateEndTurnButtonVisibility();
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
            CardAddtoHand(cardGameRule, PlayerType.Player);
            gundamRule.BeginTurn();
            SyncResourceViewsFromRule(Gundam2024RuleScript.PlayerSide.Player);
            Debug.Log($"プレイヤーの現在のリソースポイント: {gundamRule.Player.resource}");
            PlayerresourcePointText.text = gundamRule.Player.resource.ToString();
            // PlayerlevelText.text = $"Level: {cardGameRule.GetResourcePoints()}";
            
        }
        else
        {
            Debug.Log("エネミーのターン開始処理を実行します。");
            // エネミーのターン開始処理をここに書く
            // 例: エネミーの手札を引く、リソースを獲得するなど
            CardAddtoHand(enemyCardGameRule, PlayerType.Enemy);
            gundamRule.BeginTurn();
            SyncResourceViewsFromRule(Gundam2024RuleScript.PlayerSide.Enemy);
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
        AdvanceRuleToNextTurnStart();
        UpdateEndTurnButtonVisibility();

        Debug.Log("エンドフェイズの処理を実行します。");
        ChangePhase(BattlePhase.StartTurn);

    }

    // Update is called once per frame
    void Update()
    {
        
    }

    private Gundam2024RuleScript.PlayerSide ToRuleSide(PlayerType type)
    {
        return type == PlayerType.Player
            ? Gundam2024RuleScript.PlayerSide.Player
            : Gundam2024RuleScript.PlayerSide.Enemy;
    }

    private void SyncResourceViewsFromRule(Gundam2024RuleScript.PlayerSide side)
    {
        CardGameRule targetRule = side == Gundam2024RuleScript.PlayerSide.Player ? cardGameRule : enemyCardGameRule;
        Gundam2024RuleScript.PlayerState state = side == Gundam2024RuleScript.PlayerSide.Player ? gundamRule.Player : gundamRule.Enemy;
        targetRule.ApplyExternalResourceState(state.level, state.resource);

        if (side == Gundam2024RuleScript.PlayerSide.Player)
        {
            PlayerlevelText.text = $"LV:{state.level}";
            PlayerresourcePointText.text = state.resource.ToString();
        }
    }

    private void AdvanceRuleToNextTurnStart()
    {
        // どのフェイズ状態からでも安全に次ターン開始へ進める。
        for (int i = 0; i < 6; i++)
        {
            gundamRule.AdvancePhase();
            if (gundamRule.CurrentPhase == Gundam2024RuleScript.TurnPhase.Start)
            {
                break;
            }
        }
    }

    private void SyncAllResourceViewsFromRule()
    {
        SyncResourceViewsFromRule(Gundam2024RuleScript.PlayerSide.Player);
        SyncResourceViewsFromRule(Gundam2024RuleScript.PlayerSide.Enemy);
    }

    private void SendCardToTrash(CardController cardController, PlayerType ownerType)
    {
        if (cardController == null || cardController.Data == null)
        {
            return;
        }

        CardGameRule ownerRule = ownerType == PlayerType.Player ? cardGameRule : enemyCardGameRule;
        ownerRule.AddCardToTrash(cardController.Data.id);

        playerBattleZoneCards.Remove(cardController);
        enemyBattleZoneCards.Remove(cardController);
        playerHandCards.Remove(cardController.Data);
        enemyHandCards.Remove(cardController.Data);
        Destroy(cardController.gameObject);
    }

    private void SendCardToField(CardController cardController, PlayerType ownerType, CardGameRule ownerRule)
    {
        if (cardController == null || ownerRule == null)
        {
            return;
        }

        cardController.transform.SetParent(ownerRule.PlayerDeployPanel, false);

        if (ownerType == PlayerType.Player)
        {
            playerHandCards.Remove(cardController.Data);
            if (!playerBattleZoneCards.Contains(cardController))
            {
                playerBattleZoneCards.Add(cardController);
            }
        }
        else
        {
            enemyHandCards.Remove(cardController.Data);
            if (!enemyBattleZoneCards.Contains(cardController))
            {
                enemyBattleZoneCards.Add(cardController);
            }
        }
    }

    private PlayerType ResolveCardOwner(Transform cardTransform)
    {
        if (cardTransform == null)
        {
            return currentPlayerType;
        }

        if (cardTransform.IsChildOf(cardGameRule.PlayerDeployPanel) || cardTransform.IsChildOf(cardGameRule.HandScrollContent))
        {
            return PlayerType.Player;
        }

        if (cardTransform.IsChildOf(enemyCardGameRule.PlayerDeployPanel) || cardTransform.IsChildOf(enemyCardGameRule.HandScrollContent))
        {
            return PlayerType.Enemy;
        }

        return currentPlayerType;
    }

    private void ConfigureEndTurnButtonInHandPanel()
    {
        if (EndTurnButton == null)
        {
            return;
        }

        RectTransform handPanel = cardGameRule.PlayerHandPanel;
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

        Canvas.ForceUpdateCanvases();
        float handWidth = handPanel.rect.width;
        float minWidthForFiveCards = cardGameRule.GetHandMinimumWidthForVisibleCards(5);
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

        // 5枚が最低並ぶ幅を優先し、余剰幅ぶんだけ右側をボタン領域として確保する。
        cardGameRule.SetHandScrollRightMargin(endTurnAreaWidth);
    }

    private void UpdateEndTurnButtonVisibility()
    {
        if (EndTurnButton == null)
        {
            return;
        }

        bool isMyTurn = currentPlayerType == PlayerType.Player;
        EndTurnButton.gameObject.SetActive(true);
        EndTurnButton.interactable = isMyTurn;

        Image buttonImage = EndTurnButton.GetComponent<Image>();
        if (buttonImage != null)
        {
            buttonImage.color = isMyTurn
                ? new Color32(255, 255, 255, 255)
                : new Color32(150, 150, 150, 255);
        }

        TextMeshProUGUI label = EndTurnButton.GetComponentInChildren<TextMeshProUGUI>();
        if (label != null)
        {
            label.color = isMyTurn
                ? new Color32(20, 20, 20, 255)
                : new Color32(90, 90, 90, 255);
        }
    }
}
