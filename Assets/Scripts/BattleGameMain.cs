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

    [Header("先攻・後攻アラート")]
    [Tooltip("未指定時はシーン内の Canvas を自動検索します。")]
    [SerializeField] private Canvas turnOrderAlertCanvas;
    [SerializeField] private float turnOrderAlertDurationSeconds = 1f;

    [Header("オープニング・シールド")]
    [Tooltip("未指定時は EX ベース 3 として扱います。Gundam_Rules.pdf に準拠。")]
    [SerializeField] private ExBaseData exBaseData;
    private const int OpeningShieldCardCount = 5;

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

    private void Start()
    {
        StartCoroutine(BattleSetupCoroutine());
    }

    /// <summary>
    /// デッキ構築・初期5枚・マリガン・ゲーム開始まで（コルーチンでUI待機を挟む）。
    /// </summary>
    private IEnumerator BattleSetupCoroutine()
    {
        Debug.Log("バトルゲームのメインシーン");
        isFirstPlayer = DecideTurnOrder();
        PlayerType firstPlayerThisGame = currentPlayerType;

        playerDeckData = DeckSettinObject.Instance.LoadDeckReturn();
        enemyDeckData = DeckSettinObject.Instance.LoadEnemyDeckReturn();

        cardGameRule.SetUp(PlayerFieldPanel);
        cardGameRule.CreateShuffledDeck(playerDeckData);
        cardGameRule.ResourcAndLevelTextGet(PlayerresourcePointText, PlayerlevelText, ExresourcePointText);
        enemyCardGameRule.SetUp(EnemyPlayerFieldPanel);
        enemyCardGameRule.PlayerFieldPanel.SetRotation(180f);
        enemyCardGameRule.CreateShuffledDeck(enemyDeckData);

        gundamRule.InitializeGame(
            cardGameRule.GetRemainingCount(),
            enemyCardGameRule.GetRemainingCount(),
            ToRuleSide(firstPlayerThisGame));

        const int openingHandSize = 5;
        for (int i = 0; i < openingHandSize; i++)
        {
            CardAddtoHand(cardGameRule, PlayerType.Player);
        }
        for (int i = 0; i < openingHandSize; i++)
        {
            CardAddtoHand(enemyCardGameRule, PlayerType.Enemy);
        }
        currentPlayerType = firstPlayerThisGame;
        gundamRule.SyncOpeningHandState(
            openingHandSize,
            cardGameRule.GetRemainingCount(),
            openingHandSize,
            enemyCardGameRule.GetRemainingCount());
        Debug.Log($"[ドロー] 初期手札: プレイヤー{openingHandSize}枚、エネミー{openingHandSize}枚を引きました。");

        // マリガン：プレイヤーは Yes/No、エネミーは 1/2
        Canvas canvas = ResolveBattleCanvas();
        if (canvas != null)
        {
            bool? playerChoice = null;
            yield return MulliganPromptCoroutine(
                canvas,
                "Do you want to shuffle your hand and draw 5 cards again? (Mulligan)",
                value => playerChoice = value);

            if (playerChoice == true)
            {
                PerformMulligan(cardGameRule, playerHandCards, openingHandSize, PlayerType.Player);
                Debug.Log("[マリガン] プレイヤー：実行（手札を山札に戻しシャッフル後、5枚ドロー）。");
            }
            else
            {
                Debug.Log("[マリガン] プレイヤー：見送り。");
            }

            if (Random.value < 0.5f)
            {
                PerformMulligan(enemyCardGameRule, enemyHandCards, openingHandSize, PlayerType.Enemy);
                Debug.Log("[マリガン] エネミー：実行（確率 1/2）。");
            }
            else
            {
                Debug.Log("[マリガン] エネミー：見送り（確率 1/2）。");
            }
        }
        else
        {
            Debug.LogWarning("[マリガン] Canvas が見つからないため、マリガンをスキップしました。");
        }

        gundamRule.SyncOpeningHandState(
            openingHandSize,
            cardGameRule.GetRemainingCount(),
            openingHandSize,
            enemyCardGameRule.GetRemainingCount());

        int exBasePoints = exBaseData != null ? exBaseData.startingPoints : 3;
        cardGameRule.SetupShieldFromDeckAfterMulligan(CardImagePrefab, OnCardClicked, OpeningShieldCardCount, exBasePoints);
        enemyCardGameRule.SetupShieldFromDeckAfterMulligan(CardImagePrefab, OnCardClicked, OpeningShieldCardCount, exBasePoints);

        gundamRule.ApplyExBaseAndShieldAfterMulligan(
            Gundam2024RuleScript.PlayerSide.Player,
            exBasePoints,
            cardGameRule.GetShieldCardIds().Count,
            cardGameRule.GetRemainingCount());
        gundamRule.ApplyExBaseAndShieldAfterMulligan(
            Gundam2024RuleScript.PlayerSide.Enemy,
            exBasePoints,
            enemyCardGameRule.GetShieldCardIds().Count,
            enemyCardGameRule.GetRemainingCount());

        SyncAllResourceViewsFromRule();

        ChangePhase(BattlePhase.StartTurn);

        ConfigureEndTurnButtonInHandPanel();
        if (EndTurnButton != null)
        {
            EndTurnButton.onClick.RemoveAllListeners();
            EndTurnButton.onClick.AddListener(() => ChangePhase(BattlePhase.EndTurn));
        }
        UpdateEndTurnButtonVisibility();

        ShowTurnOrderAlert(firstPlayerThisGame);
    }

    /// <summary>先攻アラート・マリガンで共通利用する Canvas を取得する。</summary>
    private Canvas ResolveBattleCanvas()
    {
        if (turnOrderAlertCanvas != null)
        {
            return turnOrderAlertCanvas;
        }
        Canvas canvas = GetComponentInParent<Canvas>()?.rootCanvas;
        if (canvas == null && Filtercanvas != null)
        {
            canvas = Filtercanvas;
        }
        if (canvas == null)
        {
            canvas = Object.FindObjectOfType<Canvas>();
        }
        return canvas;
    }

    /// <summary>手札のカードを山札に戻しシャッフルして、指定枚数ドローし直す。</summary>
    private void PerformMulligan(CardGameRule rule, List<CardData> handList, int drawCount, PlayerType playerType)
    {
        List<int> ids = CollectHandCardIdsFromHandContent(rule);
        ClearHandVisuals(rule, handList);
        rule.ReturnCardIdsToDeckAndShuffle(ids);
        for (int i = 0; i < drawCount; i++)
        {
            CardAddtoHand(rule, playerType);
        }
    }

    private static List<int> CollectHandCardIdsFromHandContent(CardGameRule rule)
    {
        var ids = new List<int>();
        RectTransform content = rule.HandScrollContent;
        if (content == null)
        {
            return ids;
        }
        for (int i = 0; i < content.childCount; i++)
        {
            CardController cc = content.GetChild(i).GetComponent<CardController>();
            if (cc != null && cc.Data != null)
            {
                ids.Add(cc.Data.id);
            }
        }
        return ids;
    }

    private void ClearHandVisuals(CardGameRule rule, List<CardData> handList)
    {
        RectTransform content = rule.HandScrollContent;
        if (content == null)
        {
            return;
        }
        for (int i = content.childCount - 1; i >= 0; i--)
        {
            Destroy(content.GetChild(i).gameObject);
        }
        handList.Clear();
    }

    /// <summary>マリガン Yes/No を表示し、選択が入るまで待つ。</summary>
    private IEnumerator MulliganPromptCoroutine(Canvas canvas, string message, System.Action<bool> onChosen)
    {
        GameObject root = new GameObject("MulliganPrompt", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        root.transform.SetParent(canvas.transform, false);
        root.transform.SetAsLastSibling();
        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.SetFullSize();
        Image dim = root.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.55f);
        dim.raycastTarget = true;

        GameObject panel = new GameObject("MulliganPanel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(root.transform, false);
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(520f, 220f);
        panelRect.anchoredPosition = Vector2.zero;
        panel.GetComponent<Image>().color = new Color32(240, 240, 240, 255);

        GameObject titleObj = new GameObject("Title", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        titleObj.transform.SetParent(panel.transform, false);
        TextMeshProUGUI titleTmp = titleObj.GetComponent<TextMeshProUGUI>();
        titleTmp.text = message;
        titleTmp.fontSize = 22;
        titleTmp.alignment = TextAlignmentOptions.Center;
        titleTmp.color = Color.black;
        RectTransform titleRt = titleObj.GetComponent<RectTransform>();
        titleRt.anchorMin = new Vector2(0f, 0.55f);
        titleRt.anchorMax = new Vector2(1f, 1f);
        titleRt.offsetMin = new Vector2(16f, 0f);
        titleRt.offsetMax = new Vector2(-16f, -12f);

        Button yesButton = panel.CreateChildButton("Yes");
        RectTransform yesRt = yesButton.GetComponent<RectTransform>();
        yesRt.anchorMin = new Vector2(0.15f, 0.12f);
        yesRt.anchorMax = new Vector2(0.45f, 0.42f);
        yesRt.offsetMin = Vector2.zero;
        yesRt.offsetMax = Vector2.zero;

        Button noButton = panel.CreateChildButton("No");
        RectTransform noRt = noButton.GetComponent<RectTransform>();
        noRt.anchorMin = new Vector2(0.55f, 0.12f);
        noRt.anchorMax = new Vector2(0.85f, 0.42f);
        noRt.offsetMin = Vector2.zero;
        noRt.offsetMax = Vector2.zero;

        bool finished = false;
        yesButton.onClick.AddListener(() =>
        {
            finished = true;
            onChosen?.Invoke(true);
        });
        noButton.onClick.AddListener(() =>
        {
            finished = true;
            onChosen?.Invoke(false);
        });

        yield return new WaitUntil(() => finished);
        Destroy(root);
    }

    /// <summary>
    /// ゲーム開始時の先攻／後攻を画面中央に短時間表示する（TMPro使用）。
    /// </summary>
    private void ShowTurnOrderAlert(PlayerType firstPlayer)
    {
        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            Debug.LogWarning("先攻アラート: Canvas が見つかりません。Inspector で Turn Order Alert Canvas を指定してください。");
            return;
        }

        string message = firstPlayer == PlayerType.Player
            ? "your turn first"
            : "opponent turn first";
        StartCoroutine(TurnOrderAlertCoroutine(canvas, message, turnOrderAlertDurationSeconds));
    }

    private IEnumerator TurnOrderAlertCoroutine(Canvas canvas, string message, float duration)
    {
        GameObject root = new GameObject("TurnOrderAlert", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        root.transform.SetParent(canvas.transform, false);
        root.transform.SetAsLastSibling();
        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.SetFullSize();
        Image dim = root.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.5f);
        dim.raycastTarget = false;

        GameObject textObj = new GameObject("TurnOrderAlertText", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObj.transform.SetParent(root.transform, false);
        TextMeshProUGUI tmp = textObj.GetComponent<TextMeshProUGUI>();
        tmp.text = message;
        tmp.fontSize = 38;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        textObj.GetComponent<RectTransform>().SetFullSize();

        yield return new WaitForSeconds(duration);
        if (root != null)
        {
            Object.Destroy(root);
        }
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
        bool isInShield = ownerRule.ShieldCardsContent != null
            && cardController.transform.IsChildOf(ownerRule.ShieldCardsContent);

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

        // シールドエリア：詳細表示のみ（場・手札と同様にフィルターで閲覧）
        if (isInShield)
        {
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
        // 先攻後攻は1回の乱数で決定（isFirstPlayer / currentPlayerType / currentPlayer を矛盾なく同期）
        bool playerGoesFirst = Random.value < 0.5f;
        isFirstPlayer = playerGoesFirst;
        currentPlayerType = playerGoesFirst ? PlayerType.Player : PlayerType.Enemy;
        currentPlayer = playerGoesFirst;

        if (playerGoesFirst)
        {
            Debug.Log("your turn first");
            return true;
        }

        Debug.Log("opponent turn first");
        return false;
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
            // 先攻・後攻に関わらずレベル+1・リソースをレベルに同期してから、ドロー1枚
            gundamRule.SetCurrentTurnPlayer(Gundam2024RuleScript.PlayerSide.Player);
            gundamRule.BeginTurn();
            CardAddtoHand(cardGameRule, PlayerType.Player);
            SyncResourceViewsFromRule(Gundam2024RuleScript.PlayerSide.Player);
            Debug.Log($"[ドロー] プレイヤーのターン開始ドロー1枚。LV:{gundamRule.Player.level} Resource:{gundamRule.Player.resource}");
            Debug.Log($"プレイヤーの現在のリソースポイント: {gundamRule.Player.resource}");
            PlayerresourcePointText.text = gundamRule.Player.resource.ToString();
            ApplyTurnStartAttackFlgForCurrentPlayer();
        }
        else
        {
            Debug.Log("エネミーのターン開始処理を実行します。");
            gundamRule.SetCurrentTurnPlayer(Gundam2024RuleScript.PlayerSide.Enemy);
            gundamRule.BeginTurn();
            CardAddtoHand(enemyCardGameRule, PlayerType.Enemy);
            SyncResourceViewsFromRule(Gundam2024RuleScript.PlayerSide.Enemy);
            Debug.Log($"[ドロー] エネミーのターン開始ドロー1枚。LV:{gundamRule.Enemy.level} Resource:{gundamRule.Enemy.resource}");
            ApplyTurnStartAttackFlgForCurrentPlayer();
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
        targetRule.SetExBaseDisplay(state.exBase);

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

        // ユニット配備直後は攻撃不可（次の自分ターン開始で True）
        if (cardController.Data.type == Type.Unit)
        {
            cardController.SetAttackFlg(AttackFlg.False);
        }
    }

    /// <summary>
    /// 自分ターン開始時：場の自軍ユニットの AttackFlg を True にリフレッシュ（ルールブック準拠の追跡用）。
    /// </summary>
    private void ApplyTurnStartAttackFlgForCurrentPlayer()
    {
        if (currentPlayerType == PlayerType.Player)
        {
            Debug.Log("[AttackFlg] プレイヤーターン開始：場のユニットを True に設定");
            foreach (var c in playerBattleZoneCards)
            {
                if (c != null && c.Data != null && c.Data.type == Type.Unit)
                {
                    c.SetAttackFlg(AttackFlg.True);
                }
            }
        }
        else
        {
            Debug.Log("[AttackFlg] エネミーターン開始：場のユニットを True に設定");
            foreach (var c in enemyBattleZoneCards)
            {
                if (c != null && c.Data != null && c.Data.type == Type.Unit)
                {
                    c.SetAttackFlg(AttackFlg.True);
                }
            }
        }
    }

    private PlayerType ResolveCardOwner(Transform cardTransform)
    {
        if (cardTransform == null)
        {
            return currentPlayerType;
        }

        if (cardTransform.IsChildOf(cardGameRule.PlayerDeployPanel)
            || cardTransform.IsChildOf(cardGameRule.HandScrollContent)
            || (cardGameRule.ShieldCardsContent != null && cardTransform.IsChildOf(cardGameRule.ShieldCardsContent)))
        {
            return PlayerType.Player;
        }

        if (cardTransform.IsChildOf(enemyCardGameRule.PlayerDeployPanel)
            || cardTransform.IsChildOf(enemyCardGameRule.HandScrollContent)
            || (enemyCardGameRule.ShieldCardsContent != null && cardTransform.IsChildOf(enemyCardGameRule.ShieldCardsContent)))
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
