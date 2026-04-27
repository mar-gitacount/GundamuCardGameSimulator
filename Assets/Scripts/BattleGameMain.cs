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
    [Header("フェイズ表示")]
    [SerializeField] private float phasePauseDurationSeconds = 0.9f;
    [Header("敵アタック通知")]
    [SerializeField] private float enemyAttackNoticeSeconds = 1.0f;

    [Header("オープニング・シールド")]
    [Tooltip("未指定時は EX ベース 3 として扱います。Gundam_Rules.pdf に準拠。")]
    [SerializeField] private ExBaseData exBaseData;
    private const int OpeningShieldCardCount = 6;

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
    private bool isMatchFinished;

    /// <summary>「相手ユニットを攻撃」選択後、次にタップする相手ユニット。</summary>
    private CardController pendingUnitAttackAttacker;
    private CardController pendingOnAttackEffectResolvedAttacker;
    private bool isEndTurnFlowRunning;
    private bool isOnActionPopupOpen;
    private GameObject activeOnActionPopupRoot;
    private bool isTurnPhaseSequenceRunning;
    private bool blockShieldFlowDuringShieldAttack;
    private Gundam2024RuleScript.PlayerSide blockedShieldFlowSide;

    private void Awake()
    {
        gundamRule.OnShieldDamaged += OnGundamShieldDamaged;
    }

    private void OnDestroy()
    {
        if (gundamRule != null)
        {
            gundamRule.OnShieldDamaged -= OnGundamShieldDamaged;
        }
    }

    private void OnGundamShieldDamaged(Gundam2024RuleScript.PlayerSide side, int oldShield, int newShield)
    {
        int broken = oldShield - newShield;
        if (broken <= 0)
        {
            return;
        }

        CardGameRule rule = side == Gundam2024RuleScript.PlayerSide.Player ? cardGameRule : enemyCardGameRule;
        rule.MoveTopShieldCardsToTrash(broken);
    }

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

        const int openingHandSize = 5;
        int minDeckTotalForOpening = openingHandSize + OpeningShieldCardCount;

        playerDeckData = DeckSettinObject.Instance.LoadDeckReturn();
        enemyDeckData = DeckSettinObject.Instance.LoadEnemyDeckReturn();
        enemyDeckData = EnsureDeckHasMinimumCardsForOpening(enemyDeckData, playerDeckData, minDeckTotalForOpening, "Enemy");
        playerDeckData = EnsureDeckHasMinimumCardsForOpening(playerDeckData, enemyDeckData, minDeckTotalForOpening, "Player");

        cardGameRule.SetUp(PlayerFieldPanel);
        cardGameRule.CreateShuffledDeck(playerDeckData);
        cardGameRule.ResourcAndLevelTextGet(PlayerresourcePointText, PlayerlevelText, ExresourcePointText);
        enemyCardGameRule.SetUp(EnemyPlayerFieldPanel);
        enemyCardGameRule.PlayerFieldPanel.SetRotation(180f);
        enemyCardGameRule.CreateShuffledDeck(enemyDeckData);

        cardGameRule.BindTrashAreaClick(() => OpenTrashInspectionPanel(cardGameRule));
        enemyCardGameRule.BindTrashAreaClick(() => OpenTrashInspectionPanel(enemyCardGameRule));

        gundamRule.InitializeGame(
            cardGameRule.GetRemainingCount(),
            enemyCardGameRule.GetRemainingCount(),
            ToRuleSide(firstPlayerThisGame));

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

        Gundam2024RuleScript.PlayerSide secondPlayerSide = firstPlayerThisGame == PlayerType.Player
            ? Gundam2024RuleScript.PlayerSide.Enemy
            : Gundam2024RuleScript.PlayerSide.Player;
        gundamRule.AddExResource(secondPlayerSide, 1);

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

    /// <summary>
    /// 初期手札とオープニング・シールドは山札から引くため、総枚数が不足するとシールドが規定枚数に届かない。
    /// 最低必要枚数まで、既存デッキ内のカードIDを複製して埋める（相手デッキからIDを借りることもある）。
    /// </summary>
    private static Dictionary<int, int> EnsureDeckHasMinimumCardsForOpening(
        Dictionary<int, int> deck,
        Dictionary<int, int> fallbackForPadId,
        int minimumTotalCards,
        string deckLabelForLog)
    {
        var result = new Dictionary<int, int>();
        if (deck != null)
        {
            foreach (KeyValuePair<int, int> kv in deck)
            {
                if (kv.Value > 0)
                {
                    result[kv.Key] = kv.Value;
                }
            }
        }

        int total = 0;
        foreach (KeyValuePair<int, int> kv in result)
        {
            total += kv.Value;
        }

        int? padId = FirstPositiveCountCardId(result) ?? FirstPositiveCountCardId(fallbackForPadId);
        if (!padId.HasValue)
        {
            Debug.LogWarning($"[Deck:{deckLabelForLog}] パッド用のカードIDが取得できません（デッキが空の可能性）。");
            return result;
        }

        int id = padId.Value;
        int added = 0;
        while (total < minimumTotalCards)
        {
            if (!result.ContainsKey(id))
            {
                result[id] = 0;
            }

            result[id]++;
            total++;
            added++;
        }

        if (added > 0)
        {
            Debug.Log($"[Deck:{deckLabelForLog}] オープニング要件のため山札を {added} 枚パッドしました（合計 {total} 枚、最低 {minimumTotalCards} 枚）。");
        }

        return result;
    }

    private static int? FirstPositiveCountCardId(Dictionary<int, int> deck)
    {
        if (deck == null)
        {
            return null;
        }

        foreach (KeyValuePair<int, int> kv in deck)
        {
            if (kv.Value > 0)
            {
                return kv.Key;
            }
        }

        return null;
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
        if (isMatchFinished)
        {
            return;
        }

        if (cardController == null || cardController.Data == null)
        {
            return;
        }

        if (TryHandlePendingUnitAttackTarget(cardController))
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
        bool isOnAnyDeployField =
            cardController.transform.IsChildOf(cardGameRule.PlayerDeployPanel)
            || cardController.transform.IsChildOf(enemyCardGameRule.PlayerDeployPanel);

        if (isInShield && cardController.IsShieldFaceHidden)
        {
            Debug.Log("シールドは裏向きです。破壊されると中身が表示されます。");
            return;
        }

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

        if (isOnAnyDeployField && cardController.Data != null)
        {
            TextMeshProUGUI battleStatText = FilterPanel.CreateChildTextCustom("BattleStatText", UIAnchor.TopCenter, 320, 44);
            battleStatText.text = $"AP:{cardController.CurrentPower}  HP:{cardController.CurrentHp}";
            battleStatText.fontSize = 28;
            battleStatText.color = Color.black;
            RectTransform statRt = battleStatText.GetComponent<RectTransform>();
            statRt.anchoredPosition = new Vector2(0f, -30f);
            battleStatText.transform.SetAsLastSibling();

            if (cardController.Data.type == Type.Unit && cardController.MountedPilot != null)
            {
                GameObject pilotCopy = FilterPanel.CreateChildImageFrom(cardController.MountedPilot.gameObject);
                RectTransform pilotCopyRt = pilotCopy.GetComponent<RectTransform>();
                if (pilotCopyRt != null)
                {
                    pilotCopyRt.anchoredPosition = new Vector2(0f, -120f);
                    pilotCopyRt.localScale = Vector3.one * 0.95f;
                }
                pilotCopy.transform.SetAsLastSibling();
            }
        }

        // どのケースでも閉じられるようにする
        var closeButton = FilterPanel.CreateChildButton("close");
        RectTransform closeBtnRect = closeButton.GetComponent<RectTransform>();
        closeBtnRect.sizeDelta = new Vector2(140, 44);
        closeBtnRect.anchoredPosition = new Vector2(0, -130);
        closeButton.onClick.AddListener(() => Destroy(FilterPanel));
    
        // 場のカードはトラッシュ送り操作を可能にする。
        if (isOnField)
        {
            bool canShowUnitAttackMenu = currentPhase == BattlePhase.MainPhase
                && ownerType == currentPlayerType
                && cardController.Data.type == Type.Unit
                && cardController.AttackFlgState == AttackFlg.True;

            if (canShowUnitAttackMenu)
            {
                Gundam2024RuleScript.PlayerState opponentState = ownerType == PlayerType.Player
                    ? gundamRule.Enemy
                    : gundamRule.Player;
                bool showShieldAttack = gundamRule.CanShowUnitShieldAttackOption(
                    opponentState,
                    cardController.CurrentPower);
                bool showDirectAttack = opponentState.shield <= 0;

                if (showShieldAttack || showDirectAttack)
                {
                    string shieldLabel = showDirectAttack
                        ? "Direct Attack"
                        : opponentState.exBase > 0
                            ? $"Attack Shield (deal {cardController.CurrentPower} to EX Base)"
                            : "Attack Shield (break 1)";
                    var shieldAttackBtn = FilterPanel.CreateChildButton(shieldLabel);
                    RectTransform shieldRect = shieldAttackBtn.GetComponent<RectTransform>();
                    shieldRect.sizeDelta = new Vector2(320, 50);
                    shieldRect.anchoredPosition = new Vector2(0, -10);
                    shieldAttackBtn.onClick.AddListener(() =>
                    {
                        TryUnitShieldAttackFromUnit(cardController);
                        Destroy(FilterPanel);
                    });
                }

                var unitAttackBtn = FilterPanel.CreateChildButton("Attack Unit (tap enemy REST unit)");
                RectTransform unitAtkRect = unitAttackBtn.GetComponent<RectTransform>();
                unitAtkRect.sizeDelta = new Vector2(320, 50);
                unitAtkRect.anchoredPosition = new Vector2(0, -70);
                unitAttackBtn.onClick.AddListener(() =>
                {
                    pendingUnitAttackAttacker = cardController;
                    OpenEnemyUnitAttackTargetSelectionUI(cardController, ownerType);
                    Destroy(FilterPanel);
                });

                closeBtnRect.anchoredPosition = new Vector2(0, -200);
            }

            var trashButton = FilterPanel.CreateChildButton("send to trash");
            RectTransform trashBtnRect = trashButton.GetComponent<RectTransform>();
            trashBtnRect.sizeDelta = new Vector2(180, 50);
            trashBtnRect.anchoredPosition = new Vector2(0, canShowUnitAttackMenu ? -130 : -70);

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
        Gundam2024RuleScript.PlayerState ownerState = ownerSide == Gundam2024RuleScript.PlayerSide.Player
            ? gundamRule.Player
            : gundamRule.Enemy;
        int currentLevel = ownerState.TotalLevel;
        int currentResource = ownerState.resource;

        if (currentLevel < cardController.Data.level)
        {
            Debug.Log("レベルが足りません。");
            Destroy(FilterPanel);
            return;
        }

        if (cardController.Data.type == Type.Pilot)
        {
            List<CardController> mountTargets = GetMountableUnits(ownerType);
            if (mountTargets.Count == 0)
            {
                Debug.Log("パイロットを乗せるユニットがバトルゾーンにいません。");
                return;
            }

            int requiredExForPilot = Mathf.Max(0, cost - currentResource);
            if (requiredExForPilot > 0)
            {
                if (ownerState.exResource < requiredExForPilot)
                {
                    Debug.Log("リソース不足のためパイロットを配備できません。");
                    return;
                }

                var exUseLabel = FilterPanel.CreateChildTextCustom("UseExPromptPilot", UIAnchor.TopCenter, 420, 60);
                exUseLabel.text = $"Resource が {requiredExForPilot} 足りません。EXリソースを利用しますか？";
                exUseLabel.fontSize = 20;
                exUseLabel.alignment = TextAlignmentOptions.Center;
                exUseLabel.color = Color.black;
                exUseLabel.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -20f);

                var yesBtn = FilterPanel.CreateChildButton($"Yes (Use EX:{requiredExForPilot})");
                RectTransform yesRt = yesBtn.GetComponent<RectTransform>();
                yesRt.sizeDelta = new Vector2(220f, 50f);
                yesRt.anchoredPosition = new Vector2(-125f, -90f);
                yesBtn.onClick.AddListener(() =>
                {
                    ShowPilotMountTargetButtons(FilterPanel, cardController, ownerType, ownerSide, cost, requiredExForPilot);
                });

                var noBtn = FilterPanel.CreateChildButton("No");
                RectTransform noRt = noBtn.GetComponent<RectTransform>();
                noRt.sizeDelta = new Vector2(220f, 50f);
                noRt.anchoredPosition = new Vector2(125f, -90f);
                noBtn.onClick.AddListener(() => Destroy(FilterPanel));
                return;
            }

            ShowPilotMountTargetButtons(FilterPanel, cardController, ownerType, ownerSide, cost, 0);
            return;
        }

        if (currentResource < cost)
        {
            int requiredEx = cost - currentResource;
            if (ownerState.exResource < requiredEx)
            {
                Debug.Log("リソースポイントが足りません。EXリソースを含めても不足しています。");
                return;
            }

            var exUseLabel = FilterPanel.CreateChildTextCustom("UseExPrompt", UIAnchor.TopCenter, 380, 60);
            exUseLabel.text = $"Resource が {requiredEx} 足りません。EXリソースを利用しますか？";
            exUseLabel.fontSize = 20;
            exUseLabel.alignment = TextAlignmentOptions.Center;
            exUseLabel.color = Color.black;
            exUseLabel.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -20f);

            var yesBtn = FilterPanel.CreateChildButton($"Yes (Use EX:{requiredEx})");
            RectTransform yesRt = yesBtn.GetComponent<RectTransform>();
            yesRt.sizeDelta = new Vector2(220f, 50f);
            yesRt.anchoredPosition = new Vector2(-125f, -90f);
            yesBtn.onClick.AddListener(() =>
            {
                if (!gundamRule.TryConsumeResource(ownerSide, cost, requiredEx, cardController.Data.id))
                {
                    Debug.Log("EX/リソースが不足しているため配備できません。");
                    return;
                }

                SendCardToField(cardController, ownerType, ownerRule);
                SyncResourceViewsFromRule(ownerSide);
                Destroy(FilterPanel);
            });

            var noBtn = FilterPanel.CreateChildButton("No");
            RectTransform noRt = noBtn.GetComponent<RectTransform>();
            noRt.sizeDelta = new Vector2(220f, 50f);
            noRt.anchoredPosition = new Vector2(125f, -90f);
            noBtn.onClick.AddListener(() => Destroy(FilterPanel));
            return;
        }

        var playButton = FilterPanel.CreateChildButton("send to field");
        RectTransform btnRect = playButton.GetComponent<RectTransform>();
        btnRect.sizeDelta = new Vector2(240, 50);
        btnRect.anchoredPosition = new Vector2(0, -10);

        playButton.onClick.AddListener(() =>
        {
            if (!gundamRule.TryConsumeResource(ownerSide, cost, 0, cardController.Data.id))
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
        if (isMatchFinished)
        {
            return;
        }

        switch (nextPhase)
        {
            case BattlePhase.StartTurn:
                if (!isTurnPhaseSequenceRunning)
                {
                    StartCoroutine(ExecuteTurnPhaseSequenceCoroutine());
                }
                break;
            case BattlePhase.ActivePhase:
                currentPhase = BattlePhase.ActivePhase;
                UpdateEndTurnButtonVisibility();
                Debug.Log("アクティブフェイズに入りました。");
                // アクティブフェイズの処理をここに書く
                break;
            case BattlePhase.DrawPhase:
                currentPhase = BattlePhase.DrawPhase;
                UpdateEndTurnButtonVisibility();
                Debug.Log("ドローフェイズに入りました。");
                // ドローフェイズの処理をここに書く
                break;
            case BattlePhase.ResourcePhase:
                currentPhase = BattlePhase.ResourcePhase;
                UpdateEndTurnButtonVisibility();
                Debug.Log("リソースフェイズに入りました。");
                // リソースフェイズの処理をここに書く
                break;
            case BattlePhase.MainPhase:
                currentPhase = BattlePhase.MainPhase;
                UpdateEndTurnButtonVisibility();
                Debug.Log("メインフェイズに入りました。");
                // メインフェイズの処理をここに書く
                ExcuteMainPhase();
                break;
            case BattlePhase.EndTurn:
                StartCoroutine(ExecuteEndTurnWithPhasePauseCoroutine());
                break;
            case BattlePhase.OpponentTurn:
                Debug.Log("相手のターンに入りました。");
                break;
        }
        
    }

    private IEnumerator ExecuteTurnPhaseSequenceCoroutine()
    {
        if (isTurnPhaseSequenceRunning)
        {
            yield break;
        }

        isTurnPhaseSequenceRunning = true;
        yield return ShowPhasePauseCoroutine(currentPlayerType == PlayerType.Player ? "Player Turn" : "Enemy Turn");
        currentPhase = BattlePhase.DrawPhase;
        UpdateEndTurnButtonVisibility();
        yield return ShowPhasePauseCoroutine("Draw Phase");

        currentPhase = BattlePhase.ResourcePhase;
        UpdateEndTurnButtonVisibility();
        yield return ShowPhasePauseCoroutine("Resource Phase");

        currentPhase = BattlePhase.ActivePhase;
        UpdateEndTurnButtonVisibility();
        yield return ShowPhasePauseCoroutine("Card & Resource Active");

        ExecuteTurnStartCore();

        currentPhase = BattlePhase.MainPhase;
        UpdateEndTurnButtonVisibility();
        yield return ShowPhasePauseCoroutine("Main Phase");
        ExcuteMainPhase();

        isTurnPhaseSequenceRunning = false;
    }

    private IEnumerator ExecuteEndTurnWithPhasePauseCoroutine()
    {
        currentPhase = BattlePhase.EndTurn;
        UpdateEndTurnButtonVisibility();
        yield return ShowPhasePauseCoroutine("End Phase");
        ExcueteEndTurn();
    }

    private IEnumerator ShowPhasePauseCoroutine(string phaseLabel)
    {
        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            yield return new WaitForSeconds(0.2f);
            yield break;
        }

        GameObject root = new GameObject("PhasePauseOverlay", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        root.transform.SetParent(canvas.transform, false);
        root.transform.SetAsLastSibling();
        root.SetFullSize();

        Image bg = root.GetComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.55f);
        bg.raycastTarget = true;

        TextMeshProUGUI phaseText = root.CreateChildTextCustom("PhasePauseText", UIAnchor.TopCenter, 680, 120);
        phaseText.text = phaseLabel;
        phaseText.fontSize = 44;
        if (phaseLabel == "Enemy Turn")
        {
            phaseText.color = new Color32(255, 90, 90, 255);
        }
        else if (phaseLabel == "Player Turn")
        {
            phaseText.color = new Color32(40, 110, 255, 255);
        }
        else
        {
            phaseText.color = Color.white;
        }
        phaseText.alignment = TextAlignmentOptions.Center;
        phaseText.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -180f);

        float wait = Mathf.Max(0.1f, phasePauseDurationSeconds);
        yield return new WaitForSeconds(wait);
        if (root != null)
        {
            Destroy(root);
        }
    }
    void ExecuteTurnStartCore()
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
            TriggerAllTimedEffectsForSide(PlayerType.Player, EffectTiming.OnTurnStart);
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
            TriggerAllTimedEffectsForSide(PlayerType.Enemy, EffectTiming.OnTurnStart);
        }
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
        if (isMatchFinished)
        {
            yield break;
        }

        Debug.Log("エネミーの行動を開始します。");
        yield return new WaitForSeconds(0.8f);

        bool deployed = TryEnemyDeployUnitFromHand();
        if (deployed)
        {
            yield return new WaitForSeconds(0.6f);
        }

        int attacked = 0;
        while (true)
        {
            int attackedNow = TryEnemyShieldAttacks();
            if (attackedNow <= 0)
            {
                if (isOnActionPopupOpen)
                {
                    // Close 後に onClose コールバックで攻撃が実行されるため、完了まで待って再評価する。
                    yield return new WaitUntil(() => !isOnActionPopupOpen);
                    yield return new WaitForSeconds(0.15f);
                    continue;
                }
                break;
            }

            attacked += attackedNow;
            if (isOnActionPopupOpen)
            {
                // アクションステップの Close まで次の攻撃に進ませない。
                yield return new WaitUntil(() => !isOnActionPopupOpen);
            }

            // 1回攻撃ごとに間隔を入れて、連続攻撃が速すぎる体感を防ぐ。
            yield return new WaitForSeconds(0.6f);
        }

        Debug.Log($"エネミーの行動が終了しました。deploy:{deployed} shieldAttack:{attacked}");
        // エンドフェイズに移行する
        ChangePhase(BattlePhase.EndTurn);
    }

    /// <summary>
    /// エネミー手札から、現在のレベル/リソースで出せる最初のユニットを1体だけ配備する。
    /// </summary>
    private bool TryEnemyDeployUnitFromHand()
    {
        RectTransform hand = enemyCardGameRule.HandScrollContent;
        if (hand == null)
        {
            return false;
        }

        Gundam2024RuleScript.PlayerSide side = Gundam2024RuleScript.PlayerSide.Enemy;
        for (int i = 0; i < hand.childCount; i++)
        {
            CardController cc = hand.GetChild(i).GetComponent<CardController>();
            if (cc == null || cc.Data == null || cc.Data.type != Type.Unit)
            {
                continue;
            }

            if (!gundamRule.CanPlayCard(side, cc.Data))
            {
                continue;
            }

            if (!gundamRule.TryConsumeResource(side, cc.Data.cost, 0, cc.Data.id))
            {
                continue;
            }

            SendCardToField(cc, PlayerType.Enemy, enemyCardGameRule);
            SyncResourceViewsFromRule(side);
            Debug.Log($"[Enemy] ユニット配備: {cc.Data.cardName}");
            return true;
        }

        return false;
    }

    /// <summary>
    /// 攻撃可能なエネミーユニットで、シールド攻撃かRESTユニット攻撃かを簡易評価して1回攻撃する。
    /// </summary>
    private int TryEnemyShieldAttacks()
    {
        if (isMatchFinished)
        {
            return 0;
        }

        // 1回の呼び出しで最大1回だけ攻撃する。
        List<CardController> snapshot = new List<CardController>(enemyBattleZoneCards);
        foreach (CardController unit in snapshot)
        {
            if (unit == null || unit.Data == null || unit.Data.type != Type.Unit)
            {
                continue;
            }

            if (unit.AttackFlgState != AttackFlg.True)
            {
                continue;
            }

            bool canAttackShield = gundamRule.CanShowUnitShieldAttackOption(gundamRule.Player, unit.CurrentPower);
            bool canDirectAttack = gundamRule.Player.shield <= 0;
            List<CardController> restTargets = GetEnemyAiRestTargets(PlayerType.Enemy);
            bool canAttackUnit = restTargets.Count > 0;
            if (!canAttackShield && !canDirectAttack && !canAttackUnit)
            {
                continue;
            }

            AttackFlg before = unit.AttackFlgState;
            bool attackShield = ShouldEnemyAiPreferShieldAttack(unit, canAttackShield || canDirectAttack, canAttackUnit, restTargets);
            if (attackShield)
            {
                TryUnitShieldAttackFromUnit(unit);
                Debug.Log($"[EnemyAI] {unit.Data.cardName} chose shield attack.");
                ShowEnemyAttackDecisionNotice($"{unit.Data.cardName} attacks SHIELD");
            }
            else
            {
                CardController target = SelectEnemyAiUnitAttackTarget(restTargets);
                if (target == null)
                {
                    continue;
                }

                TryUnitVsUnitAttack(unit, target, PlayerType.Enemy, PlayerType.Player);
                Debug.Log($"[EnemyAI] {unit.Data.cardName} chose unit attack target:{target.Data.cardName}");
                ShowEnemyAttackDecisionNotice($"{unit.Data.cardName} attacks UNIT: {target.Data.cardName}");
            }

            // 攻撃が成立した時だけカウント（OnAction待機で未成立なら数えない）。
            if (before == AttackFlg.True && unit.AttackFlgState == AttackFlg.False)
            {
                return 1;
            }
            if (isMatchFinished)
            {
                return 0;
            }
            if (isOnActionPopupOpen)
            {
                return 0;
            }
        }

        return 0;
    }

    private void ShowEnemyAttackDecisionNotice(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            return;
        }

        Transform existing = canvas.transform.Find("EnemyAttackNotice");
        if (existing != null)
        {
            Destroy(existing.gameObject);
        }

        GameObject root = new GameObject("EnemyAttackNotice", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        root.transform.SetParent(canvas.transform, false);
        root.transform.SetAsLastSibling();

        RectTransform rt = root.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(760f, 86f);
        rt.anchoredPosition = new Vector2(0f, 160f);

        Image bg = root.GetComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.72f);
        bg.raycastTarget = false;

        TextMeshProUGUI text = root.CreateChildTextCustom("EnemyAttackNoticeText", UIAnchor.FullSize, 740, 80);
        text.text = message;
        text.fontSize = 34;
        text.color = new Color32(255, 110, 110, 255);
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;

        float life = Mathf.Max(0.2f, enemyAttackNoticeSeconds);
        Destroy(root, life);
    }

    private List<CardController> GetEnemyAiRestTargets(PlayerType attackerOwner)
    {
        List<CardController> enemies = GetAliveEnemyUnits(attackerOwner);
        List<CardController> rest = new List<CardController>();
        for (int i = 0; i < enemies.Count; i++)
        {
            CardController c = enemies[i];
            if (c != null && c.IsRestState)
            {
                rest.Add(c);
            }
        }
        return rest;
    }

    private bool ShouldEnemyAiPreferShieldAttack(
        CardController attacker,
        bool canShieldAttack,
        bool canUnitAttack,
        List<CardController> restTargets)
    {
        if (!canShieldAttack && canUnitAttack)
        {
            return false;
        }
        if (canShieldAttack && !canUnitAttack)
        {
            return true;
        }
        if (!canShieldAttack && !canUnitAttack)
        {
            return false;
        }

        int shieldScore = gundamRule.Player.shield <= 1 ? 100 : 35;
        if (gundamRule.Player.exBase > 0)
        {
            shieldScore += Mathf.Clamp(attacker.CurrentPower, 0, 20);
        }
        else
        {
            shieldScore += 12;
        }

        CardController bestUnitTarget = SelectEnemyAiUnitAttackTarget(restTargets);
        int unitScore = 30;
        if (bestUnitTarget != null)
        {
            unitScore += Mathf.Clamp(bestUnitTarget.CurrentPower, 0, 20);
            if (attacker.CurrentPower >= bestUnitTarget.CurrentHp)
            {
                unitScore += 18;
            }
        }

        return shieldScore >= unitScore;
    }

    private CardController SelectEnemyAiUnitAttackTarget(List<CardController> restTargets)
    {
        CardController best = null;
        int bestScore = int.MinValue;
        for (int i = 0; i < restTargets.Count; i++)
        {
            CardController t = restTargets[i];
            if (t == null || t.Data == null)
            {
                continue;
            }

            int score = t.CurrentPower * 2 - t.CurrentHp;
            if (score > bestScore)
            {
                bestScore = score;
                best = t;
            }
        }
        return best;
    }

    void ExcueteEndTurn()
    {
        if (isEndTurnFlowRunning)
        {
            return;
        }

        StartCoroutine(ExecuteEndTurnCoroutine());
    }

    private IEnumerator ExecuteEndTurnCoroutine()
    {
        isEndTurnFlowRunning = true;
        pendingUnitAttackAttacker = null;
        pendingOnAttackEffectResolvedAttacker = null;
        PlayerType endingTurnSide = currentPlayerType;
        bool waitingForClose = false;
        bool startedOnActionStep = TryHandleSingleSideOnActionStep(
            endingTurnSide,
            "turn end",
            () => waitingForClose = false);
        if (startedOnActionStep)
        {
            waitingForClose = true;
            yield return new WaitUntil(() => !waitingForClose);
        }

        TriggerAllTimedEffectsForSide(endingTurnSide, EffectTiming.OnTurnEnd);
        // ターン終了時は盤面全体の「ターン終了で切れる補正」を解除する。
        ClearTimedPowerModifiersForAllBattleUnits(EffectDuration.UntilEndOfTurn);
        DumpTurnResourceUsageLogs(endingTurnSide, "end turn");

        // プレイヤーとエネミーのターンを切り替える
        currentPlayerType = (currentPlayerType == PlayerType.Player) ? PlayerType.Enemy : PlayerType.Player;
        AdvanceRuleToNextTurnStart();
        UpdateEndTurnButtonVisibility();

        Debug.Log("エンドフェイズの処理を実行します。");
        ChangePhase(BattlePhase.StartTurn);
        isEndTurnFlowRunning = false;
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
        targetRule.ApplyExternalResourceState(state.TotalLevel, state.resource, state.exResource);
        targetRule.SetExBaseDisplay(state.exBase);

        if (side == Gundam2024RuleScript.PlayerSide.Player)
        {
            PlayerlevelText.text = $"LV:{state.TotalLevel}";
            PlayerresourcePointText.text = state.resource.ToString();
            if (ExresourcePointText != null)
            {
                ExresourcePointText.text = state.exResource.ToString();
            }
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

    /// <summary>
    /// カード効果の共通入口: EXリソース増減を適用してUIを同期する。
    /// amount が正なら増加、負なら減少。
    /// </summary>
    public void ApplyCardEffectExResourceDelta(PlayerType target, int amount)
    {
        Gundam2024RuleScript.PlayerSide side = ToRuleSide(target);
        if (amount > 0)
        {
            gundamRule.AddExResource(side, amount);
        }
        else if (amount < 0)
        {
            gundamRule.AddExResource(side, amount);
        }

        SyncResourceViewsFromRule(side);
    }

    private void SendCardToTrash(CardController cardController, PlayerType ownerType)
    {
        if (cardController == null || cardController.Data == null)
        {
            return;
        }

        if (cardController.Data.type == Type.Unit && cardController.MountedPilot != null)
        {
            SendCardToTrash(cardController.MountedPilot, ownerType);
        }

        TriggerCardEffects(cardController, ownerType, EffectTiming.OnDestroyed);

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

        // ユニット配備直後はアクティブ（起き状態）で配置する。
        if (cardController.Data.type == Type.Unit)
        {
            cardController.ResetRuntimeStatsFromData();
            // 配備ターン: 見た目はアクティブ(起き)だが、攻撃フラグは false
            cardController.SetAttackFlg(AttackFlg.False);
            cardController.SetUnitRestVisual(false);
        }

        TriggerCardEffects(cardController, ownerType, EffectTiming.OnPlayed);
    }

    private List<CardController> GetMountableUnits(PlayerType ownerType)
    {
        List<CardController> source = ownerType == PlayerType.Player ? playerBattleZoneCards : enemyBattleZoneCards;
        List<CardController> result = new List<CardController>();
        foreach (CardController c in source)
        {
            if (c == null || c.Data == null || c.Data.type != Type.Unit)
            {
                continue;
            }

            if (c.CanMountPilot())
            {
                result.Add(c);
            }
        }
        return result;
    }

    private void ShowPilotMountTargetButtons(
        GameObject filterPanel,
        CardController pilotCard,
        PlayerType ownerType,
        Gundam2024RuleScript.PlayerSide ownerSide,
        int cost,
        int exToUse)
    {
        if (filterPanel == null || pilotCard == null || pilotCard.Data == null)
        {
            return;
        }

        foreach (Transform child in filterPanel.transform)
        {
            Button btn = child.GetComponent<Button>();
            if (btn != null)
            {
                TextMeshProUGUI label = btn.GetComponentInChildren<TextMeshProUGUI>();
                bool isClose = label != null && string.Equals(label.text, "close", System.StringComparison.OrdinalIgnoreCase);
                if (!isClose)
                {
                    btn.interactable = false;
                }
            }
        }

        List<CardController> targets = GetMountableUnits(ownerType);
        if (targets.Count == 0)
        {
            Debug.Log("搭乗可能なユニットがありません。");
            return;
        }

        TextMeshProUGUI title = filterPanel.CreateChildTextCustom("PilotTargetTitle", UIAnchor.TopCenter, 460, 40);
        title.text = "搭乗先ユニットを選択";
        title.fontSize = 22;
        title.alignment = TextAlignmentOptions.Center;
        title.color = Color.black;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -160f);

        for (int i = 0; i < targets.Count; i++)
        {
            CardController target = targets[i];
            string label = $"{target.Data.cardName} (AP:{target.CurrentPower} HP:{target.CurrentHp})";
            Button targetBtn = filterPanel.CreateChildButton(label);
            RectTransform tr = targetBtn.GetComponent<RectTransform>();
            tr.sizeDelta = new Vector2(380f, 44f);
            tr.anchoredPosition = new Vector2(0f, -210f - (i * 52f));
            targetBtn.onClick.AddListener(() =>
            {
                if (!gundamRule.TryConsumeResource(ownerSide, cost, exToUse, pilotCard.Data.id))
                {
                    Debug.Log("リソース不足でパイロットを搭乗できません。");
                    return;
                }

                if (ownerType == PlayerType.Player)
                {
                    playerHandCards.Remove(pilotCard.Data);
                }
                else
                {
                    enemyHandCards.Remove(pilotCard.Data);
                }

                if (!target.TryAttachPilot(pilotCard))
                {
                    Debug.Log("パイロット搭乗に失敗しました。");
                    return;
                }

                Debug.Log($"[Pilot] {pilotCard.Data.cardName} を {target.Data.cardName} に搭乗。AP:{target.CurrentPower} HP:{target.CurrentHp}");
                TriggerCardEffects(pilotCard, ownerType, EffectTiming.OnPlayed);
                SyncResourceViewsFromRule(ownerSide);
                Destroy(filterPanel);
            });
        }
    }

    /// <summary>
    /// 自分ターン開始時：場の自軍ユニットをアクティブ(True)へ更新。
    /// 表示は起き状態になり、この状態で攻撃可能。
    /// </summary>
    private void ApplyTurnStartAttackFlgForCurrentPlayer()
    {
        if (currentPlayerType == PlayerType.Player)
        {
            Debug.Log("[AttackFlg] プレイヤーターン開始：場のユニットをアクティブ(True)に設定");
            foreach (var c in playerBattleZoneCards)
            {
                if (c != null && c.Data != null && c.Data.type == Type.Unit)
                {
                    c.SetAttackFlg(AttackFlg.True);
                    c.SetUnitRestVisual(false);
                }
            }
        }
        else
        {
            Debug.Log("[AttackFlg] エネミーターン開始：場のユニットをアクティブ(True)に設定");
            foreach (var c in enemyBattleZoneCards)
            {
                if (c != null && c.Data != null && c.Data.type == Type.Unit)
                {
                    c.SetAttackFlg(AttackFlg.True);
                    c.SetUnitRestVisual(false);
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

    /// <summary>トラッシュエリアクリックで、トラッシュに入ったカードを一覧表示する。</summary>
    private void OpenTrashInspectionPanel(CardGameRule rule)
    {
        if (rule == null || CardImagePrefab == null)
        {
            return;
        }

        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            return;
        }

        GameObject root = new GameObject("TrashInspectRoot", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        root.transform.SetParent(canvas.transform, false);
        root.transform.SetAsLastSibling();
        root.SetFullSize();
        Image dim = root.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.55f);
        dim.raycastTarget = true;

        TextMeshProUGUI title = root.CreateChildTextCustom("TrashTitle", UIAnchor.TopCenter, 520, 48);
        title.text = "トラッシュ一覧";
        title.fontSize = 28;
        title.color = Color.white;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -24f);

        GameObject scrollGo = root.CreateGridScrollView(560, 360, UIAnchor.TopCenter);
        RectTransform scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.anchoredPosition = new Vector2(0f, -88f);
        scrollGo.ConfigureGridCellFromViewportHeight(0.75f, 56f);

        ScrollRect sr = scrollGo.GetComponent<ScrollRect>();
        RectTransform content = sr != null ? sr.content : null;
        if (content != null)
        {
            IReadOnlyList<int> ids = rule.GetTrashCardIds();
            if (ids.Count == 0)
            {
                TextMeshProUGUI empty = content.gameObject.CreateChildTextCustom("EmptyTrash", UIAnchor.TopCenter, 480, 40);
                empty.text = "（トラッシュは空です）";
                empty.fontSize = 22;
                empty.color = new Color(0.9f, 0.9f, 0.9f, 1f);
                empty.GetComponent<RectTransform>().anchoredPosition = Vector2.zero;
            }
            else
            {
                foreach (int id in ids)
                {
                    CardData data = DeckSettinObject.Instance.GetCardDataById(id);
                    if (data == null)
                    {
                        continue;
                    }

                    GameObject go = Instantiate(CardImagePrefab, content);
                    CardController cc = go.GetComponent<CardController>();
                    if (cc != null)
                    {
                        cc.SetUp(data, _ => { });
                        go.transform.localScale = new Vector3(0.4f, 0.4f, 1f);
                    }
                }
            }
        }

        Button closeBtn = root.CreateChildButton("Close");
        RectTransform closeRt = closeBtn.GetComponent<RectTransform>();
        closeRt.sizeDelta = new Vector2(160f, 44f);
        closeRt.anchorMin = new Vector2(0.5f, 0f);
        closeRt.anchorMax = new Vector2(0.5f, 0f);
        closeRt.pivot = new Vector2(0.5f, 0f);
        closeRt.anchoredPosition = new Vector2(0f, 36f);
        closeBtn.onClick.AddListener(() => Destroy(root));
    }

    private bool IsOnDeployPanel(CardController c, PlayerType owner)
    {
        if (c == null)
        {
            return false;
        }

        CardGameRule rule = owner == PlayerType.Player ? cardGameRule : enemyCardGameRule;
        return c.transform.IsChildOf(rule.PlayerDeployPanel);
    }

    private bool IsUnitAliveOnAnyDeployField(CardController c)
    {
        if (c == null || c.Data == null || c.Data.type != Type.Unit)
        {
            return false;
        }

        bool onField = c.transform.IsChildOf(cardGameRule.PlayerDeployPanel)
            || c.transform.IsChildOf(enemyCardGameRule.PlayerDeployPanel);
        return onField && c.CurrentHp > 0;
    }

    private bool IsRestEnemyUnitTarget(CardController target, PlayerType attackerOwner)
    {
        if (target == null || target.Data == null || target.Data.type != Type.Unit)
        {
            return false;
        }

        bool inPlayerField = target.transform.IsChildOf(cardGameRule.PlayerDeployPanel);
        bool inEnemyField = target.transform.IsChildOf(enemyCardGameRule.PlayerDeployPanel);
        if (!inPlayerField && !inEnemyField)
        {
            return false;
        }

        PlayerType targetOwner = inPlayerField ? PlayerType.Player : PlayerType.Enemy;
        if (targetOwner == attackerOwner)
        {
            return false;
        }

        if (!target.IsRestState)
        {
            return false;
        }

        return IsUnitAliveOnAnyDeployField(target);
    }

    /// <summary>「相手ユニットを攻撃」後のターゲット解決。true のときは以降のフィルター処理を行わない。</summary>
    private bool TryHandlePendingUnitAttackTarget(CardController clicked)
    {
        if (pendingUnitAttackAttacker == null)
        {
            return false;
        }

        if (currentPhase != BattlePhase.MainPhase)
        {
            pendingUnitAttackAttacker = null;
            return false;
        }

        PlayerType attackerOwner = ResolveCardOwner(pendingUnitAttackAttacker.transform);
        if (attackerOwner != currentPlayerType)
        {
            pendingUnitAttackAttacker = null;
            return false;
        }

        if (!IsUnitAliveOnAnyDeployField(pendingUnitAttackAttacker))
        {
            pendingUnitAttackAttacker = null;
            return false;
        }

        bool clickedOnAnyField = clicked != null
            && (clicked.transform.IsChildOf(cardGameRule.PlayerDeployPanel)
                || clicked.transform.IsChildOf(enemyCardGameRule.PlayerDeployPanel));

        if (clicked == pendingUnitAttackAttacker && clickedOnAnyField)
        {
            pendingUnitAttackAttacker = null;
            pendingOnAttackEffectResolvedAttacker = null;
            Debug.Log("Unit attack canceled.");
            return true;
        }

        if (IsRestEnemyUnitTarget(clicked, attackerOwner))
        {
            PlayerType defenderOwner = ResolveCardOwner(clicked.transform);
            TryUnitVsUnitAttack(pendingUnitAttackAttacker, clicked, attackerOwner, defenderOwner);
            return true;
        }

        if (clickedOnAnyField)
        {
            Debug.Log("Only REST enemy units can be selected as attack targets.");
            return true;
        }

        pendingUnitAttackAttacker = null;
        pendingOnAttackEffectResolvedAttacker = null;
        Debug.Log("Attack target selection canceled.");
        return false;
    }

    private void OpenEnemyUnitAttackTargetSelectionUI(CardController attacker, PlayerType attackerOwner)
    {
        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            Debug.Log("Battle canvas is not available.");
            return;
        }

        List<CardController> enemyUnits = GetAliveEnemyUnits(attackerOwner);
        if (enemyUnits.Count == 0)
        {
            Debug.Log("No enemy units to attack.");
            return;
        }

        GameObject root = new GameObject("AttackEnemySelect", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        root.transform.SetParent(canvas.transform, false);
        root.transform.SetAsLastSibling();
        root.SetFullSize();
        Image bg = root.GetComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.5f);
        bg.raycastTarget = true;

        TextMeshProUGUI title = root.CreateChildTextCustom("AttackEnemyTitle", UIAnchor.TopCenter, 620, 48);
        title.text = "Select enemy unit to attack";
        title.color = Color.white;
        title.fontSize = 24;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -24f);

        GameObject scrollGo = root.CreateGridScrollView(620, 420, UIAnchor.TopCenter);
        RectTransform scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.anchoredPosition = new Vector2(0f, -80f);
        scrollGo.ConfigureGridCellFromViewportHeight(0.78f, 56f);
        ScrollRect sr = scrollGo.GetComponent<ScrollRect>();
        RectTransform content = sr != null ? sr.content : null;
        if (content == null)
        {
            Destroy(root);
            return;
        }

        for (int i = 0; i < enemyUnits.Count; i++)
        {
            CardController unit = enemyUnits[i];
            if (unit == null || unit.Data == null)
            {
                continue;
            }

            GameObject cardItem = Instantiate(CardImagePrefab, content);
            CardController itemCc = cardItem.GetComponent<CardController>();
            if (itemCc != null)
            {
                itemCc.SetUp(unit.Data, _ => { });
            }

            GameObject statBg = new GameObject("StatBg", typeof(RectTransform), typeof(Image));
            statBg.transform.SetParent(cardItem.transform, false);
            RectTransform statBgRt = statBg.GetComponent<RectTransform>();
            statBgRt.anchorMin = new Vector2(0f, 0f);
            statBgRt.anchorMax = new Vector2(1f, 0f);
            statBgRt.pivot = new Vector2(0.5f, 0f);
            statBgRt.sizeDelta = new Vector2(0f, 28f);
            statBgRt.anchoredPosition = new Vector2(0f, 0f);
            Image statBgImg = statBg.GetComponent<Image>();
            statBgImg.color = new Color(0f, 0f, 0f, 0.55f);
            statBgImg.raycastTarget = false;

            TextMeshProUGUI statText = statBg.CreateChildTextCustom("StatText", UIAnchor.FullSize, 120, 24);
            statText.text = $"AP:{unit.CurrentPower} HP:{unit.CurrentHp} {(unit.IsRestState ? "REST" : "ACTIVE")}";
            statText.fontSize = 14;
            statText.color = Color.white;
            statText.alignment = TextAlignmentOptions.Center;

            Button btn = cardItem.GetComponent<Button>();
            if (btn == null)
            {
                btn = cardItem.AddComponent<Button>();
            }

            CardController selectedUnit = unit;
            btn.onClick.AddListener(() =>
            {
                pendingUnitAttackAttacker = attacker;
                Destroy(root);

                PlayerType defenderOwner = ResolveCardOwner(selectedUnit.transform);
                if (TryOpenOnAttackEnemySelectionPanel(
                    attacker,
                    attackerOwner,
                    selectedUnit,
                    () =>
                    {
                        pendingOnAttackEffectResolvedAttacker = attacker;
                        TryUnitVsUnitAttack(attacker, selectedUnit, attackerOwner, defenderOwner);
                    }))
                {
                    return;
                }

                // デバフ対象選択UIが不要な場合のみ、即攻撃解決へ進む。
                pendingOnAttackEffectResolvedAttacker = attacker;
                TryUnitVsUnitAttack(attacker, selectedUnit, attackerOwner, defenderOwner);
            });
        }

        Button cancel = root.CreateChildButton("Cancel");
        RectTransform cancelRt = cancel.GetComponent<RectTransform>();
        cancelRt.sizeDelta = new Vector2(180f, 46f);
        cancelRt.anchoredPosition = new Vector2(0f, 48f);
        cancel.onClick.AddListener(() =>
        {
            pendingUnitAttackAttacker = null;
            pendingOnAttackEffectResolvedAttacker = null;
            Destroy(root);
        });
    }

    private bool TryOpenOnAttackEnemySelectionPanel(
        CardController attacker,
        PlayerType attackerOwner,
        CardController attackedTarget,
        System.Action onResolved = null)
    {
        if (attacker == null || attacker.Data == null)
        {
            return false;
        }

        List<CardController> effectSources = new List<CardController> { attacker };
        if (attacker.MountedPilot != null && attacker.MountedPilot.Data != null)
        {
            effectSources.Add(attacker.MountedPilot);
        }

        for (int sourceIndex = 0; sourceIndex < effectSources.Count; sourceIndex++)
        {
            CardController sourceCard = effectSources[sourceIndex];
            if (sourceCard == null || sourceCard.Data == null || sourceCard.Data.timedEffects == null)
            {
                continue;
            }

            for (int i = 0; i < sourceCard.Data.timedEffects.Count; i++)
            {
                TimedEffectData timed = sourceCard.Data.timedEffects[i];
                if (timed == null || timed.timing != EffectTiming.OnAttack || timed.effects == null)
                {
                    continue;
                }

                for (int j = 0; j < timed.effects.Count; j++)
                {
                    EffectData effect = timed.effects[j];
                    if (effect == null)
                    {
                        continue;
                    }

                    bool enemyUnitTarget = effect.target == TargetType.EnemyUnit || effect.target == TargetType.EnemyAllUnits;
                    if (!enemyUnitTarget)
                    {
                        continue;
                    }

                    if (effect.selectionMode == EffectSelectionMode.AttackedTargetOnly)
                    {
                        List<CardController> singleTarget = new List<CardController> { attackedTarget };
                        ApplyEffectToSpecificTargets(sourceCard, attackerOwner, effect, singleTarget);
                        continue;
                    }

                    List<CardController> enemyUnits = GetAliveEnemyUnits(attackerOwner);
                    if (enemyUnits.Count == 0)
                    {
                        return false;
                    }

                    OpenEnemyUnitEffectSelectionUI(sourceCard, attackerOwner, effect, enemyUnits, onResolved);
                    return true;
                }
            }
        }

        return false;
    }

    private void OpenEnemyUnitEffectSelectionUI(
        CardController attacker,
        PlayerType attackerOwner,
        EffectData effect,
        List<CardController> enemyUnits,
        System.Action onResolved = null)
    {
        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            pendingOnAttackEffectResolvedAttacker = pendingUnitAttackAttacker;
            return;
        }

        GameObject root = new GameObject("OnAttackEffectSelect", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        root.transform.SetParent(canvas.transform, false);
        root.transform.SetAsLastSibling();
        root.SetFullSize();
        Image bg = root.GetComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.5f);
        bg.raycastTarget = true;

        TextMeshProUGUI title = root.CreateChildTextCustom("EffectSelectTitle", UIAnchor.TopCenter, 620, 48);
        title.text = "Select debuff target unit";
        title.color = Color.white;
        title.fontSize = 24;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -24f);

        GameObject scrollGo = root.CreateGridScrollView(620, 420, UIAnchor.TopCenter);
        RectTransform scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.anchoredPosition = new Vector2(0f, -80f);
        scrollGo.ConfigureGridCellFromViewportHeight(0.78f, 56f);
        ScrollRect sr = scrollGo.GetComponent<ScrollRect>();
        RectTransform content = sr != null ? sr.content : null;

        List<CardController> selected = new List<CardController>();
        for (int i = 0; i < enemyUnits.Count; i++)
        {
            CardController unit = enemyUnits[i];
            if (content == null)
            {
                continue;
            }

            GameObject cardItem = Instantiate(CardImagePrefab, content);
            CardController itemCc = cardItem.GetComponent<CardController>();

            GameObject statBg = new GameObject("StatBg", typeof(RectTransform), typeof(Image));
            statBg.transform.SetParent(cardItem.transform, false);
            RectTransform statBgRt = statBg.GetComponent<RectTransform>();
            statBgRt.anchorMin = new Vector2(0f, 0f);
            statBgRt.anchorMax = new Vector2(1f, 0f);
            statBgRt.pivot = new Vector2(0.5f, 0f);
            statBgRt.sizeDelta = new Vector2(0f, 28f);
            statBgRt.anchoredPosition = new Vector2(0f, 0f);
            Image statBgImg = statBg.GetComponent<Image>();
            statBgImg.color = new Color(0f, 0f, 0f, 0.55f);
            statBgImg.raycastTarget = false;

            TextMeshProUGUI statText = statBg.CreateChildTextCustom("StatText", UIAnchor.FullSize, 120, 24);
            statText.text = $"AP:{unit.CurrentPower} HP:{unit.CurrentHp} {(unit.IsRestState ? "REST" : "ACTIVE")}";
            statText.fontSize = 14;
            statText.color = Color.white;
            statText.alignment = TextAlignmentOptions.Center;

            Button btn = cardItem.GetComponent<Button>();
            if (btn == null)
            {
                btn = cardItem.AddComponent<Button>();
            }

            Image baseImage = cardItem.GetComponent<Image>();
            Color original = baseImage != null ? baseImage.color : Color.white;
            bool consumed = false;
            UnityEngine.Events.UnityAction handleSelect = () =>
            {
                if (consumed)
                {
                    return;
                }

                if (effect.selectionMode == EffectSelectionMode.SelectSingleEnemyUnit)
                {
                    consumed = true;
                    ApplyEffectToSpecificTargets(attacker, attackerOwner, effect, new List<CardController> { unit });
                    pendingOnAttackEffectResolvedAttacker = attacker;
                    Debug.Log("OnAttack effect target selected. Now select attack target.");
                    Destroy(root);
                    onResolved?.Invoke();
                    return;
                }

                if (selected.Contains(unit))
                {
                    selected.Remove(unit);
                    if (baseImage != null)
                    {
                        baseImage.color = original;
                    }
                }
                else
                {
                    selected.Add(unit);
                    if (baseImage != null)
                    {
                        baseImage.color = new Color(0.7f, 1f, 0.7f, 1f);
                    }
                }
            };

            if (itemCc != null && unit.Data != null)
            {
                itemCc.SetUp(unit.Data, _ => handleSelect());
            }

            btn.targetGraphic = baseImage;
            btn.onClick.AddListener(handleSelect);
        }

        if (effect.selectionMode == EffectSelectionMode.SelectMultipleEnemyUnits)
        {
            Button confirm = root.CreateChildButton("Confirm");
            RectTransform confirmRt = confirm.GetComponent<RectTransform>();
            confirmRt.sizeDelta = new Vector2(180f, 46f);
            confirmRt.anchoredPosition = new Vector2(-100f, 48f);
            confirm.onClick.AddListener(() =>
            {
                if (selected.Count == 0)
                {
                    Debug.Log("効果対象を1体以上選択してください。");
                    return;
                }
                ApplyEffectToSpecificTargets(attacker, attackerOwner, effect, selected);
                pendingOnAttackEffectResolvedAttacker = attacker;
                Debug.Log("OnAttack effect targets selected. Now select attack target.");
                Destroy(root);
                onResolved?.Invoke();
            });
        }

        Button cancel = root.CreateChildButton("Cancel");
        RectTransform cancelRt = cancel.GetComponent<RectTransform>();
        cancelRt.sizeDelta = new Vector2(180f, 46f);
        cancelRt.anchoredPosition = new Vector2(100f, 48f);
        cancel.onClick.AddListener(() =>
        {
            pendingUnitAttackAttacker = null;
            pendingOnAttackEffectResolvedAttacker = null;
            Destroy(root);
        });
    }

    private List<CardController> GetAliveEnemyUnits(PlayerType attackerOwner)
    {
        List<CardController> source = attackerOwner == PlayerType.Player ? enemyBattleZoneCards : playerBattleZoneCards;
        List<CardController> result = new List<CardController>();
        for (int i = 0; i < source.Count; i++)
        {
            CardController c = source[i];
            if (c != null && c.Data != null && c.Data.type == Type.Unit && c.CurrentHp > 0)
            {
                result.Add(c);
            }
        }
        return result;
    }

    private void ApplyEffectToSpecificTargets(CardController sourceCard, PlayerType ownerType, EffectData effect, List<CardController> targets)
    {
        int magnitude = Mathf.Abs(effect.value);
        if (magnitude == 0)
        {
            return;
        }

        if (effect.type == EffectType.Draw)
        {
            CardGameRule rule = ownerType == PlayerType.Player ? cardGameRule : enemyCardGameRule;
            for (int i = 0; i < magnitude; i++)
            {
                CardAddtoHand(rule, ownerType);
            }
            return;
        }

        for (int i = 0; i < targets.Count; i++)
        {
            CardController t = targets[i];
            if (t == null || t.Data == null)
            {
                continue;
            }

            switch (effect.type)
            {
                case EffectType.Damage:
                    t.ApplyDamage(magnitude);
                    if (t.CurrentHp <= 0)
                    {
                        SendCardToTrash(t, ResolveCardOwner(t.transform));
                    }
                    break;
                case EffectType.Buff:
                    ApplyStatEffect(t, magnitude, effect.statTarget, effect.duration);
                    break;
                case EffectType.Debuff:
                    ApplyStatEffect(t, -magnitude, effect.statTarget, effect.duration);
                    break;
            }
        }

        SyncAllResourceViewsFromRule();
    }

    /// <summary>
    /// シールド攻撃。AP が 1 未満のときは何もしない。
    /// EXベースありなら power を EX ベースに与え、無いならシールド 1 枚のみ破壊（<see cref="Gundam2024RuleScript.TryApplyUnitShieldAttack"/>）。
    /// </summary>
    private void TryUnitShieldAttackFromUnit(CardController attacker, bool skipOnActionPause = false, bool skipOnAttackSelection = false)
    {
        if (attacker == null || attacker.Data == null || attacker.Data.type != Type.Unit)
        {
            return;
        }

        // シールド攻撃は攻撃可能フラグ(True)のみで判定する。
        if (attacker.AttackFlgState != AttackFlg.True)
        {
            Debug.Log("This unit cannot attack.");
            return;
        }

        if (currentPhase != BattlePhase.MainPhase)
        {
            return;
        }

        PlayerType attackerOwner = ResolveCardOwner(attacker.transform);
        if (attackerOwner != currentPlayerType)
        {
            return;
        }

        Gundam2024RuleScript.PlayerSide targetSide = attackerOwner == PlayerType.Player
            ? Gundam2024RuleScript.PlayerSide.Enemy
            : Gundam2024RuleScript.PlayerSide.Player;
        Gundam2024RuleScript.PlayerState defender = targetSide == Gundam2024RuleScript.PlayerSide.Player
            ? gundamRule.Player
            : gundamRule.Enemy;

        if (defender.shield <= 0)
        {
            Debug.Log("[DirectAttack] Shield is 0. Resolving direct attack.");
            attacker.SetAttackFlg(AttackFlg.False);
            attacker.SetUnitRestVisual(true);
            pendingUnitAttackAttacker = null;
            pendingOnAttackEffectResolvedAttacker = null;
            HandleDirectAttackWinLose(attackerOwner);
            return;
        }

        if (attacker.CurrentPower <= 0)
        {
            Debug.Log("[ShieldAttack] AP is 0 — cannot break shields or damage EX Base.");
            return;
        }

        // OnAction より前の時点で EX ベースがあったかを固定する（OnAction で EX が 0 になった後にシールドが割れるのを防ぐ）。
        bool hadExBaseLayerAtShieldAttackStart = defender.exBase > 0;
        if (hadExBaseLayerAtShieldAttackStart)
        {
            blockShieldFlowDuringShieldAttack = true;
            blockedShieldFlowSide = targetSide;
        }

        try
        {
            // シールド攻撃時も OnAttack の対象選択効果を先に解決する。
            if (!skipOnAttackSelection && pendingOnAttackEffectResolvedAttacker != attacker)
            {
                if (TryOpenOnAttackEnemySelectionPanel(
                    attacker,
                    attackerOwner,
                    null,
                    () => TryUnitShieldAttackFromUnit(attacker, skipOnActionPause, true)))
                {
                    return;
                }

                pendingOnAttackEffectResolvedAttacker = attacker;
            }

            if (!skipOnActionPause
                && TryRunAttackActionSteps(
                    attackerOwner == PlayerType.Player ? PlayerType.Enemy : PlayerType.Player,
                    attackerOwner,
                    () => TryUnitShieldAttackFromUnit(attacker, true, true),
                    attacker))
            {
                return;
            }

            if (!gundamRule.TryApplyUnitShieldAttack(targetSide, attacker.CurrentPower, hadExBaseLayerAtShieldAttackStart))
            {
                Debug.Log("Cannot attack shield (no shields or invalid power for EX Base).");
                return;
            }

            attacker.SetAttackFlg(AttackFlg.False);
            attacker.SetUnitRestVisual(true);
            if (hadExBaseLayerAtShieldAttackStart)
            {
                Debug.Log($"[Attack] Shield attack vs EX layer. EX Base is now {defender.exBase}.");
            }
            else
            {
                Debug.Log("[Attack] Broke 1 shield (no EX Base).");
            }

            TriggerCardEffects(attacker, attackerOwner, EffectTiming.OnAttack);
            TriggerMountedPilotOnAttackEffects(attacker, attackerOwner);
            pendingUnitAttackAttacker = null;
            pendingOnAttackEffectResolvedAttacker = null;
            ClearTimedPowerModifiersForAllBattleUnits(EffectDuration.UntilEndOfBattle);
            DumpTurnResourceUsageLogs(attackerOwner, "unit shield attack");

            SyncAllResourceViewsFromRule();
        }
        finally
        {
            if (hadExBaseLayerAtShieldAttackStart)
            {
                blockShieldFlowDuringShieldAttack = false;
            }
        }
    }

    private void TryUnitVsUnitAttack(CardController attacker, CardController defender, PlayerType attackerOwner, PlayerType defenderOwner, bool skipOnActionPause = false)
    {
        if (currentPhase != BattlePhase.MainPhase || attackerOwner != currentPlayerType)
        {
            return;
        }

        if (attacker.Data.type != Type.Unit || defender.Data.type != Type.Unit)
        {
            Debug.Log("Only units can attack each other.");
            return;
        }

        // 攻撃対象確定後に、OnAttackの対象選択(デバフ等)を行う。
        if (pendingOnAttackEffectResolvedAttacker != attacker)
        {
            if (TryOpenOnAttackEnemySelectionPanel(
                attacker,
                attackerOwner,
                defender,
                () => TryUnitVsUnitAttack(attacker, defender, attackerOwner, defenderOwner, skipOnActionPause)))
            {
                return;
            }

            pendingOnAttackEffectResolvedAttacker = attacker;
        }

        if (!skipOnActionPause
            && TryRunAttackActionSteps(
                defenderOwner,
                attackerOwner,
                () => TryUnitVsUnitAttack(attacker, defender, attackerOwner, defenderOwner, true),
                attacker))
        {
            return;
        }

        // 基本ルール: ユニットはレスト状態の相手ユニットのみ攻撃できる。
        if (!defender.IsRestState)
        {
            Debug.Log("Only REST units can be attacked.");
            return;
        }

        // 攻撃側は攻撃可能フラグ(True)で判定する。
        if (attacker.AttackFlgState != AttackFlg.True)
        {
            Debug.Log("This unit cannot attack.");
            return;
        }

        // OnAttack 効果（ユニット本体＋搭乗パイロット）を「この防御対象(defender)」に確実適用してから戦闘値を確定する。
        int defenderPowerBeforeEffects = defender.CurrentPower;
        ApplyOnAttackEffectsForCombatPair(attacker, attackerOwner, defender);
        int attackerPowerForCombat = attacker.CurrentPower;
        int defenderPowerForCombat = defender.CurrentPower;
        if (defenderPowerForCombat == defenderPowerBeforeEffects)
        {
            int fallbackApDelta = ComputeOnAttackApDeltaToDefender(attacker);
            if (fallbackApDelta != 0)
            {
                defenderPowerForCombat = Mathf.Max(0, defenderPowerForCombat + fallbackApDelta);
                Debug.Log($"[OnAttackFallback] apply AP delta:{fallbackApDelta} to defender combat power.");
            }
        }
        Debug.Log($"[CombatPower] attacker:{attackerPowerForCombat} defender:{defenderPowerForCombat}");

        defender.ApplyDamage(attackerPowerForCombat);
        attacker.ApplyDamage(defenderPowerForCombat);
        attacker.SetAttackFlg(AttackFlg.False);
        attacker.SetUnitRestVisual(true);

        if (defender.CurrentHp <= 0)
        {
            SendCardToTrash(defender, defenderOwner);
        }

        if (attacker.CurrentHp <= 0)
        {
            SendCardToTrash(attacker, attackerOwner);
        }

        pendingUnitAttackAttacker = null;
        pendingOnAttackEffectResolvedAttacker = null;
        ClearTimedPowerModifiersForAllBattleUnits(EffectDuration.UntilEndOfBattle);
        DumpTurnResourceUsageLogs(attackerOwner, "unit vs unit attack");
        SyncAllResourceViewsFromRule();
    }

    private void ApplyOnAttackEffectsForCombatPair(CardController attacker, PlayerType attackerOwner, CardController defender)
    {
        if (attacker == null || attacker.Data == null || defender == null || defender.Data == null)
        {
            return;
        }

        ApplyOnAttackEffectsFromSourceToDefender(attacker, attackerOwner, attacker.Data, defender);
        if (attacker.MountedPilot != null && attacker.MountedPilot.Data != null)
        {
            ApplyOnAttackEffectsFromSourceToDefender(attacker.MountedPilot, attackerOwner, attacker.MountedPilot.Data, defender);
        }
    }

    private void ApplyOnAttackEffectsFromSourceToDefender(CardController sourceCard, PlayerType ownerType, CardData data, CardController defender)
    {
        List<EffectData> effects = GetEffectsByTiming(data, EffectTiming.OnAttack);
        for (int i = 0; i < effects.Count; i++)
        {
            EffectData effect = effects[i];
            if (effect == null)
            {
                continue;
            }

            if (effect.target == TargetType.EnemyUnit)
            {
                ApplyEffectToSpecificTargets(sourceCard, ownerType, effect, new List<CardController> { defender });
                continue;
            }

            if (effect.target == TargetType.EnemyAllUnits)
            {
                ApplyEffectToSpecificTargets(sourceCard, ownerType, effect, GetAliveEnemyUnits(ownerType));
                continue;
            }

            ApplyEffect(sourceCard, ownerType, effect);
        }
    }

    private int ComputeOnAttackApDeltaToDefender(CardController attacker)
    {
        int delta = 0;
        if (attacker == null)
        {
            return 0;
        }

        delta += ComputeOnAttackApDeltaFromData(attacker.Data);
        if (attacker.MountedPilot != null)
        {
            delta += ComputeOnAttackApDeltaFromData(attacker.MountedPilot.Data);
        }
        return delta;
    }

    private static int ComputeOnAttackApDeltaFromData(CardData data)
    {
        if (data == null || data.timedEffects == null)
        {
            return 0;
        }

        int delta = 0;
        for (int i = 0; i < data.timedEffects.Count; i++)
        {
            TimedEffectData timed = data.timedEffects[i];
            if (timed == null || timed.timing != EffectTiming.OnAttack || timed.effects == null)
            {
                continue;
            }

            for (int j = 0; j < timed.effects.Count; j++)
            {
                EffectData effect = timed.effects[j];
                if (effect == null)
                {
                    continue;
                }

                bool targetEnemyUnit = effect.target == TargetType.EnemyUnit || effect.target == TargetType.EnemyAllUnits;
                bool affectsAp = effect.statTarget == EffectStatTarget.AP || effect.statTarget == EffectStatTarget.Both;
                if (!targetEnemyUnit || !affectsAp)
                {
                    continue;
                }

                int magnitude = Mathf.Abs(effect.value);
                if (magnitude == 0)
                {
                    continue;
                }

                if (effect.type == EffectType.Debuff)
                {
                    delta -= magnitude;
                }
                else if (effect.type == EffectType.Buff)
                {
                    delta += magnitude;
                }
            }
        }

        return delta;
    }

    private void TriggerMountedPilotOnAttackEffects(CardController attacker, PlayerType attackerOwner)
    {
        if (attacker == null || attacker.Data == null || attacker.Data.type != Type.Unit)
        {
            return;
        }

        CardController pilot = attacker.MountedPilot;
        if (pilot == null || pilot.Data == null)
        {
            return;
        }

        TriggerCardEffects(pilot, attackerOwner, EffectTiming.OnAttack);
    }

    private void ApplyOnAttackAutoTargetEffects(CardController attacker, PlayerType attackerOwner, CardController defender)
    {
        if (attacker == null || attacker.Data == null || defender == null || defender.Data == null)
        {
            return;
        }

        ApplyOnAttackAutoTargetEffectsFromData(attacker, attackerOwner, attacker.Data, defender);
        if (attacker.MountedPilot != null && attacker.MountedPilot.Data != null)
        {
            ApplyOnAttackAutoTargetEffectsFromData(attacker.MountedPilot, attackerOwner, attacker.MountedPilot.Data, defender);
        }
    }

    private void ApplyOnAttackAutoTargetEffectsFromData(CardController sourceCard, PlayerType ownerType, CardData data, CardController defender)
    {
        if (data == null || data.timedEffects == null)
        {
            return;
        }

        for (int i = 0; i < data.timedEffects.Count; i++)
        {
            TimedEffectData timed = data.timedEffects[i];
            if (timed == null || timed.timing != EffectTiming.OnAttack || timed.effects == null)
            {
                continue;
            }

            for (int j = 0; j < timed.effects.Count; j++)
            {
                EffectData effect = timed.effects[j];
                if (effect == null)
                {
                    continue;
                }

                bool enemyUnitTarget = effect.target == TargetType.EnemyUnit || effect.target == TargetType.EnemyAllUnits;
                if (!enemyUnitTarget || effect.selectionMode != EffectSelectionMode.AttackedTargetOnly)
                {
                    continue;
                }

                ApplyEffectToSpecificTargets(sourceCard, ownerType, effect, new List<CardController> { defender });
            }
        }
    }

    private void DumpTurnResourceUsageLogs(PlayerType side, string context)
    {
        Gundam2024RuleScript.PlayerSide ruleSide = ToRuleSide(side);
        IReadOnlyList<Gundam2024RuleScript.ResourceUsageLog> logs = gundamRule.GetCurrentTurnResourceUsageLogs(ruleSide);
        if (logs == null || logs.Count == 0)
        {
            Debug.Log($"[ResourceUsageDump] context:{context} side:{side} logs:empty");
            return;
        }

        Debug.Log($"[ResourceUsageDump] context:{context} side:{side} count:{logs.Count}");
        for (int i = 0; i < logs.Count; i++)
        {
            var log = logs[i];
            Debug.Log($"[ResourceUsageDump] #{i} turn:{log.turnIndex} side:{log.side} cardId:{log.cardId} resourceUsed:{log.resourceUsed} exUsed:{log.exUsed}");
        }
    }

    private void HandleDirectAttackWinLose(PlayerType attackerOwner)
    {
        if (isMatchFinished)
        {
            return;
        }

        TriggerAllTimedEffectsForSide(PlayerType.Player, EffectTiming.OnEndOfGame);
        TriggerAllTimedEffectsForSide(PlayerType.Enemy, EffectTiming.OnEndOfGame);
        isMatchFinished = true;
        bool playerWin = attackerOwner == PlayerType.Player;
        ShowResultOverlay(playerWin ? "WIN" : "LOSE");
    }

    private void TriggerAllTimedEffectsForSide(PlayerType ownerType, EffectTiming timing)
    {
        List<CardController> source = ownerType == PlayerType.Player ? playerBattleZoneCards : enemyBattleZoneCards;
        for (int i = 0; i < source.Count; i++)
        {
            CardController card = source[i];
            if (card == null || card.Data == null)
            {
                continue;
            }
            TriggerCardEffects(card, ownerType, timing);
        }
    }

    private void TriggerCardEffects(CardController sourceCard, PlayerType ownerType, EffectTiming timing)
    {
        if (sourceCard == null || sourceCard.Data == null || sourceCard.Data.timedEffects == null)
        {
            return;
        }

        for (int i = 0; i < sourceCard.Data.timedEffects.Count; i++)
        {
            TimedEffectData timed = sourceCard.Data.timedEffects[i];
            if (timed == null || timed.timing != timing || timed.effects == null)
            {
                continue;
            }

            for (int j = 0; j < timed.effects.Count; j++)
            {
                EffectData effect = timed.effects[j];
                if (effect == null)
                {
                    continue;
                }
                if (timing == EffectTiming.OnAttack
                    && (effect.target == TargetType.EnemyUnit || effect.target == TargetType.EnemyAllUnits))
                {
                    // Enemy unit target effects are resolved before attack target decision.
                    continue;
                }
                ApplyEffect(sourceCard, ownerType, effect);
            }
        }
    }

    private void ApplyEffect(CardController sourceCard, PlayerType ownerType, EffectData effect)
    {
        int magnitude = Mathf.Abs(effect.value);
        if (magnitude == 0)
        {
            return;
        }

        List<CardController> targets = ResolveEffectTargets(sourceCard, ownerType, effect.target);
        switch (effect.type)
        {
            case EffectType.Draw:
                CardGameRule rule = ownerType == PlayerType.Player ? cardGameRule : enemyCardGameRule;
                for (int i = 0; i < magnitude; i++)
                {
                    CardAddtoHand(rule, ownerType);
                }
                Debug.Log($"[Effect] Draw x{magnitude} by cardId:{sourceCard.Data.id}");
                break;

            case EffectType.Damage:
                for (int i = 0; i < targets.Count; i++)
                {
                    targets[i].ApplyDamage(magnitude);
                    PlayerType targetOwner = ResolveCardOwner(targets[i].transform);
                    if (targets[i].CurrentHp <= 0)
                    {
                        SendCardToTrash(targets[i], targetOwner);
                    }
                }
                if (effect.target == TargetType.EnemyPlayer || effect.target == TargetType.SelfPlayer)
                {
                    Gundam2024RuleScript.PlayerSide targetSide = effect.target == TargetType.EnemyPlayer
                        ? ToRuleSide(ownerType == PlayerType.Player ? PlayerType.Enemy : PlayerType.Player)
                        : ToRuleSide(ownerType);
                    if (blockShieldFlowDuringShieldAttack && targetSide == blockedShieldFlowSide)
                    {
                        gundamRule.DamageExBaseOnly(targetSide, magnitude);
                    }
                    else
                    {
                        gundamRule.DamagePlayerArea(targetSide, magnitude);
                    }
                }
                Debug.Log($"[Effect] Damage {magnitude} target:{effect.target} by cardId:{sourceCard.Data.id}");
                break;

            case EffectType.Buff:
            case EffectType.Debuff:
                int sign = effect.type == EffectType.Buff ? 1 : -1;
                int signedValue = sign * magnitude;
                for (int i = 0; i < targets.Count; i++)
                {
                    ApplyStatEffect(targets[i], signedValue, effect.statTarget, effect.duration);
                }
                Debug.Log($"[Effect] {effect.type} {magnitude} target:{effect.target} stat:{effect.statTarget} by cardId:{sourceCard.Data.id}");
                break;
        }

        SyncAllResourceViewsFromRule();
    }

    private static void ApplyStatEffect(CardController target, int signedValue, EffectStatTarget statTarget, EffectDuration duration)
    {
        int powerDelta = 0;
        int hpDelta = 0;
        switch (statTarget)
        {
            case EffectStatTarget.AP:
                powerDelta = signedValue;
                break;
            case EffectStatTarget.HP:
                hpDelta = signedValue;
                break;
            default:
                powerDelta = signedValue;
                hpDelta = signedValue;
                break;
        }
        target.AddEffectStatBonus(powerDelta, hpDelta, duration);
    }

    private List<CardController> ResolveEffectTargets(CardController sourceCard, PlayerType ownerType, TargetType targetType)
    {
        List<CardController> allies = ownerType == PlayerType.Player ? playerBattleZoneCards : enemyBattleZoneCards;
        List<CardController> enemies = ownerType == PlayerType.Player ? enemyBattleZoneCards : playerBattleZoneCards;
        List<CardController> result = new List<CardController>();

        switch (targetType)
        {
            case TargetType.Self:
                if (sourceCard != null)
                {
                    result.Add(sourceCard);
                }
                break;
            case TargetType.AllyUnit:
                AddFirstAliveUnit(allies, result);
                break;
            case TargetType.EnemyUnit:
                AddFirstAliveUnit(enemies, result);
                break;
            case TargetType.AllyAllUnits:
                AddAllAliveUnits(allies, result);
                break;
            case TargetType.EnemyAllUnits:
                AddAllAliveUnits(enemies, result);
                break;
        }

        return result;
    }

    private static void AddFirstAliveUnit(List<CardController> source, List<CardController> result)
    {
        for (int i = 0; i < source.Count; i++)
        {
            CardController c = source[i];
            if (c != null && c.Data != null && c.Data.type == Type.Unit && c.CurrentHp > 0)
            {
                result.Add(c);
                return;
            }
        }
    }

    private static void AddAllAliveUnits(List<CardController> source, List<CardController> result)
    {
        for (int i = 0; i < source.Count; i++)
        {
            CardController c = source[i];
            if (c != null && c.Data != null && c.Data.type == Type.Unit && c.CurrentHp > 0)
            {
                result.Add(c);
            }
        }
    }

    private void ClearTimedPowerModifiersForSide(PlayerType side, EffectDuration duration)
    {
        List<CardController> source = side == PlayerType.Player ? playerBattleZoneCards : enemyBattleZoneCards;
        for (int i = 0; i < source.Count; i++)
        {
            CardController c = source[i];
            if (c != null && c.Data != null && c.Data.type == Type.Unit)
            {
                c.ClearPowerModifiersByDuration(duration);
            }
        }
    }

    private void ClearTimedPowerModifiersForAllBattleUnits(EffectDuration duration)
    {
        ClearTimedPowerModifiersForSide(PlayerType.Player, duration);
        ClearTimedPowerModifiersForSide(PlayerType.Enemy, duration);
    }

    private bool LogHandOnActionCandidates(PlayerType ownerType, string context, System.Action onClose = null)
    {
        return LogHandOnActionCandidates(ownerType, context, true, onClose);
    }

    private bool LogHandOnActionCandidates(PlayerType ownerType, string context, bool showPopup, System.Action onClose = null)
    {
        RectTransform hand = ownerType == PlayerType.Player
            ? cardGameRule.HandScrollContent
            : enemyCardGameRule.HandScrollContent;
        if (hand == null)
        {
            return false;
        }

        List<string> candidates = new List<string>();
        List<CardData> cards = new List<CardData>();
        for (int i = 0; i < hand.childCount; i++)
        {
            CardController cc = hand.GetChild(i).GetComponent<CardController>();
            if (cc == null || cc.Data == null || cc.Data.timedEffects == null)
            {
                continue;
            }

            if (HasEffectTiming(cc.Data, EffectTiming.OnAction) && CanExecuteOnActionCardNow(ownerType, cc.Data))
            {
                candidates.Add($"{cc.Data.id}:{cc.Data.cardName}");
                cards.Add(cc.Data);
            }
        }

        if (candidates.Count == 0)
        {
            Debug.Log($"[OnActionCandidates] context:{context} side:{ownerType} none");
            return false;
        }

        Debug.Log($"[OnActionCandidates] context:{context} side:{ownerType} cards:{string.Join(", ", candidates)}");
        if (showPopup)
        {
            ShowOnActionHandCandidatesPopup(ownerType, context, cards, onClose);
        }
        return true;
    }

    private bool TryRunAttackActionSteps(
        PlayerType defenderSide,
        PlayerType attackerSide,
        System.Action onComplete,
        CardController attackingUnitForUiHighlight = null)
    {
        if (TryHandleSingleSideOnActionStep(defenderSide, "attack:defender", () =>
        {
            if (TryHandleSingleSideOnActionStep(attackerSide, "attack:attacker", onComplete, attackingUnitForUiHighlight))
            {
                return;
            }
            onComplete?.Invoke();
        }, attackingUnitForUiHighlight))
        {
            return true;
        }

        if (TryHandleSingleSideOnActionStep(attackerSide, "attack:attacker", onComplete, attackingUnitForUiHighlight))
        {
            return true;
        }

        onComplete?.Invoke();
        return false;
    }

    private bool TryHandleSingleSideOnActionStep(
        PlayerType side,
        string context,
        System.Action onStepDone,
        CardController attackingUnitForUiHighlight = null)
    {
        // 敵側は将来AI実装予定のため、現時点ではバックグラウンドスキップ（停止UIを出さない）。
        if (side == PlayerType.Enemy)
        {
            bool hasEnemyCandidates = LogHandOnActionCandidates(side, context, false);
            if (hasEnemyCandidates)
            {
                Debug.Log($"[OnActionStep] side:{side} context:{context} skipped for background AI.");
            }
            return false;
        }

        return TryOpenOnActionCommandSelection(side, context, onStepDone, attackingUnitForUiHighlight);
    }

    private bool TryOpenOnActionCommandSelection(
        PlayerType side,
        string context,
        System.Action onStepDone,
        CardController attackingUnitForUiHighlight = null)
    {
        RectTransform hand = side == PlayerType.Player ? cardGameRule.HandScrollContent : enemyCardGameRule.HandScrollContent;
        if (hand == null || CardImagePrefab == null)
        {
            return false;
        }

        List<CardController> commandCards = new List<CardController>();
        for (int i = 0; i < hand.childCount; i++)
        {
            CardController cc = hand.GetChild(i).GetComponent<CardController>();
            if (cc == null || cc.Data == null || cc.Data.type != Type.Command)
            {
                continue;
            }
            if (!HasEffectTiming(cc.Data, EffectTiming.OnAction) || !CanExecuteOnActionCardNow(side, cc.Data))
            {
                continue;
            }
            commandCards.Add(cc);
        }

        if (commandCards.Count == 0)
        {
            Debug.Log($"[OnActionCandidates] context:{context} side:{side} none");
            return false;
        }

        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            return false;
        }

        DestroyActiveOnActionPopupIfAny();
        GameObject root = new GameObject("OnActionCommandSelect", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        activeOnActionPopupRoot = root;
        isOnActionPopupOpen = true;
        root.transform.SetParent(canvas.transform, false);
        root.transform.SetAsLastSibling();
        root.SetFullSize();
        Image dim = root.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.55f);
        dim.raycastTarget = true;

        TextMeshProUGUI title = root.CreateChildTextCustom("OnActionCommandTitle", UIAnchor.TopCenter, 720, 48);
        title.text = $"OnAction Command Select ({side}) [{context}]";
        title.color = Color.white;
        title.fontSize = 24;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -24f);

        bool showAttackHighlight = attackingUnitForUiHighlight != null
            && attackingUnitForUiHighlight.Data != null
            && !string.IsNullOrEmpty(context)
            && context.Contains("attack");

        GameObject scrollGo = root.CreateGridScrollView(680, 410, UIAnchor.TopCenter);
        RectTransform scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.anchoredPosition = new Vector2(0f, showAttackHighlight ? -98f : -86f);
        scrollGo.ConfigureGridCellFromViewportHeight(0.78f, 56f);
        ScrollRect sr = scrollGo.GetComponent<ScrollRect>();
        RectTransform content = sr != null ? sr.content : null;

        if (showAttackHighlight && content != null)
        {
            GameObject highlightRow = Instantiate(CardImagePrefab, content);
            highlightRow.transform.SetAsFirstSibling();

            GameObject statBg = new GameObject("AttackerHighlightStatBg", typeof(RectTransform), typeof(Image));
            statBg.transform.SetParent(highlightRow.transform, false);
            RectTransform statBgRt = statBg.GetComponent<RectTransform>();
            statBgRt.anchorMin = new Vector2(0f, 0f);
            statBgRt.anchorMax = new Vector2(1f, 0f);
            statBgRt.pivot = new Vector2(0.5f, 0f);
            statBgRt.sizeDelta = new Vector2(0f, 28f);
            statBgRt.anchoredPosition = new Vector2(0f, 0f);
            Image statBgImg = statBg.GetComponent<Image>();
            statBgImg.color = new Color(0f, 0f, 0f, 0.55f);
            statBgImg.raycastTarget = false;

            TextMeshProUGUI statText = statBg.CreateChildTextCustom("AttackerHighlightText", UIAnchor.FullSize, 200, 24);
            statText.text =
                $"攻撃中: {attackingUnitForUiHighlight.Data.cardName}  AP:{attackingUnitForUiHighlight.CurrentPower}  HP:{attackingUnitForUiHighlight.CurrentHp}";
            statText.fontSize = 14;
            statText.color = new Color(1f, 0.22f, 0.22f, 1f);
            statText.alignment = TextAlignmentOptions.Center;

            CardController hcc = highlightRow.GetComponent<CardController>();
            if (hcc != null)
            {
                hcc.SetUp(attackingUnitForUiHighlight.Data, _ => { });
            }

            Button hbtn = highlightRow.GetComponent<Button>();
            if (hbtn != null)
            {
                hbtn.interactable = false;
            }
        }

        for (int i = 0; i < commandCards.Count; i++)
        {
            CardController command = commandCards[i];
            if (content == null || command == null || command.Data == null)
            {
                continue;
            }

            GameObject go = Instantiate(CardImagePrefab, content);
            CardController cc = go.GetComponent<CardController>();
            if (cc != null)
            {
                cc.SetUp(command.Data, _ => { });
            }

            Button btn = go.GetComponent<Button>();
            if (btn == null)
            {
                btn = go.AddComponent<Button>();
            }

            bool isAttackContext = showAttackHighlight;
            btn.onClick.AddListener(() =>
            {
                TryExecuteOnActionCommand(side, command, isAttackContext, attackingUnitForUiHighlight, () =>
                {
                    isOnActionPopupOpen = false;
                    activeOnActionPopupRoot = null;
                    Destroy(root);
                    onStepDone?.Invoke();
                });
            });
        }

        Button closeBtn = root.CreateChildButton("Close");
        RectTransform closeRt = closeBtn.GetComponent<RectTransform>();
        closeRt.sizeDelta = new Vector2(180f, 48f);
        closeRt.anchorMin = new Vector2(0.5f, 0f);
        closeRt.anchorMax = new Vector2(0.5f, 0f);
        closeRt.pivot = new Vector2(0.5f, 0f);
        closeRt.anchoredPosition = new Vector2(0f, 36f);
        closeBtn.onClick.AddListener(() =>
        {
            isOnActionPopupOpen = false;
            activeOnActionPopupRoot = null;
            Destroy(root);
            onStepDone?.Invoke();
        });

        return true;
    }

    private void TryExecuteOnActionCommand(
        PlayerType side,
        CardController command,
        bool isAttackContext,
        CardController attackingUnitForUiHighlight,
        System.Action onDone)
    {
        if (command == null || command.Data == null)
        {
            onDone?.Invoke();
            return;
        }

        List<EffectData> onActionEffects = GetEffectsByTiming(command.Data, EffectTiming.OnAction);
        if (onActionEffects.Count == 0)
        {
            onDone?.Invoke();
            return;
        }

        EffectData enemyTargetEffect = onActionEffects.Find(e => e != null && e.target == TargetType.EnemyUnit);
        if (enemyTargetEffect != null)
        {
            OpenOnActionEnemyTargetSelection(side, command, enemyTargetEffect, isAttackContext, attackingUnitForUiHighlight, onDone);
            return;
        }

        if (!gundamRule.TryConsumeResource(ToRuleSide(side), command.Data.cost, 0, command.Data.id))
        {
            Debug.Log("OnAction: リソース不足で実行できません。");
            onDone?.Invoke();
            return;
        }

        ApplyEffect(command, side, onActionEffects[0]);
        SendUsedCommandToTrash(command, side);
        onDone?.Invoke();
    }

    private void OpenOnActionEnemyTargetSelection(
        PlayerType side,
        CardController command,
        EffectData effect,
        bool isAttackContext,
        CardController attackingUnitForUiHighlight,
        System.Action onDone)
    {
        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            onDone?.Invoke();
            return;
        }

        List<CardController> enemyUnits = GetAliveEnemyUnits(side);
        if (enemyUnits.Count == 0)
        {
            Debug.Log("OnAction: 対象となる敵ユニットがいません。");
            onDone?.Invoke();
            return;
        }

        GameObject root = new GameObject("OnActionTargetSelect", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        root.transform.SetParent(canvas.transform, false);
        root.transform.SetAsLastSibling();
        root.SetFullSize();
        Image dim = root.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.55f);
        dim.raycastTarget = true;

        TextMeshProUGUI title = root.CreateChildTextCustom("OnActionTargetTitle", UIAnchor.TopCenter, 640, 48);
        title.text = "OnAction Target Select";
        title.color = Color.white;
        title.fontSize = 24;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -24f);

        for (int i = 0; i < enemyUnits.Count; i++)
        {
            CardController t = enemyUnits[i];
            Button btn = root.CreateChildButton($"{t.Data.cardName} AP:{t.CurrentPower} HP:{t.CurrentHp}");
            bool isAttackingCardButton = isAttackContext
                && attackingUnitForUiHighlight != null
                && t == attackingUnitForUiHighlight;
            if (isAttackingCardButton)
            {
                TextMeshProUGUI btnText = btn.GetComponentInChildren<TextMeshProUGUI>();
                if (btnText != null)
                {
                    btnText.color = Color.red;
                }
            }
            RectTransform rt = btn.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(420f, 44f);
            rt.anchoredPosition = new Vector2(0f, -100f - (i * 52f));
            btn.onClick.AddListener(() =>
            {
                if (!gundamRule.TryConsumeResource(ToRuleSide(side), command.Data.cost, 0, command.Data.id))
                {
                    Debug.Log("OnAction: リソース不足で実行できません。");
                    Destroy(root);
                    onDone?.Invoke();
                    return;
                }

                ApplyEffectToSpecificTargets(command, side, effect, new List<CardController> { t });
                SendUsedCommandToTrash(command, side);
                Destroy(root);
                onDone?.Invoke();
            });
        }

        Button close = root.CreateChildButton("Close");
        RectTransform closeRt = close.GetComponent<RectTransform>();
        closeRt.sizeDelta = new Vector2(180f, 46f);
        closeRt.anchorMin = new Vector2(0.5f, 0f);
        closeRt.anchorMax = new Vector2(0.5f, 0f);
        closeRt.pivot = new Vector2(0.5f, 0f);
        closeRt.anchoredPosition = new Vector2(0f, 34f);
        close.onClick.AddListener(() =>
        {
            Destroy(root);
            onDone?.Invoke();
        });
    }

    private static List<EffectData> GetEffectsByTiming(CardData data, EffectTiming timing)
    {
        List<EffectData> result = new List<EffectData>();
        if (data == null || data.timedEffects == null)
        {
            return result;
        }
        for (int i = 0; i < data.timedEffects.Count; i++)
        {
            TimedEffectData timed = data.timedEffects[i];
            if (timed == null || timed.timing != timing || timed.effects == null)
            {
                continue;
            }
            for (int j = 0; j < timed.effects.Count; j++)
            {
                if (timed.effects[j] != null)
                {
                    result.Add(timed.effects[j]);
                }
            }
        }
        return result;
    }

    private void SendUsedCommandToTrash(CardController command, PlayerType ownerType)
    {
        if (command == null || command.Data == null)
        {
            return;
        }

        CardGameRule ownerRule = ownerType == PlayerType.Player ? cardGameRule : enemyCardGameRule;
        ownerRule.AddCardToTrash(command.Data.id);
        playerHandCards.Remove(command.Data);
        enemyHandCards.Remove(command.Data);
        Destroy(command.gameObject);
    }

    private bool CanExecuteOnActionCardNow(PlayerType ownerType, CardData card)
    {
        if (card == null)
        {
            return false;
        }

        Gundam2024RuleScript.PlayerState state = ownerType == PlayerType.Player
            ? gundamRule.Player
            : gundamRule.Enemy;
        return state.TotalLevel >= card.level && state.resource >= card.cost;
    }

    private static bool HasEffectTiming(CardData data, EffectTiming timing)
    {
        if (data == null || data.timedEffects == null)
        {
            return false;
        }

        for (int i = 0; i < data.timedEffects.Count; i++)
        {
            TimedEffectData timed = data.timedEffects[i];
            if (timed != null && timed.timing == timing && timed.effects != null && timed.effects.Count > 0)
            {
                return true;
            }
        }

        return false;
    }

    private void ShowOnActionHandCandidatesPopup(PlayerType ownerType, string context, List<CardData> cards, System.Action onClose = null)
    {
        if (cards == null || cards.Count == 0 || CardImagePrefab == null)
        {
            return;
        }

        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            return;
        }

        DestroyActiveOnActionPopupIfAny();
        GameObject root = new GameObject("OnActionCandidatesPopup", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        activeOnActionPopupRoot = root;
        isOnActionPopupOpen = true;
        root.transform.SetParent(canvas.transform, false);
        root.transform.SetAsLastSibling();
        root.SetFullSize();
        Image dim = root.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.55f);
        dim.raycastTarget = true;

        TextMeshProUGUI title = root.CreateChildTextCustom("OnActionTitle", UIAnchor.TopCenter, 640, 48);
        title.text = $"OnAction candidates ({ownerType}) [{context}]";
        title.fontSize = 24;
        title.color = Color.white;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -24f);

        GameObject scrollGo = root.CreateGridScrollView(640, 400, UIAnchor.TopCenter);
        RectTransform scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.anchoredPosition = new Vector2(0f, -88f);
        scrollGo.ConfigureGridCellFromViewportHeight(0.78f, 56f);

        ScrollRect sr = scrollGo.GetComponent<ScrollRect>();
        RectTransform content = sr != null ? sr.content : null;
        if (content != null)
        {
            for (int i = 0; i < cards.Count; i++)
            {
                CardData data = cards[i];
                if (data == null)
                {
                    continue;
                }

                GameObject go = Instantiate(CardImagePrefab, content);
                CardController cc = go.GetComponent<CardController>();
                if (cc != null)
                {
                    cc.SetUp(data, _ => { });
                }

                TextMeshProUGUI info = go.CreateChildTextCustom("OnActionCardInfo", UIAnchor.BottomCenter, 120, 24);
                info.text = $"ID:{data.id}";
                info.fontSize = 14;
                info.color = Color.white;
                info.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, 2f);
            }
        }

        Button closeBtn = root.CreateChildButton("Close");
        RectTransform closeRt = closeBtn.GetComponent<RectTransform>();
        closeRt.sizeDelta = new Vector2(180f, 48f);
        closeRt.anchorMin = new Vector2(0.5f, 0f);
        closeRt.anchorMax = new Vector2(0.5f, 0f);
        closeRt.pivot = new Vector2(0.5f, 0f);
        closeRt.anchoredPosition = new Vector2(0f, 36f);
        closeBtn.onClick.AddListener(() =>
        {
            isOnActionPopupOpen = false;
            activeOnActionPopupRoot = null;
            Destroy(root);
            onClose?.Invoke();
        });
    }

    private void DestroyActiveOnActionPopupIfAny()
    {
        if (activeOnActionPopupRoot != null)
        {
            Destroy(activeOnActionPopupRoot);
            activeOnActionPopupRoot = null;
            isOnActionPopupOpen = false;
        }
    }

    private void ShowResultOverlay(string resultText)
    {
        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            Debug.Log($"[Result] {resultText}");
            return;
        }

        GameObject root = new GameObject("BattleResultOverlay", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        root.transform.SetParent(canvas.transform, false);
        root.transform.SetAsLastSibling();
        root.SetFullSize();

        Image bg = root.GetComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.65f);
        bg.raycastTarget = true;

        TextMeshProUGUI result = root.CreateChildTextCustom("ResultText", UIAnchor.FullSize, 420, 120);
        result.text = resultText;
        result.fontSize = 72;
        result.alignment = TextAlignmentOptions.Center;
        result.color = resultText == "WIN" ? new Color32(255, 230, 80, 255) : new Color32(255, 120, 120, 255);
        RectTransform resultRt = result.GetComponent<RectTransform>();
        resultRt.anchorMin = new Vector2(0.5f, 0.5f);
        resultRt.anchorMax = new Vector2(0.5f, 0.5f);
        resultRt.pivot = new Vector2(0.5f, 0.5f);
        resultRt.sizeDelta = new Vector2(420f, 120f);
        resultRt.anchoredPosition = new Vector2(0f, 40f);

        Button close = root.CreateChildButton("Close");
        RectTransform closeRt = close.GetComponent<RectTransform>();
        closeRt.sizeDelta = new Vector2(180f, 52f);
        closeRt.anchorMin = new Vector2(0.5f, 0.5f);
        closeRt.anchorMax = new Vector2(0.5f, 0.5f);
        closeRt.pivot = new Vector2(0.5f, 0.5f);
        closeRt.anchoredPosition = new Vector2(0f, -60f);
        close.onClick.AddListener(() => Destroy(root));
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
