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

    /// <summary>OnHandAuto の再入防止（同一 CardController）。</summary>
    private readonly HashSet<CardController> onHandAutoProcessing = new HashSet<CardController>();

    private CardController copyCardController;
    private bool isMatchFinished;

    /// <summary>「相手ユニットを攻撃」選択後、次にタップする相手ユニット。</summary>
    private CardController pendingUnitAttackAttacker;
    private CardController pendingOnAttackEffectResolvedAttacker;
    private bool isEndTurnFlowRunning;
    private bool isOnActionPopupOpen;
    private GameObject activeOnActionPopupRoot;
    private GameObject activeAttackFlowDebugPanelRoot;
    private bool isAttackedSidePanelOpen;
    /// <summary>攻撃フロー中のテスト用「actionthink」表示中。true の間は進行を止める。</summary>
    private bool isActionThinkPauseOpen;
    /// <summary>攻撃後 OnAction の「プレイヤー手前」に actionthink を挟むテスト用フラグ。</summary>
    [SerializeField] private bool enableAttackFlowActionThinkTest = true;
    [SerializeField] private bool enableShieldAttackFlowDebugLog = true;
    private bool isShieldAttackResolving;
    private bool isTurnPhaseSequenceRunning;
    private bool blockShieldFlowDuringShieldAttack;
    private Gundam2024RuleScript.PlayerSide blockedShieldFlowSide;

    private enum AttackFlowStrikeKind
    {
        None,
        Shield,
        UnitVsUnit,
    }

    /// <summary>OnAction ログ用：直近の攻撃フロー（シールド／ユニット先／ブロックリダイレクト）。</summary>
    private AttackFlowStrikeKind attackFlowStrikeKind;
    private CardController attackFlowAttackerUnit;
    private PlayerType attackFlowAttackerOwner;
    private CardController attackFlowDeclaredDefenderUnit;
    private CardController attackFlowBlockRedirectUnit;
    private int attackFlowDefenderShieldCountAtStrike = -1;

    private void ClearAttackFlowContext()
    {
        attackFlowStrikeKind = AttackFlowStrikeKind.None;
        attackFlowAttackerUnit = null;
        attackFlowAttackerOwner = PlayerType.Player;
        attackFlowDeclaredDefenderUnit = null;
        attackFlowBlockRedirectUnit = null;
        attackFlowDefenderShieldCountAtStrike = -1;
    }

    private void RegisterAttackFlowContextForOnAction(
        CardController attacker,
        PlayerType attackerOwner,
        AttackFlowStrikeKind strike,
        CardController declaredDefenderOrNull,
        CardController blockRedirectUnitOrNull)
    {
        attackFlowStrikeKind = strike;
        attackFlowAttackerUnit = attacker;
        attackFlowAttackerOwner = attackerOwner;
        attackFlowDeclaredDefenderUnit = declaredDefenderOrNull;
        attackFlowBlockRedirectUnit = blockRedirectUnitOrNull;
        attackFlowDefenderShieldCountAtStrike = -1;
        if (strike == AttackFlowStrikeKind.Shield && gundamRule != null)
        {
            Gundam2024RuleScript.PlayerSide targetSide = attackerOwner == PlayerType.Player
                ? Gundam2024RuleScript.PlayerSide.Enemy
                : Gundam2024RuleScript.PlayerSide.Player;
            Gundam2024RuleScript.PlayerState st = targetSide == Gundam2024RuleScript.PlayerSide.Player
                ? gundamRule.Player
                : gundamRule.Enemy;
            attackFlowDefenderShieldCountAtStrike = st.shield;
        }
    }

    private void AppendAttackFlowContextToSnapshot(System.Text.StringBuilder sb, PlayerType snapshotActiveSide)
    {
        if (attackFlowStrikeKind == AttackFlowStrikeKind.None)
        {
            sb.AppendLine("  === AttackContext === (none)");
            return;
        }

        sb.AppendLine("  === AttackContext (OnAction付近) ===");
        sb.Append("  strike:").Append(attackFlowStrikeKind);
        if (attackFlowStrikeKind == AttackFlowStrikeKind.Shield && attackFlowDefenderShieldCountAtStrike >= 0)
        {
            sb.Append(" | defenderShieldCountAtOnAction:").Append(attackFlowDefenderShieldCountAtStrike);
        }

        sb.AppendLine();
        AppendAttackFlowUnitLine(sb, "  Attacker(攻撃元ユニット)", attackFlowAttackerUnit, attackFlowAttackerOwner);

        if (attackFlowStrikeKind == AttackFlowStrikeKind.Shield)
        {
            sb.AppendLine("  AttackDestination: 相手シールド（シールド攻撃）");
        }
        else if (attackFlowStrikeKind == AttackFlowStrikeKind.UnitVsUnit)
        {
            PlayerType defOwner = attackFlowDeclaredDefenderUnit != null
                ? ResolveCardOwner(attackFlowDeclaredDefenderUnit.transform)
                : PlayerType.Player;
            AppendAttackFlowUnitLine(sb, "  AttackDestination(攻撃先RESTユニット)", attackFlowDeclaredDefenderUnit, defOwner);
        }

        if (attackFlowBlockRedirectUnit != null && attackFlowBlockRedirectUnit.Data != null)
        {
            PlayerType bo = ResolveCardOwner(attackFlowBlockRedirectUnit.transform);
            AppendAttackFlowUnitLine(sb, "  BlockRedirectUnit(ブロック/リダイレクト)", attackFlowBlockRedirectUnit, bo);
        }
        else
        {
            sb.AppendLine("  BlockRedirectUnit: (none)");
        }

        AppendBlockRedirectProbeLines(sb, snapshotActiveSide);
    }

    /// <summary>
    /// スナップショット閲覧者（OnAction の commandOwner / activeSide）から見たユニット・ブロック／リダイレクトの有無。
    /// </summary>
    private void AppendBlockRedirectProbeLines(System.Text.StringBuilder sb, PlayerType viewerSide)
    {
        if (attackFlowStrikeKind == AttackFlowStrikeKind.None)
        {
            return;
        }

        bool unitRedirectActive = attackFlowBlockRedirectUnit != null && attackFlowBlockRedirectUnit.Data != null;
        sb.Append("  BlockRedirectProbe: unitRedirectActive:").Append(unitRedirectActive);
        if (unitRedirectActive)
        {
            PlayerType bo = ResolveCardOwner(attackFlowBlockRedirectUnit.transform);
            int slot = TryGetUnitBattleZoneSlotIndex(attackFlowBlockRedirectUnit);
            sb.Append(" blockUnitOwner:").Append(bo).Append(" blockUnit:").Append(attackFlowBlockRedirectUnit.Data.cardName).Append("(id:")
                .Append(attackFlowBlockRedirectUnit.Data.id).Append(") zoneSlotIndex:#").Append(slot);
        }

        sb.AppendLine();
        bool opponentHostsBlocker = unitRedirectActive
            && ResolveCardOwner(attackFlowBlockRedirectUnit.transform) != viewerSide;
        bool selfHostsBlocker = unitRedirectActive
            && ResolveCardOwner(attackFlowBlockRedirectUnit.transform) == viewerSide;
        sb.Append("  FromViewer(viewerSide:").Append(viewerSide).Append("): opponentIsBlockingWithUnitRedirect:")
            .Append(opponentHostsBlocker).Append(" selfFieldHostsBlockRedirectUnit:").Append(selfHostsBlocker).AppendLine();
    }

    /// <summary>仮想／ヘッダ用の 1 行ブロック状態。</summary>
    private string FormatBlockRedirectProbeInline(PlayerType viewerSide)
    {
        if (attackFlowStrikeKind == AttackFlowStrikeKind.None)
        {
            return "blockProbe:attackContextNone";
        }

        bool unitRedirectActive = attackFlowBlockRedirectUnit != null && attackFlowBlockRedirectUnit.Data != null;
        if (!unitRedirectActive)
        {
            return "blockProbe:unitRedirectActive:False opponentIsBlockingWithUnitRedirect:False selfFieldHostsBlockRedirectUnit:False";
        }

        PlayerType bo = ResolveCardOwner(attackFlowBlockRedirectUnit.transform);
        bool opponentHosts = bo != viewerSide;
        return "blockProbe:unitRedirectActive:True blockUnitOwner:" + bo + " opponentIsBlockingWithUnitRedirect:" + opponentHosts
            + " selfFieldHostsBlockRedirectUnit:" + !opponentHosts;
    }

    private static void AppendAttackFlowUnitLine(System.Text.StringBuilder sb, string label, CardController unit, PlayerType owner)
    {
        sb.Append(label).Append(": ");
        if (unit == null || unit.Data == null)
        {
            sb.AppendLine("(none)");
            return;
        }

        sb.Append(unit.Data.cardName).Append("(id:").Append(unit.Data.id).Append(") AP=").Append(unit.CurrentPower).Append(" HP=").Append(unit.CurrentHp)
            .Append(" REST:").Append(unit.IsRestState).Append(" owner:").Append(owner).AppendLine();
    }

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
        CardFeatureRegistry.EnsureLoaded();
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

            float fieldActionY = canShowUnitAttackMenu ? -130f : -70f;
            if (TryAddOnMainEffectApplyButton(FilterPanel, cardController, ownerType, fieldActionY))
            {
                fieldActionY -= 60f;
            }

            var trashButton = FilterPanel.CreateChildButton("send to trash");
            RectTransform trashBtnRect = trashButton.GetComponent<RectTransform>();
            trashBtnRect.sizeDelta = new Vector2(180, 50);
            trashBtnRect.anchoredPosition = new Vector2(0, fieldActionY);

            trashButton.onClick.AddListener(() =>
            {
                SendCardToTrash(cardController, ownerType);
                Destroy(FilterPanel);
            });

            closeBtnRect.anchoredPosition = new Vector2(0, fieldActionY - 70f);
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

        int cost = cardController.CurrentCost;
        Gundam2024RuleScript.PlayerState ownerState = ownerSide == Gundam2024RuleScript.PlayerSide.Player
            ? gundamRule.Player
            : gundamRule.Enemy;
        int currentLevel = ownerState.TotalLevel;
        int currentResource = ownerState.resource;

        if (currentLevel < cardController.CurrentLevel)
        {
            Debug.Log("レベルが足りません。");
            Destroy(FilterPanel);
            return;
        }

        float handActionY = -10f;
        if (TryAddOnMainEffectApplyButton(FilterPanel, cardController, ownerType, handActionY))
        {
            handActionY -= 60f;
            closeBtnRect.anchoredPosition = new Vector2(0, handActionY - 70f);
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
        btnRect.anchoredPosition = new Vector2(0, handActionY);

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
        CardController drawnCard = cardImage.GetComponent<CardController>();
        drawnCard.SetUp(drawCardData, OnCardClicked);
        if (targetType == PlayerType.Player)
        {
            playerHandCards.Add(drawnCard.Data);
        }
        else
        {
            enemyHandCards.Add(drawnCard.Data);
        }

        TriggerOnHandAutoEffects(drawnCard, targetType, skipHandZoneCheck: true);
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
            if (isAttackedSidePanelOpen)
            {
                yield return new WaitUntil(() => !isAttackedSidePanelOpen);
                yield return new WaitForSeconds(0.1f);
                continue;
            }

            if (isActionThinkPauseOpen)
            {
                yield return new WaitUntil(() => !isActionThinkPauseOpen);
                yield return new WaitForSeconds(0.1f);
                continue;
            }

            int attackedNow = TryEnemyShieldAttacks();
            if (attackedNow <= 0)
            {
                if (isOnActionPopupOpen || isActionThinkPauseOpen)
                {
                    // Close 後に onClose コールバックで攻撃が実行されるため、完了まで待って再評価する。
                    yield return new WaitUntil(() => !isOnActionPopupOpen && !isActionThinkPauseOpen);
                    yield return new WaitForSeconds(0.15f);
                    continue;
                }
                break;
            }

            attacked += attackedNow;
            if (isOnActionPopupOpen || isActionThinkPauseOpen)
            {
                // アクションステップの Close まで次の攻撃に進ませない。
                yield return new WaitUntil(() => !isOnActionPopupOpen && !isActionThinkPauseOpen);
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

            if (!gundamRule.CanPlayCard(side, cc.CurrentLevel, cc.CurrentCost))
            {
                continue;
            }

            if (!gundamRule.TryConsumeResource(side, cc.CurrentCost, 0, cc.Data.id))
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
        if (isAttackedSidePanelOpen)
        {
            return 0;
        }

        if (isActionThinkPauseOpen)
        {
            return 0;
        }

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

            List<CardController> eligibleEnemyHand = CollectEligibleEnemyHandCommandsForEnemyAiSim();
            if (canAttackUnit && restTargets.Count > 0)
            {
                LogEnemyAiPreAttackUnitAttackSimulation(unit, restTargets, eligibleEnemyHand);
            }

            if (canAttackShield || canDirectAttack)
            {
                LogEnemyAiPreShieldAttackSimulation(unit, eligibleEnemyHand);
                LogEnemyAiShieldAttackRedirectScenariosPick(unit, restTargets, eligibleEnemyHand);
            }

            CardController restBestTarget = null;
            int restBestScore = int.MinValue;
            if (canAttackUnit && restTargets.Count > 0)
            {
                restBestTarget = SelectEnemyAiUnitAttackTarget(
                    unit,
                    restTargets,
                    out restBestScore,
                    logResult: true,
                    verbosePerTargetLines: true,
                    eligibleCommandsCache: eligibleEnemyHand);
                if (!ShouldEnemyAiExecuteAttackByScore(restBestScore, hasRestTargetPickScore: true))
                {
                    Debug.Log(
                        $"[EnemyAI] skip all attacks (shield/unit) — {unit.Data.cardName} bestRestScore:{restBestScore} "
                        + $"(execute only when none / + / >= {EnemyAiAttackMinScoreToExecute})");
                    EnemyAiSkipEnemyUnitAttackWithoutBattle(
                        unit,
                        PlayerType.Enemy,
                        $"skipAllAttacks bestRestScore:{restBestScore} < {EnemyAiAttackMinScoreToExecute}");
                    continue;
                }
            }

            AttackFlg before = unit.AttackFlgState;
            bool attackShield = ShouldEnemyAiPreferShieldAttack(
                unit,
                canAttackShield || canDirectAttack,
                canAttackUnit,
                restTargets,
                eligibleEnemyHand);
            if (attackShield)
            {
                TryUnitShieldAttackFromUnit(unit);
                Debug.Log($"[EnemyAI] {unit.Data.cardName} chose shield attack.");
                ShowEnemyAttackDecisionNotice($"{unit.Data.cardName} attacks SHIELD");
            }
            else
            {
                if (restBestTarget == null)
                {
                    continue;
                }

                TryUnitVsUnitAttack(unit, restBestTarget, PlayerType.Enemy, PlayerType.Player);
                Debug.Log($"[EnemyAI] {unit.Data.cardName} chose unit attack target:{restBestTarget.Data.cardName} score:{restBestScore}");
                ShowEnemyAttackDecisionNotice($"{unit.Data.cardName} attacks UNIT: {restBestTarget.Data.cardName}");
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
            if (isOnActionPopupOpen || isActionThinkPauseOpen)
            {
                return 0;
            }
        }

        return 0;
    }

    private void ShowEnemyAttackDecisionNotice(string message)
    {
        Debug.Log($"[EnemyAttackDecisionNotice] message:{message}");
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
        List<CardController> restTargets,
        List<CardController> eligibleCommandsCache)
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

        int shieldScore = ComputeEnemyAiShieldAttackHeuristicScoreForCompare(attacker);

        CardController bestUnitTarget = SelectEnemyAiUnitAttackTarget(
            attacker,
            restTargets,
            out _,
            logResult: false,
            verbosePerTargetLines: false,
            eligibleCommandsCache: eligibleCommandsCache);
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

    private const int EnemyAiAttackScoreBonusRawKillPlayer = 55;
    private const int EnemyAiAttackScorePenaltyOneSidedEnemyDeath = 95;
    private const int EnemyAiAttackScorePenaltyCannotKillAfterHandSim = 85;
    private const int EnemyAiAttackScorePenaltyCannotKillNoHandCommands = 50;
    /// <summary>REST ユニット攻撃: このスコア未満なら攻撃しない（-10・0・プラスは実行可）。</summary>
    private const int EnemyAiAttackMinScoreToExecute = -10;

    /// <summary>
    /// REST 評価スコアが無い(none)／<see cref="EnemyAiAttackMinScoreToExecute"/> 以上（-10・0・+）ならユニット攻撃を実行する。
    /// </summary>
    private static bool ShouldEnemyAiExecuteAttackByScore(int bestTargetScore, bool hasRestTargetPickScore)
    {
        if (!hasRestTargetPickScore)
        {
            return true;
        }

        return bestTargetScore >= EnemyAiAttackMinScoreToExecute;
    }

    /// <summary>敵 AI がプレイヤーユニットへ攻撃する直前のスコア判定。閾値未満なら true（中断）。</summary>
    private bool TryEnemyAiAbortUnitAttackIfScoreTooLow(
        CardController enemyAttacker,
        CardController playerDefender,
        string context)
    {
        if (enemyAttacker == null || playerDefender == null || playerDefender.Data == null)
        {
            return true;
        }

        List<CardController> eligible = CollectEligibleEnemyHandCommandsForEnemyAiSim();
        int score = ScoreEnemyAttackPlayerUnitTarget(enemyAttacker, playerDefender, eligible, out string line);
        if (ShouldEnemyAiExecuteAttackByScore(score, hasRestTargetPickScore: true))
        {
            return false;
        }

        Debug.Log($"[EnemyAI] abort {context} — score:{score} below min {EnemyAiAttackMinScoreToExecute} ({line})");
        return true;
    }

    /// <summary>
    /// 敵 AI がユニット戦を行わずにその攻撃だけ打ち切る（スコア中止）。<b>REST は付けない</b>。攻撃フラグのみ下ろし、再入防止のため pending を消す。
    /// </summary>
    private void EnemyAiSkipEnemyUnitAttackWithoutBattle(CardController attacker, PlayerType attackerOwner, string reason)
    {
        if (attacker == null || attackerOwner != PlayerType.Enemy)
        {
            return;
        }

        Debug.Log($"[EnemyAI] skipAttack(noBattle) reason:{reason} unit:{attacker.Data?.cardName}(id:{attacker.Data?.id})");
        attacker.SetAttackFlg(AttackFlg.False);
        pendingUnitAttackAttacker = null;
        pendingOnAttackEffectResolvedAttacker = null;
        attackFlowBlockRedirectUnit = null;
        ClearAttackFlowContext();
        SyncAllResourceViewsFromRule();
    }

    private List<CardController> CollectEligibleEnemyHandCommandsForEnemyAiSim()
    {
        List<CardController> list = new List<CardController>();
        RectTransform hand = enemyCardGameRule != null ? enemyCardGameRule.HandScrollContent : null;
        if (hand == null)
        {
            return list;
        }

        for (int i = 0; i < hand.childCount; i++)
        {
            CardController cc = hand.GetChild(i).GetComponent<CardController>();
            if (cc == null || cc.Data == null || cc.Data.type != Type.Command)
            {
                continue;
            }

            if (!HasEffectTiming(cc.Data, EffectTiming.OnAction) || !CanExecuteOnActionCardNow(PlayerType.Enemy, cc))
            {
                continue;
            }

            list.Add(cc);
        }

        return list;
    }

    private void ApplyEnemyOnActionVirtualChainToBattleSnaps(
        List<VirtualBattleUnitSnap> working,
        CardController command,
        PlayerType commandOwnerSide)
    {
        if (working == null || command == null || command.Data == null)
        {
            return;
        }

        List<EffectData> onActionEffects = GetEffectsByTiming(command.Data, EffectTiming.OnAction);
        if (onActionEffects == null)
        {
            return;
        }

        for (int ei = 0; ei < onActionEffects.Count; ei++)
        {
            EffectData eff = onActionEffects[ei];
            if (eff == null)
            {
                continue;
            }

            if (eff.type == EffectType.Draw || eff.type == EffectType.BlockRedirect)
            {
                continue;
            }

            int magnitude = Mathf.Abs(eff.value);
            if (magnitude == 0)
            {
                continue;
            }

            List<CardController> targets = ResolveEffectTargets(command, commandOwnerSide, eff.target);
            if (targets == null || targets.Count == 0)
            {
                continue;
            }

            ApplyVirtualBattleEffectToTargetsOnSnaps(working, eff, targets);
        }
    }

    private List<CardController> GetPlayerUnitsForShieldAttackReactionPanel()
    {
        List<CardController> defenderUnits = GetAliveEnemyUnits(PlayerType.Enemy);
        List<CardController> reactionCandidates = new List<CardController>();
        for (int i = 0; i < defenderUnits.Count; i++)
        {
            CardController unit = defenderUnits[i];
            if (unit == null || unit.Data == null)
            {
                continue;
            }

            if (!HasEffectTiming(unit.Data, EffectTiming.OnEnemyAttack))
            {
                continue;
            }

            reactionCandidates.Add(unit);
        }

        return reactionCandidates;
    }

    private static bool CouldPlayerUnitRedirectShieldAttackToUnitCombat(CardController playerUnit)
    {
        if (playerUnit == null || playerUnit.Data == null)
        {
            return false;
        }

        if (!HasEffectTiming(playerUnit.Data, EffectTiming.OnEnemyAttack))
        {
            return false;
        }

        if (!HasBlockRedirectOnEnemyAttack(playerUnit.Data))
        {
            return false;
        }

        bool cannotSelectBecauseBlockRedirectWhileRest = HasBlockRedirectOnEnemyAttack(playerUnit.Data) && playerUnit.IsRestState;
        return !cannotSelectBecauseBlockRedirectWhileRest;
    }

    private int ComputeEnemyAiShieldAttackHeuristicScoreForCompare(CardController attacker)
    {
        Gundam2024RuleScript.PlayerState p = gundamRule.Player;
        int shieldScore = p.shield <= 1 ? 100 : 35;
        if (p.exBase > 0)
        {
            shieldScore += Mathf.Clamp(attacker.CurrentPower, 0, 20);
        }
        else
        {
            shieldScore += 12;
        }

        return shieldScore;
    }

    private void AppendEnemyAiVirtualHandCmdGlobalProbeLines(
        System.Text.StringBuilder sb,
        string indent,
        List<CardController> eligibleHandCommands)
    {
        List<CardController> eligible = eligibleHandCommands ?? new List<CardController>();
        if (eligible.Count == 0)
        {
            sb.Append(indent).AppendLine("(no eligible OnAction hand commands — skip global per-command virtual rows)");
            return;
        }

        for (int ci = 0; ci < eligible.Count; ci++)
        {
            CardController cmd = eligible[ci];
            if (cmd == null || cmd.Data == null)
            {
                continue;
            }

            List<VirtualBattleUnitSnap> work = CloneVirtualBattleSnaps(BuildFullBattleVirtualSnapshot());
            ApplyEnemyOnActionVirtualChainToBattleSnaps(work, cmd, PlayerType.Enemy);
            sb.Append(indent).Append("cmd[").Append(ci).Append("]:").Append(cmd.Data.cardName).Append("(id:").Append(cmd.Data.id)
                .Append(") afterVirtualField:").Append(FormatVirtualBattleFieldLine(work)).AppendLine();
        }
    }

    private void AppendEnemyAiVirtualExchangeProbeForAttackerVsPlayerUnit(
        System.Text.StringBuilder sb,
        string indent,
        CardController enemyAttacker,
        CardController playerUnit,
        List<CardController> eligibleHandCommands)
    {
        if (enemyAttacker == null || enemyAttacker.Data == null || playerUnit == null || playerUnit.Data == null)
        {
            return;
        }

        List<CardController> eligible = eligibleHandCommands ?? new List<CardController>();
        int rawPlayerHpAfter = Mathf.Max(0, playerUnit.CurrentHp - enemyAttacker.CurrentPower);
        int rawEnemyHpAfter = Mathf.Max(0, enemyAttacker.CurrentHp - playerUnit.CurrentPower);
        bool rawKillPlayer = rawPlayerHpAfter <= 0;
        bool oneSidedEnemyDie = rawEnemyHpAfter <= 0 && !rawKillPlayer;
        sb.Append(indent).Append("rawExchange(noHandCmd): playerHpAfter=").Append(rawPlayerHpAfter).Append(" enemyHpAfter=").Append(rawEnemyHpAfter)
            .Append(rawKillPlayer ? " PLAYER_UNIT_DEAD" : "").Append(oneSidedEnemyDie ? " ENEMY_ONLY_DEAD" : "").AppendLine();

        if (eligible.Count == 0)
        {
            sb.Append(indent).AppendLine("(no eligible OnAction hand commands — skip per-command virtual rows for this pair)");
            return;
        }

        for (int ci = 0; ci < eligible.Count; ci++)
        {
            CardController cmd = eligible[ci];
            if (cmd == null || cmd.Data == null)
            {
                continue;
            }

            List<VirtualBattleUnitSnap> work = CloneVirtualBattleSnaps(BuildFullBattleVirtualSnapshot());
            ApplyEnemyOnActionVirtualChainToBattleSnaps(work, cmd, PlayerType.Enemy);
            VirtualBattleUnitSnap ats = FindBattleVirtualSnap(work, enemyAttacker);
            VirtualBattleUnitSnap pts = FindBattleVirtualSnap(work, playerUnit);
            sb.Append(indent).Append("cmd[").Append(ci).Append("]:").Append(cmd.Data.cardName).Append("(id:").Append(cmd.Data.id).Append(") afterVirtualField:")
                .Append(FormatVirtualBattleFieldLine(work));
            if (ats == null || pts == null)
            {
                sb.AppendLine(" | postCmdExchange: (attacker or target missing from virtual snap)");
                continue;
            }

            int playerHpAfterEx = Mathf.Max(0, pts.Hp - ats.Ap);
            bool killPlayerAfter = playerHpAfterEx <= 0;
            sb.Append(" | virtualAttacker AP=").Append(ats.Ap).Append(" HP=").Append(ats.Hp).Append(" virtualTarget AP=").Append(pts.Ap).Append(" HP=")
                .Append(pts.Hp).Append(" => playerHpAfterExchange=").Append(playerHpAfterEx).Append(" killPlayer:").Append(killPlayerAfter).AppendLine();
        }
    }

    /// <summary>
    /// 敵ユニット攻撃の <see cref="TryUnitVsUnitAttack"/> より前に、候補ごとの素の交換と手札 OnAction 仮想適用後の盤面をログする（本番状態は変更しない）。
    /// </summary>
    private void LogEnemyAiPreAttackUnitAttackSimulation(
        CardController attacker,
        List<CardController> restTargets,
        List<CardController> eligibleHandCommands)
    {
        if (attacker == null || attacker.Data == null || restTargets == null)
        {
            return;
        }

        List<CardController> eligible = eligibleHandCommands ?? new List<CardController>();
        List<VirtualBattleUnitSnap> baselineSnaps = BuildFullBattleVirtualSnapshot();
        System.Text.StringBuilder sb = new System.Text.StringBuilder(2048);
        sb.AppendLine(
            "[EnemyAiPreAttackSim] phase:BeforeTryUnitVsUnitAttack note:仮想シミュのみ。直後の [EnemyAiUnitAttackPick] がスコア確定→実攻撃。");
        sb.Append("  attacker:").Append(attacker.Data.cardName).Append("(id:").Append(attacker.Data.id).Append(") AP=").Append(attacker.CurrentPower)
            .Append(" HP=").Append(attacker.CurrentHp).Append(" eligibleOnActionHandCmds:").Append(eligible.Count).AppendLine();
        sb.Append("  baselineVirtualField: ").Append(FormatVirtualBattleFieldLine(baselineSnaps)).AppendLine();

        for (int ti = 0; ti < restTargets.Count; ti++)
        {
            CardController target = restTargets[ti];
            if (target == null || target.Data == null)
            {
                continue;
            }

            sb.Append("  --- candidateTarget:").Append(target.Data.cardName).Append("(id:").Append(target.Data.id).Append(") AP=")
                .Append(target.CurrentPower).Append(" HP=").Append(target.CurrentHp).AppendLine();
            AppendEnemyAiVirtualExchangeProbeForAttackerVsPlayerUnit(sb, "    ", attacker, target, eligible);
        }

        Debug.Log(sb.ToString());
    }

    /// <summary>
    /// 敵シールド攻撃の <see cref="TryUnitShieldAttackFromUnit"/> より前に、手札 OnAction の仮想適用と
    /// プレイヤー側「シールドでブロック→ユニット戦」になり得る反応ユニットごとの仮想交換をログする。
    /// </summary>
    private void LogEnemyAiPreShieldAttackSimulation(CardController enemyAttacker, List<CardController> eligibleHandCommands)
    {
        if (enemyAttacker == null || enemyAttacker.Data == null)
        {
            return;
        }

        List<CardController> eligible = eligibleHandCommands ?? new List<CardController>();
        List<VirtualBattleUnitSnap> baselineSnaps = BuildFullBattleVirtualSnapshot();
        System.Text.StringBuilder sb = new System.Text.StringBuilder(2048);
        sb.AppendLine(
            "[EnemyAiPreShieldAttackSim] phase:BeforeTryUnitShieldAttack note:仮想シミュのみ。プレイヤーが BlockRedirect 可能ならユニット戦に移行し得る。直後に [EnemyAiShieldAttackPick]。");
        sb.Append("  attacker:").Append(enemyAttacker.Data.cardName).Append("(id:").Append(enemyAttacker.Data.id).Append(") AP=").Append(enemyAttacker.CurrentPower)
            .Append(" HP=").Append(enemyAttacker.CurrentHp).Append(" eligibleOnActionHandCmds:").Append(eligible.Count).AppendLine();
        sb.Append("  defenderPlayer shield:").Append(gundamRule.Player.shield).Append(" exBase:").Append(gundamRule.Player.exBase).AppendLine();
        sb.Append("  baselineVirtualField: ").Append(FormatVirtualBattleFieldLine(baselineSnaps)).AppendLine();
        sb.AppendLine("  --- globalHandCmdVirtual (盤面全体・対象ペアなし) ---");
        AppendEnemyAiVirtualHandCmdGlobalProbeLines(sb, "  ", eligible);

        List<CardController> reactionUnits = GetPlayerUnitsForShieldAttackReactionPanel();
        sb.AppendLine("  --- playerReactionUnits (シールド攻撃パネルと同型。BlockRedirect+REST だとユニット戦へ誘導不可) ---");
        if (reactionUnits.Count == 0)
        {
            sb.AppendLine("  (no player units with OnEnemyAttack timing)");
        }

        for (int ri = 0; ri < reactionUnits.Count; ri++)
        {
            CardController ru = reactionUnits[ri];
            if (ru == null || ru.Data == null)
            {
                continue;
            }

            bool canRedirectUnitCombat = CouldPlayerUnitRedirectShieldAttackToUnitCombat(ru);
            sb.Append("  --- reactionUnit:").Append(ru.Data.cardName).Append("(id:").Append(ru.Data.id).Append(") REST:").Append(ru.IsRestState)
                .Append(" hasBlockRedirectOnEnemyAttack:").Append(HasBlockRedirectOnEnemyAttack(ru.Data))
                .Append(" couldRedirectToUnitCombat:").Append(canRedirectUnitCombat).AppendLine();
            if (!canRedirectUnitCombat)
            {
                sb.AppendLine("    (skip pair exchange probe — シールド継続 or 効果のみ想定)");
                continue;
            }

            AppendEnemyAiVirtualExchangeProbeForAttackerVsPlayerUnit(sb, "    ", enemyAttacker, ru, eligible);
        }

        Debug.Log(sb.ToString());
    }

    private void LogEnemyAiShieldAttackRedirectScenariosPick(
        CardController enemyAttacker,
        List<CardController> restTargets,
        List<CardController> eligibleHandCommands)
    {
        if (enemyAttacker == null || enemyAttacker.Data == null)
        {
            return;
        }

        List<CardController> eligible = eligibleHandCommands ?? new List<CardController>();
        const string tag = "[EnemyAiShieldAttackPick]";
        int shieldHeuristic = ComputeEnemyAiShieldAttackHeuristicScoreForCompare(enemyAttacker);
        System.Text.StringBuilder sb = new System.Text.StringBuilder(1200);
        sb.Append(tag).Append(" phase:BeforeTryUnitShieldAttack_DecisionAid shieldHeuristicScore:").Append(shieldHeuristic).AppendLine();

        CardController bestRestPick = null;
        int bestRestScore = int.MinValue;
        if (restTargets != null && restTargets.Count > 0)
        {
            bestRestPick = SelectEnemyAiUnitAttackTarget(
                enemyAttacker,
                restTargets,
                out _,
                logResult: false,
                verbosePerTargetLines: false,
                eligibleCommandsCache: eligible);
            if (bestRestPick != null)
            {
                bestRestScore = ScoreEnemyAttackPlayerUnitTarget(enemyAttacker, bestRestPick, eligible, out string brLine);
                sb.Append("  restUnitAttackBestIfChosenInstead: ").Append(brLine).AppendLine();
            }
            else
            {
                sb.AppendLine("  restUnitAttackBestIfChosenInstead: (no REST player target)");
            }
        }
        else
        {
            sb.AppendLine("  restUnitAttackBestIfChosenInstead: (no REST player targets)");
        }

        List<CardController> reactionUnits = GetPlayerUnitsForShieldAttackReactionPanel();
        CardController bestBlock = null;
        int bestBlockScore = int.MinValue;
        bool bestBlockRawKill = false;
        int bestBlockThreat = int.MinValue;
        int bestBlockIndex = int.MaxValue;
        CardController worstBlock = null;
        int worstBlockScore = int.MaxValue;
        bool worstBlockRawKill = false;
        int worstBlockThreat = int.MaxValue;
        int worstBlockIndex = int.MinValue;
        int redirectIndex = 0;
        sb.AppendLine("  --- scores if shield is BLOCKED→unit combat (player picks each redirect-capable unit) ---");

        for (int ri = 0; ri < reactionUnits.Count; ri++)
        {
            CardController ru = reactionUnits[ri];
            if (ru == null || ru.Data == null)
            {
                continue;
            }

            if (!CouldPlayerUnitRedirectShieldAttackToUnitCombat(ru))
            {
                sb.Append("  skipScore redirectNotAvailable:").Append(ru.Data.cardName).Append("(id:").Append(ru.Data.id).AppendLine(")");
                continue;
            }

            int sc = ScoreEnemyAttackPlayerUnitTarget(enemyAttacker, ru, eligible, out string line);
            sb.Append("  ").Append(line).AppendLine();
            int rawPlayerHpAfter = Mathf.Max(0, ru.CurrentHp - enemyAttacker.CurrentPower);
            bool rawKill = rawPlayerHpAfter <= 0;
            int threat = ru.CurrentPower * 2 - ru.CurrentHp;
            if (bestBlock == null
                || IsBetterEnemyAiAttackPick(sc, rawKill, threat, redirectIndex, bestBlockScore, bestBlockRawKill, bestBlockThreat, bestBlockIndex))
            {
                bestBlock = ru;
                bestBlockScore = sc;
                bestBlockRawKill = rawKill;
                bestBlockThreat = threat;
                bestBlockIndex = redirectIndex;
            }

            if (worstBlock == null
                || IsStrictlyWorseEnemyAiAttackPick(sc, rawKill, threat, redirectIndex, worstBlockScore, worstBlockRawKill, worstBlockThreat, worstBlockIndex))
            {
                worstBlock = ru;
                worstBlockScore = sc;
                worstBlockRawKill = rawKill;
                worstBlockThreat = threat;
                worstBlockIndex = redirectIndex;
            }

            redirectIndex++;
        }

        if (bestBlock != null)
        {
            sb.Append("  bestMoveIfBlockedToUnit:").Append(bestBlock.Data.cardName).Append("(id:").Append(bestBlock.Data.id).Append(") score:")
                .Append(bestBlockScore).AppendLine();
        }
        else
        {
            sb.AppendLine("  bestMoveIfBlockedToUnit:(none — no redirect-capable blocker)");
        }

        if (worstBlock != null)
        {
            sb.Append("  worstMoveIfBlockedToUnit:").Append(worstBlock.Data.cardName).Append("(id:").Append(worstBlock.Data.id).Append(") score:")
                .Append(worstBlockScore).AppendLine();
        }
        else
        {
            sb.AppendLine("  worstMoveIfBlockedToUnit:(none)");
        }

        if (bestBlock != null && worstBlock != null && ReferenceEquals(bestBlock, worstBlock))
        {
            sb.AppendLine(
                "  note:blockedBest==blockedWorst は正常。BlockRedirect かつパネルで選べる反応ユニットが 1 体しかいないため、ベストもワーストもそのユニット。");
        }
        else if (bestBlock != null && worstBlock != null && bestBlockScore == worstBlockScore)
        {
            sb.AppendLine(
                "  note:blocked スコア同値で別ユニット。ScoreEnemyAttackPlayerUnitTarget が同じになり得る（同型の AP/HP と手札シミュ結果）。");
        }

        if (bestBlock != null && worstBlock != null)
        {
            sb.Insert(
                tag.Length,
                " summaryLine: shieldHeuristic=" + shieldHeuristic + " | blockedBest:" + bestBlock.Data.cardName + "(id:" + bestBlock.Data.id + ") score:"
                + bestBlockScore + " | blockedWorst:" + worstBlock.Data.cardName + "(id:" + worstBlock.Data.id + ") score:" + worstBlockScore);
        }
        else if (bestBlock != null)
        {
            sb.Insert(
                tag.Length,
                " summaryLine: shieldHeuristic=" + shieldHeuristic + " | blockedBest:" + bestBlock.Data.cardName + "(id:" + bestBlock.Data.id + ") score:" + bestBlockScore
                + " | blockedWorst:(none)");
        }
        else
        {
            sb.Insert(tag.Length, " summaryLine: shieldHeuristic=" + shieldHeuristic + " | blockedBest:(none) | blockedWorst:(none)");
        }

        sb.Append("  note:shieldPath vs RESTユニット直攻は ShouldEnemyAiPreferShieldAttack で比較。ここはブロック時のユニット戦のみ worst/best。").AppendLine();
        Debug.Log(sb.ToString());
    }

    private bool EnemyAiAnyHandCommandSimAllowsKillPlayerUnit(
        CardController enemyAttacker,
        CardController playerTarget,
        List<CardController> eligibleCommands,
        out string simDetail)
    {
        simDetail = "";
        if (enemyAttacker == null || playerTarget == null || eligibleCommands == null || eligibleCommands.Count == 0)
        {
            return false;
        }

        for (int ci = 0; ci < eligibleCommands.Count; ci++)
        {
            CardController cmd = eligibleCommands[ci];
            if (cmd == null || cmd.Data == null)
            {
                continue;
            }

            List<VirtualBattleUnitSnap> work = CloneVirtualBattleSnaps(BuildFullBattleVirtualSnapshot());
            ApplyEnemyOnActionVirtualChainToBattleSnaps(work, cmd, PlayerType.Enemy);
            VirtualBattleUnitSnap ats = FindBattleVirtualSnap(work, enemyAttacker);
            VirtualBattleUnitSnap pts = FindBattleVirtualSnap(work, playerTarget);
            if (ats == null || pts == null)
            {
                continue;
            }

            int playerHpAfterExchange = Mathf.Max(0, pts.Hp - ats.Ap);
            if (playerHpAfterExchange <= 0)
            {
                simDetail = "cmd:" + cmd.Data.cardName + "(id:" + cmd.Data.id + ")";
                return true;
            }
        }

        return false;
    }

    private int ScoreEnemyAttackPlayerUnitTarget(
        CardController attacker,
        CardController playerTarget,
        List<CardController> eligibleHandCommands,
        out string scoreLine)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder(256);
        if (playerTarget == null || playerTarget.Data == null || attacker == null || attacker.Data == null)
        {
            scoreLine = "(invalid target)";
            return int.MinValue;
        }

        int basePart = playerTarget.CurrentPower * 2 - playerTarget.CurrentHp;
        int score = basePart;
        int rawPlayerHpAfter = Mathf.Max(0, playerTarget.CurrentHp - attacker.CurrentPower);
        int rawEnemyHpAfter = Mathf.Max(0, attacker.CurrentHp - playerTarget.CurrentPower);
        bool rawKill = rawPlayerHpAfter <= 0;
        bool rawOneSidedEnemyDeath = rawEnemyHpAfter <= 0 && !rawKill;

        sb.Append("target:").Append(playerTarget.Data.cardName).Append("(id:").Append(playerTarget.Data.id).Append(") base:")
            .Append(basePart);

        if (rawKill)
        {
            score += EnemyAiAttackScoreBonusRawKillPlayer;
            sb.Append(" +rawKillPlayer:").Append(EnemyAiAttackScoreBonusRawKillPlayer);
        }

        if (rawOneSidedEnemyDeath)
        {
            score -= EnemyAiAttackScorePenaltyOneSidedEnemyDeath;
            sb.Append(" -oneSidedEnemyDie:").Append(EnemyAiAttackScorePenaltyOneSidedEnemyDeath);
        }

        if (!rawKill)
        {
            bool handKill = EnemyAiAnyHandCommandSimAllowsKillPlayerUnit(
                attacker,
                playerTarget,
                eligibleHandCommands,
                out string handDetail);
            if (!handKill)
            {
                int pen = eligibleHandCommands != null && eligibleHandCommands.Count > 0
                    ? EnemyAiAttackScorePenaltyCannotKillAfterHandSim
                    : EnemyAiAttackScorePenaltyCannotKillNoHandCommands;
                score -= pen;
                sb.Append(" -cannotKillPlayer:").Append(pen).Append(eligibleHandCommands != null && eligibleHandCommands.Count > 0
                    ? "(afterHandSim)"
                    : "(noOnActionHand)");
            }
            else
            {
                sb.Append(" +handSimKill(").Append(handDetail).Append(')');
            }
        }

        sb.Append(" total:").Append(score);
        scoreLine = sb.ToString();
        return score;
    }

    private static bool IsBetterEnemyAiAttackPick(
        int newScore,
        bool newRawKill,
        int newThreat,
        int newIndex,
        int bestScore,
        bool bestRawKill,
        int bestThreat,
        int bestIndex)
    {
        if (newScore != bestScore)
        {
            return newScore > bestScore;
        }

        if (newRawKill != bestRawKill)
        {
            return newRawKill;
        }

        if (newThreat != bestThreat)
        {
            return newThreat > bestThreat;
        }

        return newIndex < bestIndex;
    }

    private static bool IsStrictlyWorseEnemyAiAttackPick(
        int newScore,
        bool newRawKill,
        int newThreat,
        int newIndex,
        int worstScore,
        bool worstRawKill,
        int worstThreat,
        int worstIndex)
    {
        if (newScore != worstScore)
        {
            return newScore < worstScore;
        }

        if (newRawKill != worstRawKill)
        {
            return !newRawKill && worstRawKill;
        }

        if (newThreat != worstThreat)
        {
            return newThreat < worstThreat;
        }

        return newIndex > worstIndex;
    }

    private CardController SelectEnemyAiUnitAttackTarget(
        CardController attacker,
        List<CardController> restTargets,
        out int bestPickScore,
        bool logResult = true,
        bool verbosePerTargetLines = true,
        List<CardController> eligibleCommandsCache = null)
    {
        bestPickScore = int.MinValue;
        if (attacker == null || restTargets == null || restTargets.Count == 0)
        {
            return null;
        }

        List<CardController> eligibleCommands = eligibleCommandsCache ?? CollectEligibleEnemyHandCommandsForEnemyAiSim();
        CardController best = null;
        int bestScore = int.MinValue;
        bool bestRawKill = false;
        int bestThreat = int.MinValue;
        int bestIndex = int.MaxValue;
        CardController worst = null;
        int worstScore = int.MaxValue;
        bool worstRawKill = false;
        int worstThreat = int.MaxValue;
        int worstIndex = int.MinValue;
        const string enemyAiUnitAttackPickLogTag = "[EnemyAiUnitAttackPick]";
        System.Text.StringBuilder pickLog = new System.Text.StringBuilder(restTargets.Count * 200);
        pickLog.Append(enemyAiUnitAttackPickLogTag).Append(" phase:AfterPreAttackSim_DecisionLog");
        if (!verbosePerTargetLines)
        {
            pickLog.Append(" mode:summaryForShieldCompare");
        }

        pickLog.Append(" attacker:").Append(attacker.Data.cardName).Append("(id:").Append(attacker.Data.id).Append(") eligibleOnActionHandCmds:")
            .Append(eligibleCommands.Count).AppendLine();

        for (int i = 0; i < restTargets.Count; i++)
        {
            CardController t = restTargets[i];
            if (t == null || t.Data == null)
            {
                continue;
            }

            int sc = ScoreEnemyAttackPlayerUnitTarget(attacker, t, eligibleCommands, out string line);
            if (verbosePerTargetLines)
            {
                pickLog.Append("  ").Append(line).AppendLine();
            }

            int rawPlayerHpAfter = Mathf.Max(0, t.CurrentHp - attacker.CurrentPower);
            bool rawKill = rawPlayerHpAfter <= 0;
            int threat = t.CurrentPower * 2 - t.CurrentHp;
            if (best == null
                || IsBetterEnemyAiAttackPick(sc, rawKill, threat, i, bestScore, bestRawKill, bestThreat, bestIndex))
            {
                best = t;
                bestScore = sc;
                bestRawKill = rawKill;
                bestThreat = threat;
                bestIndex = i;
            }

            if (worst == null
                || IsStrictlyWorseEnemyAiAttackPick(sc, rawKill, threat, i, worstScore, worstRawKill, worstThreat, worstIndex))
            {
                worst = t;
                worstScore = sc;
                worstRawKill = rawKill;
                worstThreat = threat;
                worstIndex = i;
            }
        }

        if (best != null)
        {
            pickLog.Append("  bestMove:").Append(best.Data.cardName).Append("(id:").Append(best.Data.id).Append(") score:")
                .Append(bestScore).AppendLine();
        }
        else
        {
            pickLog.AppendLine("  bestMove:(none)");
        }

        if (worst != null)
        {
            pickLog.Append("  worstMove:").Append(worst.Data.cardName).Append("(id:").Append(worst.Data.id).Append(") score:")
                .Append(worstScore).AppendLine();
        }
        else
        {
            pickLog.AppendLine("  worstMove:(none)");
        }

        if (best != null && worst != null && ReferenceEquals(best, worst))
        {
            pickLog.AppendLine(
                "  note:bestMove と worstMove が同一スコア・同一カードなのは、REST 攻撃候補が 1 体しかないため（ベストもワーストもその 1 体）。");
        }
        else if (best != null && worst != null && bestScore == worstScore)
        {
            pickLog.AppendLine(
                "  note:スコア同値で別カード。タイブレーク: best=有利側ルール→同点なら若いスロット index、worst=不利側→同点なら遅い index。");
        }

        if (best != null)
        {
            pickLog.Append("  => chosen(sameAsBestMove):").Append(best.Data.cardName).Append("(id:").Append(best.Data.id).Append(") bestScore:")
                .Append(bestScore).AppendLine();
        }
        else
        {
            pickLog.AppendLine("  => chosen:(none)");
        }

        if (best != null && worst != null)
        {
            pickLog.Insert(
                enemyAiUnitAttackPickLogTag.Length,
                " summaryLine: bestMove:" + best.Data.cardName + "(id:" + best.Data.id + ") score:" + bestScore + " | worstMove:"
                + worst.Data.cardName + "(id:" + worst.Data.id + ") score:" + worstScore);
        }
        else if (best != null)
        {
            pickLog.Insert(
                enemyAiUnitAttackPickLogTag.Length,
                " summaryLine: bestMove:" + best.Data.cardName + "(id:" + best.Data.id + ") score:" + bestScore + " | worstMove:(none)");
        }

        if (best != null)
        {
            bestPickScore = bestScore;
        }

        if (logResult)
        {
            Debug.Log(pickLog.ToString());
        }

        return best;
    }

    void ExcueteEndTurn()
    {
        if (isEndTurnFlowRunning || isAttackedSidePanelOpen || isActionThinkPauseOpen)
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
        ClearTimedStatModifiersForAllInPlayCards(EffectDuration.UntilEndOfTurn);
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

    // カードの攻撃対象を選択するUIを表示するメソッド
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
                case EffectType.BlockRedirect:
                    // BlockRedirect は戦闘フロー分岐で解釈するため、ここでは何もしない。
                    break;
            }
        }

        SyncAllResourceViewsFromRule();
    }

    /// <summary>
    /// シールド攻撃。AP が 1 未満のときは何もしない。
    /// EXベースありなら power を EX ベースに与え、無いならシールド 1 枚のみ破壊（<see cref="Gundam2024RuleScript.TryApplyUnitShieldAttack"/>）。
    /// </summary>
    private void TryUnitShieldAttackFromUnit(
        CardController attacker,
        bool skipOnActionPause = false,
        bool skipOnAttackSelection = false,
        bool skipAttackedSidePanelPause = false)
    {
        if (enableShieldAttackFlowDebugLog)
        {
            string attackerName = attacker != null && attacker.Data != null ? attacker.Data.cardName : "null";
            Debug.Log(
                $"[TryUnitShieldAttackFromUnit] called attacker:{attackerName} skipOnActionPause:{skipOnActionPause} skipOnAttackSelection:{skipOnAttackSelection} skipAttackedSidePanelPause:{skipAttackedSidePanelPause}");
        }

        if (isAttackedSidePanelOpen && !skipAttackedSidePanelPause)
        {
            return;
        }

        if (isActionThinkPauseOpen && !skipAttackedSidePanelPause)
        {
            return;
        }

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

        if (attacker.CurrentHp <= 0)
        {
            Debug.Log("[ShieldAttack] HP is 0 — consume attack and set REST.");
            attacker.SetAttackFlg(AttackFlg.False);
            attacker.SetUnitRestVisual(true);
            pendingUnitAttackAttacker = null;
            pendingOnAttackEffectResolvedAttacker = null;
            return;
        }

        Gundam2024RuleScript.PlayerSide targetSide = attackerOwner == PlayerType.Player
            ? Gundam2024RuleScript.PlayerSide.Enemy
            : Gundam2024RuleScript.PlayerSide.Player;
        Gundam2024RuleScript.PlayerState defender = targetSide == Gundam2024RuleScript.PlayerSide.Player
            ? gundamRule.Player
            : gundamRule.Enemy;

        // OnAction より前の時点で EX ベースがあったかを固定する（OnAction で EX が 0 になった後にシールドが割れるのを防ぐ）。
        bool hadExBaseLayerAtShieldAttackStart = defender.exBase > 0;
        if (hadExBaseLayerAtShieldAttackStart)
        {
            blockShieldFlowDuringShieldAttack = true;
            blockedShieldFlowSide = targetSide;
        }

        if (isShieldAttackResolving)
        {
            if (enableShieldAttackFlowDebugLog)
            {
                Debug.Log("[TryUnitShieldAttackFromUnit] skipped by isShieldAttackResolving guard.");
            }
            return;
        }
        isShieldAttackResolving = true;

        try
        {
            // シールド攻撃時も OnAttack の対象選択効果を先に解決する。
            if (!skipOnAttackSelection && pendingOnAttackEffectResolvedAttacker != attacker)
            {
                // 効果適用するためのカードを選択するUI生成
                if (TryOpenOnAttackEnemySelectionPanel(
                    attacker,
                    attackerOwner,
                    null,
                    () => TryUnitShieldAttackFromUnit(attacker, skipOnActionPause, true, skipAttackedSidePanelPause)))
                {
                    return;
                }

                pendingOnAttackEffectResolvedAttacker = attacker;
            }

            CardController selectedDefenderFromShieldPanel = null;
            if (!skipAttackedSidePanelPause
                && TryOpenAttackedSideUnitsPanel(
                    attackerOwner,
                    attacker,
                    selected =>
                    {
                        selectedDefenderFromShieldPanel = selected;
                    },
                    () =>
                    {
                        if (selectedDefenderFromShieldPanel != null)
                        {
                            PlayerType selectedDefenderOwner = ResolveCardOwner(selectedDefenderFromShieldPanel.transform);
                            bool shouldRedirect = ExecuteDefenderOnAttackReaction(
                                selectedDefenderFromShieldPanel,
                                attacker,
                                selectedDefenderOwner);
                            if (shouldRedirect)
                            {
                                if (attackerOwner == PlayerType.Enemy
                                    && selectedDefenderOwner == PlayerType.Player
                                    && TryEnemyAiAbortUnitAttackIfScoreTooLow(
                                        attacker,
                                        selectedDefenderFromShieldPanel,
                                        "ShieldToUnit block redirect"))
                                {
                                    EnemyAiSkipEnemyUnitAttackWithoutBattle(
                                        attacker,
                                        attackerOwner,
                                        "ShieldToUnit redirect score gate");
                                    return;
                                }

                                selectedDefenderFromShieldPanel.SetUnitRestVisual(true);
                                Debug.Log($"[ShieldToUnit] redirect to unit battle defender:{selectedDefenderFromShieldPanel.Data.cardName}");
                                attackFlowBlockRedirectUnit = selectedDefenderFromShieldPanel;
                                TryUnitVsUnitAttack(
                                    attacker,
                                    selectedDefenderFromShieldPanel,
                                    attackerOwner,
                                    selectedDefenderOwner,
                                    false,
                                    true);
                                return;
                            }
                        }

                        TryUnitShieldAttackFromUnit(attacker, skipOnActionPause, true, true);
                    }))
            {
                return;
            }
            

            RegisterAttackFlowContextForOnAction(
                attacker,
                attackerOwner,
                AttackFlowStrikeKind.Shield,
                null,
                null);

            if (!skipOnActionPause
                && TryRunAttackActionSteps(
                    attackerOwner == PlayerType.Player ? PlayerType.Enemy : PlayerType.Player,
                    attackerOwner,
                    () => TryUnitShieldAttackFromUnit(attacker, true, true, true),
                    attacker))
            {
                return;
            }

            if (attacker.CurrentPower <= 0)
            {
                Debug.Log("[ShieldAttack] AP is 0 — cannot break shields or direct attack.");
                attacker.SetAttackFlg(AttackFlg.False);
                attacker.SetUnitRestVisual(true);
                pendingUnitAttackAttacker = null;
                pendingOnAttackEffectResolvedAttacker = null;
                ClearAttackFlowContext();
                return;
            }

            if (defender.shield <= 0)
            {
                Debug.Log($"[DirectAttack] Shield is 0. Resolving direct attack. attackPower:{attacker.CurrentPower}");
                attacker.SetAttackFlg(AttackFlg.False);
                attacker.SetUnitRestVisual(true);
                pendingUnitAttackAttacker = null;
                pendingOnAttackEffectResolvedAttacker = null;
                HandleDirectAttackWinLose(attackerOwner);
                ClearAttackFlowContext();
                return;
            }

            if (!gundamRule.TryApplyUnitShieldAttack(targetSide, attacker.CurrentPower, hadExBaseLayerAtShieldAttackStart))
            {
                Debug.Log("Cannot attack shield (no shields or invalid power for EX Base).");
                ClearAttackFlowContext();
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
            ClearTimedStatModifiersForAllInPlayCards(EffectDuration.UntilEndOfBattle);
            DumpTurnResourceUsageLogs(attackerOwner, "unit shield attack");

            SyncAllResourceViewsFromRule();
            ClearAttackFlowContext();
        }
        finally
        {
            isShieldAttackResolving = false;
            if (hadExBaseLayerAtShieldAttackStart)
            {
                blockShieldFlowDuringShieldAttack = false;
            }
        }
    }

    private void TryUnitVsUnitAttack(
        CardController attacker,
        CardController defender,
        PlayerType attackerOwner,
        PlayerType defenderOwner,
        bool skipOnActionPause = false,
        bool skipAttackedSidePanelPause = false)
    {
        if (isAttackedSidePanelOpen && !skipAttackedSidePanelPause)
        {
            return;
        }

        if (isActionThinkPauseOpen && !skipAttackedSidePanelPause)
        {
            return;
        }

        if (currentPhase != BattlePhase.MainPhase || attackerOwner != currentPlayerType)
        {
            return;
        }

        if (attacker.Data.type != Type.Unit || defender.Data.type != Type.Unit)
        {
            Debug.Log("Only units can attack each other.");
            return;
        }

        if (attacker.CurrentHp <= 0)
        {
            Debug.Log("[UnitAttack] Attacker HP is 0 — consume attack and set REST.");
            attacker.SetAttackFlg(AttackFlg.False);
            attacker.SetUnitRestVisual(true);
            pendingUnitAttackAttacker = null;
            pendingOnAttackEffectResolvedAttacker = null;
            return;
        }

        // 敵 AI のスコア中止は TryEnemyShieldAttacks およびシールド→ブロック直前のみ（バトル開始後は判定しない。中止時は REST も付けない）。

        // 攻撃対象確定後に、OnAttackの対象選択(デバフ等)を行う。
        if (pendingOnAttackEffectResolvedAttacker != attacker)
        {
            // 効果適用するためのカードを選択するUI生成
            if (TryOpenOnAttackEnemySelectionPanel(
                attacker,
                attackerOwner,
                defender,
                () => TryUnitVsUnitAttack(attacker, defender, attackerOwner, defenderOwner, skipOnActionPause, skipAttackedSidePanelPause)))
            {
                return;
            }

            pendingOnAttackEffectResolvedAttacker = attacker;
        }

        if (!skipAttackedSidePanelPause
            && TryOpenAttackedSideUnitsPanel(
                attackerOwner,
                attacker,
                selected =>
                {
                    if (selected != null)
                    {
                        PlayerType selectedDefenderOwner = ResolveCardOwner(selected.transform);
                        bool shouldRedirect = ExecuteDefenderOnAttackReaction(
                            selected,
                            attacker,
                            selectedDefenderOwner);
                        if (shouldRedirect)
                        {
                            attackFlowBlockRedirectUnit = selected;
                            selected.SetUnitRestVisual(true);
                        }
                        defender = selected;
                    }
                },
                () => TryUnitVsUnitAttack(attacker, defender, attackerOwner, defenderOwner, skipOnActionPause, true)))
        {
            return;
        }

        RegisterAttackFlowContextForOnAction(
            attacker,
            attackerOwner,
            AttackFlowStrikeKind.UnitVsUnit,
            defender,
            attackFlowBlockRedirectUnit);

        if (!skipOnActionPause
            && TryRunAttackActionSteps(
                defenderOwner,
                attackerOwner,
                () => TryUnitVsUnitAttack(attacker, defender, attackerOwner, defenderOwner, true, true),
                attacker))
        {
            return;
        }

        if (defender != null && defender.Data != null)
        {
            Debug.Log(
                $"[DefenderInfo] {defender.Data.cardName} AP:{defender.CurrentPower} HP:{defender.CurrentHp} {(defender.IsRestState ? "REST" : "ACTIVE")} owner:{defenderOwner}");
        }

        // 基本ルール: ユニットはレスト状態の相手ユニットのみ攻撃できる。
        if (!defender.IsRestState)
        {
            Debug.Log("Only REST units can be attacked.");
            ClearAttackFlowContext();
            return;
        }

        // 攻撃側は攻撃可能フラグ(True)で判定する。
        if (attacker.AttackFlgState != AttackFlg.True)
        {
            Debug.Log("This unit cannot attack.");
            ClearAttackFlowContext();
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

        int defenderHpBeforeExchange = defender.CurrentHp;
        int attackerHpBeforeExchange = attacker.CurrentHp;

        defender.ApplyDamage(attackerPowerForCombat);
        attacker.ApplyDamage(defenderPowerForCombat);
        int defenderHpAfterExchange = defender.CurrentHp;
        int attackerHpAfterExchange = attacker.CurrentHp;
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
        ClearTimedStatModifiersForAllInPlayCards(EffectDuration.UntilEndOfBattle);
        DumpTurnResourceUsageLogs(attackerOwner, "unit vs unit attack");
        SyncAllResourceViewsFromRule();
        if (attackFlowBlockRedirectUnit != null && defender == attackFlowBlockRedirectUnit)
        {
            LogUnitAttackBlockedExchangeCalc(
                attacker,
                defender,
                attackerOwner,
                attackerPowerForCombat,
                defenderPowerForCombat,
                attackerHpBeforeExchange,
                defenderHpBeforeExchange,
                attackerHpAfterExchange,
                defenderHpAfterExchange);
        }

        LogAttackPostBattleFieldCompact(attacker, attackerOwner);
        ClearAttackFlowContext();
    }

    /// <summary>ユニット対ユニット攻防の処理が終わった直後の味方・敵フィールド 1 行ずつ（<c>[AttackPostBattle]</c>）。</summary>
    private void LogAttackPostBattleFieldCompact(CardController attacker, PlayerType attackerOwner)
    {
        CardController highlight = attacker != null && attacker.gameObject != null ? attacker : null;
        System.Text.StringBuilder sb = new System.Text.StringBuilder(640);
        sb.Append("[AttackPostBattle] phase:AfterUnitVsUnitExchange attackerOwner:").Append(attackerOwner).AppendLine();
        sb.AppendLine("  === Field_AP_HP_afterUnitVsUnitBattle (攻撃中=[ユニットナウ] / ブロック中=[ブロックナウ] は場に残っている場合のみ付与) ===");
        CardController blockHighlight = attackFlowBlockRedirectUnit != null && attackFlowBlockRedirectUnit.gameObject != null
            ? attackFlowBlockRedirectUnit
            : null;
        AppendCompactSideUnitsApHpLine(sb, "味方", playerBattleZoneCards, highlight, blockHighlight);
        AppendCompactSideUnitsApHpLine(sb, "敵", enemyBattleZoneCards, highlight, blockHighlight);

        Debug.Log(sb.ToString());
    }

    /// <summary>
    /// ブロック（防御側ユニットへのリダイレクト）でユニット同士の交換が行われたとき、OnAction とは別に交換計算式を 1 本ログする。
    /// </summary>
    private void LogUnitAttackBlockedExchangeCalc(
        CardController attacker,
        CardController blockUnit,
        PlayerType attackerOwner,
        int attackerApCombat,
        int blockApCombat,
        int attackerHpBeforeExchange,
        int blockHpBeforeExchange,
        int attackerHpAfterExchange,
        int blockHpAfterExchange)
    {
        if (attacker == null || attacker.Data == null || blockUnit == null || blockUnit.Data == null)
        {
            return;
        }

        int rawBlockHpMinusAttackAp = blockHpBeforeExchange - attackerApCombat;
        int rawAttackHpMinusBlockAp = attackerHpBeforeExchange - blockApCombat;
        System.Text.StringBuilder sb = new System.Text.StringBuilder(768);
        sb.AppendLine(
            "[UnitAttackBlockedExchangeCalc] note:ブロック(リダイレクト)時のユニット同士交換。AP は OnAttack 効果適用後の戦闘値。HP は ApplyDamage 直前/直後（トラッシュ前）。OnAction コマンドログとは別。");
        sb.Append("  attackerOwner:").Append(attackerOwner).Append(" attackNow:").Append(attacker.Data.cardName).Append("(id:").Append(attacker.Data.id)
            .Append(") blockNow:").Append(blockUnit.Data.cardName).Append("(id:").Append(blockUnit.Data.id).AppendLine(")");
        sb.Append("  戦闘確定AP  attackNow:").Append(attackerApCombat).Append("  blockNow:").Append(blockApCombat).AppendLine();
        sb.Append("  交換前HP  attackNow:").Append(attackerHpBeforeExchange).Append("  blockNow:").Append(blockHpBeforeExchange).AppendLine();
        sb.Append("  ブロックナウHP-アタックナウAP: ").Append(blockHpBeforeExchange).Append('-').Append(attackerApCombat).Append('=').Append(rawBlockHpMinusAttackAp)
            .Append(" -> 交換後HP:").Append(blockHpAfterExchange).AppendLine();
        sb.Append("  アタックナウHP-ブロックナウAP: ").Append(attackerHpBeforeExchange).Append('-').Append(blockApCombat).Append('=').Append(rawAttackHpMinusBlockAp)
            .Append(" -> 交換後HP:").Append(attackerHpAfterExchange).AppendLine();
        Debug.Log(sb.ToString());
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

    private bool ExecuteDefenderOnAttackReaction(
        CardController reactionUnit,
        CardController attacker,
        PlayerType defenderOwner)
    {
        if (reactionUnit == null || reactionUnit.Data == null)
        {
            return false;
        }

        List<EffectData> effects = GetEffectsByTiming(reactionUnit.Data, EffectTiming.OnEnemyAttack);
        bool shouldRedirectToSelectedDefender = false;
        for (int i = 0; i < effects.Count; i++)
        {
            EffectData effect = effects[i];
            if (effect == null)
            {
                continue;
            }

            if (effect.type == EffectType.BlockRedirect)
            {
                shouldRedirectToSelectedDefender = true;
                continue;
            }

            bool enemyUnitTarget = effect.target == TargetType.EnemyUnit || effect.target == TargetType.EnemyAllUnits;
            if (enemyUnitTarget && attacker != null)
            {
                if (effect.target == TargetType.EnemyUnit)
                {
                    ApplyEffectToSpecificTargets(reactionUnit, defenderOwner, effect, new List<CardController> { attacker });
                }
                else
                {
                    ApplyEffect(reactionUnit, defenderOwner, effect);
                }
            }
            else
            {
                ApplyEffect(reactionUnit, defenderOwner, effect);
            }
        }

        return shouldRedirectToSelectedDefender;
    }

    private static bool HasBlockRedirectOnEnemyAttack(CardData data)
    {
        if (data == null || data.timedEffects == null)
        {
            return false;
        }

        for (int i = 0; i < data.timedEffects.Count; i++)
        {
            TimedEffectData timed = data.timedEffects[i];
            if (timed == null || timed.timing != EffectTiming.OnEnemyAttack || timed.effects == null)
            {
                continue;
            }

            for (int j = 0; j < timed.effects.Count; j++)
            {
                EffectData effect = timed.effects[j];
                if (effect != null && effect.type == EffectType.BlockRedirect)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool TryOpenAttackedSideUnitsPanel(
        PlayerType attackerOwner,
        CardController attackingUnitForDisplay,
        System.Action<CardController> onSelectDefender,
        System.Action onCloseResume)
    {
        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            return false;
        }

        if (activeAttackFlowDebugPanelRoot != null)
        {
            Destroy(activeAttackFlowDebugPanelRoot);
            activeAttackFlowDebugPanelRoot = null;
        }

        GameObject root = new GameObject("AttackFlowDebugPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        activeAttackFlowDebugPanelRoot = root;
        activeOnActionPopupRoot = root;
        isOnActionPopupOpen = true;
        isAttackedSidePanelOpen = true;
        root.transform.SetParent(canvas.transform, false);
        root.transform.SetAsLastSibling();
        root.SetFullSize();

        Image dim = root.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.5f);
        dim.raycastTarget = true;

        TextMeshProUGUI title = root.CreateChildTextCustom("AttackedSideUnitsTitle", UIAnchor.TopCenter, 720, 48);
        title.text = "アタックされる側ユニット一覧";
        title.color = Color.white;
        title.fontSize = 24;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -24f);

        GameObject scrollGo = root.CreateGridScrollView(700, 430, UIAnchor.TopCenter);
        RectTransform scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.anchoredPosition = new Vector2(0f, -84f);
        scrollGo.ConfigureGridCellFromViewportHeight(0.78f, 56f);
        ScrollRect sr = scrollGo.GetComponent<ScrollRect>();
        RectTransform content = sr != null ? sr.content : null;
        if (content == null)
        {
            Destroy(root);
            activeAttackFlowDebugPanelRoot = null;
            if (activeOnActionPopupRoot == root)
            {
                activeOnActionPopupRoot = null;
                isOnActionPopupOpen = false;
            }
            isAttackedSidePanelOpen = false;
            return false;
        }

        List<CardController> defenderUnits = GetAliveEnemyUnits(attackerOwner);
        List<CardController> reactionCandidates = new List<CardController>();
        for (int i = 0; i < defenderUnits.Count; i++)
        {
            CardController unit = defenderUnits[i];
            if (unit == null || unit.Data == null)
            {
                continue;
            }

            if (!HasEffectTiming(unit.Data, EffectTiming.OnEnemyAttack))
            {
                continue;
            }

            reactionCandidates.Add(unit);
        }

        // 「BlockRedirect かつ REST で選べない」など、実際に選択可能な候補が無い場合はパネルを出さずに進行させる。
        bool hasSelectableCandidate = false;
        for (int i = 0; i < reactionCandidates.Count; i++)
        {
            CardController unit = reactionCandidates[i];
            if (unit == null || unit.Data == null)
            {
                continue;
            }

            bool hasBlockRedirect = HasBlockRedirectOnEnemyAttack(unit.Data);
            bool cannotSelect = hasBlockRedirect && unit.IsRestState;
            if (!cannotSelect)
            {
                hasSelectableCandidate = true;
                break;
            }
        }

        bool showAttackerInfoOnly = !hasSelectableCandidate
            && attackerOwner == PlayerType.Enemy
            && attackingUnitForDisplay != null
            && attackingUnitForDisplay.Data != null;
        if (!hasSelectableCandidate && !showAttackerInfoOnly)
        {
            Destroy(root);
            activeAttackFlowDebugPanelRoot = null;
            if (activeOnActionPopupRoot == root)
            {
                activeOnActionPopupRoot = null;
                isOnActionPopupOpen = false;
            }
            isAttackedSidePanelOpen = false;
            return false;
        }

        CardController selectedDefender = null;
        if (showAttackerInfoOnly)
        {
            title.text = "敵の攻撃カード";
            GameObject cardItem = Instantiate(CardImagePrefab, content);
            CardController itemCc = cardItem.GetComponent<CardController>();
            if (itemCc != null)
            {
                itemCc.SetUp(attackingUnitForDisplay.Data, _ => { });
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
            statText.text = $"AP:{attackingUnitForDisplay.CurrentPower} HP:{attackingUnitForDisplay.CurrentHp} {(attackingUnitForDisplay.IsRestState ? "REST" : "ACTIVE")}";
            statText.fontSize = 14;
            statText.color = Color.white;
            statText.alignment = TextAlignmentOptions.Center;

            Button btn = cardItem.GetComponent<Button>();
            if (btn != null)
            {
                btn.interactable = false;
            }
        }
        else if (reactionCandidates.Count == 0)
        {
            TextMeshProUGUI empty = root.CreateChildTextCustom("AttackedSideEmpty", UIAnchor.TopCenter, 480, 40);
            empty.text = "OnEnemyAttack を持つ対象ユニットがいません";
            empty.fontSize = 20;
            empty.color = Color.white;
            empty.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -280f);
        }
        else
        {
            for (int i = 0; i < reactionCandidates.Count; i++)
            {
                CardController unit = reactionCandidates[i];
                if (unit == null || unit.Data == null)
                {
                    continue;
                }

                Debug.Log(
                    $"[AttackedSidePanelList] index:{i} card:{unit.Data.cardName} AP:{unit.CurrentPower} HP:{unit.CurrentHp} {(unit.IsRestState ? "REST" : "ACTIVE")}");

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

                if (btn != null)
                {
                    bool hasBlockRedirect = HasBlockRedirectOnEnemyAttack(unit.Data);
                    bool cannotSelect = hasBlockRedirect && unit.IsRestState;
                    if (cannotSelect)
                    {
                        btn.interactable = false;
                        statText.color = new Color(0.75f, 0.75f, 0.75f, 1f);
                    }

                    CardController clickedUnit = unit;
                    TextMeshProUGUI clickedStatText = statText;
                    btn.onClick.AddListener(() =>
                    {
                        if (cannotSelect)
                        {
                            return;
                        }

                        selectedDefender = clickedUnit;
                        title.text = $"アタックされる側ユニット一覧（選択: {clickedUnit.Data.cardName}）";
                        if (clickedStatText != null)
                        {
                            clickedStatText.color = new Color(1f, 0.22f, 0.22f, 1f);
                        }
                    });
                }
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
            if (activeAttackFlowDebugPanelRoot == root)
            {
                activeAttackFlowDebugPanelRoot = null;
            }
            if (activeOnActionPopupRoot == root)
            {
                activeOnActionPopupRoot = null;
                isOnActionPopupOpen = false;
            }
            isAttackedSidePanelOpen = false;
            Destroy(root);
            onSelectDefender?.Invoke(selectedDefender);
            onCloseResume?.Invoke();
        });

        return true;
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

    /// <summary>
    /// 手札に入ったカードの OnHandAuto を即時適用（プレイヤー操作・リソース消費なし）。
    /// </summary>
    /// <param name="skipHandZoneCheck">CardAddtoHand 直後など、手札判定を省略して適用する。</param>
    private void TriggerOnHandAutoEffects(CardController card, PlayerType ownerType, bool skipHandZoneCheck = false)
    {
        if (card == null || card.Data == null || !onHandAutoProcessing.Add(card))
        {
            return;
        }

        try
        {
            if (!skipHandZoneCheck && !IsCardInOwnerHand(card, ownerType))
            {
                Debug.LogWarning(
                    $"[OnHandAuto] skipped (not in hand) card:{card.Data.cardName}(id:{card.Data.id}) side:{ownerType}");
                return;
            }

            if (!HasEffectTiming(card.Data, EffectTiming.OnHandAuto))
            {
                return;
            }

            List<EffectData> effects = GetEffectsByTiming(card.Data, EffectTiming.OnHandAuto);
            if (effects.Count == 0)
            {
                return;
            }

            Debug.Log(
                $"[OnHandAuto] side:{ownerType} card:{card.Data.cardName}(id:{card.Data.id}) "
                + $"costBefore:{card.CurrentCost} effects:{effects.Count}");
            for (int i = 0; i < effects.Count; i++)
            {
                EffectData effect = effects[i];
                if (effect == null)
                {
                    continue;
                }

                ApplyEffectForOnHandAuto(card, ownerType, effect);
            }

            Debug.Log(
                $"[OnHandAuto] done card:{card.Data.cardName}(id:{card.Data.id}) costAfter:{card.CurrentCost}");
        }
        finally
        {
            onHandAutoProcessing.Remove(card);
        }
    }

    /// <summary>
    /// OnHandAuto 用。Self への Buff/Debuff は手札の <see cref="CardController"/> に直接付与する。
    /// </summary>
    private void ApplyEffectForOnHandAuto(CardController source, PlayerType ownerType, EffectData effect)
    {
        int magnitude = Mathf.Abs(effect.value);
        if (magnitude == 0)
        {
            return;
        }

        switch (effect.type)
        {
            case EffectType.Buff:
            case EffectType.Debuff:
                int sign = effect.type == EffectType.Buff ? 1 : -1;
                int signedValue = sign * magnitude;
                if (effect.target == TargetType.Self)
                {
                    int costBefore = source.CurrentCost;
                    int levelBefore = source.CurrentLevel;
                    ApplyStatEffect(source, signedValue, effect.statTarget, effect.duration);
                    Debug.Log(
                        $"[OnHandAuto] Self {effect.type} stat:{effect.statTarget} value:{effect.value} "
                        + $"cost:{costBefore}->{source.CurrentCost} level:{levelBefore}->{source.CurrentLevel} "
                        + $"card:{source.Data.cardName}(id:{source.Data.id})");
                    return;
                }

                break;
        }

        ApplyEffect(source, ownerType, effect);
    }

    private bool IsCardInOwnerHand(CardController card, PlayerType ownerType)
    {
        CardGameRule rule = ownerType == PlayerType.Player ? cardGameRule : enemyCardGameRule;
        return rule != null
            && rule.HandScrollContent != null
            && card.transform.IsChildOf(rule.HandScrollContent);
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

            case EffectType.BlockRedirect:
                // BlockRedirect は戦闘フロー分岐で解釈するため、ここでは何もしない。
                Debug.Log($"[Effect] BlockRedirect marker by cardId:{sourceCard.Data.id}");
                break;
        }

        SyncAllResourceViewsFromRule();
    }

    private static void ApplyStatEffect(CardController target, int signedValue, EffectStatTarget statTarget, EffectDuration duration)
    {
        int powerDelta = 0;
        int hpDelta = 0;
        int costDelta = 0;
        int levelDelta = 0;
        switch (statTarget)
        {
            case EffectStatTarget.AP:
                powerDelta = signedValue;
                break;
            case EffectStatTarget.HP:
                hpDelta = signedValue;
                break;
            case EffectStatTarget.Cost:
                costDelta = signedValue;
                break;
            case EffectStatTarget.Level:
                levelDelta = signedValue;
                break;
            default:
                powerDelta = signedValue;
                hpDelta = signedValue;
                costDelta = signedValue;
                levelDelta = signedValue;
                break;
        }
        target.AddEffectStatBonus(powerDelta, hpDelta, costDelta, levelDelta, duration);
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

    private void ClearTimedStatModifiersOnHand(PlayerType side, EffectDuration duration)
    {
        RectTransform hand = side == PlayerType.Player
            ? cardGameRule.HandScrollContent
            : enemyCardGameRule.HandScrollContent;
        if (hand == null)
        {
            return;
        }

        for (int i = 0; i < hand.childCount; i++)
        {
            CardController c = hand.GetChild(i).GetComponent<CardController>();
            if (c != null)
            {
                c.ClearTimedStatModifiersByDuration(duration);
            }
        }
    }

    private static void ClearTimedStatModifiersOnCardList(List<CardController> cards, EffectDuration duration)
    {
        if (cards == null)
        {
            return;
        }

        for (int i = 0; i < cards.Count; i++)
        {
            CardController c = cards[i];
            if (c != null)
            {
                c.ClearTimedStatModifiersByDuration(duration);
            }
        }
    }

    private void ClearTimedStatModifiersForSide(PlayerType side, EffectDuration duration)
    {
        List<CardController> zone = side == PlayerType.Player ? playerBattleZoneCards : enemyBattleZoneCards;
        ClearTimedStatModifiersOnCardList(zone, duration);
        ClearTimedStatModifiersOnHand(side, duration);
    }

    private void ClearTimedStatModifiersForAllInPlayCards(EffectDuration duration)
    {
        ClearTimedStatModifiersForSide(PlayerType.Player, duration);
        ClearTimedStatModifiersForSide(PlayerType.Enemy, duration);
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

            if (HasEffectTiming(cc.Data, EffectTiming.OnAction) && CanExecuteOnActionCardNow(ownerType, cc))
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

    /// <summary>
    /// エネミー OnAction：手札の利用可能なコマンドカードをログ出力し、一覧ポップアップを出す。候補があるとき true（Close まで攻撃フロー待機）。
    /// </summary>
    private bool TryShowEnemyOnActionCommandCandidatesPopup(
        string context,
        System.Action onStepDone,
        CardController attackingUnitInAttackFlow = null)
    {
        RectTransform hand = enemyCardGameRule != null ? enemyCardGameRule.HandScrollContent : null;
        List<CardData> commandCards = new List<CardData>();
        List<CardController> eligibleEnemyHandCommands = new List<CardController>();
        List<string> logLines = new List<string>();
        if (hand != null)
        {
            for (int i = 0; i < hand.childCount; i++)
            {
                CardController cc = hand.GetChild(i).GetComponent<CardController>();
                if (cc == null || cc.Data == null || cc.Data.type != Type.Command)
                {
                    continue;
                }

                if (!HasEffectTiming(cc.Data, EffectTiming.OnAction) || !CanExecuteOnActionCardNow(PlayerType.Enemy, cc))
                {
                    continue;
                }

                eligibleEnemyHandCommands.Add(cc);
                commandCards.Add(cc.Data);
                logLines.Add($"{cc.Data.cardName} (id:{cc.Data.id}, cost:{cc.CurrentCost}, lv:{cc.CurrentLevel})");
            }
        }

        if (commandCards.Count == 0)
        {
            Debug.Log($"[EnemyOnActionCommands] context:{context} (none)");
            return false;
        }

        Debug.Log(
            $"[EnemyOnActionCommands] context:{context} count:{commandCards.Count} → {string.Join(" | ", logLines)}");

        LogFullBoardSnapshotForCommandTiming(context, PlayerType.Enemy, attackingUnitInAttackFlow);
        LogEnemyAiOnActionHypotheticalSearchSpace(context, eligibleEnemyHandCommands, attackingUnitInAttackFlow);

        if (hand != null)
        {
            for (int vi = 0; vi < hand.childCount; vi++)
            {
                CardController cc = hand.GetChild(vi).GetComponent<CardController>();
                if (cc == null || cc.Data == null || cc.Data.type != Type.Command)
                {
                    continue;
                }

                if (!HasEffectTiming(cc.Data, EffectTiming.OnAction) || !CanExecuteOnActionCardNow(PlayerType.Enemy, cc))
                {
                    continue;
                }

                LogVirtualOnActionCommandOutcomeForPlayerUnits(cc, PlayerType.Enemy, context);
                if (attackFlowBlockRedirectUnit != null)
                {
                    LogVirtualOnActionCommandOutcomeForFocusBlockerUnit(cc, PlayerType.Enemy, attackFlowBlockRedirectUnit, context);
                }
            }
        }

        if (CardImagePrefab == null || ResolveBattleCanvas() == null)
        {
            Debug.LogWarning("[EnemyOnActionCommands] CardImagePrefab or canvas missing — skip popup.");
            onStepDone?.Invoke();
            return false;
        }

        ShowOnActionHandCandidatesPopup(PlayerType.Enemy, $"{context} [commands]", commandCards, onStepDone);
        return true;
    }


    private bool TryRunAttackBlockSteps(PlayerType defenderSide, PlayerType attackerSide, System.Action onComplete, CardController attackingUnitInAttackFlow = null)
    {
        return TryRunAttackActionSteps(defenderSide, attackerSide, onComplete, attackingUnitInAttackFlow);
    }

    /// <summary>
    /// 攻撃フロー後半：呼び出し元で「アタック宣言〜ブロック可否」まで済んだあとの OnAction 順序。
    /// 敵アクション →（テスト）actionthink → プレイヤーアクション。
    /// </summary>
    private bool TryRunAttackOnActionPhasesAfterBlock(System.Action onComplete, CardController attackingUnitInAttackFlow = null)
    {
        void runPlayerOnActionOrFinish()
        {
            if (TryHandleSingleSideOnActionStep(PlayerType.Player, "attack:player-action", onComplete, attackingUnitInAttackFlow))
            {
                return;
            }

            onComplete?.Invoke();
        }

        void afterEnemyOnAction()
        {
            if (enableAttackFlowActionThinkTest && TryOpenActionThinkTestPause(runPlayerOnActionOrFinish))
            {
                return;
            }

            runPlayerOnActionOrFinish();
        }

        if (TryHandleSingleSideOnActionStep(PlayerType.Enemy, "attack:enemy-action", afterEnemyOnAction, attackingUnitInAttackFlow))
        {
            return true;
        }

        if (enableAttackFlowActionThinkTest && TryOpenActionThinkTestPause(runPlayerOnActionOrFinish))
        {
            return true;
        }

        if (TryHandleSingleSideOnActionStep(PlayerType.Player, "attack:player-action", onComplete, attackingUnitInAttackFlow))
        {
            return true;
        }

        onComplete?.Invoke();
        return false;
    }

    /// <summary>
    /// テスト用：プレイヤー OnAction の直前。表示中は <see cref="isActionThinkPauseOpen"/> で進行停止。
    /// 戻り値 true のときコールバックは Continue 後に呼ばれる。
    /// </summary>
    private bool TryOpenActionThinkTestPause(System.Action onContinue)
    {
        if (!enableAttackFlowActionThinkTest || onContinue == null)
        {
            return false;
        }

        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            return false;
        }

        Debug.Log("actionthink");

        isActionThinkPauseOpen = true;
        GameObject root = new GameObject("ActionThinkTestPause", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        root.transform.SetParent(canvas.transform, false);
        root.transform.SetAsLastSibling();
        root.SetFullSize();
        Image dim = root.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.45f);
        dim.raycastTarget = true;

        TextMeshProUGUI title = root.CreateChildTextCustom("ActionThinkTitle", UIAnchor.TopCenter, 720, 56);
        title.text = "actionthink";
        title.color = new Color(1f, 0.95f, 0.2f, 1f);
        title.fontSize = 26;
        title.alignment = TextAlignmentOptions.Center;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -100f);

        Button cont = root.CreateChildButton("Continue");
        RectTransform crt = cont.GetComponent<RectTransform>();
        crt.sizeDelta = new Vector2(220f, 50f);
        crt.anchorMin = new Vector2(0.5f, 0.5f);
        crt.anchorMax = new Vector2(0.5f, 0.5f);
        crt.pivot = new Vector2(0.5f, 0.5f);
        crt.anchoredPosition = new Vector2(0f, -40f);

        cont.onClick.AddListener(() =>
        {
            isActionThinkPauseOpen = false;
            Destroy(root);
            onContinue.Invoke();
        });

        return true;
    }

    private bool TryRunAttackActionSteps(
        PlayerType defenderSide,
        PlayerType attackerSide,
        System.Action onComplete,
        CardController attackingUnitInAttackFlow = null)
    {
        if (isAttackedSidePanelOpen)
        {
            return true;
        }

        if (enableShieldAttackFlowDebugLog)
        {
            Debug.Log(
                $"[AttackFlow] OnAction order: enemy → actionthink? → player (defender:{defenderSide} attacker:{attackerSide})");
        }

        return TryRunAttackOnActionPhasesAfterBlock(onComplete, attackingUnitInAttackFlow);
    }

    /// <summary>プレイヤー盤面ユニットの仮想 HP/AP（本番の CardController は変更しない）。</summary>
    private sealed class VirtualPlayerUnitSnap
    {
        public CardController Controller;
        public int Slot;
        public string Name;
        public int Id;
        public int Hp;
        public int Ap;
    }

    /// <summary>味方・敵バトルゾーン両方の仮想ユニット（アタック時 OnAction の A/B 仮想ログ用）。</summary>
    private sealed class VirtualBattleUnitSnap
    {
        public CardController Controller;
        public PlayerType FieldOwner;
        public int Slot;
        public string Name;
        public int Id;
        public int Hp;
        public int Ap;
    }

    private List<VirtualBattleUnitSnap> BuildFullBattleVirtualSnapshot()
    {
        List<VirtualBattleUnitSnap> list = new List<VirtualBattleUnitSnap>();
        if (playerBattleZoneCards != null)
        {
            for (int i = 0; i < playerBattleZoneCards.Count; i++)
            {
                CardController c = playerBattleZoneCards[i];
                if (c == null || c.Data == null || c.Data.type != Type.Unit)
                {
                    continue;
                }

                list.Add(new VirtualBattleUnitSnap
                {
                    Controller = c,
                    FieldOwner = PlayerType.Player,
                    Slot = i,
                    Name = c.Data.cardName,
                    Id = c.Data.id,
                    Hp = c.CurrentHp,
                    Ap = c.CurrentPower,
                });
            }
        }

        if (enemyBattleZoneCards != null)
        {
            for (int i = 0; i < enemyBattleZoneCards.Count; i++)
            {
                CardController c = enemyBattleZoneCards[i];
                if (c == null || c.Data == null || c.Data.type != Type.Unit)
                {
                    continue;
                }

                list.Add(new VirtualBattleUnitSnap
                {
                    Controller = c,
                    FieldOwner = PlayerType.Enemy,
                    Slot = i,
                    Name = c.Data.cardName,
                    Id = c.Data.id,
                    Hp = c.CurrentHp,
                    Ap = c.CurrentPower,
                });
            }
        }

        return list;
    }

    private List<VirtualPlayerUnitSnap> BuildVirtualPlayerZoneSnapshot()
    {
        List<VirtualPlayerUnitSnap> list = new List<VirtualPlayerUnitSnap>();
        if (playerBattleZoneCards == null)
        {
            return list;
        }

        for (int i = 0; i < playerBattleZoneCards.Count; i++)
        {
            CardController c = playerBattleZoneCards[i];
            if (c == null || c.Data == null || c.Data.type != Type.Unit)
            {
                continue;
            }

            list.Add(new VirtualPlayerUnitSnap
            {
                Controller = c,
                Slot = i,
                Name = c.Data.cardName,
                Id = c.Data.id,
                Hp = c.CurrentHp,
                Ap = c.CurrentPower,
            });
        }

        return list;
    }

    private static List<VirtualPlayerUnitSnap> CloneVirtualPlayerSnaps(List<VirtualPlayerUnitSnap> source)
    {
        List<VirtualPlayerUnitSnap> dst = new List<VirtualPlayerUnitSnap>(source.Count);
        for (int i = 0; i < source.Count; i++)
        {
            VirtualPlayerUnitSnap s = source[i];
            dst.Add(new VirtualPlayerUnitSnap
            {
                Controller = s.Controller,
                Slot = s.Slot,
                Name = s.Name,
                Id = s.Id,
                Hp = s.Hp,
                Ap = s.Ap,
            });
        }

        return dst;
    }

    private static VirtualPlayerUnitSnap FindPlayerVirtualSnap(List<VirtualPlayerUnitSnap> list, CardController unit)
    {
        if (unit == null || list == null)
        {
            return null;
        }

        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Controller == unit)
            {
                return list[i];
            }
        }

        return null;
    }

    private static void ApplyVirtualStatToSnap(VirtualPlayerUnitSnap snap, int signedValue, EffectStatTarget statTarget)
    {
        switch (statTarget)
        {
            case EffectStatTarget.AP:
                snap.Ap = Mathf.Max(0, snap.Ap + signedValue);
                break;
            case EffectStatTarget.HP:
                snap.Hp = Mathf.Max(0, snap.Hp + signedValue);
                break;
            default:
                snap.Ap = Mathf.Max(0, snap.Ap + signedValue);
                snap.Hp = Mathf.Max(0, snap.Hp + signedValue);
                break;
        }
    }

    private static string FormatVirtualPlayerUnitsLine(List<VirtualPlayerUnitSnap> snaps)
    {
        if (snaps == null || snaps.Count == 0)
        {
            return "(no player units on field)";
        }

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = 0; i < snaps.Count; i++)
        {
            VirtualPlayerUnitSnap s = snaps[i];
            if (i > 0)
            {
                sb.Append("  |  ");
            }

            sb.Append('#').Append(s.Slot).Append(':').Append(s.Name).Append("(id:").Append(s.Id).Append(") AP=").Append(s.Ap).Append(" HP=").Append(s.Hp);
        }

        return sb.ToString();
    }

    private static List<VirtualBattleUnitSnap> CloneVirtualBattleSnaps(List<VirtualBattleUnitSnap> source)
    {
        if (source == null)
        {
            return new List<VirtualBattleUnitSnap>();
        }

        List<VirtualBattleUnitSnap> dst = new List<VirtualBattleUnitSnap>(source.Count);
        for (int i = 0; i < source.Count; i++)
        {
            VirtualBattleUnitSnap s = source[i];
            dst.Add(new VirtualBattleUnitSnap
            {
                Controller = s.Controller,
                FieldOwner = s.FieldOwner,
                Slot = s.Slot,
                Name = s.Name,
                Id = s.Id,
                Hp = s.Hp,
                Ap = s.Ap,
            });
        }

        return dst;
    }

    private static VirtualBattleUnitSnap FindBattleVirtualSnap(List<VirtualBattleUnitSnap> list, CardController unit)
    {
        if (unit == null || list == null)
        {
            return null;
        }

        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Controller == unit)
            {
                return list[i];
            }
        }

        return null;
    }

    private static void ApplyVirtualStatToBattleSnap(VirtualBattleUnitSnap snap, int signedValue, EffectStatTarget statTarget)
    {
        switch (statTarget)
        {
            case EffectStatTarget.AP:
                snap.Ap = Mathf.Max(0, snap.Ap + signedValue);
                break;
            case EffectStatTarget.HP:
                snap.Hp = Mathf.Max(0, snap.Hp + signedValue);
                break;
            default:
                snap.Ap = Mathf.Max(0, snap.Ap + signedValue);
                snap.Hp = Mathf.Max(0, snap.Hp + signedValue);
                break;
        }
    }

    private static void ApplyVirtualBattleEffectToTargetsOnSnaps(
        List<VirtualBattleUnitSnap> working,
        EffectData effect,
        List<CardController> targets)
    {
        if (working == null || effect == null || targets == null)
        {
            return;
        }

        int magnitude = Mathf.Abs(effect.value);
        if (magnitude == 0)
        {
            return;
        }

        if (effect.type == EffectType.Draw || effect.type == EffectType.BlockRedirect)
        {
            return;
        }

        for (int i = 0; i < targets.Count; i++)
        {
            CardController t = targets[i];
            if (t == null || t.Data == null)
            {
                continue;
            }

            VirtualBattleUnitSnap snap = FindBattleVirtualSnap(working, t);
            if (snap == null)
            {
                continue;
            }

            switch (effect.type)
            {
                case EffectType.Damage:
                    snap.Hp = Mathf.Max(0, snap.Hp - magnitude);
                    break;
                case EffectType.Buff:
                    ApplyVirtualStatToBattleSnap(snap, magnitude, effect.statTarget);
                    break;
                case EffectType.Debuff:
                    ApplyVirtualStatToBattleSnap(snap, -magnitude, effect.statTarget);
                    break;
            }
        }
    }

    private static string FormatVirtualBattleFieldLine(List<VirtualBattleUnitSnap> snaps)
    {
        if (snaps == null || snaps.Count == 0)
        {
            return "(empty)";
        }

        System.Text.StringBuilder sb = new System.Text.StringBuilder(snaps.Count * 48);
        sb.Append("味方 ");
        AppendVirtualBattleSideSlice(sb, snaps, PlayerType.Player);
        sb.Append(" | 敵 ");
        AppendVirtualBattleSideSlice(sb, snaps, PlayerType.Enemy);
        return sb.ToString();
    }

    private static void AppendVirtualBattleSideSlice(
        System.Text.StringBuilder sb,
        List<VirtualBattleUnitSnap> snaps,
        PlayerType owner)
    {
        List<int> indices = new List<int>();
        for (int i = 0; i < snaps.Count; i++)
        {
            if (snaps[i].FieldOwner == owner)
            {
                indices.Add(i);
            }
        }

        indices.Sort((a, b) => snaps[a].Slot.CompareTo(snaps[b].Slot));
        if (indices.Count == 0)
        {
            sb.Append("(none)");
            return;
        }

        for (int k = 0; k < indices.Count; k++)
        {
            if (k > 0)
            {
                sb.Append("  |  ");
            }

            VirtualBattleUnitSnap s = snaps[indices[k]];
            sb.Append('#').Append(s.Slot).Append(':').Append(s.Name).Append("(id:").Append(s.Id).Append(") AP=").Append(s.Ap).Append(" HP=").Append(s.Hp);
        }
    }

    /// <summary>仮想枝の見出し用。0→PatternA, 1→PatternB …（26 超は Pattern27 形式）。</summary>
    private static string FormatHypothesisPatternLetterLabel(int zeroBasedIndex)
    {
        if (zeroBasedIndex < 0)
        {
            zeroBasedIndex = 0;
        }

        if (zeroBasedIndex < 26)
        {
            return "Pattern" + (char)('A' + zeroBasedIndex);
        }

        return "Pattern" + (zeroBasedIndex + 1);
    }

    /// <summary>ユニットが座っているバトルゾーンのスロット番号（見つからなければ -1）。</summary>
    private int TryGetUnitBattleZoneSlotIndex(CardController unit)
    {
        if (unit == null)
        {
            return -1;
        }

        if (playerBattleZoneCards != null)
        {
            for (int i = 0; i < playerBattleZoneCards.Count; i++)
            {
                if (playerBattleZoneCards[i] == unit)
                {
                    return i;
                }
            }
        }

        if (enemyBattleZoneCards != null)
        {
            for (int i = 0; i < enemyBattleZoneCards.Count; i++)
            {
                if (enemyBattleZoneCards[i] == unit)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    /// <summary>
    /// OnAction 仮想適用後の盤面スナップ上で、攻撃中ユニット vs 宣言／ブロック防御ユニットの 1 交換を簡易計算してログする（本番は変更しない）。
    /// 計算式は <see cref="TryUnitVsUnitAttack"/> の相互 ApplyDamage に合わせ AP をダメージ量とする（OnAttack 連鎖は未再現）。
    /// </summary>
    private void LogVirtualHypotheticalBattleExchangeAfterOnActionCommand(
        List<VirtualBattleUnitSnap> snapsAfterOnActionCommand,
        CardController hypotheticalCommandTarget,
        string patternLabel,
        int pickIndex,
        CardController command,
        PlayerType commandOwnerSide,
        CardController attackingUnitInAttackFlow,
        string relatedHypotheticalLogTag)
    {
        if (snapsAfterOnActionCommand == null || hypotheticalCommandTarget?.Data == null || command?.Data == null
            || attackingUnitInAttackFlow == null || attackingUnitInAttackFlow.Data == null)
        {
            return;
        }

        if (attackFlowStrikeKind == AttackFlowStrikeKind.None)
        {
            return;
        }

        if (attackFlowStrikeKind == AttackFlowStrikeKind.Shield)
        {
            Debug.Log(
                "[OnActionHypotheticalBattleSim] relatedTag:" + relatedHypotheticalLogTag + " patternLabel:" + patternLabel + " pickIndex:"
                + pickIndex + " hypotheticalCommandPick:" + hypotheticalCommandTarget.Data.cardName + "(id:" + hypotheticalCommandTarget.Data.id
                + ") cmdId:" + command.Data.id + " note:strike:Shield — unitVsUnit exchange sim skipped.");
            return;
        }

        if (attackFlowStrikeKind != AttackFlowStrikeKind.UnitVsUnit)
        {
            return;
        }

        CardController combatDefender = attackFlowBlockRedirectUnit != null ? attackFlowBlockRedirectUnit : attackFlowDeclaredDefenderUnit;
        if (combatDefender == null || combatDefender.Data == null || combatDefender.Data.type != Type.Unit)
        {
            Debug.Log(
                "[OnActionHypotheticalBattleSim] relatedTag:" + relatedHypotheticalLogTag + " patternLabel:" + patternLabel + " pickIndex:"
                + pickIndex + " note:UnitVsUnit but no combat defender in AttackContext — exchange sim skipped.");
            return;
        }

        List<VirtualBattleUnitSnap> battle = CloneVirtualBattleSnaps(snapsAfterOnActionCommand);
        VirtualBattleUnitSnap atkSnap = FindBattleVirtualSnap(battle, attackingUnitInAttackFlow);
        VirtualBattleUnitSnap defSnap = FindBattleVirtualSnap(battle, combatDefender);
        if (atkSnap == null || defSnap == null)
        {
            Debug.Log(
                "[OnActionHypotheticalBattleSim] relatedTag:" + relatedHypotheticalLogTag + " patternLabel:" + patternLabel + " pickIndex:"
                + pickIndex + " note:attacker or defender not in virtual snapshot — exchange sim skipped.");
            return;
        }

        int atkPower = atkSnap.Ap;
        int defPower = defSnap.Ap;
        int atkHpBefore = atkSnap.Hp;
        int defHpBefore = defSnap.Hp;
        if (atkHpBefore <= 0)
        {
            Debug.Log(
                "[OnActionHypotheticalBattleSim] relatedTag:" + relatedHypotheticalLogTag + " patternLabel:" + patternLabel + " pickIndex:"
                + pickIndex + " note:virtual attacker HP<=0 after command — exchange sim skipped.");
            return;
        }

        defSnap.Hp = Mathf.Max(0, defSnap.Hp - atkPower);
        atkSnap.Hp = Mathf.Max(0, atkSnap.Hp - defPower);

        System.Text.StringBuilder sb = new System.Text.StringBuilder(900);
        sb.Append("[OnActionHypotheticalBattleSim] relatedTag:").Append(relatedHypotheticalLogTag).Append(" patternLabel:").Append(patternLabel)
            .Append(" pickIndex:").Append(pickIndex).Append(" hypotheticalCommandPick:").Append(hypotheticalCommandTarget.Data.cardName).Append("(id:")
            .Append(hypotheticalCommandTarget.Data.id).Append(") cmd:").Append(command.Data.cardName).Append("(id:").Append(command.Data.id).Append(") side:")
            .Append(commandOwnerSide).AppendLine();
        sb.AppendLine(
            "  note:Virtual 1-exchange after OnAction on field: defender.HP -= attacker.AP; attacker.HP -= defender.AP (AP from virtual snap post-command). OnAttack-timing modifiers not re-applied.");
        sb.Append("  combatAttacker:").Append(attackingUnitInAttackFlow.Data.cardName).Append("(id:").Append(attackingUnitInAttackFlow.Data.id)
            .Append(") virtualAP=").Append(atkPower).Append(" virtualHP_beforeExchange=").Append(atkHpBefore).AppendLine();
        sb.Append("  combatDefender:").Append(combatDefender.Data.cardName).Append("(id:").Append(combatDefender.Data.id).Append(") virtualAP=")
            .Append(defPower).Append(" virtualHP_beforeExchange=").Append(defHpBefore);
        if (attackFlowBlockRedirectUnit != null && combatDefender == attackFlowBlockRedirectUnit)
        {
            sb.Append(" [defenderIsBlockRedirectUnit]");
        }

        sb.AppendLine();
        sb.Append("  exchangeCalc: defenderHP ").Append(defHpBefore).Append(" - attackerAP ").Append(atkPower).Append(" -> ").Append(defSnap.Hp)
            .Append("; attackerHP ").Append(atkHpBefore).Append(" - defenderAP ").Append(defPower).Append(" -> ").Append(atkSnap.Hp).AppendLine();
        sb.Append("  virtualFieldOneLineAfterCommandAndCombat: ").Append(FormatVirtualBattleFieldLine(battle)).AppendLine();
        sb.AppendLine("  === VirtualField_AP_HP_by_side_afterCommandAndCombat (攻撃中=[ユニットナウ] / ブロック中=[ブロックナウ]) ===");
        AppendCompactVirtualSideUnitsApHpLine(sb, "味方", playerBattleZoneCards, battle, PlayerType.Player, attackingUnitInAttackFlow, attackFlowBlockRedirectUnit);
        AppendCompactVirtualSideUnitsApHpLine(sb, "敵", enemyBattleZoneCards, battle, PlayerType.Enemy, attackingUnitInAttackFlow, attackFlowBlockRedirectUnit);
        if (attackingUnitInAttackFlow != null && attackFlowBlockRedirectUnit != null)
        {
            AppendAttackBlockNowVsAttackNowCalcLines(sb, attackingUnitInAttackFlow, attackFlowBlockRedirectUnit, battle);
        }

        Debug.Log(sb.ToString());
    }

    /// <summary>
    /// OnAction で敵ユニット候補ごとに「そのユニットを選んだ場合の仮想盤面」を、本番スナップショットと同型でログする（ゲーム状態は変更しない）。
    /// </summary>
    /// <param name="logTagPrimary">先頭 1 行のタグ（敵 AI 探索用は <c>[EnemyAiOnActionHypothetical]</c> など）。</param>
    /// <param name="logTagDetail">詳細盤面ログのタグ。</param>
    /// <param name="searchRole">ログ上の発生源（AI 向けに区別）。</param>
    private void LogOnActionHypotheticalBoardForEnemyPick(
        CardController command,
        PlayerType commandOwnerSide,
        EffectData effect,
        CardController hypotheticalEnemyTarget,
        int candidateIndex,
        CardController attackingUnitInAttackFlow,
        int commandQueueIndex,
        int commandQueueCount,
        string logTagPrimary = "[OnActionHypotheticalBoard]",
        string logTagDetail = "[OnActionHypotheticalBoardDetail]",
        string searchRole = "OnActionTargetPicker")
    {
        if (command == null || command.Data == null || effect == null || hypotheticalEnemyTarget == null
            || hypotheticalEnemyTarget.Data == null)
        {
            return;
        }

        string patternLabel = FormatHypothesisPatternLetterLabel(candidateIndex);
        int targetSlot = TryGetUnitBattleZoneSlotIndex(hypotheticalEnemyTarget);

        List<VirtualBattleUnitSnap> before = BuildFullBattleVirtualSnapshot();
        List<VirtualBattleUnitSnap> after = CloneVirtualBattleSnaps(before);
        ApplyVirtualBattleEffectToTargetsOnSnaps(after, effect, new List<CardController> { hypotheticalEnemyTarget });
        LogVirtualHypotheticalBattleExchangeAfterOnActionCommand(
            after,
            hypotheticalEnemyTarget,
            patternLabel,
            candidateIndex,
            command,
            commandOwnerSide,
            attackingUnitInAttackFlow,
            logTagPrimary);
        System.Text.StringBuilder header = new System.Text.StringBuilder(512);
        header.AppendLine(
            logTagPrimary + " patternRow:" + patternLabel + " pickIndex:" + candidateIndex + " targetUnit:"
            + hypotheticalEnemyTarget.Data.cardName + "(id:" + hypotheticalEnemyTarget.Data.id + ") zoneSlotIndex:#" + targetSlot
            + " cmdId:" + command.Data.id);
        header.Append(logTagPrimary).Append(" searchRole:").Append(searchRole).Append(" patternLabel:").Append(patternLabel)
            .Append(" hypothesisPattern:PickEnemyIndex_").Append(candidateIndex).Append("_IfPick_")
            .Append(hypotheticalEnemyTarget.Data.cardName).Append("(id:").Append(hypotheticalEnemyTarget.Data.id).Append(")")
            .Append(" command:").Append(command.Data.cardName).Append("(id:").Append(command.Data.id).Append(")")
            .Append(" effect:").Append(effect.type).Append(" value:").Append(effect.value).Append(" stat:").Append(effect.statTarget)
            .Append(" side:").Append(commandOwnerSide).Append(" zoneSlotIndex:#").Append(targetSlot);
        if (commandQueueIndex >= 0 && commandQueueCount > 0)
        {
            header.Append(" queue:").Append(commandQueueIndex + 1).Append("/").Append(commandQueueCount);
        }

        header.Append(' ').Append(FormatBlockRedirectProbeInline(commandOwnerSide));
        Debug.Log(header.ToString());

        System.Text.StringBuilder sb = new System.Text.StringBuilder(4096);
        sb.Append(logTagDetail).Append(" searchRole:").Append(searchRole).Append(" patternLabel:").Append(patternLabel)
            .Append(" hypothesisPattern:PickEnemyIndex_").Append(candidateIndex).Append(" (same pick as previous line)")
            .AppendLine();
        sb.Append("  -------- ").Append(patternLabel).Append(" / target:").Append(hypotheticalEnemyTarget.Data.cardName).Append("(id:")
            .Append(hypotheticalEnemyTarget.Data.id).Append(") zoneSlotIndex:#").Append(targetSlot).Append(" --------").AppendLine();
        sb.AppendLine(
            "  note:Virtual field AP/HP below = if this command's EnemyUnit effect resolves on hypotheticalPick only (Damage/Buff/Debuff). Draw/trash/resource unchanged vs live.");
        sb.Append("  summaryOneLine:before ").Append(FormatVirtualBattleFieldLine(before)).AppendLine();
        sb.Append("  summaryOneLine:after  ").Append(FormatVirtualBattleFieldLine(after)).AppendLine();

        System.Text.StringBuilder ctx = new System.Text.StringBuilder(192);
        ctx.Append("onActionHypothetical|").Append(searchRole).Append("|").Append(patternLabel).Append("|PickEnemyIndex_").Append(candidateIndex)
            .Append("|ifPickId:").Append(hypotheticalEnemyTarget.Data.id).Append("|cmdId:").Append(command.Data.id).Append("|side:")
            .Append(commandOwnerSide);
        if (commandQueueIndex >= 0 && commandQueueCount > 0)
        {
            ctx.Append("|queue:").Append(commandQueueIndex + 1).Append('/').Append(commandQueueCount);
        }

        AppendHypotheticalOnActionBoardSnapshotLines(sb, ctx.ToString(), commandOwnerSide, attackingUnitInAttackFlow, after);
        Debug.Log(sb.ToString());
    }

    /// <summary>
    /// エネミー手札の OnAction コマンドごとに、対プレイヤー（<see cref="TargetType.EnemyUnit"/>）の仮想枝と盤面を列挙する。評価関数・後続 AI 用。
    /// </summary>
    private void LogEnemyAiOnActionHypotheticalSearchSpace(
        string flowContext,
        List<CardController> eligibleEnemyHandCommands,
        CardController attackingUnitInAttackFlow)
    {
        if (eligibleEnemyHandCommands == null || eligibleEnemyHandCommands.Count == 0)
        {
            return;
        }

        int approxBranchRows = 0;
        for (int i = 0; i < eligibleEnemyHandCommands.Count; i++)
        {
            CardController c = eligibleEnemyHandCommands[i];
            if (c?.Data == null)
            {
                continue;
            }

            List<EffectData> fx = GetEffectsByTiming(c.Data, EffectTiming.OnAction);
            EffectData enemyPick = fx.Find(e => e != null && e.target == TargetType.EnemyUnit);
            approxBranchRows += enemyPick != null ? GetAliveEnemyUnits(PlayerType.Enemy).Count : 1;
        }

        Debug.Log(
            "[EnemyAiOnActionSearch] phase:HypotheticalSpaceOpening flowContext:" + flowContext + " eligibleHandCommands:"
            + eligibleEnemyHandCommands.Count + " approxHypotheticalLogs:" + approxBranchRows
            + " tags:[EnemyAiOnActionHypothetical] one-line + [EnemyAiOnActionHypotheticalDetail] board "
            + FormatBlockRedirectProbeInline(PlayerType.Enemy));

        int nCmd = eligibleEnemyHandCommands.Count;
        for (int ci = 0; ci < nCmd; ci++)
        {
            CardController cmd = eligibleEnemyHandCommands[ci];
            if (cmd?.Data == null)
            {
                continue;
            }

            List<EffectData> onActionEffects = GetEffectsByTiming(cmd.Data, EffectTiming.OnAction);
            if (onActionEffects == null || onActionEffects.Count == 0)
            {
                Debug.Log(
                    "[EnemyAiOnActionSearch] commandSkip cmdId:" + cmd.Data.id + " reason:noOnActionEffects flowContext:" + flowContext);
                continue;
            }

            EffectData enemyTargetEffect = onActionEffects.Find(e => e != null && e.target == TargetType.EnemyUnit);
            if (enemyTargetEffect != null)
            {
                List<CardController> playerSideTargets = GetAliveEnemyUnits(PlayerType.Enemy);
                Debug.Log(
                    "[EnemyAiOnActionSearch] commandBranch source:Hand cmdIndex:" + ci + "/" + nCmd + " cmdId:" + cmd.Data.id
                    + " name:" + cmd.Data.cardName + " playerSideUnitBranches:" + playerSideTargets.Count + " cost:" + cmd.CurrentCost
                    + " flowContext:" + flowContext);
                System.Text.StringBuilder patternTable = new System.Text.StringBuilder(256);
                patternTable.Append("[EnemyAiOnActionSearch] patternTable cmdQueue:").Append(ci + 1).Append('/').Append(nCmd).Append(" cmdId:")
                    .Append(cmd.Data.id).Append(' ').Append(FormatBlockRedirectProbeInline(PlayerType.Enemy)).AppendLine();
                for (int pi = 0; pi < playerSideTargets.Count; pi++)
                {
                    CardController pt = playerSideTargets[pi];
                    if (pt?.Data == null)
                    {
                        continue;
                    }

                    int ps = TryGetUnitBattleZoneSlotIndex(pt);
                    patternTable.Append("  patternRow:").Append(FormatHypothesisPatternLetterLabel(pi)).Append(" → target:")
                        .Append(pt.Data.cardName).Append("(id:").Append(pt.Data.id).Append(") zoneSlotIndex:#").Append(ps).AppendLine();
                }

                Debug.Log(patternTable.ToString());
                for (int ti = 0; ti < playerSideTargets.Count; ti++)
                {
                    LogOnActionHypotheticalBoardForEnemyPick(
                        cmd,
                        PlayerType.Enemy,
                        enemyTargetEffect,
                        playerSideTargets[ti],
                        ti,
                        attackingUnitInAttackFlow,
                        ci,
                        Mathf.Max(1, nCmd),
                        "[EnemyAiOnActionHypothetical]",
                        "[EnemyAiOnActionHypotheticalDetail]",
                        "EnemyAiHandCommandSearch");
                }
            }
            else
            {
                Debug.Log(
                    "[EnemyAiOnActionSearch] commandBranch source:Hand cmdIndex:" + ci + "/" + nCmd + " cmdId:" + cmd.Data.id
                    + " name:" + cmd.Data.cardName + " noEnemyUnitPickerEffect flowContext:" + flowContext);
                LogEnemyAiHypotheticalDirectOnActionEffectChain(
                    cmd,
                    PlayerType.Enemy,
                    onActionEffects,
                    attackingUnitInAttackFlow,
                    ci,
                    Mathf.Max(1, nCmd),
                    flowContext);
            }
        }
    }

    /// <summary>
    /// EnemyUnit 選択を伴わない OnAction の連鎖を、仮想盤面として 1 枝ログする（Draw/BlockRedirect はスキップ）。
    /// </summary>
    private void LogEnemyAiHypotheticalDirectOnActionEffectChain(
        CardController command,
        PlayerType commandOwnerSide,
        List<EffectData> onActionEffects,
        CardController attackingUnitInAttackFlow,
        int commandIndex,
        int commandCount,
        string flowContext)
    {
        if (command?.Data == null || onActionEffects == null)
        {
            return;
        }

        List<VirtualBattleUnitSnap> before = BuildFullBattleVirtualSnapshot();
        List<VirtualBattleUnitSnap> working = CloneVirtualBattleSnaps(before);
        System.Text.StringBuilder trace = new System.Text.StringBuilder(128);
        for (int ei = 0; ei < onActionEffects.Count; ei++)
        {
            EffectData eff = onActionEffects[ei];
            if (eff == null)
            {
                continue;
            }

            if (eff.type == EffectType.Draw || eff.type == EffectType.BlockRedirect)
            {
                trace.Append('[').Append(ei).Append(':').Append(eff.type).Append(" skip] ");
                continue;
            }

            int magnitude = Mathf.Abs(eff.value);
            if (magnitude == 0)
            {
                continue;
            }

            List<CardController> targets = ResolveEffectTargets(command, commandOwnerSide, eff.target);
            if (targets == null || targets.Count == 0)
            {
                trace.Append('[').Append(ei).Append(":noTargets] ");
                continue;
            }

            ApplyVirtualBattleEffectToTargetsOnSnaps(working, eff, targets);
            trace.Append('[').Append(ei).Append(':').Append(eff.type).Append('x').Append(targets.Count).Append("] ");
        }

        System.Text.StringBuilder header = new System.Text.StringBuilder(384);
        string patternLabel = "PatternCmd_" + FormatHypothesisPatternLetterLabel(commandIndex);
        header.AppendLine(
            "[EnemyAiOnActionHypothetical] patternRow:" + patternLabel + " cmdQueueIndex:" + commandIndex + " cmdId:" + command.Data.id
            + " flowContext:" + flowContext);
        header.Append("[EnemyAiOnActionHypothetical] searchRole:EnemyAiHandCommandSearch patternLabel:").Append(patternLabel)
            .Append(" hypothesisPattern:DirectChain_cmdIdx_")
            .Append(commandIndex).Append("_cmdId_").Append(command.Data.id).Append(" command:").Append(command.Data.cardName)
            .Append(" side:").Append(commandOwnerSide).Append(" queue:").Append(commandIndex + 1).Append("/").Append(commandCount)
            .Append(" flowContext:").Append(flowContext).Append(" virtualTrace:").Append(trace.Length > 0 ? trace.ToString() : "(none)")
            .Append(' ').Append(FormatBlockRedirectProbeInline(commandOwnerSide));
        Debug.Log(header.ToString());

        System.Text.StringBuilder sb = new System.Text.StringBuilder(4096);
        sb.Append("[EnemyAiOnActionHypotheticalDetail] patternLabel:").Append(patternLabel)
            .Append(" hypothesisPattern:DirectChain_cmdIdx_").Append(commandIndex).Append(" (same branch as previous line)")
            .AppendLine();
        sb.Append("  -------- ").Append(patternLabel).Append(" / DirectOnActionEffectChain cmdId:").Append(command.Data.id).Append(" --------")
            .AppendLine();
        sb.AppendLine(
            "  note:Virtual field = sequential Damage/Buff/Debuff from ResolveEffectTargets per effect; Draw/BlockRedirect skipped. Rules unchanged vs live.");
        sb.Append("  summaryOneLine:before ").Append(FormatVirtualBattleFieldLine(before)).AppendLine();
        sb.Append("  summaryOneLine:after  ").Append(FormatVirtualBattleFieldLine(working)).AppendLine();
        System.Text.StringBuilder ctx = new System.Text.StringBuilder(192);
        ctx.Append("enemyAiOnActionHypothetical|DirectChain|").Append(patternLabel).Append("|cmdIdx_").Append(commandIndex).Append("|cmdId:")
            .Append(command.Data.id).Append("|side:").Append(commandOwnerSide).Append("|flow:").Append(flowContext);
        AppendHypotheticalOnActionBoardSnapshotLines(sb, ctx.ToString(), commandOwnerSide, attackingUnitInAttackFlow, working);
        Debug.Log(sb.ToString());
    }

    /// <summary>
    /// アタック系 OnAction でコマンドを使わず閉じたときの盤面スナップショット。
    /// </summary>
    private void LogAttackOnActionDecisionWithBoard(
        string pattern,
        string flowContext,
        PlayerType side,
        CardController attackingUnitInAttackFlow)
    {
        if (string.IsNullOrEmpty(flowContext) || !flowContext.Contains("attack"))
        {
            return;
        }

        System.Text.StringBuilder sb = new System.Text.StringBuilder(1400);
        sb.Append("[AttackOnActionDecision] pattern:").Append(pattern).Append(" flowContext:").Append(flowContext).Append(" side:")
            .Append(side).AppendLine();
        AppendBoardStateSnapshotLines(sb, "attackOnActionDecision|" + pattern + "|" + flowContext, side, attackingUnitInAttackFlow);
        Debug.Log(sb.ToString());
    }

    /// <summary>
    /// OnAction の効果を、プレイヤー盤面ユニットへの影響だけ数値シミュレーションしてログする（ゲーム状態は変更しない）。
    /// </summary>
    private void LogVirtualOnActionCommandOutcomeForPlayerUnits(CardController commandCard, PlayerType commandOwner, string contextTag)
    {
        if (commandCard == null || commandCard.Data == null)
        {
            return;
        }

        List<EffectData> effects = GetEffectsByTiming(commandCard.Data, EffectTiming.OnAction);
        if (effects == null || effects.Count == 0)
        {
            return;
        }

        List<VirtualPlayerUnitSnap> before = BuildVirtualPlayerZoneSnapshot();
        List<VirtualPlayerUnitSnap> working = CloneVirtualPlayerSnaps(before);
        System.Text.StringBuilder notes = new System.Text.StringBuilder();

        for (int ei = 0; ei < effects.Count; ei++)
        {
            EffectData effect = effects[ei];
            if (effect == null)
            {
                continue;
            }

            int magnitude = Mathf.Abs(effect.value);
            switch (effect.type)
            {
                case EffectType.BlockRedirect:
                    notes.Append("[BlockRedirect] ");
                    continue;
                case EffectType.Draw:
                    notes.Append("[Draw ").Append(effect.value).Append("] ");
                    continue;
            }

            if (magnitude == 0)
            {
                continue;
            }

            switch (effect.type)
            {
                case EffectType.Damage:
                {
                    List<CardController> dmgTargets = ResolveEffectTargets(commandCard, commandOwner, effect.target);
                    for (int ti = 0; ti < dmgTargets.Count; ti++)
                    {
                        VirtualPlayerUnitSnap snap = FindPlayerVirtualSnap(working, dmgTargets[ti]);
                        if (snap != null)
                        {
                            snap.Hp = Mathf.Max(0, snap.Hp - magnitude);
                        }
                    }

                    if (effect.target == TargetType.EnemyPlayer || effect.target == TargetType.SelfPlayer)
                    {
                        notes.Append("[AreaDamage mag=").Append(magnitude).Append(" target=").Append(effect.target).Append(" → unit HP/AP には未反映] ");
                    }

                    break;
                }
                case EffectType.Buff:
                case EffectType.Debuff:
                {
                    int sign = effect.type == EffectType.Buff ? 1 : -1;
                    int signedValue = sign * magnitude;
                    List<CardController> statTargets = ResolveEffectTargets(commandCard, commandOwner, effect.target);
                    for (int ti = 0; ti < statTargets.Count; ti++)
                    {
                        VirtualPlayerUnitSnap snap = FindPlayerVirtualSnap(working, statTargets[ti]);
                        if (snap != null)
                        {
                            ApplyVirtualStatToSnap(snap, signedValue, effect.statTarget);
                        }
                    }

                    break;
                }
            }
        }

        string beforeLine = FormatVirtualPlayerUnitsLine(before);
        string afterLine = FormatVirtualPlayerUnitsLine(working);
        Debug.Log(
            $"[VirtualOnAction→PlayerUnits] ctx:{contextTag} commandOwner:{commandOwner} card:{commandCard.Data.cardName}(id:{commandCard.Data.id})\n"
            + $"  before: {beforeLine}\n"
            + $"  after:  {afterLine}\n"
            + $"  notes: {(notes.Length > 0 ? notes.ToString() : "(none)")}");
    }

    /// <summary>
    /// ブロック／リダイレクトしたユニット 1 体にだけ、OnAction コマンドが当たった場合の仮想 HP/AP（ログのみ）。
    /// </summary>
    private void LogVirtualOnActionCommandOutcomeForFocusBlockerUnit(
        CardController commandCard,
        PlayerType commandOwner,
        CardController focusUnit,
        string contextTag)
    {
        if (commandCard == null
            || commandCard.Data == null
            || focusUnit == null
            || focusUnit.Data == null
            || focusUnit.Data.type != Type.Unit)
        {
            return;
        }

        if (string.IsNullOrEmpty(contextTag) || !contextTag.Contains("attack"))
        {
            return;
        }

        List<EffectData> effects = GetEffectsByTiming(commandCard.Data, EffectTiming.OnAction);
        if (effects == null || effects.Count == 0)
        {
            return;
        }

        List<VirtualPlayerUnitSnap> before = new List<VirtualPlayerUnitSnap>
        {
            new VirtualPlayerUnitSnap
            {
                Controller = focusUnit,
                Slot = -1,
                Name = focusUnit.Data.cardName,
                Id = focusUnit.Data.id,
                Hp = focusUnit.CurrentHp,
                Ap = focusUnit.CurrentPower,
            },
        };
        List<VirtualPlayerUnitSnap> working = CloneVirtualPlayerSnaps(before);
        System.Text.StringBuilder notes = new System.Text.StringBuilder();

        for (int ei = 0; ei < effects.Count; ei++)
        {
            EffectData effect = effects[ei];
            if (effect == null)
            {
                continue;
            }

            int magnitude = Mathf.Abs(effect.value);
            switch (effect.type)
            {
                case EffectType.BlockRedirect:
                    notes.Append("[BlockRedirect] ");
                    continue;
                case EffectType.Draw:
                    notes.Append("[Draw ").Append(effect.value).Append("] ");
                    continue;
            }

            if (magnitude == 0)
            {
                continue;
            }

            switch (effect.type)
            {
                case EffectType.Damage:
                {
                    List<CardController> dmgTargets = ResolveEffectTargets(commandCard, commandOwner, effect.target);
                    bool hitsFocus = false;
                    for (int ti = 0; ti < dmgTargets.Count; ti++)
                    {
                        if (dmgTargets[ti] == focusUnit)
                        {
                            hitsFocus = true;
                            break;
                        }
                    }

                    if (hitsFocus)
                    {
                        VirtualPlayerUnitSnap snap = FindPlayerVirtualSnap(working, focusUnit);
                        if (snap != null)
                        {
                            snap.Hp = Mathf.Max(0, snap.Hp - magnitude);
                        }
                    }
                    else if (effect.target == TargetType.EnemyPlayer || effect.target == TargetType.SelfPlayer)
                    {
                        notes.Append("[AreaDamage → blocker unit には未反映] ");
                    }

                    break;
                }
                case EffectType.Buff:
                case EffectType.Debuff:
                {
                    int sign = effect.type == EffectType.Buff ? 1 : -1;
                    int signedValue = sign * magnitude;
                    List<CardController> statTargets = ResolveEffectTargets(commandCard, commandOwner, effect.target);
                    bool hitsFocus = false;
                    for (int ti = 0; ti < statTargets.Count; ti++)
                    {
                        if (statTargets[ti] == focusUnit)
                        {
                            hitsFocus = true;
                            break;
                        }
                    }

                    if (hitsFocus)
                    {
                        VirtualPlayerUnitSnap snap = FindPlayerVirtualSnap(working, focusUnit);
                        if (snap != null)
                        {
                            ApplyVirtualStatToSnap(snap, signedValue, effect.statTarget);
                        }
                    }

                    break;
                }
            }
        }

        string beforeLine = FormatVirtualPlayerUnitsLine(before);
        string afterLine = FormatVirtualPlayerUnitsLine(working);
        Debug.Log(
            $"[VirtualOnAction→BlockerUnit] ctx:{contextTag} commandOwner:{commandOwner} card:{commandCard.Data.cardName}(id:{commandCard.Data.id}) focus:{focusUnit.Data.cardName}(id:{focusUnit.Data.id})\n"
            + $"  before: {beforeLine}\n"
            + $"  after:  {afterLine}\n"
            + $"  notes: {(notes.Length > 0 ? notes.ToString() : "(none)")}");
    }

    /// <summary>盤面（Rule / AttackContext / ゾーン / AttackingUnit）を StringBuilder に追記。先頭の [タグ] 行は呼び出し側。</summary>
    private void AppendBoardStateSnapshotLines(
        System.Text.StringBuilder sb,
        string context,
        PlayerType activeSide,
        CardController attackingUnitInAttackFlow)
    {
        sb.Append("  context:").Append(context).Append(" activeSide:").Append(activeSide)
            .Append(" battlePhase:").Append(currentPhase).Append(" currentPlayerType:").Append(currentPlayerType).AppendLine();

        Gundam2024RuleScript.PlayerState p = gundamRule.Player;
        Gundam2024RuleScript.PlayerState e = gundamRule.Enemy;
        sb.Append("  Rule_Player: level:").Append(p.level).Append(" exResource:").Append(p.exResource).Append(" TotalLevel:").Append(p.TotalLevel)
            .Append(" resource:").Append(p.resource).Append(" shield:").Append(p.shield).Append(" exBase:").Append(p.exBase)
            .Append(" handCount:").Append(p.handCount).Append(" deckCount:").Append(p.deckCount).AppendLine();
        sb.Append("  Rule_Enemy: level:").Append(e.level).Append(" exResource:").Append(e.exResource).Append(" TotalLevel:").Append(e.TotalLevel)
            .Append(" resource:").Append(e.resource).Append(" shield:").Append(e.shield).Append(" exBase:").Append(e.exBase)
            .Append(" handCount:").Append(e.handCount).Append(" deckCount:").Append(e.deckCount).AppendLine();

        AppendAttackFlowContextToSnapshot(sb, activeSide);

        sb.AppendLine("  === Field: AP/HP (味方・敵 同時) ===");
        AppendCompactSideUnitsApHpLine(sb, "味方", playerBattleZoneCards, attackingUnitInAttackFlow, attackFlowBlockRedirectUnit);
        AppendCompactSideUnitsApHpLine(sb, "敵", enemyBattleZoneCards, attackingUnitInAttackFlow, attackFlowBlockRedirectUnit);
        if (attackingUnitInAttackFlow != null && attackFlowBlockRedirectUnit != null)
        {
            AppendAttackBlockNowVsAttackNowCalcLines(sb, attackingUnitInAttackFlow, attackFlowBlockRedirectUnit, null);
        }

        AppendBattleZoneDetailSnapshotLines(sb, "詳細 PlayerBattleZone", playerBattleZoneCards, PlayerType.Player);
        AppendBattleZoneDetailSnapshotLines(sb, "詳細 EnemyBattleZone", enemyBattleZoneCards, PlayerType.Enemy);

        if (attackingUnitInAttackFlow != null && attackingUnitInAttackFlow.Data != null)
        {
            PlayerType atkOwner = ResolveCardOwner(attackingUnitInAttackFlow.transform);
            sb.Append("  AttackingUnit: ").Append(attackingUnitInAttackFlow.Data.cardName).Append("(id:").Append(attackingUnitInAttackFlow.Data.id)
                .Append(") AP=").Append(attackingUnitInAttackFlow.CurrentPower).Append(" HP=").Append(attackingUnitInAttackFlow.CurrentHp)
                .Append(" REST:").Append(attackingUnitInAttackFlow.IsRestState).Append(" AtkFlg:").Append(attackingUnitInAttackFlow.AttackFlgState)
                .Append(" owner:").Append(atkOwner).AppendLine();
        }
        else
        {
            sb.AppendLine("  AttackingUnit: (none)");
        }
    }

    /// <summary>
    /// OnAction で「敵ユニット X を選んだ場合」の仮想盤面を、<see cref="AppendBoardStateSnapshotLines"/> と同じ構成で追記（フィールド AP/HP のみ仮想値）。
    /// </summary>
    private void AppendHypotheticalOnActionBoardSnapshotLines(
        System.Text.StringBuilder sb,
        string context,
        PlayerType activeSide,
        CardController attackingUnitInAttackFlow,
        List<VirtualBattleUnitSnap> virtualSnaps)
    {
        sb.Append("  context:").Append(context).Append(" activeSide:").Append(activeSide)
            .Append(" battlePhase:").Append(currentPhase).Append(" currentPlayerType:").Append(currentPlayerType).AppendLine();

        Gundam2024RuleScript.PlayerState p = gundamRule.Player;
        Gundam2024RuleScript.PlayerState e = gundamRule.Enemy;
        sb.Append("  Rule_Player: level:").Append(p.level).Append(" exResource:").Append(p.exResource).Append(" TotalLevel:").Append(p.TotalLevel)
            .Append(" resource:").Append(p.resource).Append(" shield:").Append(p.shield).Append(" exBase:").Append(p.exBase)
            .Append(" handCount:").Append(p.handCount).Append(" deckCount:").Append(p.deckCount).AppendLine();
        sb.Append("  Rule_Enemy: level:").Append(e.level).Append(" exResource:").Append(e.exResource).Append(" TotalLevel:").Append(e.TotalLevel)
            .Append(" resource:").Append(e.resource).Append(" shield:").Append(e.shield).Append(" exBase:").Append(e.exBase)
            .Append(" handCount:").Append(e.handCount).Append(" deckCount:").Append(e.deckCount).AppendLine();

        AppendAttackFlowContextToSnapshot(sb, activeSide);

        sb.AppendLine("  === Virtual Field: if command resolves against hypothetical pick (unit AP/HP only) ===");
        AppendCompactVirtualSideUnitsApHpLine(sb, "味方", playerBattleZoneCards, virtualSnaps, PlayerType.Player, attackingUnitInAttackFlow, attackFlowBlockRedirectUnit);
        AppendCompactVirtualSideUnitsApHpLine(sb, "敵", enemyBattleZoneCards, virtualSnaps, PlayerType.Enemy, attackingUnitInAttackFlow, attackFlowBlockRedirectUnit);
        if (attackingUnitInAttackFlow != null && attackFlowBlockRedirectUnit != null)
        {
            AppendAttackBlockNowVsAttackNowCalcLines(sb, attackingUnitInAttackFlow, attackFlowBlockRedirectUnit, virtualSnaps);
        }

        AppendVirtualBattleZoneDetailSnapshotLines(sb, "詳細 PlayerBattleZone", playerBattleZoneCards, virtualSnaps, PlayerType.Player);
        AppendVirtualBattleZoneDetailSnapshotLines(sb, "詳細 EnemyBattleZone", enemyBattleZoneCards, virtualSnaps, PlayerType.Enemy);

        if (attackingUnitInAttackFlow != null && attackingUnitInAttackFlow.Data != null)
        {
            int ap = attackingUnitInAttackFlow.CurrentPower;
            int hp = attackingUnitInAttackFlow.CurrentHp;
            VirtualBattleUnitSnap vs = FindBattleVirtualSnap(virtualSnaps, attackingUnitInAttackFlow);
            if (vs != null)
            {
                ap = vs.Ap;
                hp = vs.Hp;
            }

            PlayerType atkOwner = ResolveCardOwner(attackingUnitInAttackFlow.transform);
            sb.Append("  AttackingUnit: ").Append(attackingUnitInAttackFlow.Data.cardName).Append("(id:").Append(attackingUnitInAttackFlow.Data.id)
                .Append(") AP=").Append(ap).Append(" HP=").Append(hp).Append(" REST:").Append(attackingUnitInAttackFlow.IsRestState)
                .Append(" AtkFlg:").Append(attackingUnitInAttackFlow.AttackFlgState).Append(" owner:").Append(atkOwner).AppendLine();
        }
        else
        {
            sb.AppendLine("  AttackingUnit: (none)");
        }
    }

    private void AppendAttackBlockNowVsAttackNowCalcLines(
        System.Text.StringBuilder sb,
        CardController attackNow,
        CardController blockNow,
        List<VirtualBattleUnitSnap> virtualSnapsOrNull)
    {
        if (attackNow == null || attackNow.gameObject == null || attackNow.Data == null
            || blockNow == null || blockNow.gameObject == null || blockNow.Data == null)
        {
            return;
        }

        if (attackNow.Data.type != Type.Unit || blockNow.Data.type != Type.Unit)
        {
            return;
        }

        int atkAp;
        int atkHp;
        int blkAp;
        int blkHp;
        if (virtualSnapsOrNull != null)
        {
            VirtualBattleUnitSnap a = FindBattleVirtualSnap(virtualSnapsOrNull, attackNow);
            VirtualBattleUnitSnap b = FindBattleVirtualSnap(virtualSnapsOrNull, blockNow);
            if (a == null || b == null)
            {
                return;
            }

            atkAp = a.Ap;
            atkHp = a.Hp;
            blkAp = b.Ap;
            blkHp = b.Hp;
        }
        else
        {
            atkAp = attackNow.CurrentPower;
            atkHp = attackNow.CurrentHp;
            blkAp = blockNow.CurrentPower;
            blkHp = blockNow.CurrentHp;
        }

        int blockHpMinusAttackAp = blkHp - atkAp;
        int attackHpMinusBlockAp = atkHp - blkAp;
        sb.AppendLine("  [BlockNowVsAttackNow] ブロックナウHP-アタックナウAP: " + blkHp + "-" + atkAp + "=" + blockHpMinusAttackAp);
        sb.AppendLine("  [BlockNowVsAttackNow] アタックナウHP-ブロックナウAP: " + atkHp + "-" + blkAp + "=" + attackHpMinusBlockAp);
    }

    private static void AppendCompactVirtualSideUnitsApHpLine(
        System.Text.StringBuilder sb,
        string sideLabel,
        List<CardController> zone,
        List<VirtualBattleUnitSnap> virtualSnaps,
        PlayerType fieldOwner,
        CardController attackHighlightUnit = null,
        CardController blockHighlightUnit = null)
    {
        sb.Append("  [").Append(sideLabel).Append("] ");
        if (zone == null || zone.Count == 0)
        {
            sb.AppendLine("(empty)");
            return;
        }

        bool any = false;
        for (int i = 0; i < zone.Count; i++)
        {
            CardController c = zone[i];
            if (c == null || c.Data == null)
            {
                continue;
            }

            if (any)
            {
                sb.Append("  |  ");
            }

            any = true;
            sb.Append('#').Append(i).Append(':');
            if (attackHighlightUnit != null && c == attackHighlightUnit && c.Data.type == Type.Unit)
            {
                sb.Append("[ユニットナウ]");
            }

            if (blockHighlightUnit != null && c == blockHighlightUnit && c.Data.type == Type.Unit)
            {
                sb.Append("[ブロックナウ]");
            }

            int ap = c.CurrentPower;
            int hp = c.CurrentHp;
            if (c.Data.type == Type.Unit && virtualSnaps != null)
            {
                VirtualBattleUnitSnap s = FindBattleVirtualSnap(virtualSnaps, c);
                if (s != null && s.FieldOwner == fieldOwner)
                {
                    ap = s.Ap;
                    hp = s.Hp;
                }
            }

            sb.Append(c.Data.cardName).Append(" AP=").Append(ap).Append(" HP=").Append(hp);
        }

        if (!any)
        {
            sb.Append("(empty)");
        }

        sb.AppendLine();
    }

    private static void AppendVirtualBattleZoneDetailSnapshotLines(
        System.Text.StringBuilder sb,
        string zoneLabel,
        List<CardController> zone,
        List<VirtualBattleUnitSnap> virtualSnaps,
        PlayerType fieldOwner)
    {
        sb.Append("  ").Append(zoneLabel).AppendLine(":");
        if (zone == null || zone.Count == 0)
        {
            sb.AppendLine("    (empty)");
            return;
        }

        for (int i = 0; i < zone.Count; i++)
        {
            CardController c = zone[i];
            if (c == null || c.Data == null)
            {
                sb.Append("    [").Append(i).AppendLine("] (null)");
                continue;
            }

            int ap = c.CurrentPower;
            int hp = c.CurrentHp;
            if (c.Data.type == Type.Unit && virtualSnaps != null)
            {
                VirtualBattleUnitSnap s = FindBattleVirtualSnap(virtualSnaps, c);
                if (s != null && s.FieldOwner == fieldOwner)
                {
                    ap = s.Ap;
                    hp = s.Hp;
                }
            }

            sb.Append("    [").Append(i).Append("] ").Append(c.Data.type).Append(' ').Append(c.Data.cardName).Append("(id:").Append(c.Data.id)
                .Append(") AP=").Append(ap).Append(" HP=").Append(hp).Append(" REST:").Append(c.IsRestState).Append(" AtkFlg:")
                .Append(c.AttackFlgState).Append(" zoneOwner:").Append(fieldOwner).AppendLine();
        }
    }

    /// <summary>OnAction コマンド実行後など、パターン付きで盤面スナップショットを 1 本のログに残す。</summary>
    /// <param name="onActionResolvedUnitTargetsAfterApplyOrNull">
    /// 適用後に参照が残るユニット（敵単体選択の対象や ResolveEffectTargets の結果）。ブロック文脈の評価ログ用。
    /// </param>
    private void LogCommandUseResultWithBoard(
        string pattern,
        PlayerType ownerSide,
        CardController command,
        CardController attackingUnitInAttackFlow,
        int queueIndex,
        int queueCount,
        string detail,
        List<CardController> onActionResolvedUnitTargetsAfterApplyOrNull = null)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder(1200);
        sb.Append("[CommandResult] pattern:").Append(pattern);
        if (command != null && command.Data != null)
        {
            sb.Append(" card:").Append(command.Data.cardName).Append("(id:").Append(command.Data.id).Append(')');
        }

        sb.Append(" ownerSide:").Append(ownerSide);
        if (queueIndex >= 0 && queueCount > 0)
        {
            sb.Append(" queue:").Append(queueIndex + 1).Append('/').Append(queueCount);
        }

        if (!string.IsNullOrEmpty(detail))
        {
            sb.Append(" | ").Append(detail);
        }

        sb.AppendLine();
        string boardCtx = "commandResult|" + pattern + "|side:" + ownerSide;
        AppendBoardStateSnapshotLines(sb, boardCtx, ownerSide, attackingUnitInAttackFlow);
        if (ShouldAppendMutualUnitsApHpAfterBlockedOnActionCommand(pattern))
        {
            AppendMutualZoneUnitsApHpAfterBlockedOnActionCommand(sb);
        }

        Debug.Log(sb.ToString());
        LogEvalBlockContextPostCommandBattleBoardIfApplicable(
            pattern,
            ownerSide,
            attackingUnitInAttackFlow,
            onActionResolvedUnitTargetsAfterApplyOrNull);
    }

    /// <summary>
    /// ユニット・ブロック中に OnAction が成功したあとの盤面を、評価関数向けに <c>[EvalBlockContextPostCommand]</c> で別ログする。
    /// </summary>
    private void LogEvalBlockContextPostCommandBattleBoardIfApplicable(
        string pattern,
        PlayerType commandOwnerSide,
        CardController attackingUnitInAttackFlow,
        List<CardController> onActionResolvedUnitTargetsAfterApplyOrNull)
    {
        if (!ShouldAppendMutualUnitsApHpAfterBlockedOnActionCommand(pattern))
        {
            return;
        }

        if (attackFlowStrikeKind == AttackFlowStrikeKind.None
            || attackFlowBlockRedirectUnit == null
            || attackFlowBlockRedirectUnit.Data == null)
        {
            return;
        }

        System.Text.StringBuilder sb = new System.Text.StringBuilder(4096);
        sb.Append("[EvalBlockContextPostCommand] pattern:").Append(pattern).Append(" commandOwnerSide:").Append(commandOwnerSide).Append(' ')
            .Append(FormatBlockRedirectProbeInline(commandOwnerSide)).AppendLine();
        sb.AppendLine(
            "  note:blockedAttackFlow + OnAction command applied; full board below is state AFTER command (for eval / value function).");

        CardController br = attackFlowBlockRedirectUnit;
        PlayerType bro = ResolveCardOwner(br.transform);
        sb.Append("  blockRedirectTargetAfterCommand: ").Append(br.Data.cardName).Append("(id:").Append(br.Data.id).Append(") AP=").Append(br.CurrentPower)
            .Append(" HP=").Append(br.CurrentHp).Append(" REST:").Append(br.IsRestState).Append(" owner:").Append(bro).Append(" zoneSlotIndex:#")
            .Append(TryGetUnitBattleZoneSlotIndex(br)).AppendLine();

        if (onActionResolvedUnitTargetsAfterApplyOrNull != null && onActionResolvedUnitTargetsAfterApplyOrNull.Count > 0)
        {
            sb.AppendLine("  onActionEffectUnitTargetsAfterCommand:");
            for (int i = 0; i < onActionResolvedUnitTargetsAfterApplyOrNull.Count; i++)
            {
                CardController t = onActionResolvedUnitTargetsAfterApplyOrNull[i];
                if (t == null || t.Data == null || t.Data.type != Type.Unit)
                {
                    continue;
                }

                PlayerType to = ResolveCardOwner(t.transform);
                bool sameAsBlock = t == attackFlowBlockRedirectUnit;
                sb.Append("    - ").Append(t.Data.cardName).Append("(id:").Append(t.Data.id).Append(") AP=").Append(t.CurrentPower).Append(" HP=")
                    .Append(t.CurrentHp).Append(" owner:").Append(to).Append(" zoneSlotIndex:#").Append(TryGetUnitBattleZoneSlotIndex(t));
                if (sameAsBlock)
                {
                    sb.Append(" [sameAsBlockRedirectTarget]");
                }

                sb.AppendLine();
            }
        }

        sb.AppendLine("  === fullBoardAfterCommand (eval snapshot) ===");
        AppendBoardStateSnapshotLines(
            sb,
            "evalBlockContextPostCommand|" + pattern + "|side:" + commandOwnerSide,
            commandOwnerSide,
            attackingUnitInAttackFlow);
        sb.AppendLine("  === allFieldUnitsAfterCommand (units only) ===");
        sb.Append("  PlayerUnits: ");
        AppendBattleZoneUnitApHpInline(sb, playerBattleZoneCards);
        sb.AppendLine();
        sb.Append("  EnemyUnits: ");
        AppendBattleZoneUnitApHpInline(sb, enemyBattleZoneCards);
        sb.AppendLine();
        Debug.Log(sb.ToString());
    }

    private static List<CardController> BuildOnActionUnitTargetListAfterApply(List<CardController> resolvedBeforeApply)
    {
        if (resolvedBeforeApply == null || resolvedBeforeApply.Count == 0)
        {
            return null;
        }

        List<CardController> list = new List<CardController>();
        for (int i = 0; i < resolvedBeforeApply.Count; i++)
        {
            CardController c = resolvedBeforeApply[i];
            if (c == null || c.Data == null || c.Data.type != Type.Unit)
            {
                continue;
            }

            list.Add(c);
        }

        return list.Count > 0 ? list : null;
    }

    private static bool ShouldAppendMutualUnitsApHpAfterBlockedOnActionCommand(string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return false;
        }

        return pattern == "OnAction_AfterApplyEnemyUnitTarget" || pattern == "OnAction_AfterApplyDirectEffect";
    }

    /// <summary>
    /// ユニット・ブロック／リダイレクトが有効な攻撃フローで OnAction コマンドを適用した直後、プレイヤー／エネミー双方のフィールド・ユニット AP/HP を 1 本にまとめて追記。
    /// </summary>
    private void AppendMutualZoneUnitsApHpAfterBlockedOnActionCommand(System.Text.StringBuilder sb)
    {
        if (attackFlowStrikeKind == AttackFlowStrikeKind.None
            || attackFlowBlockRedirectUnit == null
            || attackFlowBlockRedirectUnit.Data == null)
        {
            return;
        }

        sb.AppendLine("  === AfterBlock+OnActionCommand: mutual field units AP/HP (Player vs Enemy) ===");
        sb.Append("  PlayerUnits: ");
        AppendBattleZoneUnitApHpInline(sb, playerBattleZoneCards);
        sb.AppendLine();
        sb.Append("  EnemyUnits: ");
        AppendBattleZoneUnitApHpInline(sb, enemyBattleZoneCards);
        sb.AppendLine();

        CardController br = attackFlowBlockRedirectUnit;
        PlayerType bo = ResolveCardOwner(br.transform);
        int brSlot = TryGetUnitBattleZoneSlotIndex(br);
        sb.Append("  BlockRedirectUnit: ").Append(br.Data.cardName).Append("(id:").Append(br.Data.id).Append(") AP=").Append(br.CurrentPower)
            .Append(" HP=").Append(br.CurrentHp).Append(" REST:").Append(br.IsRestState).Append(" owner:").Append(bo).Append(" zoneSlotIndex:#")
            .Append(brSlot).AppendLine();
    }

    private static void AppendBattleZoneUnitApHpInline(System.Text.StringBuilder sb, List<CardController> zone)
    {
        if (zone == null || zone.Count == 0)
        {
            sb.Append("(empty)");
            return;
        }

        bool any = false;
        for (int i = 0; i < zone.Count; i++)
        {
            CardController c = zone[i];
            if (c == null || c.Data == null || c.Data.type != Type.Unit)
            {
                continue;
            }

            if (any)
            {
                sb.Append("  |  ");
            }

            any = true;
            sb.Append('#').Append(i).Append(':').Append(c.Data.cardName).Append("(id:").Append(c.Data.id).Append(") AP=").Append(c.CurrentPower)
                .Append(" HP=").Append(c.CurrentHp);
        }

        if (!any)
        {
            sb.Append("(no units)");
        }
    }

    private static void AppendBattleZoneUnitHpOnlyInline(System.Text.StringBuilder sb, List<CardController> zone)
    {
        if (zone == null || zone.Count == 0)
        {
            sb.Append("(empty)");
            return;
        }

        bool any = false;
        for (int i = 0; i < zone.Count; i++)
        {
            CardController c = zone[i];
            if (c == null || c.Data == null || c.Data.type != Type.Unit)
            {
                continue;
            }

            if (any)
            {
                sb.Append("  |  ");
            }

            any = true;
            sb.Append('#').Append(i).Append(':').Append(c.Data.cardName).Append("(id:").Append(c.Data.id).Append(") HP=").Append(c.CurrentHp);
        }

        if (!any)
        {
            sb.Append("(no units)");
        }
    }

    private struct UnitStatSnapForCommandLog
    {
        public int Id;
        public string Name;
        public PlayerType Owner;
        public int Slot;
        public int Ap;
        public int Hp;
    }

    private List<UnitStatSnapForCommandLog> SnapUnitStatsForOnActionCommandLog(List<CardController> targets)
    {
        if (targets == null || targets.Count == 0)
        {
            return null;
        }

        List<UnitStatSnapForCommandLog> list = new List<UnitStatSnapForCommandLog>();
        for (int i = 0; i < targets.Count; i++)
        {
            CardController c = targets[i];
            if (c == null || c.Data == null || c.Data.type != Type.Unit)
            {
                continue;
            }

            list.Add(new UnitStatSnapForCommandLog
            {
                Id = c.Data.id,
                Name = c.Data.cardName,
                Owner = ResolveCardOwner(c.transform),
                Slot = TryGetUnitBattleZoneSlotIndex(c),
                Ap = c.CurrentPower,
                Hp = c.CurrentHp,
            });
        }

        return list.Count > 0 ? list : null;
    }

    private CardController FindBattleZoneUnitByCardId(int cardId)
    {
        if (playerBattleZoneCards != null)
        {
            for (int i = 0; i < playerBattleZoneCards.Count; i++)
            {
                CardController c = playerBattleZoneCards[i];
                if (c != null && c.Data != null && c.Data.type == Type.Unit && c.Data.id == cardId)
                {
                    return c;
                }
            }
        }

        if (enemyBattleZoneCards != null)
        {
            for (int i = 0; i < enemyBattleZoneCards.Count; i++)
            {
                CardController c = enemyBattleZoneCards[i];
                if (c != null && c.Data != null && c.Data.type == Type.Unit && c.Data.id == cardId)
                {
                    return c;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// OnAction コマンドをユニットに適用した直後: 対象の適用前→適用後 AP/HP と、フィールド上の味方・敵ユニットの AP/HP（および HP のみ要約）。
    /// </summary>
    private void LogOnActionCommandAppliedToUnitsBattleOutcome(
        CardController command,
        PlayerType commandOwner,
        EffectData effect,
        string patternTag,
        List<UnitStatSnapForCommandLog> beforeSnaps)
    {
        if (command == null || command.Data == null)
        {
            return;
        }

        System.Text.StringBuilder sb = new System.Text.StringBuilder(1400);
        sb.Append("[OnActionCommandAppliedToUnits] patternTag:").Append(patternTag).Append(" commandOwner:").Append(commandOwner)
            .Append(" cmd:").Append(command.Data.cardName).Append("(id:").Append(command.Data.id).Append(')');
        if (effect != null)
        {
            sb.Append(" effect:").Append(effect.type).Append(" target:").Append(effect.target).Append(" value:").Append(effect.value);
        }

        sb.AppendLine();

        if (beforeSnaps != null && beforeSnaps.Count > 0)
        {
            sb.AppendLine("  affectedUnits before->after (AP/HP; missing from field = likely trashed):");
            for (int i = 0; i < beforeSnaps.Count; i++)
            {
                UnitStatSnapForCommandLog b = beforeSnaps[i];
                CardController aft = FindBattleZoneUnitByCardId(b.Id);
                sb.Append("    ").Append(b.Name).Append("(id:").Append(b.Id).Append(") owner:").Append(b.Owner).Append(" slotBefore:#").Append(b.Slot)
                    .Append(" before:AP=").Append(b.Ap).Append(" HP=").Append(b.Hp).Append(" -> ");
                if (aft == null || aft.Data == null || aft.Data.type != Type.Unit)
                {
                    sb.AppendLine("after:(not on field — trashed or invalid)");
                }
                else
                {
                    sb.Append("after:AP=").Append(aft.CurrentPower).Append(" HP=").Append(aft.CurrentHp).Append(" slotAfter:#")
                        .Append(TryGetUnitBattleZoneSlotIndex(aft)).AppendLine();
                }
            }
        }
        else
        {
            sb.AppendLine("  affectedUnits: (no unit targets in snapshot — e.g. Draw / non-unit target)");
        }

        sb.Append("  afterApply_allFieldUnits_Player_AP_HP: ");
        AppendBattleZoneUnitApHpInline(sb, playerBattleZoneCards);
        sb.AppendLine();
        sb.Append("  afterApply_allFieldUnits_Enemy_AP_HP: ");
        AppendBattleZoneUnitApHpInline(sb, enemyBattleZoneCards);
        sb.AppendLine();
        sb.Append("  afterApply_playerUnits_hpOnly: ");
        AppendBattleZoneUnitHpOnlyInline(sb, playerBattleZoneCards);
        sb.AppendLine();
        sb.Append("  afterApply_enemyUnits_hpOnly: ");
        AppendBattleZoneUnitHpOnlyInline(sb, enemyBattleZoneCards);
        Debug.Log(sb.ToString());
    }

    /// <summary>OnAction コマンド系 UI を出す直前の盤面スナップショット。アタック中に渡されたユニットも 1 行で出す。</summary>
    private void LogFullBoardSnapshotForCommandTiming(string context, PlayerType activeSide, CardController attackingUnitInAttackFlow)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder(768);
        sb.AppendLine("[BoardSnapshot]");
        AppendBoardStateSnapshotLines(sb, context, activeSide, attackingUnitInAttackFlow);
        Debug.Log(sb.ToString());
    }

    /// <summary>味方・敵それぞれ 1 行に、ゾーン内カードの AP/HP を並べて出す（同時比較用）。攻撃中に <c>[ユニットナウ]</c>、ブロック誘導ユニットに <c>[ブロックナウ]</c> を付与。</summary>
    private static void AppendCompactSideUnitsApHpLine(
        System.Text.StringBuilder sb,
        string sideLabel,
        List<CardController> zone,
        CardController attackHighlightUnit = null,
        CardController blockHighlightUnit = null)
    {
        sb.Append("  [").Append(sideLabel).Append("] ");
        if (zone == null || zone.Count == 0)
        {
            sb.AppendLine("(empty)");
            return;
        }

        bool any = false;
        for (int i = 0; i < zone.Count; i++)
        {
            CardController c = zone[i];
            if (c == null || c.Data == null)
            {
                continue;
            }

            if (any)
            {
                sb.Append("  |  ");
            }

            any = true;
            sb.Append('#').Append(i).Append(':');
            if (attackHighlightUnit != null && c == attackHighlightUnit && c.Data.type == Type.Unit)
            {
                sb.Append("[ユニットナウ]");
            }

            if (blockHighlightUnit != null && c == blockHighlightUnit && c.Data.type == Type.Unit)
            {
                sb.Append("[ブロックナウ]");
            }

            sb.Append(c.Data.cardName)
                .Append(" AP=").Append(c.CurrentPower).Append(" HP=").Append(c.CurrentHp);
        }

        if (!any)
        {
            sb.Append("(empty)");
        }

        sb.AppendLine();
    }

    private static void AppendBattleZoneDetailSnapshotLines(
        System.Text.StringBuilder sb,
        string zoneLabel,
        List<CardController> zone,
        PlayerType zoneOwner)
    {
        sb.Append("  ").Append(zoneLabel).AppendLine(":");
        if (zone == null || zone.Count == 0)
        {
            sb.AppendLine("    (empty)");
            return;
        }

        for (int i = 0; i < zone.Count; i++)
        {
            CardController c = zone[i];
            if (c == null || c.Data == null)
            {
                sb.Append("    [").Append(i).AppendLine("] (null)");
                continue;
            }

            sb.Append("    [").Append(i).Append("] ").Append(c.Data.type).Append(' ').Append(c.Data.cardName).Append("(id:").Append(c.Data.id)
                .Append(") AP=").Append(c.CurrentPower).Append(" HP=").Append(c.CurrentHp).Append(" REST:").Append(c.IsRestState)
                .Append(" AtkFlg:").Append(c.AttackFlgState).Append(" zoneOwner:").Append(zoneOwner).AppendLine();
        }
    }

    private bool TryHandleSingleSideOnActionStep(
        PlayerType side,
        string context,
        System.Action onStepDone,
        CardController attackingUnitInAttackFlow = null)
    {
        if (side == PlayerType.Enemy)
        {
            return TryShowEnemyOnActionCommandCandidatesPopup(context, onStepDone, attackingUnitInAttackFlow);
        }

        return TryOpenOnActionCommandSelection(side, context, onStepDone, attackingUnitInAttackFlow);
    }

    // アクションステップ時に利用できるコマンドカードを一覧にUI表示するメソッド
    private bool TryOpenOnActionCommandSelection(
        PlayerType side,
        string context,
        System.Action onStepDone,
        CardController attackingUnitInAttackFlow = null)
    {
        if (CardImagePrefab == null)
        {
            return false;
        }

        List<CardController> ownFieldUnitsWithOnAction = new List<CardController>();
        List<CardController> ownBattleZone = side == PlayerType.Player ? playerBattleZoneCards : enemyBattleZoneCards;
        if (ownBattleZone != null)
        {
            for (int fi = 0; fi < ownBattleZone.Count; fi++)
            {
                CardController uc = ownBattleZone[fi];
                if (uc == null || uc.Data == null || uc.Data.type != Type.Unit)
                {
                    continue;
                }

                if (uc.CurrentHp <= 0)
                {
                    continue;
                }

                if (!HasEffectTiming(uc.Data, EffectTiming.OnAction) || !CanExecuteOnActionCardNow(side, uc))
                {
                    continue;
                }

                ownFieldUnitsWithOnAction.Add(uc);
            }
        }

        RectTransform hand = side == PlayerType.Player ? cardGameRule.HandScrollContent : enemyCardGameRule.HandScrollContent;
        List<CardController> commandCards = new List<CardController>();
        if (hand != null)
        {
            for (int i = 0; i < hand.childCount; i++)
            {
                CardController cc = hand.GetChild(i).GetComponent<CardController>();
                if (cc == null || cc.Data == null || cc.Data.type != Type.Command)
                {
                    continue;
                }

                if (!HasEffectTiming(cc.Data, EffectTiming.OnAction) || !CanExecuteOnActionCardNow(side, cc))
                {
                    continue;
                }

                commandCards.Add(cc);
            }
        }

        List<CardController> onActionSelectableSources = new List<CardController>();
        onActionSelectableSources.AddRange(ownFieldUnitsWithOnAction);
        onActionSelectableSources.AddRange(commandCards);

        if (onActionSelectableSources.Count == 0)
        {
            Debug.Log($"[OnActionCandidates] context:{context} side:{side} none");
            return false;
        }

        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            return false;
        }

        LogFullBoardSnapshotForCommandTiming(context, side, attackingUnitInAttackFlow);

        for (int vci = 0; vci < commandCards.Count; vci++)
        {
            LogVirtualOnActionCommandOutcomeForPlayerUnits(commandCards[vci], side, context);
            if (attackFlowBlockRedirectUnit != null)
            {
                LogVirtualOnActionCommandOutcomeForFocusBlockerUnit(commandCards[vci], side, attackFlowBlockRedirectUnit, context);
            }
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

        bool showAttackHighlight = attackingUnitInAttackFlow != null
            && attackingUnitInAttackFlow.Data != null
            && !string.IsNullOrEmpty(context)
            && context.Contains("attack");

        GameObject scrollGo = root.CreateGridScrollView(680, 410, UIAnchor.TopCenter);
        RectTransform scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.anchoredPosition = new Vector2(0f, showAttackHighlight ? -98f : -86f);
        scrollGo.ConfigureGridCellFromViewportHeight(0.78f, 56f);
        ScrollRect sr = scrollGo.GetComponent<ScrollRect>();
        RectTransform content = sr != null ? sr.content : null;

        List<CardController> selectedCommands = new List<CardController>();
        for (int i = 0; i < onActionSelectableSources.Count; i++)
        {
            CardController command = onActionSelectableSources[i];
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

            Image baseImage = go.GetComponent<Image>();
            Color originalColor = baseImage != null ? baseImage.color : Color.white;
            CardController selectedCommand = command;
            btn.onClick.AddListener(() =>
            {
                if (selectedCommands.Contains(selectedCommand))
                {
                    selectedCommands.Remove(selectedCommand);
                    if (baseImage != null)
                    {
                        baseImage.color = originalColor;
                    }
                }
                else
                {
                    selectedCommands.Add(selectedCommand);
                    if (baseImage != null)
                    {
                        baseImage.color = new Color(0.7f, 1f, 0.7f, 1f);
                    }
                }
            });
        }

        Button confirmBtn = root.CreateChildButton("Confirm");
        RectTransform confirmRt = confirmBtn.GetComponent<RectTransform>();
        confirmRt.sizeDelta = new Vector2(180f, 48f);
        confirmRt.anchorMin = new Vector2(0.5f, 0f);
        confirmRt.anchorMax = new Vector2(0.5f, 0f);
        confirmRt.pivot = new Vector2(0.5f, 0f);
        confirmRt.anchoredPosition = new Vector2(-100f, 36f);
        confirmBtn.onClick.AddListener(() =>
        {
            if (selectedCommands.Count == 0)
            {
                Debug.Log("OnAction: カードを1枚以上選択してください。");
                return;
            }

            bool isAttackContext = showAttackHighlight;
            ExecuteOnActionCommandQueue(
                side,
                selectedCommands,
                0,
                () =>
                {
                    isOnActionPopupOpen = false;
                    activeOnActionPopupRoot = null;
                    Destroy(root);
                    onStepDone?.Invoke();
                },
                attackingUnitInAttackFlow);
            });

        Button closeBtn = root.CreateChildButton("Close");
        RectTransform closeRt = closeBtn.GetComponent<RectTransform>();
        closeRt.sizeDelta = new Vector2(180f, 48f);
        closeRt.anchorMin = new Vector2(0.5f, 0f);
        closeRt.anchorMax = new Vector2(0.5f, 0f);
        closeRt.pivot = new Vector2(0.5f, 0f);
        closeRt.anchoredPosition = new Vector2(100f, 36f);
        closeBtn.onClick.AddListener(() =>
        {
            LogAttackOnActionDecisionWithBoard("NoCommandUsed_CloseCommandPopup", context, side, attackingUnitInAttackFlow);
            isOnActionPopupOpen = false;
            activeOnActionPopupRoot = null;
            Destroy(root);
            onStepDone?.Invoke();
        });

        return true;
    }

    private void ExecuteOnActionCommandQueue(
        PlayerType side,
        List<CardController> queue,
        int index,
        System.Action onAllDone,
        CardController attackingUnitInAttackFlow = null)
    {
        if (queue == null || index >= queue.Count)
        {
            onAllDone?.Invoke();
            return;
        }

        CardController command = queue[index];
        if (command == null || command.Data == null)
        {
            ExecuteOnActionCommandQueue(side, queue, index + 1, onAllDone, attackingUnitInAttackFlow);
            return;
        }

        TryExecuteOnActionCommand(
            side,
            command,
            () => ExecuteOnActionCommandQueue(side, queue, index + 1, onAllDone, attackingUnitInAttackFlow),
            attackingUnitInAttackFlow,
            index,
            queue != null ? queue.Count : 0);
    }

    private void TryExecuteOnActionCommand(
        PlayerType side,
        CardController command,
        System.Action onDone,
        CardController attackingUnitInAttackFlow = null,
        int commandQueueIndex = -1,
        int commandQueueCount = -1)
    {
        if (command == null || command.Data == null)
        {
            onDone?.Invoke();
            return;
        }

        List<EffectData> onActionEffects = GetEffectsByTiming(command.Data, EffectTiming.OnAction);
        if (onActionEffects.Count == 0)
        {
            LogCommandUseResultWithBoard(
                "OnAction_Skipped_NoOnActionEffects",
                side,
                command,
                attackingUnitInAttackFlow,
                commandQueueIndex,
                commandQueueCount,
                "reason:no OnAction timed effects on card");
            onDone?.Invoke();
            return;
        }

        EffectData enemyTargetEffect = onActionEffects.Find(e => e != null && e.target == TargetType.EnemyUnit);
        if (enemyTargetEffect != null)
        {
            OpenOnActionEnemyTargetSelection(
                side,
                command,
                enemyTargetEffect,
                onDone,
                attackingUnitInAttackFlow,
                commandQueueIndex,
                commandQueueCount);
            return;
        }

        if (!gundamRule.TryConsumeResource(ToRuleSide(side), command.CurrentCost, 0, command.Data.id))
        {
            Debug.Log("OnAction: リソース不足で実行できません。");
            LogCommandUseResultWithBoard(
                "OnAction_Failed_InsufficientResource",
                side,
                command,
                attackingUnitInAttackFlow,
                commandQueueIndex,
                commandQueueCount,
                "phase:before_apply_direct cost not consumed");
            onDone?.Invoke();
            return;
        }

        string consumedSummary = $"{command.Data.cardName}(id:{command.Data.id})";
        EffectData applied = onActionEffects[0];
        string effectDetail =
            $"consumed:{consumedSummary}|firstEffect:{applied.type} target:{applied.target} value:{applied.value}";
        List<CardController> resolvedBeforeApply = ResolveEffectTargets(command, side, applied.target);
        List<UnitStatSnapForCommandLog> beforeSnaps = SnapUnitStatsForOnActionCommandLog(resolvedBeforeApply);
        ApplyEffect(command, side, applied);
        LogOnActionCommandAppliedToUnitsBattleOutcome(command, side, applied, "OnAction_AfterApplyDirectEffect", beforeSnaps);
        FinalizeOnActionSourceCard(command, side);
        List<CardController> unitTargetsForEvalLog = BuildOnActionUnitTargetListAfterApply(resolvedBeforeApply);
        LogCommandUseResultWithBoard(
            "OnAction_AfterApplyDirectEffect",
            side,
            null,
            attackingUnitInAttackFlow,
            commandQueueIndex,
            commandQueueCount,
            effectDetail,
            unitTargetsForEvalLog);
        onDone?.Invoke();
    }

    private void OpenOnActionEnemyTargetSelection(
        PlayerType side,
        CardController command,
        EffectData effect,
        System.Action onDone,
        CardController attackingUnitInAttackFlow = null,
        int commandQueueIndex = -1,
        int commandQueueCount = -1)
    {
        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            LogCommandUseResultWithBoard(
                "OnAction_Skipped_NoCanvas_EnemyTargetUI",
                side,
                command,
                attackingUnitInAttackFlow,
                commandQueueIndex,
                commandQueueCount,
                "reason:ResolveBattleCanvas null");
            onDone?.Invoke();
            return;
        }

        List<CardController> enemyUnits = GetAliveEnemyUnits(side);
        if (enemyUnits.Count == 0)
        {
            Debug.Log("OnAction: 対象となる敵ユニットがいません。");
            LogCommandUseResultWithBoard(
                "OnAction_Skipped_NoEnemyUnitTargets",
                side,
                command,
                attackingUnitInAttackFlow,
                commandQueueIndex,
                commandQueueCount,
                "reason:GetAliveEnemyUnits empty");
            onDone?.Invoke();
            return;
        }

        if (command != null && command.Data != null && effect != null)
        {
            System.Text.StringBuilder openPatternTable = new System.Text.StringBuilder(320);
            openPatternTable.Append("[OnActionHypotheticalBoard] phase:EnumerateHypotheticalPicks candidates:").Append(enemyUnits.Count)
                .Append(" cmdId:").Append(command.Data.id).Append(" effect:").Append(effect.type).Append(" target:").Append(effect.target)
                .Append(' ').Append(FormatBlockRedirectProbeInline(side)).AppendLine();
            for (int pi = 0; pi < enemyUnits.Count; pi++)
            {
                CardController eu = enemyUnits[pi];
                if (eu?.Data == null)
                {
                    continue;
                }

                int es = TryGetUnitBattleZoneSlotIndex(eu);
                openPatternTable.Append("  patternRow:").Append(FormatHypothesisPatternLetterLabel(pi)).Append(" → target:")
                    .Append(eu.Data.cardName).Append("(id:").Append(eu.Data.id).Append(") zoneSlotIndex:#").Append(es).AppendLine();
            }

            Debug.Log(openPatternTable.ToString());
            for (int hi = 0; hi < enemyUnits.Count; hi++)
            {
                LogOnActionHypotheticalBoardForEnemyPick(
                    command,
                    side,
                    effect,
                    enemyUnits[hi],
                    hi,
                    attackingUnitInAttackFlow,
                    commandQueueIndex,
                    commandQueueCount);
            }
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

        bool isAttackContext = attackingUnitInAttackFlow != null;
        for (int i = 0; i < enemyUnits.Count; i++)
        {
            CardController t = enemyUnits[i];
            if (attackingUnitInAttackFlow != null && t == attackingUnitInAttackFlow && t.Data != null)
            {
                Debug.Log(
                    $"[OnActionEnemyTarget] attacking enemy in target list: {t.Data.cardName} AP:{t.CurrentPower} HP:{t.CurrentHp} (index:{i} side:{side})");
            }
            
            Button btn = root.CreateChildButton($"{t.Data.cardName} AP:{t.CurrentPower} HP:{t.CurrentHp}");
            bool isAttackingCardButton = isAttackContext
                && t == attackingUnitInAttackFlow;
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
            if (attackingUnitInAttackFlow != null && t == attackingUnitInAttackFlow)
            {
                TextMeshProUGUI btnText = btn.GetComponentInChildren<TextMeshProUGUI>();
                if (btnText != null)
                {
                    btnText.color = new Color(1f, 0.22f, 0.22f, 1f);
                }
            }

            btn.onClick.AddListener(() =>
            {
                if (!gundamRule.TryConsumeResource(ToRuleSide(side), command.CurrentCost, 0, command.Data.id))
                {
                    Debug.Log("OnAction: リソース不足で実行できません。");
                    LogCommandUseResultWithBoard(
                        "OnAction_Failed_InsufficientResource",
                        side,
                        command,
                        attackingUnitInAttackFlow,
                        commandQueueIndex,
                        commandQueueCount,
                        "phase:enemy_target_ui cost not consumed");
                    Destroy(root);
                    onDone?.Invoke();
                    return;
                }

                string consumedSummary = command.Data != null ? $"{command.Data.cardName}(id:{command.Data.id})" : "?";
                string detail =
                    $"consumed:{consumedSummary}|effect:{effect.type} target:{effect.target} value:{effect.value}|pickedEnemy:{t.Data.cardName}(id:{t.Data.id})";
                List<UnitStatSnapForCommandLog> beforeSnapsPick = SnapUnitStatsForOnActionCommandLog(new List<CardController> { t });
                ApplyEffectToSpecificTargets(command, side, effect, new List<CardController> { t });
                LogOnActionCommandAppliedToUnitsBattleOutcome(command, side, effect, "OnAction_AfterApplyEnemyUnitTarget", beforeSnapsPick);
                FinalizeOnActionSourceCard(command, side);
                List<CardController> pickedForEval = BuildOnActionUnitTargetListAfterApply(new List<CardController> { t });
                LogCommandUseResultWithBoard(
                    "OnAction_AfterApplyEnemyUnitTarget",
                    side,
                    null,
                    attackingUnitInAttackFlow,
                    commandQueueIndex,
                    commandQueueCount,
                    detail,
                    pickedForEval);
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

    private bool TryAddOnMainEffectApplyButton(
        GameObject filterPanel,
        CardController cardController,
        PlayerType ownerType,
        float anchoredY)
    {
        if (filterPanel == null || cardController == null || cardController.Data == null)
        {
            return false;
        }

        if (!HasEffectTiming(cardController.Data, EffectTiming.OnMain)
            || !CanExecuteOnMainCardNow(ownerType, cardController))
        {
            return false;
        }

        Button mainBtn = filterPanel.CreateChildButton("メイン効果を発動");
        RectTransform mainRt = mainBtn.GetComponent<RectTransform>();
        mainRt.sizeDelta = new Vector2(280f, 50f);
        mainRt.anchoredPosition = new Vector2(0f, anchoredY);
        CardController source = cardController;
        mainBtn.onClick.AddListener(() =>
        {
            Destroy(filterPanel);
            TryExecuteOnMainCard(ownerType, source, null);
        });
        return true;
    }

    private void TryExecuteOnMainCard(PlayerType side, CardController source, System.Action onDone)
    {
        if (source == null || source.Data == null)
        {
            onDone?.Invoke();
            return;
        }

        if (!CanExecuteOnMainCardNow(side, source))
        {
            Debug.Log("OnMain: 現在は発動できません（ターン/フェイズ/リソース/レベル）。");
            onDone?.Invoke();
            return;
        }

        List<EffectData> effects = GetEffectsByTiming(source.Data, EffectTiming.OnMain);
        if (effects.Count == 0)
        {
            onDone?.Invoke();
            return;
        }

        TryExecuteOnMainEffectChain(side, source, effects, 0, false, onDone);
    }

    private void TryExecuteOnMainEffectChain(
        PlayerType side,
        CardController source,
        List<EffectData> effects,
        int index,
        bool resourceConsumed,
        System.Action onDone)
    {
        if (effects == null || index >= effects.Count)
        {
            if (resourceConsumed)
            {
                FinalizeOnMainSourceCard(source, side);
            }

            onDone?.Invoke();
            return;
        }

        EffectData effect = effects[index];
        if (effect == null)
        {
            TryExecuteOnMainEffectChain(side, source, effects, index + 1, resourceConsumed, onDone);
            return;
        }

        if (IsEffectTargetRequiringUnitSelection(effect.target))
        {
            List<CardController> candidates = ResolveSelectableEffectTargets(source, side, effect.target);
            if (candidates.Count == 0)
            {
                Debug.Log($"OnMain: 選択可能な対象がありません (target:{effect.target})。");
                TryExecuteOnMainEffectChain(side, source, effects, index + 1, resourceConsumed, onDone);
                return;
            }

            OpenOnMainTargetSelectionUI(side, source, effect, candidates, effects, index, resourceConsumed, onDone);
            return;
        }

        if (!TryConsumeResourceForOnMain(side, source, ref resourceConsumed))
        {
            onDone?.Invoke();
            return;
        }

        ApplyEffect(source, side, effect);
        TryExecuteOnMainEffectChain(side, source, effects, index + 1, resourceConsumed, onDone);
    }

    private bool TryConsumeResourceForOnMain(PlayerType side, CardController source, ref bool resourceConsumed)
    {
        if (resourceConsumed)
        {
            return true;
        }

        if (source == null || source.Data == null)
        {
            return false;
        }

        if (!gundamRule.TryConsumeResource(ToRuleSide(side), source.CurrentCost, 0, source.Data.id))
        {
            Debug.Log("OnMain: リソース不足で実行できません。");
            return false;
        }

        SyncResourceViewsFromRule(ToRuleSide(side));
        resourceConsumed = true;
        return true;
    }

    private static bool IsEffectTargetRequiringUnitSelection(TargetType targetType)
    {
        return targetType == TargetType.EnemyUnit || targetType == TargetType.AllyUnit;
    }

    private List<CardController> ResolveSelectableEffectTargets(
        CardController sourceCard,
        PlayerType ownerType,
        TargetType targetType)
    {
        List<CardController> allies = ownerType == PlayerType.Player ? playerBattleZoneCards : enemyBattleZoneCards;
        List<CardController> result = new List<CardController>();

        switch (targetType)
        {
            case TargetType.Self:
                if (sourceCard != null
                    && sourceCard.Data != null
                    && sourceCard.Data.type == Type.Unit
                    && sourceCard.CurrentHp > 0
                    && IsCardOnBattleZone(sourceCard))
                {
                    result.Add(sourceCard);
                }
                break;
            case TargetType.AllyUnit:
                AddAllAliveUnits(allies, result);
                break;
            case TargetType.EnemyUnit:
                AddAllAliveUnits(GetAliveEnemyUnits(ownerType), result);
                break;
        }

        return result;
    }

    private bool IsCardOnBattleZone(CardController card)
    {
        if (card == null)
        {
            return false;
        }

        List<CardController> allies = playerBattleZoneCards;
        for (int i = 0; i < allies.Count; i++)
        {
            if (allies[i] == card)
            {
                return true;
            }
        }

        for (int i = 0; i < enemyBattleZoneCards.Count; i++)
        {
            if (enemyBattleZoneCards[i] == card)
            {
                return true;
            }
        }

        return false;
    }

    private void OpenOnMainTargetSelectionUI(
        PlayerType side,
        CardController source,
        EffectData effect,
        List<CardController> candidates,
        List<EffectData> allEffects,
        int effectIndex,
        bool resourceConsumed,
        System.Action onDone)
    {
        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            onDone?.Invoke();
            return;
        }

        DestroyActiveOnActionPopupIfAny();
        GameObject root = new GameObject("OnMainTargetSelect", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        activeOnActionPopupRoot = root;
        isOnActionPopupOpen = true;
        root.transform.SetParent(canvas.transform, false);
        root.transform.SetAsLastSibling();
        root.SetFullSize();
        Image dim = root.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.55f);
        dim.raycastTarget = true;

        TextMeshProUGUI title = root.CreateChildTextCustom("OnMainTargetTitle", UIAnchor.TopCenter, 720, 48);
        title.text = "OnMain — 対象を選択";
        title.color = Color.white;
        title.fontSize = 24;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -20f);

        if (CardImagePrefab != null && source != null && source.Data != null)
        {
            GameObject sourceCardGo = Instantiate(CardImagePrefab, root.transform);
            RectTransform sourceRt = sourceCardGo.GetComponent<RectTransform>();
            if (sourceRt != null)
            {
                sourceRt.anchorMin = new Vector2(0.5f, 1f);
                sourceRt.anchorMax = new Vector2(0.5f, 1f);
                sourceRt.pivot = new Vector2(0.5f, 1f);
                sourceRt.sizeDelta = new Vector2(120f, 168f);
                sourceRt.anchoredPosition = new Vector2(0f, -78f);
            }

            CardController preview = sourceCardGo.GetComponent<CardController>();
            if (preview != null)
            {
                preview.SetUp(source.Data, _ => { });
            }

            Button sourceBlocker = sourceCardGo.GetComponent<Button>();
            if (sourceBlocker != null)
            {
                sourceBlocker.interactable = false;
            }
        }

        GameObject scrollGo = root.CreateGridScrollView(680, 360, UIAnchor.TopCenter);
        RectTransform scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.anchoredPosition = new Vector2(0f, -270f);
        scrollGo.ConfigureGridCellFromViewportHeight(0.78f, 56f);
        ScrollRect sr = scrollGo.GetComponent<ScrollRect>();
        RectTransform content = sr != null ? sr.content : null;

        for (int i = 0; i < candidates.Count; i++)
        {
            CardController candidate = candidates[i];
            if (content == null || candidate == null || candidate.Data == null || CardImagePrefab == null)
            {
                continue;
            }

            GameObject go = Instantiate(CardImagePrefab, content);
            CardController cc = go.GetComponent<CardController>();
            if (cc != null)
            {
                cc.SetUp(candidate.Data, _ => { });
            }

            TextMeshProUGUI statLabel = go.CreateChildTextCustom(
                "TargetStat",
                UIAnchor.BottomCenter,
                100,
                28);
            statLabel.text = $"AP:{candidate.CurrentPower} HP:{candidate.CurrentHp}";
            statLabel.fontSize = 14;
            statLabel.color = Color.white;
            statLabel.alignment = TextAlignmentOptions.Center;

            Button btn = go.GetComponent<Button>();
            if (btn == null)
            {
                btn = go.AddComponent<Button>();
            }

            CardController picked = candidate;
            btn.onClick.AddListener(() =>
            {
                bool consumed = resourceConsumed;
                if (!TryConsumeResourceForOnMain(side, source, ref consumed))
                {
                    CloseOnMainTargetSelectionRoot(root);
                    onDone?.Invoke();
                    return;
                }

                ApplyEffectToSpecificTargets(source, side, effect, new List<CardController> { picked });
                CloseOnMainTargetSelectionRoot(root);
                TryExecuteOnMainEffectChain(side, source, allEffects, effectIndex + 1, consumed, onDone);
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
            CloseOnMainTargetSelectionRoot(root);
            onDone?.Invoke();
        });
    }

    private void CloseOnMainTargetSelectionRoot(GameObject root)
    {
        if (root != null)
        {
            Destroy(root);
        }

        if (activeOnActionPopupRoot == root)
        {
            activeOnActionPopupRoot = null;
        }

        isOnActionPopupOpen = false;
    }

    private void FinalizeOnMainSourceCard(CardController source, PlayerType side)
    {
        FinalizeOnActionSourceCard(source, side);
    }

    private bool CanExecuteOnMainCardNow(PlayerType ownerType, CardController card)
    {
        if (card == null || card.Data == null || ownerType != currentPlayerType || currentPhase != BattlePhase.MainPhase)
        {
            return false;
        }

        Gundam2024RuleScript.PlayerState state = ownerType == PlayerType.Player
            ? gundamRule.Player
            : gundamRule.Enemy;
        return state.TotalLevel >= card.CurrentLevel && state.resource >= card.CurrentCost;
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

    private void FinalizeOnActionSourceCard(CardController source, PlayerType side)
    {
        if (source == null || source.Data == null)
        {
            return;
        }

        if (source.Data.type == Type.Command)
        {
            SendUsedCommandToTrash(source, side);
        }
    }

    private bool CanExecuteOnActionCardNow(PlayerType ownerType, CardController card)
    {
        if (card == null || card.Data == null)
        {
            return false;
        }

        Gundam2024RuleScript.PlayerState state = ownerType == PlayerType.Player
            ? gundamRule.Player
            : gundamRule.Enemy;
        return state.TotalLevel >= card.CurrentLevel && state.resource >= card.CurrentCost;
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
            LogAttackOnActionDecisionWithBoard("NoCommandUsed_CloseEnemyHandPreview", context, ownerType, null);
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
