using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro; // これを追加！

public partial class BattleGameMain : MonoBehaviour
{
    /// <summary>盤面スナップショット・仮想シミュレーション等の重い診断ログ。Editor の GPU TDR 原因になるため通常は false。</summary>
    private const bool EnableVerboseBattleDebugLogs = false;

    private readonly Dictionary<CardController, int> onRestActivatedTurnByCard = new Dictionary<CardController, int>();

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
    private bool isShieldBreakFlowOpen;
    private bool shieldBreakQueueRunning;
    private readonly Queue<PendingShieldBreakBatch> pendingShieldBreakBatches = new Queue<PendingShieldBreakBatch>();
    private GameObject activeOnActionPopupRoot;
    private GameObject activeAttackFlowDebugPanelRoot;
    private bool isAttackedSidePanelOpen;
    /// <summary>攻撃フロー中のテスト用「actionthink」表示中。true の間は進行を止める。</summary>
    private bool isActionThinkPauseOpen;
    /// <summary>攻撃後 OnAction の「プレイヤー手前」に actionthink を挟むテスト用フラグ。</summary>
    [SerializeField] private bool enableAttackFlowActionThinkTest = true;
    [SerializeField] private bool enableShieldAttackFlowDebugLog = true;
    [Tooltip("ブロック確定後にブロッカー破壊で交換戦闘が中断されるときの詳細ログ。")]
    [SerializeField] private bool enableBlockRedirectInterruptDebugLog = true;
    [Tooltip("アタック→ブロック→格闘戦 OnAction で他ユニット破壊時に攻撃者・ブロッカー・被害者の3体をログ。")]
    [SerializeField] private bool enableAttackBlockCloseCombatTrioDebugLog = true;
    [Tooltip("true のとき敵 OnAction はログ用ポップアップのみ。false で AI がコマンドを本番実行。")]
    [SerializeField] private bool enableEnemyOnActionDebugPopupOnly;
    private bool isShieldAttackResolving;
    private bool isTurnPhaseSequenceRunning;
    /// <summary>エネミー <see cref="EnemyActionCoroutine"/> 実行中。ターン進行を止める。</summary>
    private bool isEnemyMainPhaseCoroutineRunning;
    private bool blockShieldFlowDuringShieldAttack;
    private Gundam2024RuleScript.PlayerSide blockedShieldFlowSide;
    /// <summary>シールド攻撃→ブロック OnAction 完了まで isShieldAttackResolving / blockShieldFlow を維持する。</summary>
    private bool deferredShieldBlockRedirectWait;

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
    /// <summary>ブロック確定後のユニット戦フロー中か（ブロッカー破壊後も OnAction 再開で通常攻撃へ落とさない）。</summary>
    private bool attackFlowBlockRedirectEngaged;
    /// <summary>SendCardToTrash 開始〜Finish まで。Destroy 等で HP 残りのまま非同期破棄中のユニット。</summary>
    private readonly HashSet<CardController> unitsPendingSendToTrash = new HashSet<CardController>();
    /// <summary>シールド攻撃からブロックへ移行したフロー（OnAction 後にシールドダメージへ再開しない）。</summary>
    private bool attackFlowBlockRedirectFromShieldStrike;
    /// <summary>OnAction 中にブロッカーが除去されたため交換戦闘を行わない。</summary>
    private bool attackFlowBlockRedirectCombatVoided;
    /// <summary>ブロック中断後、同一攻撃でシールドダメージを解決しない。</summary>
    private bool shieldStrikeAbortedAfterBlockInterrupt;
    /// <summary>ClearAttackFlowContext では消さない。ブロッカー喪失後の交換ダメージ／シールド strike を禁止。</summary>
    private bool blockExchangeCancelledForCurrentAttack;
    /// <summary>ブロック確定後の OnAction を一度完了したら、同一攻撃で OnAction を再実行しない。</summary>
    private bool attackFlowBlockOnActionCompleted;
    /// <summary>同一攻撃でブロック選択 UI を既に閉じた（ブロック有無問わず再表示しない）。</summary>
    private bool attackFlowBlockSelectionResolved;
    /// <summary>ブロックを行わずにブロック段階を通過した後の OnAction を完了済み。</summary>
    private bool attackFlowPostBlockPassOnActionDone;
    /// <summary>ブロックパス後の OnAction→戦闘継続処理が実行中（二重起動防止）。</summary>
    private bool attackFlowPostBlockPassInProgress;

    private enum AttackFlowPipelinePhase
    {
        None,
        AwaitingBlockUi,
        PostBlockOnAction,
    }

    private AttackFlowPipelinePhase attackFlowPipelinePhase = AttackFlowPipelinePhase.None;
    private Coroutine attackFlowAfterBlockPassCoroutine;

    private const int CloseCombatCardId = 28;

    /// <summary>配備ベース（Argama 等）ありのシールド攻撃→ブロック→Close Combat 調査用。</summary>
    private void LogArgamaShieldBlockCloseCombatDebug(
        string phase,
        string detail,
        CardController attacker = null,
        CardController blocker = null,
        CardController effectSource = null)
    {
        if (!attackFlowBlockRedirectFromShieldStrike)
        {
            return;
        }

        Gundam2024RuleScript.PlayerSide enemySide = Gundam2024RuleScript.PlayerSide.Enemy;
        CardController deployedBase = GetDeployedBaseForRuleSide(enemySide);
        string baseLabel = deployedBase != null && deployedBase.Data != null
            ? $"{deployedBase.Data.cardName}(id:{deployedBase.Data.id}) HP:{deployedBase.CurrentHp}"
            : "none";

        CardController logAttacker = attacker ?? attackFlowAttackerUnit;
        CardController logBlocker = blocker ?? attackFlowBlockRedirectUnit;
        string effectLabel = effectSource != null && effectSource.Data != null
            ? $"{effectSource.Data.cardName}(id:{effectSource.Data.id})"
            : "none";

        Debug.Log(
            $"[ArgamaShieldBlockCloseCombat] phase:{phase} {detail}\n"
            + $"  deployedBase:{baseLabel}\n"
            + $"  flags cancelled:{blockExchangeCancelledForCurrentAttack} voided:{attackFlowBlockRedirectCombatVoided} "
            + $"deferredWait:{deferredShieldBlockRedirectWait} shieldAborted:{shieldStrikeAbortedAfterBlockInterrupt}\n"
            + $"  attacker:{FormatUnitDebugSnap(logAttacker)} blocker:{FormatUnitDebugSnap(logBlocker)} "
            + $"blockerPendingTrash:{logBlocker != null && unitsPendingSendToTrash.Contains(logBlocker)}\n"
            + $"  effectSource:{effectLabel}");
    }

    private static string FormatUnitDebugSnap(CardController c)
    {
        if (c == null || c.Data == null)
        {
            return "null";
        }

        return $"{c.Data.cardName}(id:{c.Data.id}) HP:{c.CurrentHp} AP:{c.CurrentPower}";
    }

    /// <summary>ブロッカー破壊などでブロック交換が中断されるときの調査用ログ。</summary>
    private void LogDestroyedBlockerInterruptDetail(string phase, string reason, CardController blocker = null)
    {
        if (!enableBlockRedirectInterruptDebugLog)
        {
            return;
        }

        CardController logBlocker = blocker ?? attackFlowBlockRedirectUnit;
        bool onDeployPanel = logBlocker != null
            && (logBlocker.transform.IsChildOf(cardGameRule.PlayerDeployPanel)
                || logBlocker.transform.IsChildOf(enemyCardGameRule.PlayerDeployPanel));

        Debug.Log(
            $"[BlockCombat][DestroyedBlocker] phase:{phase} reason:{reason}\n"
            + $"  strike:{attackFlowStrikeKind} pipeline:{attackFlowPipelinePhase} "
            + $"blockOnActionDone:{attackFlowBlockOnActionCompleted} shieldOrigin:{attackFlowBlockRedirectFromShieldStrike}\n"
            + $"  flags engaged:{attackFlowBlockRedirectEngaged} cancelled:{blockExchangeCancelledForCurrentAttack} "
            + $"voided:{attackFlowBlockRedirectCombatVoided} pendingTrash:{logBlocker != null && unitsPendingSendToTrash.Contains(logBlocker)}\n"
            + $"  blocker:{FormatEffectDamageUnitDebugSnap(logBlocker)} onDeployPanel:{onDeployPanel} "
            + $"aliveOnField:{logBlocker != null && IsUnitAliveOnAnyDeployField(logBlocker)} "
            + $"exchangeAvailable:{logBlocker != null && IsUnitAvailableForAttackExchange(logBlocker)}\n"
            + $"  attacker:{FormatEffectDamageUnitDebugSnap(attackFlowAttackerUnit)} "
            + $"declaredDefender:{FormatEffectDamageUnitDebugSnap(attackFlowDeclaredDefenderUnit)}");
    }

    /// <summary>
    /// アタック→ブロック→格闘戦 OnAction で「攻撃者・ブロッカー以外」のユニットが破壊されたとき、3体分を1本でログ。
    /// </summary>
    private void TryLogAttackBlockCloseCombatTrioDestroy(
        string phase,
        CardController destroyedUnit,
        CardController effectSource = null)
    {
        if (!enableAttackBlockCloseCombatTrioDebugLog
            || destroyedUnit == null
            || !IsAttackBlockCloseCombatOnActionContext(effectSource))
        {
            return;
        }

        CardController attacker = attackFlowAttackerUnit;
        CardController blocker = attackFlowBlockRedirectUnit;
        if (!IsThirdPartyUnitInAttackBlockTrio(destroyedUnit, attacker, blocker))
        {
            return;
        }

        string victimRole = "other";
        if (IsSameBattleUnit(destroyedUnit, attackFlowDeclaredDefenderUnit))
        {
            victimRole = "declaredDefender";
        }

        Debug.Log(
            $"[AttackBlockCloseCombat][TrioDestroy] phase:{phase} victimRole:{victimRole}\n"
            + $"  context:{DescribeAttackBlockCloseCombatOnActionContext()}\n"
            + $"  effectSource:{FormatEffectDamageSourceDebugSnap(effectSource)}\n"
            + $"  attacker:{FormatEffectDamageUnitDebugSnap(attacker)}\n"
            + $"  blocker:{FormatEffectDamageUnitDebugSnap(blocker)}\n"
            + $"  destroyed:{FormatEffectDamageUnitDebugSnap(destroyedUnit)}");
    }

    private bool IsAttackBlockCloseCombatOnActionContext(CardController effectSource)
    {
        if (attackFlowStrikeKind == AttackFlowStrikeKind.None)
        {
            return false;
        }

        if (attackFlowBlockRedirectUnit == null && !attackFlowBlockRedirectEngaged)
        {
            return false;
        }

        if (effectSource != null && IsCloseCombatCard(effectSource))
        {
            return true;
        }

        return isOnActionPopupOpen
            || attackFlowPipelinePhase == AttackFlowPipelinePhase.PostBlockOnAction
            || (!attackFlowBlockOnActionCompleted && attackFlowBlockRedirectEngaged);
    }

    private static bool IsThirdPartyUnitInAttackBlockTrio(
        CardController destroyedUnit,
        CardController attacker,
        CardController blocker)
    {
        if (destroyedUnit == null)
        {
            return false;
        }

        if (attacker != null && IsSameBattleUnit(destroyedUnit, attacker))
        {
            return false;
        }

        if (blocker != null && IsSameBattleUnit(destroyedUnit, blocker))
        {
            return false;
        }

        return true;
    }

    private string DescribeAttackBlockCloseCombatOnActionContext()
    {
        return $"strike:{attackFlowStrikeKind} pipeline:{attackFlowPipelinePhase} "
            + $"blockEngaged:{attackFlowBlockRedirectEngaged} blockOnActionDone:{attackFlowBlockOnActionCompleted} "
            + $"shieldOrigin:{attackFlowBlockRedirectFromShieldStrike} onActionOpen:{isOnActionPopupOpen} "
            + $"context:{_onlineOnActionActiveContext ?? "local"}";
    }

    private string FormatEffectDamageUnitDebugSnap(CardController c)
    {
        if (c == null || c.Data == null)
        {
            return "null";
        }

        PlayerType owner = ResolveCardOwner(c.transform);
        PlayerType zoneOwner = ResolveBattleZoneSideForUnit(c);
        int zoneIndex = ResolveBattleZoneIndexForOnlineEffect(c, zoneOwner);
        return $"{c.Data.cardName}(id:{c.Data.id}, inst:{c.BattleInstanceId}, owner:{owner}, "
            + $"zone:{zoneOwner}[{zoneIndex}], HP:{c.CurrentHp}, AP:{c.CurrentPower}, "
            + $"{(c.IsRestState ? "REST" : "ACTIVE")})";
    }

    private static string FormatEffectDamageSourceDebugSnap(CardController sourceCard)
    {
        if (sourceCard == null || sourceCard.Data == null)
        {
            return "null";
        }

        return $"{sourceCard.Data.cardName}(id:{sourceCard.Data.id}, cost:{sourceCard.CurrentCost}, "
            + $"lv:{sourceCard.CurrentLevel})";
    }

    private static bool IsCloseCombatCard(CardController card)
    {
        return card != null && card.Data != null && card.Data.id == CloseCombatCardId;
    }

    private void ClearAttackFlowContext()
    {
        attackFlowStrikeKind = AttackFlowStrikeKind.None;
        attackFlowAttackerUnit = null;
        attackFlowAttackerOwner = PlayerType.Player;
        attackFlowDeclaredDefenderUnit = null;
        attackFlowBlockRedirectUnit = null;
        attackFlowDefenderShieldCountAtStrike = -1;
        attackFlowBlockRedirectEngaged = false;
        attackFlowBlockRedirectFromShieldStrike = false;
        attackFlowBlockRedirectCombatVoided = false;
        attackFlowBlockOnActionCompleted = false;
        attackFlowBlockSelectionResolved = false;
        attackFlowPostBlockPassOnActionDone = false;
        attackFlowPostBlockPassInProgress = false;
        attackFlowPipelinePhase = AttackFlowPipelinePhase.None;
        if (attackFlowAfterBlockPassCoroutine != null)
        {
            StopCoroutine(attackFlowAfterBlockPassCoroutine);
            attackFlowAfterBlockPassCoroutine = null;
        }

        ResetOnAttackPreCombatEffectsAppliedGuard();
    }

    private void MarkAttackFlowBlockSelectionResolved()
    {
        attackFlowBlockSelectionResolved = true;
    }

    /// <summary>新しい攻撃宣言の直前に、前回攻撃のブロック／OnAction 再開フラグを消す。</summary>
    private void ResetAttackFlowBlockPassFlagsForNewDeclaration()
    {
        attackFlowBlockSelectionResolved = false;
        attackFlowPostBlockPassOnActionDone = false;
        attackFlowPostBlockPassInProgress = false;
        attackFlowBlockOnActionCompleted = false;
        attackFlowPipelinePhase = AttackFlowPipelinePhase.None;
        ClearPendingBlockRedirectSelection();
    }

    /// <summary>ブロック UI をキャンセル／未選択で閉じた直後に呼ぶ。OnAction → 戦闘まで一気通貫で進める。</summary>
    private void CancelAttackFlowBlockSelectionAndContinue()
    {
        CardController attacker = attackFlowAttackerUnit != null
            ? attackFlowAttackerUnit
            : pendingUnitAttackAttacker;
        PlayerType attackerOwner = attackFlowAttackerOwner;
        if (attacker != null)
        {
            attackerOwner = ResolveCardOwner(attacker.transform);
        }

        if (attackFlowStrikeKind == AttackFlowStrikeKind.Shield)
        {
            PlayerType defenderSide = attackerOwner == PlayerType.Player ? PlayerType.Enemy : PlayerType.Player;
            RunOnActionStepsImmediatelyAfterBlockPass(
                attacker,
                null,
                attackerOwner,
                defenderSide,
                AttackFlowStrikeKind.Shield);
            return;
        }

        CardController defender = attackFlowDeclaredDefenderUnit;
        if (attacker == null || defender == null)
        {
            Debug.LogWarning("[AttackFlow] Block cancel aborted — attack context missing.");
            CancelPendingUnitAttackFlow();
            return;
        }

        RunOnActionStepsImmediatelyAfterBlockPass(
            attacker,
            defender,
            attackerOwner,
            ResolveCardOwner(defender.transform),
            AttackFlowStrikeKind.UnitVsUnit);
    }

    /// <summary>ブロック Cancel／パス後にコルーチンで OnAction → 戦闘へ進める。</summary>
    private void RunOnActionStepsImmediatelyAfterBlockPass(
        CardController attacker,
        CardController declaredDefenderOrNull,
        PlayerType attackerOwner,
        PlayerType defenderSideForOnAction,
        AttackFlowStrikeKind strikeKind)
    {
        if (attackFlowAfterBlockPassCoroutine != null)
        {
            StopCoroutine(attackFlowAfterBlockPassCoroutine);
        }

        attackFlowAfterBlockPassCoroutine = StartCoroutine(
            CoAdvanceAttackFlowAfterBlockPass(attacker, declaredDefenderOrNull, attackerOwner, defenderSideForOnAction, strikeKind));
    }

    private IEnumerator CoAdvanceAttackFlowAfterBlockPass(
        CardController attacker,
        CardController declaredDefenderOrNull,
        PlayerType attackerOwner,
        PlayerType defenderSideForOnAction,
        AttackFlowStrikeKind strikeKind)
    {
        Debug.Log(
            $"[AttackFlow] Block pass/cancel → queue OnAction. strike:{strikeKind} "
            + $"attacker:{attacker?.Data?.cardName} defender:{declaredDefenderOrNull?.Data?.cardName}");

        attackFlowPipelinePhase = AttackFlowPipelinePhase.PostBlockOnAction;
        MarkAttackFlowBlockSelectionResolved();
        ClearPendingBlockRedirectSelection();
        attackFlowBlockRedirectEngaged = false;
        attackFlowBlockRedirectFromShieldStrike = false;
        attackFlowBlockOnActionCompleted = false;

        isAttackedSidePanelOpen = false;
        isOnActionPopupOpen = false;
        if (activeAttackFlowDebugPanelRoot != null)
        {
            Destroy(activeAttackFlowDebugPanelRoot);
            activeAttackFlowDebugPanelRoot = null;
        }

        DestroyActiveOnActionPopupIfAny();
        yield return null;

        if (!IsUnitAliveOnAnyDeployField(attacker))
        {
            CancelPendingUnitAttackFlow();
            attackFlowAfterBlockPassCoroutine = null;
            yield break;
        }

        if (strikeKind != AttackFlowStrikeKind.Shield
            && (declaredDefenderOrNull == null || !IsUnitAliveOnAnyDeployField(declaredDefenderOrNull)))
        {
            CancelPendingUnitAttackFlow();
            attackFlowAfterBlockPassCoroutine = null;
            yield break;
        }

        attackFlowStrikeKind = strikeKind;
        attackFlowAttackerUnit = attacker;
        attackFlowAttackerOwner = attackerOwner;
        attackFlowDeclaredDefenderUnit = declaredDefenderOrNull;
        attackFlowBlockRedirectUnit = null;
        pendingOnAttackEffectResolvedAttacker = attacker;

        if (attackFlowPostBlockPassOnActionDone)
        {
            CompleteAttackFlowAfterPostBlockOnAction(
                attacker,
                declaredDefenderOrNull,
                attackerOwner,
                defenderSideForOnAction,
                strikeKind);
            attackFlowAfterBlockPassCoroutine = null;
            yield break;
        }

        if (TrySettleAttackFlowAfterOnActionPhases())
        {
            attackFlowAfterBlockPassCoroutine = null;
            yield break;
        }

        bool onActionFinished = false;
        System.Action finishOnAction = () =>
        {
            if (onActionFinished)
            {
                return;
            }

            onActionFinished = true;
            attackFlowPostBlockPassOnActionDone = true;
            if (TrySettleAttackFlowAfterOnActionPhases())
            {
                return;
            }

            CompleteAttackFlowAfterPostBlockOnAction(
                attackFlowAttackerUnit != null ? attackFlowAttackerUnit : attacker,
                attackFlowDeclaredDefenderUnit != null ? attackFlowDeclaredDefenderUnit : declaredDefenderOrNull,
                attackFlowAttackerOwner,
                defenderSideForOnAction,
                strikeKind);
        };

        attackFlowPostBlockPassInProgress = true;
        TryRunAttackActionSteps(defenderSideForOnAction, attackerOwner, finishOnAction, attacker);

        // オフラインは同期的に onComplete が返るが、オンラインは P2P 待ちのため
        // isOnActionPopupOpen だけでは完了判定できない（待機オーバーレイのみの段階がある）。
        while (!onActionFinished)
        {
            yield return null;
        }

        attackFlowPostBlockPassInProgress = false;
        attackFlowAfterBlockPassCoroutine = null;
    }

    private void CompleteAttackFlowAfterPostBlockOnAction(
        CardController attacker,
        CardController declaredDefenderOrNull,
        PlayerType attackerOwner,
        PlayerType defenderSideForOnAction,
        AttackFlowStrikeKind strikeKind)
    {
        if (strikeKind == AttackFlowStrikeKind.Shield)
        {
            TryUnitShieldAttackFromUnit(attacker, true, true, true, skipOnlineBlockPhase: true);
            return;
        }

        PlayerType defenderOwner = declaredDefenderOrNull != null
            ? ResolveCardOwner(declaredDefenderOrNull.transform)
            : defenderSideForOnAction;
        ExecuteUnitVsUnitDeclaredCombat(attacker, declaredDefenderOrNull, attackerOwner, defenderOwner);
    }

    /// <summary>ブロックを行わずにブロック選択 UI を閉じたとき、誤って確定したブロック状態を破棄する。</summary>
    private void ClearPendingBlockRedirectSelection()
    {
        attackFlowBlockRedirectUnit = null;
        attackFlowBlockRedirectEngaged = false;
        attackFlowBlockRedirectFromShieldStrike = false;
    }

    /// <summary>ブロック確定後は交換戦闘が成立しなくてもブロッカーは必ずレストする（ルール準拠）。</summary>
    private void CommitBlockerRestIfBlockWasCommitted()
    {
        if (!attackFlowBlockRedirectEngaged || attackFlowBlockRedirectUnit == null)
        {
            return;
        }

        CardController blocker = attackFlowBlockRedirectUnit;
        if (!IsUnitAliveOnAnyDeployField(blocker))
        {
            LogDestroyedBlockerInterruptDetail(
                "CommitBlockerRestIfBlockWasCommitted",
                "blocker not alive on deploy field — skip rest without exchange",
                blocker);
            return;
        }

        PlayerType blockerOwner = ResolveCardOwner(blocker.transform);
        if (!TryApplyRestToUnit(blocker))
        {
            return;
        }

        // 交換戦闘なしのブロックレストは Attack 通知に載らないため、攻撃フロー権限側から同期する。
        SyncOnlineRestFromAttackAuthority(blocker);

        Debug.Log($"[BlockCombat] Blocker rested without exchange: {blocker.Data?.cardName}");
    }

    /// <summary>攻撃宣言済みの攻撃ユニットをレストし、相手画面へ同期する（交換戦闘不成立時の補完）。</summary>
    private void CommitAttackerRestIfAttackWasDeclared()
    {
        if (attackFlowStrikeKind == AttackFlowStrikeKind.None
            && attackFlowAttackerUnit == null
            && pendingUnitAttackAttacker == null)
        {
            return;
        }

        CardController attacker = attackFlowAttackerUnit ?? pendingUnitAttackAttacker;
        if (!IsUnitAliveOnAnyDeployField(attacker))
        {
            return;
        }

        if (!attacker.IsRestState)
        {
            TryApplyRestToUnit(attacker);
        }

        SyncOnlineRestFromAttackAuthority(attacker);
        Debug.Log($"[AttackFlow] Attacker rest ensured/synced: {attacker.Data?.cardName}");
    }

    private void FinalizeBlockInterruptWithoutExchange()
    {
        CommitBlockerRestIfBlockWasCommitted();
        CommitAttackerRestIfAttackWasDeclared();
        LogArgamaShieldBlockCloseCombatDebug("FinalizeBlockInterrupt", "attack flow ending without exchange");
        pendingUnitAttackAttacker = null;
        pendingOnAttackEffectResolvedAttacker = null;
        blockExchangeCancelledForCurrentAttack = false;
        shieldStrikeAbortedAfterBlockInterrupt = false;
        attackFlowBlockRedirectCombatVoided = false;
        FinishDeferredShieldAttackBlockFlow();
        ClearAttackFlowContext();
    }

    /// <summary>OnAction 等でブロッカーが消えた時点で交換戦闘を永久に禁止（同一攻撃内）。</summary>
    private void MarkBlockExchangeCancelled(string reason, bool finalizeFlowNow = false)
    {
        if (!blockExchangeCancelledForCurrentAttack)
        {
            blockExchangeCancelledForCurrentAttack = true;
            shieldStrikeAbortedAfterBlockInterrupt = true;
            attackFlowBlockRedirectCombatVoided = true;
            Debug.Log($"[BlockCombat] Exchange cancelled: {reason}");
            LogArgamaShieldBlockCloseCombatDebug("MarkBlockExchangeCancelled", $"reason:{reason} finalizeNow:{finalizeFlowNow}");
        }

        if (finalizeFlowNow)
        {
            FinalizeBlockInterruptWithoutExchange();
        }
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
        RegisterBaseProtectionCallbacks();
    }

    private void OnDestroy()
    {
        if (gundamRule != null)
        {
            gundamRule.OnShieldDamaged -= OnGundamShieldDamaged;
        }
    }

    private void OnGundamShieldDamaged(Gundam2024RuleScript.PlayerSide side, int oldShield, int newShield, bool simultaneousReveal)
    {
        int broken = oldShield - newShield;
        if (broken <= 0)
        {
            return;
        }

        if (ShouldDeferEnemyShieldBreakToRemoteDefender(side))
        {
            _onlineDeferredEnemyShieldBreak = new OnlineDeferredEnemyShieldBreak
            {
                Count = broken,
                SimultaneousReveal = simultaneousReveal,
            };
            isShieldBreakFlowOpen = true;
            return;
        }

        isShieldBreakFlowOpen = true;
        pendingShieldBreakBatches.Enqueue(new PendingShieldBreakBatch
        {
            Side = side,
            Count = broken,
            SimultaneousReveal = simultaneousReveal,
        });
        if (!shieldBreakQueueRunning)
        {
            StartCoroutine(RunShieldBreakQueueCoroutine());
        }
    }

    private void Start()
    {
        if (DeckSettinObject.Instance != null && !DeckSettinObject.Instance.IsBattleCanvasVisible())
        {
            return;
        }

        RestartBattleFromBeginning();
    }

    /// <summary>
    /// デッキ構築・初期5枚・マリガン・ゲーム開始まで（コルーチンでUI待機を挟む）。
    /// </summary>
    private IEnumerator BattleSetupCoroutine()
    {
        Debug.Log("バトルゲームのメインシーン");
        CardFeatureRegistry.EnsureLoaded();
        NamedEffectSetRegistry.EnsureLoaded();
        InitializeBattleOpponent();
        ResetOnlineBattleInstanceIds();
        isFirstPlayer = DecideTurnOrder();
        PlayerType firstPlayerThisGame = currentPlayerType;

        const int openingHandSize = 5;
        int minDeckTotalForOpening = openingHandSize + OpeningShieldCardCount;

        playerDeckData = DeckSettinObject.Instance.LoadDeckReturn();
        enemyDeckData = DeckSettinObject.Instance.LoadEnemyDeckReturn();
        ConfigureOnlineBattleDecks(ref playerDeckData, ref enemyDeckData);
        enemyDeckData = EnsureDeckHasMinimumCardsForOpening(enemyDeckData, playerDeckData, minDeckTotalForOpening, "Enemy");
        playerDeckData = EnsureDeckHasMinimumCardsForOpening(playerDeckData, enemyDeckData, minDeckTotalForOpening, "Player");

        cardGameRule.SetUp(PlayerFieldPanel);
        cardGameRule.CreateShuffledDeck(playerDeckData, GetOnlineDeckSeed(true));
        cardGameRule.ResourcAndLevelTextGet(PlayerresourcePointText, PlayerlevelText, ExresourcePointText);
        enemyCardGameRule.SetUp(EnemyPlayerFieldPanel);
        enemyCardGameRule.PlayerFieldPanel.SetRotation(180f);
        enemyCardGameRule.CreateShuffledDeck(enemyDeckData, GetOnlineDeckSeed(false));

        cardGameRule.BindDiscardZoneToggleClick(() => cardGameRule.ToggleDiscardZoneView());
        cardGameRule.BindDiscardZoneCountClick(() => OpenDiscardZoneInspectionPanel(cardGameRule));
        enemyCardGameRule.BindDiscardZoneToggleClick(() => enemyCardGameRule.ToggleDiscardZoneView());
        enemyCardGameRule.BindDiscardZoneCountClick(() => OpenDiscardZoneInspectionPanel(enemyCardGameRule));
        BindEnemyAiPlayerTrashObservation();
        RegisterOnlineZoneSyncObservers();

        gundamRule.InitializeGame(
            cardGameRule.GetRemainingCount(),
            enemyCardGameRule.GetRemainingCount(),
            ToRuleSide(firstPlayerThisGame));

        for (int i = 0; i < openingHandSize; i++)
        {
            CardAddtoHand(cardGameRule, PlayerType.Player);
        }
        if (ShouldSkipEnemyOpeningHandOnline())
        {
            Debug.Log("[OnlineBattle] Skipped local opponent opening hand. Opponent hand is on their device.");
        }
        else
        {
            for (int i = 0; i < openingHandSize; i++)
            {
                CardAddtoHand(enemyCardGameRule, PlayerType.Enemy);
            }
        }
        currentPlayerType = firstPlayerThisGame;
        int enemyHandCountForSync = ShouldSkipEnemyOpeningHandOnline() ? enemyHandCards.Count : openingHandSize;
        gundamRule.SyncOpeningHandState(
            openingHandSize,
            cardGameRule.GetRemainingCount(),
            enemyHandCountForSync,
            enemyCardGameRule.GetRemainingCount());
        cardGameRule.RefreshHandCountDisplay();
        enemyCardGameRule.RefreshHandCountDisplay();
        Debug.Log($"[ドロー] 初期手札: プレイヤー{openingHandSize}枚、エネミー{enemyHandCountForSync}枚を引きました。");

        int exBasePoints = exBaseData != null ? exBaseData.startingPoints : 3;

        // マリガン：プレイヤーは Yes/No、エネミーは 1/2（オンラインは P2P 同期）
        Canvas canvas = ResolveBattleCanvas();
        if (canvas != null)
        {
            if (IsOnlineBattle())
            {
                yield return RunOnlineMulliganAndBootstrapCoroutine(canvas, openingHandSize, exBasePoints);
            }
            else
            {
                bool? playerChoice = null;
                isMulliganPromptOpen = true;
                yield return MulliganPromptCoroutine(
                    canvas,
                    "Do you want to shuffle your hand and draw 5 cards again? (Mulligan)",
                    value => playerChoice = value);
                isMulliganPromptOpen = false;

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

                int exBasePointsLocal = exBasePoints;
                cardGameRule.SetupShieldFromDeckAfterMulligan(CardImagePrefab, OnCardClicked, OpeningShieldCardCount, exBasePointsLocal);
                enemyCardGameRule.SetupShieldFromDeckAfterMulligan(CardImagePrefab, OnCardClicked, OpeningShieldCardCount, exBasePointsLocal);
            }
        }
        else
        {
            Debug.LogWarning("[マリガン] Canvas が見つからないため、マリガンをスキップしました。");
        }

        RefreshAllHandsConditionalOnHandAuto();

        gundamRule.SyncOpeningHandState(
            openingHandSize,
            cardGameRule.GetRemainingCount(),
            enemyHandCountForSync,
            enemyCardGameRule.GetRemainingCount());

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
        ConfigureBattleMenuButtonInHandPanel();
        if (EndTurnButton != null)
        {
            EndTurnButton.onClick.RemoveAllListeners();
            EndTurnButton.onClick.AddListener(OnEndTurnButtonClicked);
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

        RefreshAllHandsConditionalOnHandAuto();
        rule.RefreshHandCountDisplay();
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
        if (isMatchFinished || isShieldBreakFlowOpen || shieldBreakQueueRunning)
        {
            return;
        }

        if (!IsLocalOnlineTurn())
        {
            Debug.Log("[OnlineBattle] Wait for your turn.");
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
        bool isInHand = ownerRule.HandScrollContent != null
            && cardController.transform.IsChildOf(ownerRule.HandScrollContent);
        bool isInBaseSlot = IsCardInBaseSlot(cardController);
        bool isInShield = ownerRule.ShieldCardsContent != null
            && cardController.transform.IsChildOf(ownerRule.ShieldCardsContent);
        // 手札が PlayerDeployPanel 配下にある UI でも、手札カードを場扱いにしない。
        bool isOnField = !isInHand
            && !isInShield
            && (cardController.transform.IsChildOf(ownerRule.PlayerDeployPanel) || isInBaseSlot);

        bool isOnAnyDeployField = !isInHand
            && (cardController.transform.IsChildOf(cardGameRule.PlayerDeployPanel)
                || cardController.transform.IsChildOf(enemyCardGameRule.PlayerDeployPanel)
                || isInBaseSlot);

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

            if (cardController.Data.IsUnitLike() && cardController.MountedPilot != null)
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
                && cardController.Data.IsUnitLike()
                && cardController.AttackFlgState == AttackFlg.True;

            if (canShowUnitAttackMenu)
            {
                Gundam2024RuleScript.PlayerState opponentState = ownerType == PlayerType.Player
                    ? gundamRule.Enemy
                    : gundamRule.Player;
                bool showShieldAttack = gundamRule.CanShowUnitShieldAttackOption(
                    opponentState,
                    cardController.CurrentPower);
                bool showDirectAttack = !gundamRule.HasShieldZoneProtection(
                    ownerType == PlayerType.Player
                        ? Gundam2024RuleScript.PlayerSide.Enemy
                        : Gundam2024RuleScript.PlayerSide.Player);
                bool allowShieldOrDirectAttack = !cardController.CannotDirectAttackPlayerOrShield()
                    && (showShieldAttack || showDirectAttack);

                if (allowShieldOrDirectAttack)
                {
                    string shieldLabel = showDirectAttack
                        ? "Direct Attack"
                        : opponentState.exBase > 0 || HasActiveDeployedBaseForRuleSide(
                            ownerType == PlayerType.Player
                                ? Gundam2024RuleScript.PlayerSide.Enemy
                                : Gundam2024RuleScript.PlayerSide.Player)
                            ? $"Attack Shield (deal {cardController.CurrentPower} to Base/EX)"
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

                var unitAttackBtn = FilterPanel.CreateChildButton(
                    cardController.HasAttackActiveEnemyAbility()
                        ? "Attack Unit (tap enemy unit)"
                        : "Attack Unit (tap enemy REST unit)");
                RectTransform unitAtkRect = unitAttackBtn.GetComponent<RectTransform>();
                unitAtkRect.sizeDelta = new Vector2(320, 50);
                unitAtkRect.anchoredPosition = new Vector2(0, -70);
                unitAttackBtn.onClick.AddListener(() =>
                {
                    pendingUnitAttackAttacker = cardController;
                    pendingOnAttackEffectResolvedAttacker = null;
                    ClearOnAttackPreCombatCompletedForNewAttack();
                    OpenEnemyUnitAttackTargetSelectionUI(cardController, ownerType);
                    Destroy(FilterPanel);
                });

                closeBtnRect.anchoredPosition = new Vector2(0, -200);
            }

            float fieldActionY = canShowUnitAttackMenu ? -130f : -70f;
            if (TryAddOnRestSelfActivateButton(FilterPanel, cardController, ownerType, fieldActionY))
            {
                fieldActionY -= 60f;
            }

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

        // シールドエリア：OnRest の能動起動を許可
        if (isInShield)
        {
            if (TryAddOnRestSelfActivateButton(FilterPanel, cardController, ownerType, -10f))
            {
                closeBtnRect.anchoredPosition = new Vector2(0, -80f);
            }
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

        if (CanDeployShieldFromHand(cardController))
        {
            float shieldActionY = -10f;
            if (TryAddOnMainEffectApplyButton(FilterPanel, cardController, ownerType, shieldActionY))
            {
                shieldActionY -= 60f;
                closeBtnRect.anchoredPosition = new Vector2(0, shieldActionY - 70f);
            }

            var deployShieldBtn = FilterPanel.CreateChildButton("Deploy Shield");
            RectTransform shieldBtnRect = deployShieldBtn.GetComponent<RectTransform>();
            shieldBtnRect.sizeDelta = new Vector2(240, 50);
            shieldBtnRect.anchoredPosition = new Vector2(0, shieldActionY);
            deployShieldBtn.onClick.AddListener(() =>
            {
                DeployShieldCardFromHand(cardController, ownerType, ownerRule);
                Destroy(FilterPanel);
            });
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
                if (!TryPayHandDeployCost(ownerSide, cardController, requiredEx))
                {
                    Debug.Log("EX/リソースが不足しているため配備できません。");
                    return;
                }

                if (cardController.Data.type == Type.Base)
                {
                    BeginDeployBaseFromHand(cardController, ownerType, ownerRule);
                }
                else
                {
                    SendCardToField(cardController, ownerType, ownerRule);
                }

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

        if (cardController.Data.type == Type.Base)
        {
            var deployBaseBtn = FilterPanel.CreateChildButton("Deploy Base");
            RectTransform baseBtnRect = deployBaseBtn.GetComponent<RectTransform>();
            baseBtnRect.sizeDelta = new Vector2(240, 50);
            baseBtnRect.anchoredPosition = new Vector2(0, handActionY);
            deployBaseBtn.onClick.AddListener(() =>
            {
                if (!TryPayHandDeployCost(ownerSide, cardController, 0))
                {
                    Debug.Log("リソースポイントが足りません！");
                    return;
                }

                BeginDeployBaseFromHand(cardController, ownerType, ownerRule);
                SyncResourceViewsFromRule(ownerSide);
                Destroy(FilterPanel);
            });
            return;
        }

        var playButton = FilterPanel.CreateChildButton("send to field");
        RectTransform btnRect = playButton.GetComponent<RectTransform>();
        btnRect.sizeDelta = new Vector2(240, 50);
        btnRect.anchoredPosition = new Vector2(0, handActionY);

        playButton.onClick.AddListener(() =>
        {
            if (!TryPayHandDeployCost(ownerSide, cardController, 0))
            {
                Debug.Log("リソースポイントが足りません！");
                return;
            }

            if (cardController.Data.type == Type.Base)
            {
                BeginDeployBaseFromHand(cardController, ownerType, ownerRule);
            }
            else
            {
                SendCardToField(cardController, ownerType, ownerRule);
            }

            SyncResourceViewsFromRule(ownerSide);
            Destroy(FilterPanel);
        });
        
        // Instantiate(CardImagePrefab, playerHandTransform);
    }
    //! 以下の関数もCardGameRuleに移す予定。
    void CardAddtoHand(CardGameRule targetRule, PlayerType targetType)
    {
        CardAddtoHandAndReturn(targetRule, targetType);
    }

    private CardController CardAddtoHandAndReturn(CardGameRule targetRule, PlayerType targetType)
    {
        int cardId = targetRule.Draw();
        if (cardId < 0)
        {
            Debug.LogWarning("山札切れでドローできませんでした。");
            return null;
        }

        CardData drawCardData = DeckSettinObject.Instance.GetCardDataById(cardId);
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
        targetRule.RefreshHandCountDisplay();
        return drawnCard;
    }
    public bool DecideTurnOrder()
    {
        // 先攻後攻は1回の乱数で決定（isFirstPlayer / currentPlayerType / currentPlayer を矛盾なく同期）
        bool playerGoesFirst = TryOverrideTurnOrderFromOnlineMatch(out bool onlinePlayerGoesFirst)
            ? onlinePlayerGoesFirst
            : Random.value < 0.5f;
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
        if (isMatchFinished || isShieldBreakFlowOpen || shieldBreakQueueRunning)
        {
            return;
        }

        switch (nextPhase)
        {
            case BattlePhase.StartTurn:
                if (!isTurnPhaseSequenceRunning && !isEnemyMainPhaseCoroutineRunning)
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
        yield return WaitForShieldBreakFlowCompleteCoroutine();
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
        yield return WaitForBattleFlowIdleCoroutine();
        yield return ShowPhasePauseCoroutine("End Phase");
        while (isEnemyMainPhaseCoroutineRunning || IsBattleFlowBlockingTurnProgress())
        {
            yield return null;
        }

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
        RefreshAllFieldOwnerTurnPassives();

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
            ClearPaidActivationUsesForSide(PlayerType.Player);
            TriggerAllTimedEffectsForSide(PlayerType.Player, EffectTiming.OnTurnStart);
        }
        else
        {
            Debug.Log("エネミーのターン開始処理を実行します。");
            gundamRule.SetCurrentTurnPlayer(Gundam2024RuleScript.PlayerSide.Enemy);
            gundamRule.BeginTurn();
            if (!ShouldSkipEnemyDrawOnline())
            {
                CardAddtoHand(enemyCardGameRule, PlayerType.Enemy);
            }
            else
            {
                Debug.Log("[OnlineBattle] Skipped local opponent draw. Opponent draws on their device.");
            }
            SyncResourceViewsFromRule(Gundam2024RuleScript.PlayerSide.Enemy);
            Debug.Log($"[ドロー] エネミーのターン開始ドロー1枚。LV:{gundamRule.Enemy.level} Resource:{gundamRule.Enemy.resource}");
            ApplyTurnStartAttackFlgForCurrentPlayer();
            ClearPaidActivationUsesForSide(PlayerType.Enemy);
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
            battleOpponent?.OnEnterEnemyMainPhase(this);
        }


    }
    IEnumerator EnemyActionCoroutine()
    {
        if (isMatchFinished)
        {
            yield break;
        }

        isEnemyMainPhaseCoroutineRunning = true;
        try
        {
            Debug.Log("エネミーの行動を開始します。");
            yield return new WaitForSeconds(0.8f);

            int deployedCount = TryEnemyDeployAllAffordableUnitsFromHand();
            if (deployedCount > 0)
            {
                yield return new WaitForSeconds(0.6f);
            }

            int mountedCount = TryEnemyMountAllAffordablePilotsFromHand();
            if (mountedCount > 0)
            {
                yield return WaitForBattleFlowIdleCoroutine();
                yield return new WaitForSeconds(0.6f);
            }

            if (TryEnemyExecuteOnMainFromHand())
            {
                yield return WaitForBattleFlowIdleCoroutine();
                yield return new WaitForSeconds(0.15f);
            }

            if (TryEnemyExecuteScoredOnRestBeforeAttacks())
            {
                yield return new WaitForSeconds(0.4f);
            }

            int attacked = 0;
            while (true)
            {
                yield return WaitForBattleFlowIdleCoroutine();

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
                    if (IsBattleFlowBlockingTurnProgress())
                    {
                        yield return WaitForBattleFlowIdleCoroutine();
                        yield return new WaitForSeconds(0.15f);
                        continue;
                    }

                    break;
                }

                attacked += attackedNow;
                yield return WaitForBattleFlowIdleCoroutine();

                yield return new WaitForSeconds(0.6f);
            }

            if (TryEnemyDeployBaseWhenIdle())
            {
                yield return new WaitForSeconds(0.5f);
            }

            yield return WaitForBattleFlowIdleCoroutine();
            Debug.Log($"エネミーの行動が終了しました。deployUnits:{deployedCount} shieldAttack:{attacked}");
            ChangePhase(BattlePhase.EndTurn);
        }
        finally
        {
            isEnemyMainPhaseCoroutineRunning = false;
        }
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
            if (unit == null || unit.Data == null || !unit.Data.IsUnitLike())
            {
                continue;
            }

            if (unit.AttackFlgState != AttackFlg.True)
            {
                continue;
            }

            bool canAttackShield = gundamRule.CanShowUnitShieldAttackOption(gundamRule.Player, unit.CurrentPower);
            bool canDirectAttack = !gundamRule.HasShieldZoneProtection(Gundam2024RuleScript.PlayerSide.Player);
            bool canShieldOrDirectAttack = !unit.CannotDirectAttackPlayerOrShield() && (canAttackShield || canDirectAttack);
            List<CardController> restTargets = GetEnemyUnitAttackTargets(PlayerType.Enemy, unit);
            bool canAttackUnit = restTargets.Count > 0;
            if (!canShieldOrDirectAttack && !canAttackUnit)
            {
                continue;
            }

            List<CardController> eligibleEnemyHand = CollectEligibleEnemyHandCommandsForEnemyAiSim();
            if (canAttackUnit && restTargets.Count > 0)
            {
                LogEnemyAiPreAttackUnitAttackSimulation(unit, restTargets, eligibleEnemyHand);
            }

            if (canShieldOrDirectAttack)
            {
                Debug.Log($"[EnemyAI] canAttackShield:{canAttackShield} canDirectAttack:{canDirectAttack} isNotDirectAttack:{unit.CannotDirectAttackPlayerOrShield()}");
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
                canShieldOrDirectAttack,
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
            if (IsBattleFlowBlockingTurnProgress())
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

    private List<CardController> GetAliveRestEnemyUnitsForOwner(PlayerType ownerType)
    {
        List<CardController> enemies = GetAliveEnemyUnits(ownerType);
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

    /// <summary>攻撃者がユニット戦で選べる敵ユニット一覧（通常は REST のみ。アクティブ攻撃効果で ACTIVE も可）。</summary>
    private List<CardController> GetEnemyUnitAttackTargets(PlayerType attackerOwner, CardController attacker)
    {
        List<CardController> enemies = GetAliveEnemyUnits(attackerOwner);
        if (attacker == null || !attacker.HasAttackActiveEnemyAbility())
        {
            return GetAliveRestEnemyUnitsForOwner(attackerOwner);
        }

        List<CardController> result = new List<CardController>(enemies.Count);
        for (int i = 0; i < enemies.Count; i++)
        {
            CardController enemy = enemies[i];
            if (enemy == null)
            {
                continue;
            }

            if (enemy.IsRestState || attacker.CanAttackerTargetActiveEnemy(enemy))
            {
                result.Add(enemy);
            }
        }

        return result;
    }

    private static bool CanAttackerTargetEnemyUnitForCombat(CardController attacker, CardController target)
    {
        if (target == null || target.Data == null || !target.Data.IsUnitLike())
        {
            return false;
        }

        if (target.IsRestState)
        {
            return true;
        }

        return attacker != null && attacker.CanAttackerTargetActiveEnemy(target);
    }

    private List<CardController> GetEnemyAiRestTargets(PlayerType attackerOwner)
    {
        return GetAliveRestEnemyUnitsForOwner(attackerOwner);
    }

    private List<CardController> GetAliveEnemyUnitsForEffectTarget(PlayerType ownerType, TargetType targetType)
    {
        if (targetType == TargetType.RestEnemyUnit)
        {
            return GetAliveRestEnemyUnitsForOwner(ownerType);
        }

        return GetAliveEnemyUnits(ownerType);
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
        EnemyAiEffectPickContext ctx = BuildEnemyAiEffectPickContext(commandOwnerSide, command, null, null);
        ApplyEnemyHandCommandVirtualEffects(working, onActionEffects, command, commandOwnerSide, ctx);
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

            if (!unit.Data.IsBlockerUnit())
            {
                continue;
            }

            reactionCandidates.Add(unit);
        }

        return reactionCandidates;
    }

    private bool CouldPlayerUnitRedirectShieldAttackToUnitCombat(CardController playerUnit)
    {
        return IsBlockRedirectReactionReady(playerUnit, PlayerType.Player);
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
                .Append(" isBlocker:").Append(ru.Data.IsBlockerUnit())
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
        if (isEndTurnFlowRunning || isEnemyMainPhaseCoroutineRunning || IsBattleFlowBlockingTurnProgress())
        {
            return;
        }

        StartCoroutine(ExecuteEndTurnCoroutine());
    }

    private void OnEndTurnButtonClicked()
    {
        if (!IsLocalOnlineTurn())
        {
            Debug.Log("[OnlineBattle] Wait for your turn.");
            return;
        }

        ChangePhase(BattlePhase.EndTurn);
    }

    /// <summary>OnAction / 攻撃フロー / シールド処理など、ターン進行を止める UI・処理が走っているか。</summary>
    private bool IsBattleFlowBlockingTurnProgress()
    {
        if (isMatchFinished)
        {
            return false;
        }

        return isOnActionPopupOpen
            || isAttackedSidePanelOpen
            || isActionThinkPauseOpen
            || isMulliganPromptOpen
            || isMulliganThinkPauseOpen
            || ShouldBlockOnlineLocalPlayDueToOnAction()
            || isOnlineShieldBreakThinkPauseOpen
            || isShieldBreakFlowOpen
            || shieldBreakQueueRunning
            || isShieldAttackResolving
            || attackFlowStrikeKind != AttackFlowStrikeKind.None
            || pendingUnitAttackAttacker != null
            || pendingOnAttackEffectResolvedAttacker != null;
    }

    private IEnumerator WaitForBattleFlowIdleCoroutine()
    {
        while (IsBattleFlowBlockingTurnProgress())
        {
            yield return null;
        }
    }

    private void ReleaseOnActionPopupState(GameObject root)
    {
        if (activeOnActionPopupRoot == root)
        {
            activeOnActionPopupRoot = null;
        }

        isOnActionPopupOpen = false;
    }

    private IEnumerator ExecuteEndTurnCoroutine()
    {
        isEndTurnFlowRunning = true;
        yield return WaitForShieldBreakFlowCompleteCoroutine();
        yield return WaitForBattleFlowIdleCoroutine();
        pendingUnitAttackAttacker = null;
        pendingOnAttackEffectResolvedAttacker = null;
        PlayerType endingTurnSide = currentPlayerType;
        bool waitingForClose = false;
        bool startedOnActionStep = TryRunTurnEndOnActionPhases(endingTurnSide, () => waitingForClose = false);
        if (startedOnActionStep)
        {
            waitingForClose = true;
            yield return new WaitUntil(() => !waitingForClose);
        }

        ApplyTurnEndRepairForAllInPlayUnits();
        TriggerAllTimedEffectsForSide(endingTurnSide, EffectTiming.OnTurnEnd);
        // ターン終了時は盤面全体の「ターン終了で切れる補正」を解除する。
        ClearTimedStatModifiersForAllInPlayCards(EffectDuration.UntilEndOfTurn);
        ClearAttackActiveEnemyGrants(EffectDuration.UntilEndOfTurn);
        ClearNotDirectAttackGrants(EffectDuration.UntilEndOfTurn);
        DumpTurnResourceUsageLogs(endingTurnSide, "end turn");
        NotifyLocalPlayerEndedTurn();

        // プレイヤーとエネミーのターンを切り替える
        currentPlayerType = (currentPlayerType == PlayerType.Player) ? PlayerType.Enemy : PlayerType.Player;
        RefreshAllFieldOwnerTurnPassives();
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

    private void ReconcileShieldStateWithZone(Gundam2024RuleScript.PlayerSide side, bool force = false)
    {
        if (gundamRule == null || (!force && (shieldBreakQueueRunning || isShieldBreakFlowOpen)))
        {
            return;
        }

        CardGameRule rule = side == Gundam2024RuleScript.PlayerSide.Player ? cardGameRule : enemyCardGameRule;
        if (rule == null)
        {
            return;
        }

        gundamRule.SyncShieldCountFromZone(side, rule.GetShieldZoneCardCount());
    }

    private void SyncResourceViewsFromRule(Gundam2024RuleScript.PlayerSide side)
    {
        CardGameRule targetRule = side == Gundam2024RuleScript.PlayerSide.Player ? cardGameRule : enemyCardGameRule;
        Gundam2024RuleScript.PlayerState state = side == Gundam2024RuleScript.PlayerSide.Player ? gundamRule.Player : gundamRule.Enemy;
        targetRule.ApplyExternalResourceState(state.TotalLevel, state.resource, state.exResource);
        ReconcileShieldStateWithZone(side);
        int shieldDisplayCount = targetRule != null ? targetRule.GetShieldZoneCardCount() : state.shield;
        targetRule.SetShieldCountDisplay(shieldDisplayCount);
        SyncBaseZoneHeaderDisplay(side);
        targetRule.RefreshHandCountDisplay();

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

    private void SendCardToTrash(CardController cardController, PlayerType ownerType, CardController destroyedBy = null)
    {
        if (cardController == null || cardController.Data == null)
        {
            return;
        }

        if (unitsPendingSendToTrash.Contains(cardController))
        {
            return;
        }

        unitsPendingSendToTrash.Add(cardController);

        PruneObservedUnitWatchesOnCardRemoved(cardController);

        if (attackFlowBlockRedirectUnit != null
            && cardController == attackFlowBlockRedirectUnit
            && cardController.Data != null
            && cardController.Data.IsUnitLike())
        {
            LogArgamaShieldBlockCloseCombatDebug(
                "SendCardToTrash_Blocker",
                "blocker entering trash pipeline",
                attackFlowAttackerUnit,
                cardController);
            MarkBlockExchangeCancelled("Blocker entered trash during block flow.");
        }

        if (cardController.Data.IsUnitLike() && cardController.MountedPilot != null)
        {
            CardController pilot = cardController.DetachMountedPilotWithoutDestroy();
            if (pilot != null)
            {
                SendCardToTrash(pilot, ownerType);
            }
        }

        TriggerOnDestroyedEffects(cardController, ownerType, () =>
        {
            TriggerOnEnemyUnitDestroyedEffects(cardController, ownerType, destroyedBy, () =>
            {
                if (TryResolveEnemyUnitKillContext(
                        cardController,
                        ownerType,
                        destroyedBy,
                        out CardController killer,
                        out PlayerType killerOwner))
                {
                    TriggerObservedUnitWatchEffects(
                        cardController,
                        ownerType,
                        killer,
                        killerOwner,
                        ObservedUnitTriggerKind.EnemyUnitDestroyed,
                        () => FinishSendCardToTrash(cardController, ownerType));
                }
                else
                {
                    FinishSendCardToTrash(cardController, ownerType);
                }
            });
        });
    }

    private bool TryResolveEnemyUnitKillContext(
        CardController destroyedUnit,
        PlayerType destroyedOwner,
        CardController destroyedBy,
        out CardController killer,
        out PlayerType killerOwner)
    {
        killer = null;
        killerOwner = default;
        if (destroyedUnit == null
            || destroyedUnit.Data == null
            || !destroyedUnit.Data.IsUnitLike()
            || destroyedBy == null
            || destroyedBy.Data == null
            || !destroyedBy.Data.IsUnitLike())
        {
            return false;
        }

        killerOwner = ResolveCardOwner(destroyedBy.transform);
        if (killerOwner == destroyedOwner)
        {
            return false;
        }

        killer = destroyedBy;
        return true;
    }

    /// <summary>効果・戦闘でユニットを破壊したとき、キル元として記録するカード（敵ユニット撃破時のみ）。</summary>
    private CardController ResolveUnitKillSourceForTrash(CardController effectSource, CardController destroyedUnit)
    {
        if (effectSource == null
            || destroyedUnit == null
            || effectSource.Data == null
            || destroyedUnit.Data == null
            || !effectSource.Data.IsUnitLike())
        {
            return null;
        }

        PlayerType sourceOwner = ResolveCardOwner(effectSource.transform);
        PlayerType destroyedOwner = ResolveCardOwner(destroyedUnit.transform);
        return sourceOwner != destroyedOwner ? effectSource : null;
    }

    private void FinishSendCardToTrash(CardController cardController, PlayerType ownerType)
    {
        FinalizeRemoveCardFromPlay(
            cardController,
            ownerType,
            sendToTrashZone: cardController != null
                && cardController.Data != null
                && !cardController.Data.LeavesPlayWithoutZone());
    }

    private void RegisterCardInHandLists(CardController card, PlayerType ownerType)
    {
        if (card == null || card.Data == null)
        {
            return;
        }

        if (ownerType == PlayerType.Player)
        {
            if (!playerHandCards.Contains(card.Data))
            {
                playerHandCards.Add(card.Data);
            }
        }
        else if (!enemyHandCards.Contains(card.Data))
        {
            enemyHandCards.Add(card.Data);
        }
    }

    /// <summary>バトルゾーンのユニット（搭乗パイロット含む）をオーナーの手札へ戻す。</summary>
    private bool TryReturnBattleUnitToHand(CardController unit)
    {
        if (unit == null || unit.Data == null || !unit.Data.IsUnitLike() || !IsCardOnBattleZone(unit))
        {
            return false;
        }

        if (unit.Data.IsUnitToken())
        {
            return TryVanishBattleUnitTokenFromZone(unit);
        }

        PlayerType ownerType = ResolveCardOwner(unit.transform);
        CardGameRule rule = ownerType == PlayerType.Player ? cardGameRule : enemyCardGameRule;
        if (rule?.HandScrollContent == null)
        {
            return false;
        }

        CardController pilot = unit.DetachMountedPilotWithoutDestroy();
        if (pilot != null)
        {
            TryReturnCardInstanceToHand(pilot, ownerType, rule);
        }

        playerBattleZoneCards.Remove(unit);
        enemyBattleZoneCards.Remove(unit);
        unit.ResetRuntimeStatsFromData();
        unit.CleanupUnitBattleMountVisuals();
        unit.SetAttackFlg(AttackFlg.False);
        unit.SetUnitRestVisual(false);
        unit.RevealShieldFace();

        unit.transform.SetParent(rule.HandScrollContent, false);
        RectTransform unitRt = unit.GetComponent<RectTransform>();
        if (unitRt != null)
        {
            unitRt.localScale = Vector3.one;
        }

        RegisterCardInHandLists(unit, ownerType);
        TriggerOnHandAutoEffects(unit, ownerType, skipHandZoneCheck: true);
        Debug.Log($"[Bounce] {unit.Data.cardName}(id:{unit.Data.id}) → {ownerType} hand");
        return true;
    }

    private bool TryReturnCardInstanceToHand(CardController card, PlayerType ownerType, CardGameRule rule)
    {
        if (card == null || card.Data == null || rule?.HandScrollContent == null)
        {
            return false;
        }

        playerBattleZoneCards.Remove(card);
        enemyBattleZoneCards.Remove(card);
        card.ResetRuntimeStatsFromData();
        card.CleanupUnitBattleMountVisuals();
        card.SetAttackFlg(AttackFlg.False);
        card.SetUnitRestVisual(false);
        card.RevealShieldFace();
        card.transform.SetParent(rule.HandScrollContent, false);
        RectTransform rt = card.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.localScale = Vector3.one;
        }

        RegisterCardInHandLists(card, ownerType);
        TriggerOnHandAutoEffects(card, ownerType, skipHandZoneCheck: true);
        rule.RefreshHandCountDisplay();
        return true;
    }

    private void ApplyBounceEffect(EffectData effect, List<CardController> targets)
    {
        if (effect == null || targets == null || targets.Count == 0)
        {
            return;
        }

        int limit = effect.value > 0 ? effect.value : targets.Count;
        int applied = 0;
        for (int i = 0; i < targets.Count && applied < limit; i++)
        {
            CardController target = targets[i];
            if (target == null || target.Data == null || !target.Data.IsUnitLike() || !IsCardOnBattleZone(target))
            {
                continue;
            }

            // オンライン同期は場から外す前にキュー（zoneIndex / instanceId を保持するため）。
            QueueOnlineUnitBounce(target);
            if (TryReturnBattleUnitToHand(target))
            {
                applied++;
            }
        }

        if (applied > 0)
        {
            Debug.Log($"[Effect] Bounce applied:{applied} target:{effect.target}");
        }
    }

    private void ApplyRestEffect(EffectData effect, List<CardController> targets)
    {
        if (effect == null || targets == null || targets.Count == 0)
        {
            return;
        }

        int limit = effect.value > 0 ? effect.value : targets.Count;
        int applied = 0;
        for (int i = 0; i < targets.Count && applied < limit; i++)
        {
            if (TryApplyRestToUnit(targets[i]))
            {
                QueueOnlineUnitRest(targets[i]);
                applied++;
            }
        }

        if (applied > 0)
        {
            Debug.Log($"[Effect] Rest applied:{applied} target:{effect.target}");
        }
    }

    private void ApplyGrantAttackFlagEffect(EffectData effect, PlayerType ownerType, List<CardController> targets)
    {
        if (effect == null || effect.type != EffectType.GrantAttackFlag || targets == null || targets.Count == 0)
        {
            return;
        }

        int magnitude = effect.value;
        int limit = effect.GetGrantAttackFlagCount(magnitude > 0 ? magnitude : 1);
        int applied = 0;
        for (int i = 0; i < targets.Count && applied < limit; i++)
        {
            CardController target = targets[i];
            if (target == null || target.Data == null || !target.Data.IsUnitLike() || target.CurrentHp <= 0)
            {
                continue;
            }

            if (!effect.MatchesSelectableBattleZoneTarget(target))
            {
                continue;
            }

            if (target.AttackFlgState == AttackFlg.True)
            {
                continue;
            }

            target.SetAttackFlg(AttackFlg.True);
            applied++;
            Debug.Log(
                $"[Effect] GrantAttackFlag → {target.Data.cardName}(id:{target.Data.id}) owner:{ownerType}");
        }

        if (applied > 0)
        {
            Debug.Log($"[Effect] GrantAttackFlag applied:{applied} target:{effect.target}");
        }
    }

    private void ApplyDestroyEffect(
        CardController sourceCard,
        PlayerType ownerType,
        EffectData effect,
        List<CardController> targets)
    {
        if (effect == null || targets == null || targets.Count == 0)
        {
            return;
        }

        int limit = effect.value > 0 ? effect.value : targets.Count;
        int applied = 0;
        for (int i = 0; i < targets.Count && applied < limit; i++)
        {
            CardController target = targets[i];
            if (target == null || target.Data == null || !target.Data.IsUnitLike() || target.CurrentHp <= 0)
            {
                continue;
            }

            if (!IsCardControllerInstanceValid(target))
            {
                continue;
            }

            PlayerType targetOwner = ResolveCardOwner(target.transform);
            TryLogAttackBlockCloseCombatTrioDestroy("ApplyDestroyEffect", target, sourceCard);
            NotifyBlockRedirectUnitRemovedDuringAttackFlow(target);
            QueueOnlineUnitDestroy(target);
            SendCardToTrash(target, targetOwner, ResolveUnitKillSourceForTrash(sourceCard, target));
            applied++;
        }

        if (applied > 0)
        {
            Debug.Log(
                $"[Effect] Destroy applied:{applied} target:{effect.target} by cardId:{sourceCard?.Data?.id} owner:{ownerType}");
        }
    }

    private static bool TryApplyRestToUnit(CardController unit)
    {
        if (unit == null || unit.Data == null || !unit.Data.IsUnitLike() || unit.CurrentHp <= 0)
        {
            return false;
        }

        if (unit.IsRestState)
        {
            return false;
        }

        unit.SetAttackFlg(AttackFlg.False);
        unit.SetUnitRestVisual(true);
        return true;
    }

    private static void FilterOutAlreadyRestedUnits(List<CardController> targets)
    {
        if (targets == null)
        {
            return;
        }

        for (int i = targets.Count - 1; i >= 0; i--)
        {
            CardController c = targets[i];
            if (c != null && c.IsRestState)
            {
                targets.RemoveAt(i);
            }
        }
    }

    /// <summary>ユニットをバトルゾーンへ配備した直後の AttackFlg / 見た目を設定する。</summary>
    private static void ApplyUnitDeployFieldAttackState(CardController cardController)
    {
        if (cardController == null || cardController.Data == null || !cardController.Data.IsUnitLike())
        {
            return;
        }

        bool canAttackOnDeployTurn = cardController.Data.CanAttackOnDeployTurn();
        cardController.SetAttackFlg(canAttackOnDeployTurn ? AttackFlg.True : AttackFlg.False);
        cardController.SetUnitRestVisual(false);
    }

    private void SendCardToField(CardController cardController, PlayerType ownerType, CardGameRule ownerRule)
    {
        if (cardController == null || ownerRule == null)
        {
            return;
        }

        if (ownerType == PlayerType.Player)
        {
            RecordEnemyAiObservedPlayerCardPlay(cardController, "DeployUnit");
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

        cardController.SetEligibleForShieldZoneDeploy(false);

        // ユニット配備直後はアクティブ（起き状態）で配置する。
        if (cardController.Data.IsUnitLike())
        {
            cardController.ResetRuntimeStatsFromData();
            ApplyUnitDeployFieldAttackState(cardController);
            AssignBattleInstanceIdIfNeeded(cardController);
            ApplyPilotMountFieldAurasToDeployedUnit(cardController, ownerType);
        }

        StartCoroutine(TriggerOnPlayedEffectsAfterDeployCoroutine(cardController, ownerType));

        ownerRule.RefreshHandCountDisplay();
        if (ownerType == PlayerType.Player)
        {
            NotifyLocalPlayCardDeployed(cardController);
        }
    }

    /// <summary>手札 UI 閉鎖後に OnPlayed を解決（選択 UI が確実に表示されるよう1フレーム遅延）。</summary>
    private IEnumerator TriggerOnPlayedEffectsAfterDeployCoroutine(CardController sourceCard, PlayerType ownerType)
    {
        yield return null;
        bool finished = false;
        TriggerOnPlayedEffects(sourceCard, ownerType, () => finished = true);
        yield return new WaitUntil(() => finished);
        RefreshAllFieldOwnerTurnPassives();
    }

    private List<CardController> GetMountableUnits(PlayerType ownerType)
    {
        List<CardController> source = ownerType == PlayerType.Player ? playerBattleZoneCards : enemyBattleZoneCards;
        List<CardController> result = new List<CardController>();
        foreach (CardController c in source)
        {
            if (c == null || c.Data == null || !c.Data.IsUnitLike())
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
                if (!TryPayHandDeployCost(ownerSide, pilotCard, exToUse))
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

                if (ownerType == PlayerType.Player)
                {
                    NotifyLocalPilotMounted(target, pilotCard);
                }

                if (ownerType == PlayerType.Player)
                {
                    RecordEnemyAiObservedPlayerCardPlay(pilotCard, "MountPilot");
                }

                Debug.Log($"[Pilot] {pilotCard.Data.cardName} を {target.Data.cardName} に搭乗。AP:{target.CurrentPower} HP:{target.CurrentHp}");
                ApplyUnitAttackFlgFromLink(target, ownerType);
                TriggerOnPilotMountedEffects(target, pilotCard, ownerType, () =>
                {
                    TriggerOnLinkEffects(target, pilotCard, ownerType, () =>
                    {
                        TriggerOnPlayedEffects(pilotCard, ownerType, RefreshAllHandsConditionalOnHandAuto);
                    });
                });
                SyncResourceViewsFromRule(ownerSide);
                Destroy(filterPanel);
            });
        }
    }

    /// <summary>
    /// 自分ターン開始時：場の自軍ユニットをアクティブ(True)へ更新。
    /// 表示は起き状態になり、この状態で攻撃可能。
    /// OnRest でレストしたベース・シールド上のカードも ACTIVE に戻す。
    /// </summary>
    /// <summary>搭乗直後：Link ユニットに条件パイロットが載ったときだけ、出したターンでも AttackFlg を True にする。</summary>
    private void ApplyUnitAttackFlgFromLink(CardController unit, PlayerType ownerType)
    {
        if (unit == null || unit.Data == null || !unit.Data.IsUnitLike())
        {
            return;
        }

        if (ownerType != currentPlayerType)
        {
            return;
        }

        if (UnitLinkExtensions.GrantsSameTurnAttackOnLink(unit.Data, unit.MountedPilot))
        {
            unit.SetAttackFlg(AttackFlg.True);
        }
    }

    private void ApplyTurnStartAttackFlgForCurrentPlayer()
    {
        PlayerType side = currentPlayerType;
        CardGameRule rule = side == PlayerType.Player ? cardGameRule : enemyCardGameRule;
        List<CardController> battleZone = side == PlayerType.Player ? playerBattleZoneCards : enemyBattleZoneCards;

        Debug.Log(
            side == PlayerType.Player
                ? "[TurnStart] プレイヤー：ユニットをアクティブ化、レスト中のベース/シールドを起こす"
                : "[TurnStart] エネミー：ユニットをアクティブ化、レスト中のベース/シールドを起こす");

        for (int i = 0; i < battleZone.Count; i++)
        {
            CardController c = battleZone[i];
            if (c == null || c.Data == null || !c.Data.IsUnitLike())
            {
                continue;
            }

            c.SetAttackFlg(AttackFlg.True);
            if (c.IsRestState)
            {
                c.SetUnitRestVisual(false);
            }
        }

        StandRestingCardsInZone(rule?.DeployedBase);
        if (rule?.BaseSlotContent != null)
        {
            for (int i = 0; i < rule.BaseSlotContent.childCount; i++)
            {
                StandRestingCardsInZone(rule.BaseSlotContent.GetChild(i).GetComponent<CardController>());
            }
        }

        if (rule?.ShieldCardsContent != null)
        {
            for (int i = 0; i < rule.ShieldCardsContent.childCount; i++)
            {
                StandRestingCardsInZone(rule.ShieldCardsContent.GetChild(i).GetComponent<CardController>());
            }
        }
    }

    private static void StandRestingCardsInZone(CardController card)
    {
        if (card == null || !card.IsRestState)
        {
            return;
        }

        card.SetUnitRestVisual(false);
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

    /// <summary>トラッシュ／除外ゾーンの一覧（後方互換）。<see cref="OpenDiscardZoneInspectionPanel"/> を使用してください。</summary>
    private void OpenTrashInspectionPanel(CardGameRule rule)
    {
        OpenDiscardZoneInspectionPanel(rule);
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

    private static bool IsCardControllerInstanceValid(CardController c)
    {
        return c != null && c.gameObject != null;
    }

    private void CancelPendingUnitAttackFlow()
    {
        CommitBlockerRestIfBlockWasCommitted();
        CommitAttackerRestIfAttackWasDeclared();
        FinishDeferredShieldAttackBlockFlow();
        pendingUnitAttackAttacker = null;
        pendingOnAttackEffectResolvedAttacker = null;
        ClearOnAttackPreCombatCompletedForNewAttack();
        blockExchangeCancelledForCurrentAttack = false;
        shieldStrikeAbortedAfterBlockInterrupt = false;
        attackFlowBlockRedirectCombatVoided = false;
        ClearAttackFlowContext();
    }

    private static bool IsSameBattleUnit(CardController a, CardController b)
    {
        if (a == null || b == null)
        {
            return false;
        }

        if (ReferenceEquals(a, b))
        {
            return true;
        }

        return a.BattleInstanceId > 0 && a.BattleInstanceId == b.BattleInstanceId;
    }

    /// <summary>OnAction 中の効果で攻撃フロー参加者が除去されたとき、再開前に中断フラグを立てる。</summary>
    private void NotifyAttackFlowParticipantRemovedDuringOnAction(CardController unit)
    {
        if (unit == null || attackFlowStrikeKind == AttackFlowStrikeKind.None)
        {
            return;
        }

        if (attackFlowBlockRedirectUnit != null && IsSameBattleUnit(unit, attackFlowBlockRedirectUnit))
        {
            NotifyBlockRedirectUnitRemovedDuringAttackFlow(attackFlowBlockRedirectUnit);
            return;
        }

        if (attackFlowAttackerUnit != null && IsSameBattleUnit(unit, attackFlowAttackerUnit))
        {
            MarkBlockExchangeCancelled("Attacker removed by effect during attack OnAction.", finalizeFlowNow: false);
            return;
        }

        if (!attackFlowBlockRedirectEngaged
            && attackFlowDeclaredDefenderUnit != null
            && IsSameBattleUnit(unit, attackFlowDeclaredDefenderUnit))
        {
            MarkBlockExchangeCancelled("Defender removed by effect during attack OnAction.", finalizeFlowNow: false);
        }
    }

    private void NotifyAttackFlowParticipantRemovedByInstanceId(int instanceId)
    {
        if (instanceId <= 0 || attackFlowStrikeKind == AttackFlowStrikeKind.None)
        {
            return;
        }

        CardController unit = FindUnitByInstanceIdEitherZone(instanceId);
        if (unit != null)
        {
            NotifyAttackFlowParticipantRemovedDuringOnAction(unit);
            return;
        }

        if (attackFlowBlockRedirectUnit != null && attackFlowBlockRedirectUnit.BattleInstanceId == instanceId)
        {
            NotifyBlockRedirectUnitRemovedDuringAttackFlow(attackFlowBlockRedirectUnit);
            return;
        }

        if (attackFlowAttackerUnit != null && attackFlowAttackerUnit.BattleInstanceId == instanceId)
        {
            MarkBlockExchangeCancelled("Attacker removed during attack flow (sync).", finalizeFlowNow: false);
            return;
        }

        if (!attackFlowBlockRedirectEngaged
            && attackFlowDeclaredDefenderUnit != null
            && attackFlowDeclaredDefenderUnit.BattleInstanceId == instanceId)
        {
            MarkBlockExchangeCancelled("Defender removed during attack flow (sync).", finalizeFlowNow: false);
        }
    }

    /// <summary>OnAction 全段完了後、攻撃フローが続行不能なら片付ける。true = 中断して終了済み。</summary>
    private bool TrySettleAttackFlowAfterOnActionPhases()
    {
        if (attackFlowStrikeKind == AttackFlowStrikeKind.None)
        {
            return false;
        }

        if (blockExchangeCancelledForCurrentAttack)
        {
            LogDestroyedBlockerInterruptDetail(
                "TrySettleAttackFlowAfterOnActionPhases",
                "blockExchangeCancelledForCurrentAttack — finalize without exchange");
            FinalizeBlockInterruptWithoutExchange();
            return true;
        }

        CardController attacker = attackFlowAttackerUnit != null ? attackFlowAttackerUnit : pendingUnitAttackAttacker;
        if (!IsUnitAliveOnAnyDeployField(attacker))
        {
            
            CancelPendingUnitAttackFlow();
            return true;
        }

        if (attackFlowBlockRedirectEngaged)
        {
            if (!IsUnitAvailableForAttackExchange(attackFlowBlockRedirectUnit))
            {
                LogDestroyedBlockerInterruptDetail(
                    "TrySettleAttackFlowAfterOnActionPhases",
                    "blocker unavailable for exchange after OnAction",
                    attackFlowBlockRedirectUnit);
                FinalizeBlockInterruptWithoutExchange();
                return true;
            }
        }
        else if (attackFlowDeclaredDefenderUnit != null && !IsUnitAliveOnAnyDeployField(attackFlowDeclaredDefenderUnit))
        {
            CancelPendingUnitAttackFlow();
            return true;
        }

        return false;
    }

    /// <summary>シールド攻撃→ブロック OnAction 解決後にシールド攻撃フラグを片付ける（シールドダメージは再開しない）。</summary>
    private void FinishDeferredShieldAttackBlockFlow()
    {
        if (!deferredShieldBlockRedirectWait)
        {
            return;
        }

        deferredShieldBlockRedirectWait = false;
        isShieldAttackResolving = false;
        blockShieldFlowDuringShieldAttack = false;
        Debug.Log("[ShieldAttack] Block redirect flow settled — shield strike will not resume.");
    }

    /// <summary>ブロック戦前にブロッカーが消えた等で交換ダメージなしに攻撃フローを終了する（攻撃者は宣言時 REST のまま）。</summary>
    private void CancelInterruptedBlockRedirectAttackFlow(string reason)
    {
        MarkBlockExchangeCancelled(reason, finalizeFlowNow: true);
    }

    private void BeginShieldAttackBlockRedirectFlow(CardController blocker)
    {
        deferredShieldBlockRedirectWait = true;
        attackFlowBlockRedirectEngaged = true;
        attackFlowBlockRedirectFromShieldStrike = true;
        attackFlowBlockRedirectUnit = blocker;
        LogArgamaShieldBlockCloseCombatDebug(
            "BeginShieldBlockRedirect",
            "shield attack redirected to block combat",
            blocker: blocker);
    }

    private void NotifyBlockRedirectUnitRemovedDuringAttackFlow(CardController unit)
    {
        if (unit == null || attackFlowBlockRedirectUnit == null || unit != attackFlowBlockRedirectUnit)
        {
            LogArgamaShieldBlockCloseCombatDebug(
                "NotifyBlockerRemovedSkipped",
                unit == null
                    ? "unit is null"
                    : attackFlowBlockRedirectUnit == null
                        ? "attackFlowBlockRedirectUnit is null"
                        : $"picked:{FormatUnitDebugSnap(unit)} != redirect:{FormatUnitDebugSnap(attackFlowBlockRedirectUnit)}",
                blocker: unit);
            return;
        }

        LogArgamaShieldBlockCloseCombatDebug(
            "NotifyBlockerRemoved",
            $"blocker HP:{unit.CurrentHp} before MarkBlockExchangeCancelled",
            blocker: unit);
        LogDestroyedBlockerInterruptDetail(
            "NotifyBlockRedirectUnitRemovedDuringAttackFlow",
            "blocker removed by effect during block OnAction",
            unit);
        MarkBlockExchangeCancelled("Blocker removed by effect during block OnAction.");
    }

    private bool ShouldAbortBlockRedirectCombatBeforeExchange(CardController blocker, string logContext)
    {
        if (blockExchangeCancelledForCurrentAttack)
        {
            FinalizeBlockInterruptWithoutExchange();
            return true;
        }

        if (attackFlowBlockRedirectCombatVoided)
        {
            MarkBlockExchangeCancelled($"{logContext}: combat voided.", finalizeFlowNow: true);
            return true;
        }

        if (!IsUnitAvailableForAttackExchange(blocker))
        {
            if (blocker != null)
            {
                TrashUnitIfDeadOnField(blocker, ResolveCardOwner(blocker.transform));
            }

            MarkBlockExchangeCancelled($"{logContext}: blocker unavailable.", finalizeFlowNow: true);
            return true;
        }

        return false;
    }

    private void TrashUnitIfDeadOnField(CardController unit, PlayerType owner, CardController destroyedBy = null)
    {
        if (unit == null || unit.Data == null || unit.CurrentHp > 0)
        {
            return;
        }

        if (unitsPendingSendToTrash.Contains(unit))
        {
            return;
        }

        SendCardToTrash(unit, owner, destroyedBy);
    }

    private bool IsUnitAvailableForAttackExchange(CardController c)
    {
        if (attackFlowBlockRedirectCombatVoided && c == attackFlowBlockRedirectUnit)
        {
            return false;
        }

        if (unitsPendingSendToTrash.Contains(c))
        {
            return false;
        }

        if (c == null || c.Data == null || !c.Data.IsUnitLike())
        {
            return false;
        }

        if (!playerBattleZoneCards.Contains(c) && !enemyBattleZoneCards.Contains(c))
        {
            return false;
        }

        return c.gameObject != null
            && c.CurrentHp > 0
            && (c.transform.IsChildOf(cardGameRule.PlayerDeployPanel)
                || c.transform.IsChildOf(enemyCardGameRule.PlayerDeployPanel));
    }

    /// <summary>OnAction 等の UI 後に、攻撃フロー文脈からユニット戦を再開する（破壊済み参照を避ける）。</summary>
    private void TryResumeUnitVsUnitAttackAfterOnAction(bool skipOnActionPause, bool skipAttackedSidePanelPause)
    {
        if (attackFlowPipelinePhase == AttackFlowPipelinePhase.PostBlockOnAction)
        {
            return;
        }

        if (TrySettleAttackFlowAfterOnActionPhases())
        {
            return;
        }

        CardController attacker = attackFlowAttackerUnit != null
            ? attackFlowAttackerUnit
            : pendingUnitAttackAttacker;

        if (!IsUnitAliveOnAnyDeployField(attacker))
        {
            Debug.Log("[UnitAttack] Attacker was destroyed — cancel attack continuation.");
            CancelPendingUnitAttackFlow();
            return;
        }

        if (attackFlowPostBlockPassOnActionDone)
        {
            CardController postBlockDefender = attackFlowDeclaredDefenderUnit;
            if (!IsUnitAliveOnAnyDeployField(postBlockDefender))
            {
                CancelPendingUnitAttackFlow();
                return;
            }

            ExecuteUnitVsUnitDeclaredCombat(
                attacker,
                postBlockDefender,
                attackFlowAttackerOwner,
                ResolveCardOwner(postBlockDefender.transform));
            return;
        }

        if (attackFlowBlockSelectionResolved)
        {
            CardController declaredDefender = attackFlowDeclaredDefenderUnit;
            if (!IsUnitAliveOnAnyDeployField(declaredDefender))
            {
                CancelPendingUnitAttackFlow();
                return;
            }

            if (isOnActionPopupOpen && attackFlowStrikeKind != AttackFlowStrikeKind.None)
            {
                Debug.Log("[AttackFlow] Post-block OnAction already in progress — skip duplicate resume.");
                return;
            }

            RunOnActionStepsImmediatelyAfterBlockPass(
                attacker,
                declaredDefender,
                attackFlowAttackerOwner,
                ResolveCardOwner(declaredDefender.transform),
                AttackFlowStrikeKind.UnitVsUnit);
            return;
        }

        if (attackFlowBlockRedirectEngaged)
        {
            if (blockExchangeCancelledForCurrentAttack
                || attackFlowBlockRedirectCombatVoided
                || !IsUnitAvailableForAttackExchange(attackFlowBlockRedirectUnit))
            {
                FinalizeBlockInterruptWithoutExchange();
                return;
            }

            PlayerType blockOwner = ResolveCardOwner(attackFlowBlockRedirectUnit.transform);
            TryResolveBlockRedirectUnitCombatWithOnActionSteps(
                attacker,
                attackFlowBlockRedirectUnit,
                attackFlowAttackerOwner,
                blockOwner,
                skipOnActionPause: attackFlowBlockOnActionCompleted || skipOnActionPause);
            return;
        }

        CardController defender = attackFlowDeclaredDefenderUnit;

        if (!IsUnitAliveOnAnyDeployField(defender))
        {
            Debug.Log("[UnitAttack] Defender was destroyed — cancel attack continuation.");
            CancelPendingUnitAttackFlow();
            return;
        }

        PlayerType attackerOwner = attackFlowAttackerOwner;
        PlayerType defenderOwner = ResolveCardOwner(defender.transform);

        if (!AttackerIgnoresBlockRedirect(attacker)
            && attackFlowBlockRedirectUnit != null
            && defender == attackFlowBlockRedirectUnit)
        {
            TryResolveBlockRedirectUnitCombatWithOnActionSteps(
                attacker,
                defender,
                attackerOwner,
                defenderOwner,
                skipOnActionPause: attackFlowBlockOnActionCompleted || skipOnActionPause);
            return;
        }

        if (AttackerIgnoresBlockRedirect(attacker))
        {
            defender = attackFlowDeclaredDefenderUnit != null
                ? attackFlowDeclaredDefenderUnit
                : defender;
            if (IsCardControllerInstanceValid(defender))
            {
                defenderOwner = ResolveCardOwner(defender.transform);
            }
        }

        // 登録済み攻撃コンテキストがあるときは TryUnitVsUnitAttack 先頭（ブロック UI 再表示）に戻さない。
        if (attackFlowStrikeKind != AttackFlowStrikeKind.None)
        {
            Debug.LogWarning("[AttackFlow] Resume with active attack context but no block redirect — advancing to block/onAction phase.");
            skipAttackedSidePanelPause = true;
        }

        if (attackFlowPipelinePhase == AttackFlowPipelinePhase.PostBlockOnAction
            || attackFlowBlockSelectionResolved)
        {
            Debug.Log("[AttackFlow] Skip TryUnitVsUnitAttack re-entry — post-block pipeline active.");
            return;
        }

        TryUnitVsUnitAttack(attacker, defender, attackerOwner, defenderOwner, skipOnActionPause, skipAttackedSidePanelPause);
    }

    /// <summary>ブロックを行わなかった／キャンセル後：OnAction 1 回 → 宣言対象への通常戦闘。</summary>
    private void BeginPostBlockPassUnitAttackSequence(
        CardController attacker,
        CardController defender,
        PlayerType attackerOwner,
        PlayerType defenderOwner)
    {
        RunOnActionStepsImmediatelyAfterBlockPass(
            attacker,
            defender,
            attackerOwner,
            defenderOwner,
            AttackFlowStrikeKind.UnitVsUnit);
    }

    /// <summary>ブロックを行わなかった／キャンセル後：OnAction 1 回 → 宣言対象への通常戦闘（TryUnitVsUnitAttack 先頭から再入しない）。</summary>
    private void ContinueUnitAttackAfterBlockPassWithoutBlocking(
        CardController attacker,
        CardController defender,
        PlayerType attackerOwner,
        PlayerType defenderOwner)
    {
        BeginPostBlockPassUnitAttackSequence(attacker, defender, attackerOwner, defenderOwner);
    }

    /// <summary>シールド攻撃でブロックをキャンセルした後：OnAction → シールドダメージ解決。</summary>
    private void ContinueShieldAttackAfterBlockPassWithoutBlocking(
        CardController attacker,
        PlayerType attackerOwner)
    {
        PlayerType defenderSide = attackerOwner == PlayerType.Player ? PlayerType.Enemy : PlayerType.Player;
        RunOnActionStepsImmediatelyAfterBlockPass(
            attacker,
            null,
            attackerOwner,
            defenderSide,
            AttackFlowStrikeKind.Shield);
    }

    /// <summary>OnAction 後の通常ユニット戦闘（宣言済み防御対象へ）。</summary>
    private void ExecuteUnitVsUnitDeclaredCombat(
        CardController attacker,
        CardController defender,
        PlayerType attackerOwner,
        PlayerType defenderOwner)
    {
        if (!IsCardControllerInstanceValid(attacker) || !IsCardControllerInstanceValid(defender))
        {
            CancelPendingUnitAttackFlow();
            return;
        }

        if (defender != null && defender.Data != null)
        {
            Debug.Log(
                $"[DefenderInfo] {defender.Data.cardName} AP:{defender.CurrentPower} HP:{defender.CurrentHp} {(defender.IsRestState ? "REST" : "ACTIVE")} owner:{defenderOwner}");
        }

        if (attackFlowBlockRedirectEngaged && attackFlowBlockRedirectUnit != null)
        {
            CancelInterruptedBlockRedirectAttackFlow("Block redirect active — refusing undeclared unit exchange.");
            return;
        }

        if (attackFlowBlockRedirectEngaged)
        {
            attackFlowBlockRedirectEngaged = false;
            attackFlowBlockRedirectFromShieldStrike = false;
        }

        if (!CanAttackerTargetEnemyUnitForCombat(attacker, defender))
        {
            Debug.Log("Only REST units can be attacked (unless attacker has AttackActiveEnemyUnit).");
            CancelPendingUnitAttackFlow();
            return;
        }

        ResolveUnitVsUnitCombatStrikePowers(
            attacker,
            attackerOwner,
            defender,
            out int attackerPowerForCombat,
            out int defenderPowerForCombat);

        int defenderHpBeforeExchange = defender.CurrentHp;
        int attackerHpBeforeExchange = attacker.CurrentHp;

        defender.ApplyDamage(attackerPowerForCombat);
        attacker.ApplyDamage(defenderPowerForCombat);
        int defenderHpAfterExchange = defender.CurrentHp;
        int attackerHpAfterExchange = attacker.CurrentHp;

        NotifyLocalUnitAttackResolved(
            attacker,
            defender,
            attackerHpAfterExchange,
            defenderHpAfterExchange);

        if (defender.CurrentHp <= 0)
        {
            SendCardToTrash(defender, defenderOwner, attacker);
        }

        if (attacker.CurrentHp <= 0)
        {
            SendCardToTrash(attacker, attackerOwner);
        }

        pendingUnitAttackAttacker = null;
        pendingOnAttackEffectResolvedAttacker = null;
        ClearTimedStatModifiersForAllInPlayCards(EffectDuration.UntilEndOfBattle);
        ClearAttackActiveEnemyGrants(EffectDuration.UntilEndOfBattle);
        DumpTurnResourceUsageLogs(attackerOwner, "unit vs unit attack");
        SyncAllResourceViewsFromRule();

        LogAttackPostBattleFieldCompact(attacker, attackerOwner);
        ClearAttackFlowContext();
    }

    private bool IsUnitAliveOnAnyDeployField(CardController c)
    {
        if (!IsCardControllerInstanceValid(c) || c.Data == null || !c.Data.IsUnitLike())
        {
            return false;
        }

        bool onField = c.transform.IsChildOf(cardGameRule.PlayerDeployPanel)
            || c.transform.IsChildOf(enemyCardGameRule.PlayerDeployPanel);
        return onField && c.CurrentHp > 0;
    }

    private bool IsValidEnemyUnitAttackTarget(CardController attacker, CardController target, PlayerType attackerOwner)
    {
        if (target == null || target.Data == null || !target.Data.IsUnitLike())
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

        if (!CanAttackerTargetEnemyUnitForCombat(attacker, target))
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

        if (IsValidEnemyUnitAttackTarget(pendingUnitAttackAttacker, clicked, attackerOwner))
        {
            PlayerType defenderOwner = ResolveCardOwner(clicked.transform);
            CommitUnitAttackDeclaration(pendingUnitAttackAttacker, attackerOwner);
            BeginUnitAttackAfterTargetDeclared(
                pendingUnitAttackAttacker,
                clicked,
                attackerOwner,
                defenderOwner);
            return true;
        }

        if (clickedOnAnyField)
        {
            Debug.Log("Only REST enemy units can be selected as attack targets (unless attacker has AttackActiveEnemyUnit).");
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

        List<CardController> enemyUnits = GetEnemyUnitAttackTargets(attackerOwner, attacker);
        if (enemyUnits.Count == 0)
        {
            Debug.Log(attacker.HasAttackActiveEnemyAbility()
                ? "No enemy units to attack."
                : "No REST enemy units to attack.");
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
        title.text = attacker.HasAttackActiveEnemyAbility()
            ? "Select enemy unit to attack (REST or ACTIVE)"
            : "Select REST enemy unit to attack";
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
                CommitUnitAttackDeclaration(attacker, attackerOwner);
                BeginUnitAttackAfterTargetDeclared(
                    attacker,
                    selectedUnit,
                    attackerOwner,
                    defenderOwner);
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
    /// <summary>
    /// OnAttack の味方ユニット向け GrantAttackFlag（例: ジャスティス＋パイロット攻撃時の三隻同盟トークン）。
    /// キラのデバフと同じ「攻撃宣言後・戦闘前」タイミングで UI を出す。
    /// </summary>
    private bool TryOpenOnAttackAllyGrantAttackFlagSelection(
        CardController attacker,
        PlayerType attackerOwner,
        System.Action onResolved)
    {
        if (attacker == null || attacker.Data == null)
        {
            return false;
        }

        if (attacker.MountedPilot == null || attacker.MountedPilot.Data == null)
        {
            return false;
        }

        EffectActivationContext ctx = BuildOnAttackActivationContext(attackerOwner, attacker);
        List<CardController> effectSources = new List<CardController> { attacker };
        if (attacker.MountedPilot.Data != null)
        {
            effectSources.Add(attacker.MountedPilot);
        }

        for (int sourceIndex = 0; sourceIndex < effectSources.Count; sourceIndex++)
        {
            CardController sourceCard = effectSources[sourceIndex];
            if (sourceCard?.Data?.timedEffects == null)
            {
                continue;
            }

            for (int i = 0; i < sourceCard.Data.timedEffects.Count; i++)
            {
                TimedEffectData timed = sourceCard.Data.timedEffects[i];
                if (timed == null || timed.timing != EffectTiming.OnAttack || !timed.HasResolvedEffects())
                {
                    continue;
                }

                if (!EffectActivationEvaluator.AreTimedConditionsMet(timed, ctx))
                {
                    continue;
                }

                IReadOnlyList<EffectData> resolvedOnAttack = timed.GetResolvedEffects();
                for (int j = 0; j < resolvedOnAttack.Count; j++)
                {
                    EffectData effect = resolvedOnAttack[j];
                    if (effect == null || effect.type != EffectType.GrantAttackFlag)
                    {
                        continue;
                    }

                    if (!effect.target.IsAllyUnitPickTarget())
                    {
                        Debug.LogWarning(
                            $"[OnAttack] GrantAttackFlag skipped: target must be AllyUnit/AllyOtherUnit "
                            + $"(got {effect.target}) card:{sourceCard.Data?.cardName}");
                        continue;
                    }

                    if (!ShouldApplyChainedEffect(effect, ctx, "OnAttackAllyGrant"))
                    {
                        continue;
                    }

                    List<CardController> candidates = ResolveSelectableEffectTargets(
                        sourceCard,
                        attackerOwner,
                        effect);
                    if (candidates.Count == 0)
                    {
                        Debug.Log(
                            $"[OnAttack] GrantAttackFlag: 候補なし ({effect.FormatEffectSelectionSummary()}) "
                            + $"attacker:{attacker.Data.cardName}");
                        continue;
                    }

                    Debug.Log(
                        $"[OnAttack] GrantAttackFlag UI: 候補{candidates.Count}体 "
                        + $"attacker:{attacker.Data.cardName} pilot:{attacker.MountedPilot.Data.cardName}");

                    if (attackerOwner == PlayerType.Enemy)
                    {
                        EnemyAiEffectPickContext pickCtx = BuildEnemyAiEffectPickContext(
                            attackerOwner,
                            sourceCard,
                            attacker,
                            null);
                        CardController picked = PickEnemyAiEffectTarget(effect, pickCtx, candidates);
                        if (picked != null)
                        {
                            ApplyEffectToSpecificTargets(
                                sourceCard,
                                attackerOwner,
                                effect,
                                new List<CardController> { picked });
                        }

                        return false;
                    }

                    OpenManualUnitTargetSelectionUI(
                        sourceCard,
                        attackerOwner,
                        effect,
                        candidates,
                        attacker,
                        picked =>
                        {
                            if (picked != null)
                            {
                                ApplyEffectToSpecificTargets(
                                    sourceCard,
                                    attackerOwner,
                                    effect,
                                    new List<CardController> { picked });
                            }

                            _onAttackPreCombatCompletedAttacker = attacker;
                            onResolved?.Invoke();
                        });
                    return true;
                }
            }
        }

        return false;
    }

    private readonly struct OnAttackEnemyEffectCursor
    {
        public OnAttackEnemyEffectCursor(int sourceIndex, int timedIndex, int effectIndex)
        {
            SourceIndex = sourceIndex;
            TimedIndex = timedIndex;
            EffectIndex = effectIndex;
        }

        public int SourceIndex { get; }
        public int TimedIndex { get; }
        public int EffectIndex { get; }

        public OnAttackEnemyEffectCursor AfterCurrentEffect()
        {
            return new OnAttackEnemyEffectCursor(SourceIndex, TimedIndex, EffectIndex + 1);
        }
    }

    /// <summary>搭乗パイロットの OnAttack をユニット本体より先に解決する（例: キラのデバフ→ストフリの山札下送り）。</summary>
    private static List<CardController> BuildOnAttackEnemyEffectSources(CardController attacker)
    {
        List<CardController> effectSources = new List<CardController>();
        if (attacker?.MountedPilot != null && attacker.MountedPilot.Data != null)
        {
            effectSources.Add(attacker.MountedPilot);
        }

        if (attacker != null)
        {
            effectSources.Add(attacker);
        }

        return effectSources;
    }

    private void ContinueOnAttackEnemyEffectResolution(
        CardController attacker,
        PlayerType attackerOwner,
        CardController attackedTarget,
        System.Action onAllComplete,
        OnAttackEnemyEffectCursor cursor)
    {
        if (TryOpenOnAttackEnemySelectionPanel(attacker, attackerOwner, attackedTarget, onAllComplete, cursor))
        {
            return;
        }

        onAllComplete?.Invoke();
    }

    private bool TryOpenOnAttackEnemySelectionPanel(
        CardController attacker,
        PlayerType attackerOwner,
        CardController attackedTarget,
        System.Action onResolved = null,
        OnAttackEnemyEffectCursor cursor = default)
    {
        if (attacker == null || attacker.Data == null)
        {
            return false;
        }

        EffectActivationContext activationContext = BuildOnAttackActivationContext(attackerOwner, attacker);
        List<CardController> effectSources = BuildOnAttackEnemyEffectSources(attacker);

        for (int sourceIndex = cursor.SourceIndex; sourceIndex < effectSources.Count; sourceIndex++)
        {
            CardController sourceCard = effectSources[sourceIndex];
            if (sourceCard == null || sourceCard.Data == null || sourceCard.Data.timedEffects == null)
            {
                continue;
            }

            int timedStart = sourceIndex == cursor.SourceIndex ? cursor.TimedIndex : 0;
            for (int i = timedStart; i < sourceCard.Data.timedEffects.Count; i++)
            {
                TimedEffectData timed = sourceCard.Data.timedEffects[i];
                if (timed == null || timed.timing != EffectTiming.OnAttack || !timed.HasResolvedEffects())
                {
                    continue;
                }

                if (!EffectActivationEvaluator.AreTimedConditionsMet(timed, activationContext))
                {
                    continue;
                }

                IReadOnlyList<EffectData> resolvedOnAttack = timed.GetResolvedEffects();
                int effectStart = sourceIndex == cursor.SourceIndex && i == timedStart ? cursor.EffectIndex : 0;
                for (int j = effectStart; j < resolvedOnAttack.Count; j++)
                {
                    EffectData effect = resolvedOnAttack[j];
                    if (effect == null)
                    {
                        continue;
                    }

                    if (!effect.target.IsOpponentUnitTarget()
                        && !effect.type.UsesTargetCountValue())
                    {
                        continue;
                    }

                    OnAttackEnemyEffectCursor nextCursor = new OnAttackEnemyEffectCursor(sourceIndex, i, j).AfterCurrentEffect();
                    System.Action stepResolved = () => ContinueOnAttackEnemyEffectResolution(
                        attacker,
                        attackerOwner,
                        attackedTarget,
                        onResolved,
                        nextCursor);

                    if (TryResolveOnAttackLowestEnemyReturn(
                        sourceCard,
                        attacker,
                        attackerOwner,
                        effect,
                        stepResolved))
                    {
                        return true;
                    }

                    if (effect.type == EffectType.ReturnUnitToDeckBottom)
                    {
                        continue;
                    }

                    if (effect.type.RequiresManualUnitSelection() || EffectRequiresManualUnitSelection(effect))
                    {
                        List<CardController> bounceCandidates = ResolveSelectableEffectTargets(
                            sourceCard,
                            attackerOwner,
                            effect);
                        if (bounceCandidates.Count == 0)
                        {
                            continue;
                        }

                        OpenEnemyUnitEffectSelectionUI(
                            sourceCard,
                            attacker,
                            attackerOwner,
                            effect,
                            bounceCandidates,
                            stepResolved);
                        return true;
                    }

                    if (effect.selectionMode.IsAttackedTargetOnlyMode())
                    {
                        if (attackedTarget == null
                            || attackedTarget.Data == null
                            || !attackedTarget.Data.IsUnitLike())
                        {
                            continue;
                        }

                        if (effect.target == TargetType.RestEnemyUnit && !attackedTarget.IsRestState)
                        {
                            continue;
                        }

                        List<CardController> singleTarget = new List<CardController> { attackedTarget };
                        ApplyEffectToSpecificTargets(sourceCard, attackerOwner, effect, singleTarget);
                        continue;
                    }

                    if (effect.target == TargetType.EnemyAllUnits)
                    {
                        ApplyEffectToSpecificTargets(
                            sourceCard,
                            attackerOwner,
                            effect,
                            GetAliveEnemyUnits(attackerOwner));
                        continue;
                    }

                    if (effect.selectionMode == EffectSelectionMode.Unset)
                    {
                        List<CardController> autoTargets = ResolveEffectTargets(sourceCard, attackerOwner, effect);
                        if (autoTargets.Count == 0)
                        {
                            continue;
                        }

                        if (NeedsLowestStatUnitManualPick(effect, autoTargets))
                        {
                            OpenEnemyUnitEffectSelectionUI(
                                sourceCard,
                                attacker,
                                attackerOwner,
                                effect,
                                autoTargets,
                                stepResolved);
                            return true;
                        }

                        ApplyEffectToSpecificTargets(sourceCard, attackerOwner, effect, autoTargets);
                        continue;
                    }

                    List<CardController> enemyUnits = ResolveSelectableEffectTargets(sourceCard, attackerOwner, effect);
                    if (enemyUnits.Count == 0)
                    {
                        continue;
                    }

                    OpenEnemyUnitEffectSelectionUI(
                        sourceCard,
                        attacker,
                        attackerOwner,
                        effect,
                        enemyUnits,
                        stepResolved);
                    return true;
                }
            }
        }

        return false;
    }

    private void OpenEnemyUnitEffectSelectionUI(
        CardController effectSourceCard,
        CardController attackingUnit,
        PlayerType attackerOwner,
        EffectData effect,
        List<CardController> enemyUnits,
        System.Action onResolved = null)
    {
        CardController attackUnit = attackingUnit ?? pendingUnitAttackAttacker ?? effectSourceCard;

        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            pendingOnAttackEffectResolvedAttacker = attackUnit;
            onResolved?.Invoke();
            return;
        }

        GameObject root = new GameObject("OnAttackEffectSelect", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        activeOnActionPopupRoot = root;
        isOnActionPopupOpen = true;
        root.transform.SetParent(canvas.transform, false);
        root.transform.SetAsLastSibling();
        root.SetFullSize();
        Image bg = root.GetComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.5f);
        bg.raycastTarget = true;

        TextMeshProUGUI title = root.CreateChildTextCustom("EffectSelectTitle", UIAnchor.TopCenter, 620, 48);
        if (effect != null && effect.type == EffectType.Bounce)
        {
            title.text = "バウンス — 手札に戻すユニットを選択";
        }
        else if (effect != null && effect.type == EffectType.Rest)
        {
            title.text = "REST — 対象ユニットを選択";
        }
        else if (effect != null && effect.type == EffectType.Activate)
        {
            title.text = effect.filterTargetIsBlocker
                ? "ACTIVE化 — ブロッカーを選択（RESTのみ）"
                : "ACTIVE化 — 対象ユニットを選択（RESTのみ）";
        }
        else if (effect != null && effect.type == EffectType.Destroy)
        {
            title.text = "破壊 — 対象ユニットを選択";
        }
        else if (effect != null && effect.type == EffectType.ReturnUnitToDeckBottom)
        {
            title.text = "山札の下に戻す敵ユニットを選択";
        }
        else
        {
            title.text = effect != null && effect.target == TargetType.RestEnemyUnit
                ? "Select REST enemy unit"
                : "Select effect target unit";
        }
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

                bool immediateSinglePick = effect.type.RequiresManualUnitSelection()
                    || effect.selectionMode.IsImmediateSinglePick()
                    || effect.type == EffectType.ReturnUnitToDeckBottom;
                if (immediateSinglePick)
                {
                    consumed = true;
                    ApplyEffectToSpecificTargets(
                        effectSourceCard,
                        attackerOwner,
                        effect,
                        new List<CardController> { unit });
                    pendingOnAttackEffectResolvedAttacker = attackUnit;
                    Debug.Log(
                        $"[OnAttack] 効果対象を選択 ({effectSourceCard?.Data?.cardName} → {unit.Data?.cardName})。攻撃を続行します。");
                    ReleaseOnActionPopupState(root);
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
                ApplyEffectToSpecificTargets(effectSourceCard, attackerOwner, effect, selected);
                pendingOnAttackEffectResolvedAttacker = attackUnit;
                Debug.Log(
                    $"[OnAttack] 効果対象を複数選択 ({effectSourceCard?.Data?.cardName})。攻撃を続行します。");
                ReleaseOnActionPopupState(root);
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
            ReleaseOnActionPopupState(root);
            Destroy(root);
            CancelPendingUnitAttackFlow();
        });
    }

    private List<CardController> GetAliveEnemyUnits(PlayerType attackerOwner)
    {
        List<CardController> source = attackerOwner == PlayerType.Player ? enemyBattleZoneCards : playerBattleZoneCards;
        List<CardController> result = new List<CardController>();
        for (int i = 0; i < source.Count; i++)
        {
            CardController c = source[i];
            if (c != null && c.Data != null && c.Data.IsUnitLike() && c.CurrentHp > 0)
            {
                result.Add(c);
            }
        }
        return result;
    }

    private int ResolveEffectMagnitude(EffectData effect, PlayerType ownerType, CardController sourceCard)
    {
        return EffectMagnitudeResolver.Resolve(effect, BuildActivationContext(ownerType, sourceCard), sourceCard);
    }

    private void ApplyEffectToSpecificTargets(CardController sourceCard, PlayerType ownerType, EffectData effect, List<CardController> targets)
    {
        if (TryApplyAttackActiveEnemyUnitMarker(sourceCard, ownerType, effect))
        {
            BeginOnlineEffectSyncBatch(ownerType);
            FlushOnlineEffectSyncBatch();
            SyncAllResourceViewsFromRule();
            return;
        }

        if (TryApplyNotDirectAttackMarker(effect, targets))
        {
            SetEffectChainLastPickedTargets(targets);
            BeginOnlineEffectSyncBatch(ownerType);
            FlushOnlineEffectSyncBatch();
            SyncAllResourceViewsFromRule();
            return;
        }

        if (effect.type == EffectType.MarkObservedUnit)
        {
            RegisterObservedUnitWatch(sourceCard, ownerType, effect, targets);
            BeginOnlineEffectSyncBatch(ownerType);
            FlushOnlineEffectSyncBatch();
            SyncAllResourceViewsFromRule();
            return;
        }

        int magnitude = ResolveEffectMagnitude(effect, ownerType, sourceCard);
        if (magnitude == 0
            && !effect.type.UsesTargetCountValue()
            && effect.type != EffectType.GrantAttackFlag
            && effect.type != EffectType.MarkObservedUnit)
        {
            return;
        }

        BeginOnlineEffectSyncBatch(ownerType);

        if (effect.type == EffectType.Draw)
        {
            CardGameRule rule = ownerType == PlayerType.Player ? cardGameRule : enemyCardGameRule;
            for (int i = 0; i < magnitude; i++)
            {
                CardAddtoHand(rule, ownerType);
            }
            FlushOnlineEffectSyncBatch();
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
                {
                    int damageAmount = ResolveEffectDamageAmount(magnitude, t);
                    int hpBefore = t.CurrentHp;
                    bool isCloseCombat = IsCloseCombatCard(sourceCard);
                    Debug.Log(
                        $"[EffectDamage][LocalBefore] closeCombat:{isCloseCombat} owner:{ownerType} "
                        + $"source:{FormatEffectDamageSourceDebugSnap(sourceCard)} "
                        + $"target:{FormatEffectDamageUnitDebugSnap(t)} "
                        + $"rawMagnitude:{magnitude} resolvedDamage:{damageAmount}");
                    bool logCloseCombat = attackFlowBlockRedirectFromShieldStrike
                        && IsCloseCombatCard(sourceCard);
                    if (logCloseCombat)
                    {
                        LogArgamaShieldBlockCloseCombatDebug(
                            "CloseCombatBeforeDamage",
                            $"rawMagnitude:{magnitude} resolvedDamage:{damageAmount} target:{FormatUnitDebugSnap(t)}",
                            blocker: t,
                            effectSource: sourceCard);
                    }

                    ApplyUnitDamageAndTrackChain(t, damageAmount);
                    Debug.Log(
                        $"[EffectDamage][LocalAfter] closeCombat:{isCloseCombat} owner:{ownerType} "
                        + $"source:{FormatEffectDamageSourceDebugSnap(sourceCard)} "
                        + $"target:{FormatEffectDamageUnitDebugSnap(t)} "
                        + $"HP:{hpBefore}->{t.CurrentHp} willTrash:{t.CurrentHp <= 0}");
                    QueueOnlineUnitDamage(t);
                    if (t.CurrentHp <= 0)
                    {
                        Debug.Log(
                            $"[EffectDamage][LocalDestroyQueue] closeCombat:{isCloseCombat} "
                            + $"target:{FormatEffectDamageUnitDebugSnap(t)}");
                    //    同名カードが破壊されるのでコメントアウト
                        // QueueOnlineUnitDestroy(t);
                    }

                    if (logCloseCombat)
                    {
                        LogArgamaShieldBlockCloseCombatDebug(
                            "CloseCombatAfterDamage",
                            $"targetAfter:{FormatUnitDebugSnap(t)} willNotifyAndTrash:{t.CurrentHp <= 0}",
                            blocker: t,
                            effectSource: sourceCard);
                    }

                    if (t.CurrentHp <= 0)
                    {
                        TryLogAttackBlockCloseCombatTrioDestroy("ApplyEffect_Damage", t, sourceCard);
                        NotifyAttackFlowParticipantRemovedDuringOnAction(t);
                        SendCardToTrash(t, ResolveCardOwner(t.transform), ResolveUnitKillSourceForTrash(sourceCard, t));
                    }

                    break;
                }
                case EffectType.Buff:
                {
                    string modifierSourceKey = ResolveUnitStatModifierSourceKey(sourceCard);
                    ApplyStatEffect(t, magnitude, effect.statTarget, effect.duration, modifierSourceKey);
                    QueueOnlineUnitStat(t, magnitude, effect.statTarget, effect.duration, modifierSourceKey);
                    break;
                }
                case EffectType.Debuff:
                {
                    string modifierSourceKey = ResolveUnitStatModifierSourceKey(sourceCard);
                    ApplyStatEffect(t, -magnitude, effect.statTarget, effect.duration, modifierSourceKey);
                    QueueOnlineUnitStat(t, -magnitude, effect.statTarget, effect.duration, modifierSourceKey);
                    break;
                }
                case EffectType.BlockRedirect:
                    // BlockRedirect は戦闘フロー分岐で解釈するため、ここでは何もしない。
                    break;
                case EffectType.HighMobility:
                    // HighMobility は攻撃フロー分岐で解釈するため、ここでは何もしない。
                    break;
                case EffectType.AttackActiveEnemyUnit:
                    // AttackActiveEnemyUnit は攻撃対象判定で解釈するため、ここでは何もしない。
                    break;
                case EffectType.Bounce:
                    break;
                case EffectType.Rest:
                    break;
                case EffectType.Destroy:
                    break;
            }
        }

        if (effect.type == EffectType.Bounce)
        {
            ApplyBounceEffect(effect, targets);
        }
        else if (effect.type == EffectType.Rest)
        {
            ApplyRestEffect(effect, targets);
        }
        else if (effect.type == EffectType.Destroy)
        {
            ApplyDestroyEffect(sourceCard, ownerType, effect, targets);
        }
        else if (effect.type == EffectType.ReturnUnitToDeckBottom)
        {
            ApplyReturnUnitToDeckBottomEffect(effect, targets);
        }
        else if (effect.type == EffectType.GrantAttackFlag)
        {
            ApplyGrantAttackFlagEffect(effect, ownerType, targets);
        }
        else if (effect.type == EffectType.Activate)
        {
            ApplyActivateEffect(effect, ownerType, targets);
        }

        if (targets != null && targets.Count > 0)
        {
            SetEffectChainLastPickedTargets(targets);
        }

        FlushOnlineEffectSyncBatch();
        SyncAllResourceViewsFromRule();
    }

    private static bool AttackerIgnoresBlockRedirect(CardController attacker)
    {
        return attacker != null && attacker.HasHighMobilityAbility();
    }

    /// <summary>
    /// シールド攻撃。AP が 1 未満のときは何もしない。
    /// EXベースありなら power を EX ベースに与え、無いならシールド 1 枚のみ破壊（<see cref="Gundam2024RuleScript.TryApplyUnitShieldAttack"/>）。
    /// </summary>
    private void TryUnitShieldAttackFromUnit(
        CardController attacker,
        bool skipOnActionPause = false,
        bool skipOnAttackSelection = false,
        bool skipAttackedSidePanelPause = false,
        bool skipOnlineBlockPhase = false,
        int onlineChosenBlockerInstanceId = 0)
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

        if (attacker == null || attacker.Data == null || !attacker.Data.IsUnitLike())
        {
            return;
        }

        // シールド攻撃は攻撃可能フラグ(True)のみで判定する（宣言済みの OnAttack/OnAction 再開時は除く）。
        if (!skipOnAttackSelection && attacker.AttackFlgState != AttackFlg.True)
        {
            Debug.Log("This unit cannot attack.");
            return;
        }

        if (attacker.CannotDirectAttackPlayerOrShield())
        {
            Debug.Log("This unit cannot attack shield or the player directly (isNotDirectAttack).");
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
            CommitUnitAttackDeclaration(attacker, attackerOwner);
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

        // OnAction より前: EX ベースまたは配備ベースがあったか（OnAction 後に実シールドが割れるのを防ぐ）。
        bool hadExBaseLayerAtShieldAttackStart = defender.exBase > 0
            || HasActiveDeployedBaseForRuleSide(targetSide);
        if (hadExBaseLayerAtShieldAttackStart)
        {
            blockShieldFlowDuringShieldAttack = true;
            blockedShieldFlowSide = targetSide;
        }

        CommitUnitAttackDeclaration(attacker, attackerOwner);

        if (!skipOnAttackSelection)
        {
            void ProceedShieldAttack()
            {
                pendingOnAttackEffectResolvedAttacker = attacker;
                _onAttackPreCombatCompletedAttacker = attacker;
                TryUnitShieldAttackFromUnit(attacker, skipOnActionPause, true, skipAttackedSidePanelPause);
            }

            if (TryOpenOnAttackEffectSelectionBeforeCombat(attacker, attackerOwner, null, ProceedShieldAttack))
            {
                return;
            }

            ProceedShieldAttack();
            return;
        }

        if (ShouldUseOnlineBlockPhase(attackerOwner) && !skipOnlineBlockPhase && !AttackerIgnoresBlockRedirect(attacker))
        {
            if (CollectSelectableBlockRedirectUnits(attackerOwner).Count > 0)
            {
                if (TryBeginOnlineBlockWait(
                    attacker,
                    isShieldAttack: true,
                    originalDefender: null,
                    blockerId => ResumeOnlineShieldAttackAfterBlockResponse(attacker, attackerOwner, blockerId)))
                {
                    return;
                }
            }
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
        bool deferredShieldBreakWait = false;

        try
        {
            CommitUnitAttackDeclaration(attacker, attackerOwner);

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

            bool attackerIgnoresBlock = AttackerIgnoresBlockRedirect(attacker);
            if (attackerIgnoresBlock && enableShieldAttackFlowDebugLog)
            {
                Debug.Log($"[HighMobility] {attacker.Data.cardName} — skip block phase (shield attack)");
            }

            if (attackFlowBlockSelectionResolved)
            {
                skipAttackedSidePanelPause = true;
                skipOnlineBlockPhase = true;
                if (!attackFlowPostBlockPassOnActionDone)
                {
                    ContinueShieldAttackAfterBlockPassWithoutBlocking(attacker, attackerOwner);
                    return;
                }

                skipOnActionPause = true;
            }

            if (skipOnlineBlockPhase && onlineChosenBlockerInstanceId > 0)
            {
                CardController onlineBlocker = FindBlockerUnitFromRemoteResponse(onlineChosenBlockerInstanceId);
                PlayerType onlineBlockerOwner = onlineBlocker != null
                    ? ResolveCardOwner(onlineBlocker.transform)
                    : PlayerType.Enemy;
                if (onlineBlocker != null && IsBlockRedirectReactionReady(onlineBlocker, onlineBlockerOwner))
                {
                    ApplyDefenderOnAttackReactionEffects(onlineBlocker, attacker, onlineBlockerOwner);
                    BeginShieldAttackBlockRedirectFlow(onlineBlocker);
                    TryResolveBlockRedirectUnitCombatWithOnActionSteps(
                        attacker,
                        onlineBlocker,
                        attackerOwner,
                        onlineBlockerOwner);
                    return;
                }
            }

            if (!skipOnlineBlockPhase
                && !attackerIgnoresBlock
                && !skipAttackedSidePanelPause
                && attackerOwner == PlayerType.Player
                && !IsOnlineBattle()
                && TryAutoApplyBlockRedirectFromAttack(
                    attackerOwner,
                    attacker,
                    out CardController autoBlockFromShield,
                    out PlayerType autoBlockOwnerFromShield)
                && autoBlockFromShield != null)
            {
                Debug.Log($"[ShieldToUnit] AI auto redirect to {autoBlockFromShield.Data.cardName}");
                BeginShieldAttackBlockRedirectFlow(autoBlockFromShield);
                TryResolveBlockRedirectUnitCombatWithOnActionSteps(
                    attacker,
                    autoBlockFromShield,
                    attackerOwner,
                    autoBlockOwnerFromShield);
                return;
            }

            RegisterAttackFlowContextForOnAction(
                attacker,
                attackerOwner,
                AttackFlowStrikeKind.Shield,
                null,
                null);

            CardController selectedDefenderFromShieldPanel = null;
            PlayerType shieldBlockDefenderSide = attackerOwner == PlayerType.Player
                ? PlayerType.Enemy
                : PlayerType.Player;
            System.Action passShieldBlockAndStartOnAction = () => RunOnActionStepsImmediatelyAfterBlockPass(
                attacker,
                null,
                attackerOwner,
                shieldBlockDefenderSide,
                AttackFlowStrikeKind.Shield);

            if (!skipOnlineBlockPhase
                && !attackerIgnoresBlock
                && !skipAttackedSidePanelPause
                && !attackFlowBlockSelectionResolved
                && attackFlowPipelinePhase != AttackFlowPipelinePhase.PostBlockOnAction
                && attackerOwner == PlayerType.Enemy
                && !IsOnlineBattle())
            {
                attackFlowPipelinePhase = AttackFlowPipelinePhase.AwaitingBlockUi;
                if (TryOpenAttackedSideUnitsPanel(
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
                            if (IsBlockRedirectReactionReady(selectedDefenderFromShieldPanel, selectedDefenderOwner))
                            {
                                ApplyDefenderOnAttackReactionEffects(
                                    selectedDefenderFromShieldPanel,
                                    attacker,
                                    selectedDefenderOwner);
                                BeginShieldAttackBlockRedirectFlow(selectedDefenderFromShieldPanel);
                                Debug.Log(
                                    $"[ShieldToUnit] redirect to unit battle defender:{selectedDefenderFromShieldPanel.Data.cardName}");
                                TryResolveBlockRedirectUnitCombatWithOnActionSteps(
                                    attacker,
                                    selectedDefenderFromShieldPanel,
                                    attackerOwner,
                                    selectedDefenderOwner);
                                return;
                            }

                            Debug.LogWarning(
                                $"[ShieldToUnit] 選択ユニットはブロック不可のためシールド攻撃へ継続: "
                                + $"{selectedDefenderFromShieldPanel.Data.cardName}");
                        }

                        passShieldBlockAndStartOnAction.Invoke();
                    },
                    passShieldBlockAndStartOnAction))
                {
                    return;
                }

                attackFlowPipelinePhase = AttackFlowPipelinePhase.None;
            }

            if (shieldStrikeAbortedAfterBlockInterrupt || deferredShieldBlockRedirectWait || blockExchangeCancelledForCurrentAttack)
            {
                Debug.Log("[ShieldAttack] Shield strike skipped — block redirect flow interrupted or pending.");
                FinalizeBlockInterruptWithoutExchange();
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
                    () =>
                    {
                        if (TrySettleAttackFlowAfterOnActionPhases())
                        {
                            return;
                        }

                        TryUnitShieldAttackFromUnit(attacker, true, true, true);
                    },
                    attacker))
            {
                return;
            }

            if (attacker.CurrentPower <= 0)
            {
                Debug.Log("[ShieldAttack] AP is 0 — cannot break shields or direct attack.");
                pendingUnitAttackAttacker = null;
                pendingOnAttackEffectResolvedAttacker = null;
                ClearAttackFlowContext();
                return;
            }

            if (!gundamRule.HasShieldZoneProtection(targetSide))
            {
                Debug.Log($"[DirectAttack] No shield zone protection. Resolving direct attack. attackPower:{attacker.CurrentPower}");
                pendingUnitAttackAttacker = null;
                pendingOnAttackEffectResolvedAttacker = null;
                NotifyLocalShieldAttackResolved(attacker, 0, 0, directAttackWin: true);
                HandleDirectAttackWinLose(attackerOwner);
                ClearAttackFlowContext();
                return;
            }

            int shieldBeforeStrike = defender.shield;
            if (!TryResolveShieldAttackStrikeDamage(
                    attacker,
                    targetSide,
                    defender,
                    hadExBaseLayerAtShieldAttackStart,
                    out string shieldStrikeLog))
            {
                Debug.Log("Cannot attack shield (no shields or invalid power for EX Base).");
                ClearAttackFlowContext();
                return;
            }

            Gundam2024RuleScript.PlayerState defenderAfter = targetSide == Gundam2024RuleScript.PlayerSide.Player
                ? gundamRule.Player
                : gundamRule.Enemy;
            int shieldsBroken = shieldBeforeStrike - defenderAfter.shield;
            if (shieldsBroken > 0)
            {
                deferredShieldBreakWait = true;
                StartCoroutine(FinishUnitShieldAttackAfterBreakCoroutine(
                    attacker,
                    attackerOwner,
                    shieldStrikeLog,
                    hadExBaseLayerAtShieldAttackStart));
                return;
            }

            CompleteUnitShieldAttackPostStrikeFollowUp(attacker, attackerOwner, shieldStrikeLog);
        }
        finally
        {
            if (!deferredShieldBreakWait && !deferredShieldBlockRedirectWait)
            {
                isShieldAttackResolving = false;
                if (hadExBaseLayerAtShieldAttackStart)
                {
                    blockShieldFlowDuringShieldAttack = false;
                }
            }
        }
    }

    private void CompleteUnitShieldAttackPostStrikeFollowUp(
        CardController attacker,
        PlayerType attackerOwner,
        string shieldStrikeLog)
    {
        if (!string.IsNullOrEmpty(shieldStrikeLog))
        {
            Debug.Log(shieldStrikeLog);
        }

        TriggerCardEffects(attacker, attackerOwner, EffectTiming.OnShieldAttack);
        TriggerMountedPilotOnShieldAttackEffects(attacker, attackerOwner);
        TriggerCardEffects(attacker, attackerOwner, EffectTiming.OnAttack);
        TriggerMountedPilotOnAttackEffects(attacker, attackerOwner);
        pendingUnitAttackAttacker = null;
        pendingOnAttackEffectResolvedAttacker = null;
        ClearTimedStatModifiersForAllInPlayCards(EffectDuration.UntilEndOfBattle);
        ClearAttackActiveEnemyGrants(EffectDuration.UntilEndOfBattle);
        DumpTurnResourceUsageLogs(attackerOwner, "unit shield attack");

        Gundam2024RuleScript.PlayerSide targetSide = attackerOwner == PlayerType.Player
            ? Gundam2024RuleScript.PlayerSide.Enemy
            : Gundam2024RuleScript.PlayerSide.Player;
        Gundam2024RuleScript.PlayerState defenderAfter = targetSide == Gundam2024RuleScript.PlayerSide.Player
            ? gundamRule.Player
            : gundamRule.Enemy;
        if (!_onlineShieldAttackNotifySent)
        {
            NotifyLocalShieldAttackResolved(
                attacker,
                defenderAfter.shield,
                defenderAfter.exBase,
                directAttackWin: false);
        }
        else
        {
            _onlineShieldAttackNotifySent = false;
        }

        SyncAllResourceViewsFromRule();
        ClearAttackFlowContext();
    }

    private IEnumerator FinishUnitShieldAttackAfterBreakCoroutine(
        CardController attacker,
        PlayerType attackerOwner,
        string shieldStrikeLog,
        bool hadExBaseLayerAtShieldAttackStart)
    {
        if (_onlineDeferredEnemyShieldBreak.HasValue)
        {
            OnlineDeferredEnemyShieldBreak deferred = _onlineDeferredEnemyShieldBreak.Value;
            _onlineDeferredEnemyShieldBreak = null;
            yield return RunOnlineAttackerEnemyShieldBreakHandshakeCoroutine(attacker, deferred);
        }
        else
        {
            yield return WaitForShieldBreakFlowCompleteCoroutine();
        }
        try
        {
            CompleteUnitShieldAttackPostStrikeFollowUp(attacker, attackerOwner, shieldStrikeLog);
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

    private const int DefaultSuppressShieldBreakCount = 2;

    /// <summary>
    /// 攻撃者（＋搭乗パイロット）の OnShieldAttack にある制圧の最大破壊枚数。無ければ 0。
    /// </summary>
    private static int GetMaxSuppressBreakCountFromCardData(CardData data)
    {
        List<EffectData> effects = TimedEffectResolver.CollectEffectsByTiming(data, EffectTiming.OnShieldAttack);
        int maxBreaks = 0;
        for (int i = 0; i < effects.Count; i++)
        {
            EffectData effect = effects[i];
            if (effect == null || effect.type != EffectType.Suppress)
            {
                continue;
            }

            int breaks = effect.value > 0 ? effect.value : DefaultSuppressShieldBreakCount;
            if (breaks > maxBreaks)
            {
                maxBreaks = breaks;
            }
        }

        return maxBreaks;
    }

    private static int GetMaxSuppressBreakCountFromUnit(CardController unit)
    {
        if (unit == null || unit.Data == null)
        {
            return 0;
        }

        int maxBreaks = GetMaxSuppressBreakCountFromCardData(unit.Data);
        CardController pilot = unit.MountedPilot;
        if (pilot != null && pilot.Data != null)
        {
            maxBreaks = Mathf.Max(maxBreaks, GetMaxSuppressBreakCountFromCardData(pilot.Data));
        }

        return maxBreaks;
    }

    /// <summary>
    /// シールド攻撃のダメージ本体。EX あり→通常シールド攻撃。シールドのみ＋制圧→実シールドを複数枚。
    /// </summary>
    private bool TryResolveShieldAttackStrikeDamage(
        CardController attacker,
        Gundam2024RuleScript.PlayerSide targetSide,
        Gundam2024RuleScript.PlayerState defender,
        bool hadExBaseLayerAtShieldAttackStart,
        out string logMessage)
    {
        logMessage = null;
        if (blockExchangeCancelledForCurrentAttack || shieldStrikeAbortedAfterBlockInterrupt)
        {
            Debug.Log("[ShieldAttack] Shield strike skipped — block exchange was cancelled for this attack.");
            return false;
        }

        if (attacker == null || defender == null || gundamRule == null)
        {
            return false;
        }

        int suppressBreaks = GetMaxSuppressBreakCountFromUnit(attacker);
        bool shieldOnly = defender.exBase <= 0
            && !HasActiveDeployedBaseForRuleSide(targetSide)
            && !hadExBaseLayerAtShieldAttackStart;

        if (TryApplyShieldAttackDamageToDeployedBase(attacker, targetSide, out logMessage))
        {
            return true;
        }

        if (suppressBreaks > 0 && shieldOnly)
        {
            int applied = gundamRule.ApplySuppressShieldBreaks(targetSide, suppressBreaks);
            if (applied <= 0)
            {
                return false;
            }

            logMessage = $"[Attack] Suppress broke {applied} shield(s) (shield-only).";
            return true;
        }

        if (attacker.CurrentPower <= 0)
        {
            return false;
        }

        if (!gundamRule.TryApplyUnitShieldAttack(targetSide, attacker.CurrentPower, hadExBaseLayerAtShieldAttackStart))
        {
            return false;
        }

        if (hadExBaseLayerAtShieldAttackStart)
        {
            logMessage = $"[Attack] Shield attack vs EX layer. EX Base is now {defender.exBase}.";
        }
        else
        {
            logMessage = "[Attack] Broke 1 shield (no EX Base).";
        }

        return true;
    }

    private void TriggerMountedPilotOnShieldAttackEffects(CardController attacker, PlayerType attackerOwner)
    {
        if (attacker == null || attacker.Data == null || !attacker.Data.IsUnitLike())
        {
            return;
        }

        CardController pilot = attacker.MountedPilot;
        if (pilot == null || pilot.Data == null)
        {
            return;
        }

        TriggerCardEffects(pilot, attackerOwner, EffectTiming.OnShieldAttack);
    }

    private void SetUnitRestAndTriggerEffects(CardController unit, PlayerType ownerType)
    {
        if (unit == null || unit.Data == null)
        {
            return;
        }

        unit.SetUnitRestVisual(true);
    }

    /// <summary>攻撃対象確定（宣言）時に攻撃権を消費してレストする。中断・不成立時も ACTIVE に戻さない。</summary>
    private void CommitUnitAttackDeclaration(CardController attacker, PlayerType attackerOwner)
    {
        if (!IsCardControllerInstanceValid(attacker) || attacker.Data == null)
        {
            return;
        }

        if (attacker.AttackFlgState == AttackFlg.False && attacker.IsRestState)
        {
            return;
        }

        if (attacker.AttackFlgState == AttackFlg.True)
        {
            blockExchangeCancelledForCurrentAttack = false;
            shieldStrikeAbortedAfterBlockInterrupt = false;
            ResetAttackFlowBlockPassFlagsForNewDeclaration();
        }

        attacker.SetAttackFlg(AttackFlg.False);
        SetUnitRestAndTriggerEffects(attacker, attackerOwner);
        SyncOnlineRestFromAttackAuthority(attacker);
        ClearOnAttackPreCombatResolvedState();
        ClearOnAttackPreCombatCompletedForNewAttack();
        pendingOnAttackEffectResolvedAttacker = null;
        Debug.Log($"[AttackDeclare] {attacker.Data.cardName} attack declared — REST + attack right consumed.");
    }

    /// <summary>シールドゾーンから OnRest できるのは、表向き（裏面カバーなし）のベースのみ。</summary>
    private static bool IsVisibleBaseInShieldZone(CardController card)
    {
        return card != null
            && card.Data != null
            && card.Data.type == Type.Base
            && !card.IsShieldFaceHidden;
    }

    private bool IsCardInOwnerShieldZone(CardController card, PlayerType ownerType)
    {
        CardGameRule rule = ownerType == PlayerType.Player ? cardGameRule : enemyCardGameRule;
        return card != null
            && rule != null
            && rule.ShieldCardsContent != null
            && card.transform.IsChildOf(rule.ShieldCardsContent);
    }

    /// <summary>OnRest の起動元として有効な場所か（シールド上は表ベースのみ）。</summary>
    private bool CanUseOnRestAtCardLocation(CardController card, PlayerType ownerType)
    {
        if (card == null || card.Data == null)
        {
            return false;
        }

        if (IsCardInBaseSlot(card) || IsCardOnBattleZone(card))
        {
            return true;
        }

        if (IsCardInOwnerShieldZone(card, ownerType))
        {
            return IsVisibleBaseInShieldZone(card);
        }

        return false;
    }

    private bool CanActivateOnRestBySelf(PlayerType ownerType, CardController source)
    {
        if (source == null || source.Data == null)
        {
            return false;
        }

        if (ownerType != currentPlayerType || currentPhase != BattlePhase.MainPhase)
        {
            return false;
        }

        if (!CanUseOnRestAtCardLocation(source, ownerType))
        {
            return false;
        }

        if (source.IsRestState)
        {
            return false;
        }

        int turnIndex = gundamRule != null ? gundamRule.TurnIndex : -1;
        if (onRestActivatedTurnByCard.TryGetValue(source, out int activatedTurn) && activatedTurn == turnIndex)
        {
            return false;
        }

        return HasEffectTiming(source.Data, EffectTiming.OnRest);
    }

    private bool TryActivateOnRestBySelf(PlayerType ownerType, CardController source)
    {
        if (!CanActivateOnRestBySelf(ownerType, source))
        {
            Debug.Log("OnRest: 現在は発動できません（ターン/フェイズ/REST状態）。");
            return false;
        }

        source.SetAttackFlg(AttackFlg.False);
        source.SetUnitRestVisual(true);
        int turnIndex = gundamRule != null ? gundamRule.TurnIndex : -1;
        onRestActivatedTurnByCard[source] = turnIndex;
        // TriggerCardEffects は手動選択効果を continue で捨てるため、コルーチン解決を使う。
        TriggerTimedEffectsForCard(source, ownerType, EffectTiming.OnRest);
        SyncAllResourceViewsFromRule();
        return true;
    }

    private void TryUnitVsUnitAttack(
        CardController attacker,
        CardController defender,
        PlayerType attackerOwner,
        PlayerType defenderOwner,
        bool skipOnActionPause = false,
        bool skipAttackedSidePanelPause = false,
        bool skipOnlineBlockPhase = false,
        int onlineChosenBlockerInstanceId = 0)
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

        if (!IsCardControllerInstanceValid(attacker) || !IsCardControllerInstanceValid(defender))
        {
            Debug.Log("[UnitAttack] Attacker or defender no longer exists — cancel.");
            CancelPendingUnitAttackFlow();
            return;
        }

        if (!attacker.Data.IsUnitLike() || !defender.Data.IsUnitLike())
        {
            Debug.Log("Only units can attack each other.");
            return;
        }

        if (attackFlowBlockSelectionResolved
            || attackFlowPipelinePhase == AttackFlowPipelinePhase.PostBlockOnAction)
        {
            skipAttackedSidePanelPause = true;
            CardController resolvedAttacker = attackFlowAttackerUnit != null ? attackFlowAttackerUnit : attacker;
            CardController resolvedDefender = attackFlowDeclaredDefenderUnit != null
                ? attackFlowDeclaredDefenderUnit
                : defender;
            PlayerType resolvedDefenderOwner = ResolveCardOwner(resolvedDefender.transform);

            if (!attackFlowPostBlockPassOnActionDone)
            {
                RunOnActionStepsImmediatelyAfterBlockPass(
                    resolvedAttacker,
                    resolvedDefender,
                    attackFlowAttackerOwner,
                    resolvedDefenderOwner,
                    AttackFlowStrikeKind.UnitVsUnit);
                return;
            }

            ExecuteUnitVsUnitDeclaredCombat(
                resolvedAttacker,
                resolvedDefender,
                attackFlowAttackerOwner,
                resolvedDefenderOwner);
            return;
        }

        bool attackerIgnoresBlock = AttackerIgnoresBlockRedirect(attacker);
        if (attackerIgnoresBlock)
        {
            attackFlowBlockRedirectUnit = null;
        }

        CommitUnitAttackDeclaration(attacker, attackerOwner);

        RegisterAttackFlowContextForOnAction(
            attacker,
            attackerOwner,
            AttackFlowStrikeKind.UnitVsUnit,
            defender,
            attackerIgnoresBlock ? null : attackFlowBlockRedirectUnit);

        if (!IsUnitAliveOnAnyDeployField(attacker))
        {
            Debug.Log("[UnitAttack] Attacker not on field or HP is 0 — cancel attack.");
            CancelPendingUnitAttackFlow();
            return;
        }

        // 敵 AI のスコア中止は TryEnemyShieldAttacks およびシールド→ブロック直前のみ（バトル開始後は判定しない。宣言前のみ有効）。

        // 攻撃宣言後に、OnAttackの非戦闘効果（GrantAttackFlag 等）→対象選択(デバフ等)を行う。
        if (_onAttackPreCombatCompletedAttacker != attacker)
        {
            TryOpenOnAttackAllyGrantAttackFlagSelection(attacker, attackerOwner, null);
            _onAttackPreCombatCompletedAttacker = attacker;
        }

        if (pendingOnAttackEffectResolvedAttacker != attacker)
        {
            ResumeUnitVsUnitAttackAfterOnAttackPreCombat(
                attacker,
                attackerOwner,
                defender,
                skipOnActionPause,
                skipAttackedSidePanelPause);
            return;
        }

        if (ShouldUseOnlineBlockPhase(attackerOwner) && !skipOnlineBlockPhase && !attackerIgnoresBlock)
        {
            if (CollectSelectableBlockRedirectUnits(attackerOwner).Count > 0)
            {
                if (TryBeginOnlineBlockWait(
                    attacker,
                    isShieldAttack: false,
                    originalDefender: defender,
                    blockerId => ResumeOnlineUnitAttackAfterBlockResponse(
                        attacker,
                        defender,
                        attackerOwner,
                        defenderOwner,
                        blockerId)))
                {
                    return;
                }
            }
        }

        if (skipOnlineBlockPhase && onlineChosenBlockerInstanceId > 0)
        {
            CardController onlineBlocker = FindBlockerUnitFromRemoteResponse(onlineChosenBlockerInstanceId);
            if (onlineBlocker != null)
            {
                CommitBlockRedirectSelection(attacker, onlineBlocker, ref defender, ref defenderOwner);
                TryResolveBlockRedirectUnitCombatWithOnActionSteps(
                    attacker,
                    onlineBlocker,
                    attackerOwner,
                    defenderOwner);
                return;
            }
        }

        if (!skipOnlineBlockPhase
            && !attackerIgnoresBlock
            && !skipAttackedSidePanelPause
            && attackerOwner == PlayerType.Player
            && !IsOnlineBattle()
            && TryAutoApplyBlockRedirectFromAttack(
                attackerOwner,
                attacker,
                out CardController autoBlockUnit,
                out PlayerType autoBlockOwner)
            && autoBlockUnit != null)
        {
            attackFlowBlockRedirectUnit = autoBlockUnit;
            attackFlowBlockRedirectEngaged = true;
            defender = autoBlockUnit;
            defenderOwner = autoBlockOwner;
        }
        else if (!skipOnlineBlockPhase
            && !attackerIgnoresBlock
            && !skipAttackedSidePanelPause
            && !attackFlowBlockSelectionResolved
            && attackFlowPipelinePhase != AttackFlowPipelinePhase.PostBlockOnAction
            && attackerOwner == PlayerType.Enemy
            && !IsOnlineBattle())
        {
            CardController blockFlowAttacker = attacker;
            CardController blockFlowDefender = defender;
            PlayerType blockFlowAttackerOwner = attackerOwner;
            PlayerType blockFlowDefenderOwner = defenderOwner;
            System.Action passBlockAndStartOnAction = () => RunOnActionStepsImmediatelyAfterBlockPass(
                blockFlowAttacker,
                blockFlowDefender,
                blockFlowAttackerOwner,
                blockFlowDefenderOwner,
                AttackFlowStrikeKind.UnitVsUnit);

            attackFlowPipelinePhase = AttackFlowPipelinePhase.AwaitingBlockUi;
            if (TryOpenAttackedSideUnitsPanel(
                attackerOwner,
                attacker,
                selected =>
                {
                    if (selected == null)
                    {
                        ClearPendingBlockRedirectSelection();
                        return;
                    }

                    CommitBlockRedirectSelection(attacker, selected, ref defender, ref defenderOwner);
                },
                () =>
                {
                    if (attackFlowBlockRedirectUnit != null)
                    {
                        PlayerType blockOwner = ResolveCardOwner(attackFlowBlockRedirectUnit.transform);
                        TryResolveBlockRedirectUnitCombatWithOnActionSteps(
                            attacker,
                            attackFlowBlockRedirectUnit,
                            attackerOwner,
                            blockOwner);
                        return;
                    }

                    passBlockAndStartOnAction.Invoke();
                },
                passBlockAndStartOnAction))
            {
                return;
            }

            attackFlowPipelinePhase = AttackFlowPipelinePhase.None;
        }

        if (attackFlowPipelinePhase == AttackFlowPipelinePhase.AwaitingBlockUi)
        {
            attackFlowPipelinePhase = AttackFlowPipelinePhase.None;
        }

        if (attackFlowBlockRedirectEngaged && attackFlowBlockRedirectUnit != null)
        {
            PlayerType blockOwner = ResolveCardOwner(attackFlowBlockRedirectUnit.transform);
            TryResolveBlockRedirectUnitCombatWithOnActionSteps(
                attacker,
                attackFlowBlockRedirectUnit,
                attackerOwner,
                blockOwner,
                skipOnActionPause: attackFlowBlockOnActionCompleted || skipOnActionPause);
            return;
        }

        if (attackFlowBlockRedirectUnit != null && defender != null && defender == attackFlowBlockRedirectUnit)
        {
            TryResolveBlockRedirectUnitCombatWithOnActionSteps(
                attacker,
                defender,
                attackerOwner,
                defenderOwner,
                skipOnActionPause: attackFlowBlockOnActionCompleted || skipOnActionPause);
            return;
        }

        if (!skipOnActionPause
            && !attackFlowPostBlockPassOnActionDone
            && !attackFlowBlockSelectionResolved
            && attackFlowPipelinePhase != AttackFlowPipelinePhase.PostBlockOnAction
            && TryRunAttackActionSteps(
                defenderOwner,
                attackerOwner,
                () =>
                {
                    if (attackFlowBlockSelectionResolved)
                    {
                        if (attackFlowPostBlockPassOnActionDone)
                        {
                            ExecuteUnitVsUnitDeclaredCombat(
                                attackFlowAttackerUnit != null ? attackFlowAttackerUnit : attacker,
                                attackFlowDeclaredDefenderUnit != null ? attackFlowDeclaredDefenderUnit : defender,
                                attackFlowAttackerOwner,
                                ResolveCardOwner((attackFlowDeclaredDefenderUnit != null ? attackFlowDeclaredDefenderUnit : defender).transform));
                        }
                        else
                        {
                            RunOnActionStepsImmediatelyAfterBlockPass(
                                attackFlowAttackerUnit != null ? attackFlowAttackerUnit : attacker,
                                attackFlowDeclaredDefenderUnit != null ? attackFlowDeclaredDefenderUnit : defender,
                                attackFlowAttackerOwner,
                                ResolveCardOwner((attackFlowDeclaredDefenderUnit != null ? attackFlowDeclaredDefenderUnit : defender).transform),
                                AttackFlowStrikeKind.UnitVsUnit);
                        }

                        return;
                    }

                    TryResumeUnitVsUnitAttackAfterOnAction(true, true);
                },
                attacker))
        {
            return;
        }

        ExecuteUnitVsUnitDeclaredCombat(attacker, defender, attackerOwner, defenderOwner);
    }

    /// <summary>
    /// ブロック確定後：通常攻撃と同順で OnAction（敵→プレイヤー）を挟んでからブロック戦を解決する。
    /// </summary>
    private void TryResolveBlockRedirectUnitCombatWithOnActionSteps(
        CardController attacker,
        CardController blocker,
        PlayerType attackerOwner,
        PlayerType blockerOwner,
        bool skipOnActionPause = false)
    {
        if (!IsCardControllerInstanceValid(attacker) || attacker.Data == null)
        {
            CancelPendingUnitAttackFlow();
            return;
        }

        if (blockExchangeCancelledForCurrentAttack || attackFlowBlockRedirectCombatVoided)
        {
            FinalizeBlockInterruptWithoutExchange();
            return;
        }

        if (attackFlowBlockOnActionCompleted)
        {
            skipOnActionPause = true;
        }

        if (ShouldAbortBlockRedirectCombatBeforeExchange(blocker, "Before block OnAction"))
        {
            LogArgamaShieldBlockCloseCombatDebug("TryResolveBlock_AbortBeforeOnAction", "ShouldAbort before OnAction", attacker, blocker);
            return;
        }

        attackFlowBlockRedirectEngaged = true;
        attackFlowBlockRedirectUnit = blocker;
        CardController declaredDefenderForContext = attackFlowDeclaredDefenderUnit != null
            && attackFlowDeclaredDefenderUnit != blocker
            ? attackFlowDeclaredDefenderUnit
            : attackFlowBlockRedirectFromShieldStrike ? null : blocker;
        RegisterAttackFlowContextForOnAction(
            attacker,
            attackerOwner,
            attackFlowBlockRedirectFromShieldStrike ? AttackFlowStrikeKind.Shield : AttackFlowStrikeKind.UnitVsUnit,
            declaredDefenderForContext,
            blocker);

        if (!skipOnActionPause)
        {
            LogArgamaShieldBlockCloseCombatDebug(
                "TryResolveBlock_StartOnAction",
                $"skipOnActionPause:false blockerOwner:{blockerOwner} attackerOwner:{attackerOwner}",
                attacker,
                blocker);

            System.Action resumeCombatAfterOnAction = () =>
            {
                if (TrySettleAttackFlowAfterOnActionPhases())
                {
                    return;
                }

                attackFlowBlockOnActionCompleted = true;

                CardController resumeAttacker = attackFlowAttackerUnit;
                CardController resumeBlocker = attackFlowBlockRedirectUnit;
                if (!IsUnitAliveOnAnyDeployField(resumeAttacker))
                {
                    LogArgamaShieldBlockCloseCombatDebug(
                        "TryResolveBlock_OnActionComplete",
                        "resumeAttacker invalid — rest blocker and finalize",
                        resumeAttacker,
                        resumeBlocker);
                    TrashUnitIfDeadOnField(resumeAttacker, attackFlowAttackerOwner);
                    FinalizeBlockInterruptWithoutExchange();
                    return;
                }

                LogArgamaShieldBlockCloseCombatDebug(
                    "TryResolveBlock_ResumeAfterOnAction",
                    $"resume exchange blockerAlive:{IsUnitAvailableForAttackExchange(resumeBlocker)}",
                    resumeAttacker,
                    resumeBlocker);

                TryResolveBlockRedirectUnitCombatWithOnActionSteps(
                    resumeAttacker,
                    resumeBlocker,
                    attackFlowAttackerOwner,
                    ResolveCardOwner(resumeBlocker != null ? resumeBlocker.transform : null),
                    skipOnActionPause: true);
            };

            if (TryRunAttackActionSteps(
                blockerOwner,
                attackerOwner,
                resumeCombatAfterOnAction,
                attacker))
            {
                return;
            }

            Debug.LogWarning(
                "[AttackFlow] Block OnAction could not start — proceeding to combat without action pause.");
            resumeCombatAfterOnAction.Invoke();
            return;
        }

        LogArgamaShieldBlockCloseCombatDebug(
            "TryResolveBlock_BeforeExchange",
            $"skipOnActionPause:true blockerAlive:{IsUnitAvailableForAttackExchange(blocker)}",
            attacker,
            blocker);

        if (ShouldAbortBlockRedirectCombatBeforeExchange(blocker, "Before block exchange"))
        {
            return;
        }

        ExecuteBlockRedirectUnitCombat(attacker, blocker, attackerOwner, blockerOwner);
    }

    /// <summary>
    /// ブロックリダイレクト後のユニット戦。通常攻撃ルール（REST 対象のみ等）を迂回し、
    /// 攻撃者とブロッカーの双方が必ずレスト＋相互ダメージ（最低 1）になる。
    /// OnAction は <see cref="TryResolveBlockRedirectUnitCombatWithOnActionSteps"/> で先に処理する。
    /// </summary>
    private void ExecuteBlockRedirectUnitCombat(
        CardController attacker,
        CardController blocker,
        PlayerType attackerOwner,
        PlayerType blockerOwner)
    {
        if (blockExchangeCancelledForCurrentAttack)
        {
            Debug.Log("[BlockCombat] Exchange skipped — blockExchangeCancelledForCurrentAttack.");
            LogArgamaShieldBlockCloseCombatDebug(
                "ExecuteBlockRedirect_EntrySkip",
                "exchange skipped at entry (cancel flag set)",
                attacker,
                blocker);
            TrashUnitIfDeadOnField(blocker, blockerOwner, attacker);
            TrashUnitIfDeadOnField(attacker, attackerOwner);
            FinalizeBlockInterruptWithoutExchange();
            return;
        }

        LogArgamaShieldBlockCloseCombatDebug(
            "ExecuteBlockRedirect_Entry",
            "exchange resolution starting",
            attacker,
            blocker);

        if (!IsCardControllerInstanceValid(attacker) || attacker.Data == null)
        {
            CancelPendingUnitAttackFlow();
            return;
        }

        if (ShouldAbortBlockRedirectCombatBeforeExchange(blocker, "At block combat resolution"))
        {
            return;
        }

        if (!IsUnitAvailableForAttackExchange(blocker))
        {
            TrashUnitIfDeadOnField(blocker, blockerOwner, attacker);
            MarkBlockExchangeCancelled("Blocker not alive at exchange resolution.", finalizeFlowNow: true);
            return;
        }

        if (!attacker.Data.IsUnitLike() || !blocker.Data.IsUnitLike())
        {
            CancelInterruptedBlockRedirectAttackFlow("Invalid unit type for block combat.");
            return;
        }

        if (currentPhase != BattlePhase.MainPhase)
        {
            Debug.LogWarning("[BlockCombat] skipped: not MainPhase.");
            CancelInterruptedBlockRedirectAttackFlow("Not MainPhase.");
            return;
        }

        if (!IsUnitAvailableForAttackExchange(attacker))
        {
            TrashUnitIfDeadOnField(attacker, attackerOwner);
            CancelPendingUnitAttackFlow();
            return;
        }

        attackFlowBlockRedirectEngaged = true;
        attackFlowBlockRedirectUnit = blocker;
        CardController declaredDefenderForContext = attackFlowDeclaredDefenderUnit != null
            && attackFlowDeclaredDefenderUnit != blocker
            ? attackFlowDeclaredDefenderUnit
            : blocker;
        RegisterAttackFlowContextForOnAction(
            attacker,
            attackerOwner,
            AttackFlowStrikeKind.UnitVsUnit,
            declaredDefenderForContext,
            attackFlowBlockRedirectUnit);

        ResolveUnitVsUnitCombatStrikePowers(
            attacker,
            attackerOwner,
            blocker,
            out int attackerPowerForCombat,
            out int blockerPowerForCombat,
            applyOnAttackPairEffects: !attackFlowBlockRedirectFromShieldStrike);
        int blockerHpBeforeExchange = blocker.CurrentHp;
        int attackerHpBeforeExchange = attacker.CurrentHp;

        Debug.Log(
            $"[BlockCombat] {attacker.Data.cardName}(strikeAP:{attackerPowerForCombat}) vs {blocker.Data.cardName}(strikeAP:{blockerPowerForCombat}) "
            + $"owners attacker:{attackerOwner} blocker:{blockerOwner} note:OnActionデバフ後のCurrentPowerを使用(最低1強制なし)"
            + (attackFlowBlockRedirectFromShieldStrike ? " shieldOriginBlock:true" : string.Empty));

        if (ShouldAbortBlockRedirectCombatBeforeExchange(blocker, "Immediately before block exchange"))
        {
            return;
        }

        if (blockExchangeCancelledForCurrentAttack)
        {
            TrashUnitIfDeadOnField(blocker, blockerOwner, attacker);
            TrashUnitIfDeadOnField(attacker, attackerOwner);
            FinalizeBlockInterruptWithoutExchange();
            return;
        }

        // 交換開始時点でブロッカーが生存している場合は相互ダメージ（同時解決）。
        LogArgamaShieldBlockCloseCombatDebug(
            "ExecuteBlockRedirect_ApplyExchangeDamage",
            $"BEFORE exchange attackerHP:{attackerHpBeforeExchange} blockerHP:{blockerHpBeforeExchange} "
            + $"attackerAP:{attackerPowerForCombat} blockerAP:{blockerPowerForCombat}",
            attacker,
            blocker);

        blocker.ApplyDamage(attackerPowerForCombat);
        attacker.ApplyDamage(blockerPowerForCombat);

        LogArgamaShieldBlockCloseCombatDebug(
            "ExecuteBlockRedirect_AfterExchangeDamage",
            $"AFTER exchange attackerHP:{attacker.CurrentHp} blockerHP:{blocker.CurrentHp}",
            attacker,
            blocker);

        int blockerHpAfterExchange = blocker.CurrentHp;
        int attackerHpAfterExchange = attacker.CurrentHp;

        NotifyLocalUnitAttackResolved(
            attacker,
            blocker,
            attackerHpAfterExchange,
            blockerHpAfterExchange,
            blockCombat: true);

        if (TryApplyRestToUnit(blocker))
        {
            SyncOnlineRestFromAttackAuthority(blocker);
        }

        if (blocker.CurrentHp <= 0)
        {
            SendCardToTrash(blocker, blockerOwner, attacker);
        }

        if (attacker.CurrentHp <= 0)
        {
            SendCardToTrash(attacker, attackerOwner);
        }

        pendingUnitAttackAttacker = null;
        pendingOnAttackEffectResolvedAttacker = null;
        ClearTimedStatModifiersForAllInPlayCards(EffectDuration.UntilEndOfBattle);
        ClearAttackActiveEnemyGrants(EffectDuration.UntilEndOfBattle);
        DumpTurnResourceUsageLogs(attackerOwner, "block redirect unit combat");
        SyncAllResourceViewsFromRule();

        LogUnitAttackBlockedExchangeCalc(
            attacker,
            blocker,
            attackerOwner,
            attackerPowerForCombat,
            blockerPowerForCombat,
            attackerHpBeforeExchange,
            blockerHpBeforeExchange,
            attackerHpAfterExchange,
            blockerHpAfterExchange);
        LogAttackPostBattleFieldCompact(attacker, attackerOwner);
        FinishDeferredShieldAttackBlockFlow();
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

    /// <summary>ユニット戦の与ダメージ AP。OnAction 等の powerModifiers を CurrentPower 経由で反映（0 なら与ダメージなし）。</summary>
    private static int GetUnitStrikeDamagePower(CardController unit)
    {
        if (unit == null)
        {
            return 0;
        }

        return Mathf.Max(0, unit.CurrentPower);
    }

    /// <summary>
    /// OnAttack 効果を適用したうえで攻防の strike AP を確定する。
    /// OnAction コマンドの AP デバフは既に CurrentPower に載っている前提（戦闘直前に OnAction 済み）。
    /// </summary>
    private void ResolveUnitVsUnitCombatStrikePowers(
        CardController attacker,
        PlayerType attackerOwner,
        CardController defender,
        out int attackerStrikePower,
        out int defenderStrikePower,
        bool applyOnAttackPairEffects = true)
    {
        int defenderPowerBeforeOnAttackResolve = defender != null ? defender.CurrentPower : 0;
        int attackerPowerBeforeOnAttackResolve = attacker != null ? attacker.CurrentPower : 0;

        if (applyOnAttackPairEffects)
        {
            ApplyOnAttackPreCombatEffectsImmediately(attacker, attackerOwner);
            ApplyOnAttackEffectsForCombatPair(attacker, attackerOwner, defender);
        }

        attackerStrikePower = GetUnitStrikeDamagePower(attacker);
        defenderStrikePower = GetUnitStrikeDamagePower(defender);

        Debug.Log(
            $"[CombatPower] attackerStrike:{attackerStrikePower} (preOnAttackResolve:{attackerPowerBeforeOnAttackResolve}) "
            + $"defenderStrike:{defenderStrikePower} (preOnAttackResolve:{defenderPowerBeforeOnAttackResolve})");
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
            ApplyOnAttackEffectsFromSourceToDefender(attacker, attackerOwner, attacker.MountedPilot.Data, defender);
        }
    }

    private void ApplyOnAttackEffectsFromSourceToDefender(
        CardController attacker,
        PlayerType ownerType,
        CardData data,
        CardController defender)
    {
        if (attacker == null || attacker.Data == null || data == null || data.timedEffects == null || defender == null)
        {
            return;
        }

        CardController effectSource = data == attacker.Data ? attacker : attacker.MountedPilot;
        if (effectSource == null)
        {
            return;
        }

        EffectActivationContext activationContext = BuildOnAttackActivationContext(ownerType, attacker);
        for (int i = 0; i < data.timedEffects.Count; i++)
        {
            TimedEffectData timed = data.timedEffects[i];
            if (timed == null || timed.timing != EffectTiming.OnAttack || !timed.HasResolvedEffects())
            {
                continue;
            }

            if (!EffectActivationEvaluator.AreTimedConditionsMet(timed, activationContext))
            {
                continue;
            }

            IReadOnlyList<EffectData> resolvedOnAttack = timed.GetResolvedEffects();
            for (int j = 0; j < resolvedOnAttack.Count; j++)
            {
                EffectData effect = resolvedOnAttack[j];
                if (effect == null)
                {
                    continue;
                }

                // Draw 等は攻撃前チェーン（TryBeginOnAttackPreCombatEffectChain）で解決済み。
                if (IsOnAttackNonCombatEffect(effect))
                {
                    continue;
                }

                // 敵ユニット対象は TryOpenOnAttackEnemySelectionPanel で攻撃前に解決済み。二重適用しない。
                if (effect.target.IsOpponentUnitTarget())
                {
                    continue;
                }

                if (effect.HasEffectActivationConditions()
                    && !EffectActivationEvaluator.AreAllConditionsMet(
                        effect.effectActivationConditions,
                        activationContext))
                {
                    continue;
                }

                ApplyEffect(effectSource, ownerType, effect);
            }
        }
    }

    private void TriggerMountedPilotOnAttackEffects(CardController attacker, PlayerType attackerOwner)
    {
        if (attacker == null || attacker.Data == null || !attacker.Data.IsUnitLike())
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

    /// <summary>選択ユニットへ攻撃をリダイレクトすべきか（<see cref="CardData.isBlocker"/> または旧 BlockRedirect）。</summary>
    private bool ShouldRedirectAttackToBlocker(CardController reactionUnit, PlayerType defenderOwner)
    {
        if (reactionUnit == null || reactionUnit.Data == null)
        {
            return false;
        }

        EffectActivationContext ctx = BuildActivationContext(defenderOwner, reactionUnit);
        bool shouldRedirect = reactionUnit.Data.IsBlockerEligible(ctx);
        if (shouldRedirect)
        {
            return true;
        }

        if (reactionUnit.Data.timedEffects == null)
        {
            return false;
        }

        for (int i = 0; i < reactionUnit.Data.timedEffects.Count; i++)
        {
            TimedEffectData timed = reactionUnit.Data.timedEffects[i];
            if (timed == null || timed.timing != EffectTiming.OnEnemyAttack || !timed.HasResolvedEffects())
            {
                continue;
            }

            if (!EffectActivationEvaluator.AreTimedConditionsMet(timed, ctx))
            {
                continue;
            }

            IReadOnlyList<EffectData> resolvedOnEnemyAttack = timed.GetResolvedEffects();
            for (int j = 0; j < resolvedOnEnemyAttack.Count; j++)
            {
                EffectData effect = resolvedOnEnemyAttack[j];
                if (effect != null && effect.type == EffectType.BlockRedirect)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>OnEnemyAttack の BlockRedirect 以外の効果のみ適用（リダイレクト可否は別判定）。</summary>
    private void ApplyDefenderOnAttackReactionEffects(
        CardController reactionUnit,
        CardController attacker,
        PlayerType defenderOwner)
    {
        if (reactionUnit == null || reactionUnit.Data == null || reactionUnit.Data.timedEffects == null)
        {
            return;
        }

        EffectActivationContext ctx = BuildActivationContext(defenderOwner, reactionUnit);
        for (int i = 0; i < reactionUnit.Data.timedEffects.Count; i++)
        {
            TimedEffectData timed = reactionUnit.Data.timedEffects[i];
            if (timed == null || timed.timing != EffectTiming.OnEnemyAttack || !timed.HasResolvedEffects())
            {
                continue;
            }

            if (!EffectActivationEvaluator.AreTimedConditionsMet(timed, ctx))
            {
                continue;
            }

            IReadOnlyList<EffectData> resolvedOnEnemyAttack = timed.GetResolvedEffects();
            for (int j = 0; j < resolvedOnEnemyAttack.Count; j++)
            {
                EffectData effect = resolvedOnEnemyAttack[j];
                if (effect == null || effect.type == EffectType.BlockRedirect)
                {
                    continue;
                }

                    bool enemyUnitTarget = effect.target.IsOpponentUnitTarget();
                    if (enemyUnitTarget && attacker != null)
                    {
                        if (effect.target.IsSingleOpponentUnitPickTarget())
                        {
                            if (effect.target != TargetType.RestEnemyUnit || attacker.IsRestState)
                            {
                                ApplyEffectToSpecificTargets(
                                    reactionUnit,
                                    defenderOwner,
                                    effect,
                                    new List<CardController> { attacker });
                            }
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
        }
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

        bool shouldRedirect = ShouldRedirectAttackToBlocker(reactionUnit, defenderOwner);
        ApplyDefenderOnAttackReactionEffects(reactionUnit, attacker, defenderOwner);
        return shouldRedirect;
    }

    private void CommitBlockRedirectSelection(
        CardController attacker,
        CardController blocker,
        ref CardController defender,
        ref PlayerType defenderOwner)
    {
        if (blocker == null || blocker.Data == null)
        {
            return;
        }

        PlayerType blockerOwner = ResolveCardOwner(blocker.transform);
        if (!IsBlockRedirectReactionReady(blocker, blockerOwner))
        {
            Debug.LogWarning(
                $"[BlockRedirect] ブロック不可のユニットが選択されました: {blocker.Data.cardName} "
                + $"{(blocker.IsRestState ? "REST" : "ACTIVE")}");
            return;
        }

        ApplyDefenderOnAttackReactionEffects(blocker, attacker, blockerOwner);
        attackFlowBlockRedirectUnit = blocker;
        attackFlowBlockRedirectEngaged = true;
        defender = blocker;
        defenderOwner = blockerOwner;
        Debug.Log(
            $"[BlockRedirect] ブロッカー確定: {blocker.Data.cardName}(id:{blocker.Data.id}) owner:{blockerOwner}");
    }

    private bool IsBlockRedirectReactionReady(CardController unit, PlayerType defenderOwner)
    {
        if (unit == null || unit.Data == null || unit.IsRestState)
        {
            return false;
        }

        EffectActivationContext ctx = BuildActivationContext(defenderOwner, unit);
        return unit.Data.IsBlockerEligible(ctx);
    }

    private List<CardController> CollectSelectableBlockRedirectUnits(PlayerType attackerOwner)
    {
        PlayerType defenderOwner = attackerOwner == PlayerType.Player ? PlayerType.Enemy : PlayerType.Player;
        List<CardController> defenderUnits = GetAliveEnemyUnits(attackerOwner);
        List<CardController> result = new List<CardController>();
        for (int i = 0; i < defenderUnits.Count; i++)
        {
            CardController unit = defenderUnits[i];
            if (IsBlockRedirectReactionReady(unit, defenderOwner))
            {
                result.Add(unit);
            }
        }

        return result;
    }

    /// <summary>防御側バトルゾーンの BlockRedirect 持ちユニット（REST 含む。表示用）。</summary>
    private List<CardController> CollectBlockRedirectCapableUnits(PlayerType attackerOwner)
    {
        PlayerType defenderOwner = attackerOwner == PlayerType.Player ? PlayerType.Enemy : PlayerType.Player;
        List<CardController> defenderUnits = GetAliveEnemyUnits(attackerOwner);
        List<CardController> result = new List<CardController>();
        for (int i = 0; i < defenderUnits.Count; i++)
        {
            CardController unit = defenderUnits[i];
            if (unit == null || unit.Data == null)
            {
                continue;
            }

            if (unit.Data.IsBlockerUnit())
            {
                result.Add(unit);
            }
        }

        return result;
    }

    /// <summary>
    /// 防御側が AI、またはプレイヤーで選択可能ブロッカーが1体だけのとき自動で BlockRedirect を適用する。
    /// </summary>
    private bool TryAutoApplyBlockRedirectFromAttack(
        PlayerType attackerOwner,
        CardController attacker,
        out CardController blockUnit,
        out PlayerType blockOwner)
    {
        blockUnit = null;
        blockOwner = attackerOwner == PlayerType.Player ? PlayerType.Enemy : PlayerType.Player;
        if (attacker == null)
        {
            return false;
        }

        List<CardController> selectable = CollectSelectableBlockRedirectUnits(attackerOwner);
        if (selectable.Count == 0)
        {
            return false;
        }

        CardController pick;
        if (blockOwner == PlayerType.Enemy)
        {
            pick = PickEnemyAiBlockRedirectUnit(attacker, selectable);
        }
        else
        {
            // プレイヤー防御は手動選択（攻撃元表示パネル）。AI のみ自動。
            return false;
        }

        if (pick == null)
        {
            return false;
        }

        if (!IsBlockRedirectReactionReady(pick, blockOwner))
        {
            return false;
        }

        ApplyDefenderOnAttackReactionEffects(pick, attacker, blockOwner);
        blockUnit = pick;
        Debug.Log(
            $"[BlockRedirect] auto applied blocker:{pick.Data.cardName}(id:{pick.Data.id}) owner:{blockOwner} "
            + $"attacker:{attacker.Data?.cardName}");
        return true;
    }

    private static CardController PickEnemyAiBlockRedirectUnit(CardController attacker, List<CardController> selectable)
    {
        if (selectable == null || selectable.Count == 0)
        {
            return null;
        }

        CardController best = selectable[0];
        for (int i = 1; i < selectable.Count; i++)
        {
            CardController c = selectable[i];
            if (c == null)
            {
                continue;
            }

            if (c.CurrentHp < best.CurrentHp)
            {
                best = c;
            }
        }

        return best;
    }

    private void AppendAttackerPreviewToDefensePanel(GameObject root, CardController attacker)
    {
        if (root == null || attacker == null || attacker.Data == null || CardImagePrefab == null)
        {
            return;
        }

        TextMeshProUGUI attackerLabel = root.CreateChildTextCustom("AttackerLabel", UIAnchor.TopCenter, 240, 28);
        attackerLabel.text = "攻撃元";
        attackerLabel.fontSize = 18;
        attackerLabel.color = Color.white;
        attackerLabel.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -58f);

        GameObject attackerCard = Instantiate(CardImagePrefab, root.transform);
        RectTransform attackerRt = attackerCard.GetComponent<RectTransform>();
        if (attackerRt != null)
        {
            attackerRt.anchorMin = new Vector2(0.5f, 1f);
            attackerRt.anchorMax = new Vector2(0.5f, 1f);
            attackerRt.pivot = new Vector2(0.5f, 1f);
            attackerRt.sizeDelta = new Vector2(120f, 168f);
            attackerRt.anchoredPosition = new Vector2(0f, -88f);
        }

        CardController preview = attackerCard.GetComponent<CardController>();
        if (preview != null)
        {
            preview.SetUp(attacker.Data, _ => { });
        }

        TextMeshProUGUI attackerStat = attackerCard.CreateChildTextCustom(
            "AttackerStat",
            UIAnchor.BottomCenter,
            110,
            26);
        attackerStat.text = $"AP:{attacker.CurrentPower} HP:{attacker.CurrentHp} {(attacker.IsRestState ? "REST" : "ACTIVE")}";
        attackerStat.fontSize = 13;
        attackerStat.color = Color.white;
        attackerStat.alignment = TextAlignmentOptions.Center;

        Button attackerBtn = attackerCard.GetComponent<Button>();
        if (attackerBtn != null)
        {
            attackerBtn.interactable = false;
        }
    }

    private void AppendDefensePanelUnitCard(
        RectTransform content,
        CardController unit,
        bool canSelect,
        string statusSuffix,
        System.Action onPicked)
    {
        if (content == null || unit == null || unit.Data == null || CardImagePrefab == null)
        {
            return;
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
        statBgRt.anchoredPosition = Vector2.zero;
        Image statBgImg = statBg.GetComponent<Image>();
        statBgImg.color = new Color(0f, 0f, 0f, 0.55f);
        statBgImg.raycastTarget = false;

        TextMeshProUGUI statText = statBg.CreateChildTextCustom("StatText", UIAnchor.FullSize, 120, 24);
        statText.text = $"AP:{unit.CurrentPower} HP:{unit.CurrentHp} {(unit.IsRestState ? "REST" : "ACTIVE")}{statusSuffix}";
        statText.fontSize = 14;
        statText.color = canSelect ? Color.white : new Color(0.75f, 0.75f, 0.75f, 1f);
        statText.alignment = TextAlignmentOptions.Center;

        Button btn = cardItem.GetComponent<Button>();
        if (btn == null)
        {
            btn = cardItem.AddComponent<Button>();
        }

        btn.interactable = canSelect;
        if (canSelect)
        {
            btn.onClick.AddListener(() =>
            {
                onPicked?.Invoke();
                statText.color = new Color(1f, 0.35f, 0.35f, 1f);
            });
        }
    }

    private bool TryOpenAttackedSideUnitsPanel(
        PlayerType attackerOwner,
        CardController attackingUnitForDisplay,
        System.Action<CardController> onSelectDefender,
        System.Action onCloseResume,
        System.Action onBlockPassOrCancel = null)
    {
        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            return false;
        }

        List<CardController> selectableBlockRedirects = CollectSelectableBlockRedirectUnits(attackerOwner);
        if (selectableBlockRedirects.Count <= 0)
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

        PlayerType defenderOwner = attackerOwner == PlayerType.Player ? PlayerType.Enemy : PlayerType.Player;
        bool enemyAttackingPlayer = attackerOwner == PlayerType.Enemy
            && attackingUnitForDisplay != null
            && attackingUnitForDisplay.Data != null;
        List<CardController> blockRedirectUnits = CollectBlockRedirectCapableUnits(attackerOwner);

        if (enemyAttackingPlayer)
        {
            title.text = enableAttackFlowActionThinkTest
                ? "blockthink — Select an ACTIVE blocker, then Close"
                : "Enemy attack — Select an ACTIVE blocker, then Close";
            AppendAttackerPreviewToDefensePanel(root, attackingUnitForDisplay);
            scrollRt.anchoredPosition = new Vector2(0f, -200f);
        }
        else
        {
            title.text = enableAttackFlowActionThinkTest ? "blockthink" : "Select a blocker, then Close";
        }

        CardController selectedDefender = null;
        if (blockRedirectUnits.Count == 0)
        {
            TextMeshProUGUI empty = root.CreateChildTextCustom("AttackedSideEmpty", UIAnchor.TopCenter, 480, 40);
            empty.text = enemyAttackingPlayer
                ? "No blockers (isBlocker) on the battlefield"
                : "No blockers available";
            empty.fontSize = 20;
            empty.color = Color.white;
            empty.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -280f);
        }
        else
        {
            for (int i = 0; i < blockRedirectUnits.Count; i++)
            {
                CardController unit = blockRedirectUnits[i];
                if (unit == null || unit.Data == null)
                {
                    continue;
                }

                bool canSelect = IsBlockRedirectReactionReady(unit, defenderOwner);
                Debug.Log(
                    $"[AttackedSidePanelList] index:{i} card:{unit.Data.cardName} AP:{unit.CurrentPower} HP:{unit.CurrentHp} "
                    + $"{(unit.IsRestState ? "REST" : "ACTIVE")} selectable:{canSelect}");

                AppendDefensePanelUnitCard(
                    content,
                    unit,
                    canSelect,
                    canSelect ? "(Can block)" : " (REST — cannot block)",
                    () =>
                    {
                        selectedDefender = unit;
                        title.text = $"Blocker selected: {unit.Data.cardName}";
                    });
            }
        }

        Button closeBtn = root.CreateChildButton("Close");
        RectTransform closeRt = closeBtn.GetComponent<RectTransform>();
        closeRt.sizeDelta = new Vector2(180f, 48f);
        closeRt.anchorMin = new Vector2(0.5f, 0f);
        closeRt.anchorMax = new Vector2(0.5f, 0f);
        closeRt.pivot = new Vector2(0.5f, 0f);
        closeRt.anchoredPosition = new Vector2(100f, 36f);

        Button cancelBtn = root.CreateChildButton("Cancel");
        RectTransform cancelRt = cancelBtn.GetComponent<RectTransform>();
        cancelRt.sizeDelta = new Vector2(180f, 48f);
        cancelRt.anchorMin = new Vector2(0.5f, 0f);
        cancelRt.anchorMax = new Vector2(0.5f, 0f);
        cancelRt.pivot = new Vector2(0.5f, 0f);
        cancelRt.anchoredPosition = new Vector2(-100f, 36f);
        closeBtn.transform.SetAsLastSibling();
        cancelBtn.transform.SetAsLastSibling();

        void CloseAttackedSidePanel()
        {
            isAttackedSidePanelOpen = false;
            isOnActionPopupOpen = false;

            if (activeAttackFlowDebugPanelRoot == root)
            {
                activeAttackFlowDebugPanelRoot = null;
            }

            if (activeOnActionPopupRoot == root)
            {
                activeOnActionPopupRoot = null;
            }

            Destroy(root);
        }

        closeBtn.onClick.AddListener(() =>
        {
            CardController chosen = selectedDefender;
            CloseAttackedSidePanel();
            onSelectDefender?.Invoke(chosen);
            if (chosen != null)
            {
                onCloseResume?.Invoke();
            }
            else if (onBlockPassOrCancel != null)
            {
                onBlockPassOrCancel.Invoke();
            }
            else
            {
                onCloseResume?.Invoke();
            }
        });
        cancelBtn.onClick.AddListener(() =>
        {
            selectedDefender = null;
            CloseAttackedSidePanel();
            if (onBlockPassOrCancel != null)
            {
                onBlockPassOrCancel.Invoke();
            }
            else
            {
                ClearPendingBlockRedirectSelection();
                onSelectDefender?.Invoke(null);
                onCloseResume?.Invoke();
            }
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
            if (timed == null || timed.timing != EffectTiming.OnAttack || !timed.HasResolvedEffects())
            {
                continue;
            }

            IReadOnlyList<EffectData> resolvedOnAttack = timed.GetResolvedEffects();
            for (int j = 0; j < resolvedOnAttack.Count; j++)
            {
                EffectData effect = resolvedOnAttack[j];
                if (effect == null)
                {
                    continue;
                }

                if (!effect.target.IsOpponentUnitTarget() || !effect.selectionMode.IsAttackedTargetOnlyMode())
                {
                    continue;
                }

                if (effect.target == TargetType.RestEnemyUnit && (defender == null || !defender.IsRestState))
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
    /// 条件付きブロックは <see cref="RefreshAllHandsConditionalOnHandAuto"/> で適用する。
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

            for (int ti = 0; ti < card.Data.timedEffects.Count; ti++)
            {
                TimedEffectData timed = card.Data.timedEffects[ti];
                if (timed == null || timed.timing != EffectTiming.OnHandAuto || !timed.HasResolvedEffects())
                {
                    continue;
                }

                if (timed.HasActivationConditions())
                {
                    continue;
                }

                IReadOnlyList<EffectData> resolvedOnHandAuto = timed.GetResolvedEffects();
                Debug.Log(
                    $"[OnHandAuto] unconditional block side:{ownerType} card:{card.Data.cardName}(id:{card.Data.id}) "
                    + $"costBefore:{card.CurrentCost} effects:{resolvedOnHandAuto.Count}");
                for (int ei = 0; ei < resolvedOnHandAuto.Count; ei++)
                {
                    EffectData effect = resolvedOnHandAuto[ei];
                    if (effect == null)
                    {
                        continue;
                    }

                    ApplyEffectForOnHandAuto(card, ownerType, effect, null);
                }

                Debug.Log(
                    $"[OnHandAuto] unconditional done card:{card.Data.cardName}(id:{card.Data.id}) costAfter:{card.CurrentCost}");
            }
        }
        finally
        {
            onHandAutoProcessing.Remove(card);
        }

        RefreshAllHandsConditionalOnHandAuto();
    }

    /// <summary>
    /// OnHandAuto 用。Self への Buff/Debuff は手札の <see cref="CardController"/> に直接付与する。
    /// </summary>
    /// <param name="passiveSourceKey">条件付きパッシブ時は非 null（除去用）。無条件ワンショットは null。</param>
    private void ApplyEffectForOnHandAuto(CardController source, PlayerType ownerType, EffectData effect, string passiveSourceKey)
    {
        int magnitude = ResolveEffectMagnitude(effect, ownerType, source);
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
                    ApplyStatEffect(source, signedValue, effect.statTarget, effect.duration, passiveSourceKey);
                    int scaleCount = effect.valueMode == EffectValueMode.MultiplyByBoardCount
                        ? EffectMagnitudeResolver.CountForValueScale(
                            effect,
                            BuildActivationContext(ownerType, source),
                            source)
                        : 0;
                    Debug.Log(
                        $"[OnHandAuto] Self {effect.type} stat:{effect.statTarget} mode:{effect.valueMode} "
                        + $"value:{effect.value} resolved:{magnitude}"
                        + (effect.valueMode == EffectValueMode.MultiplyByBoardCount ? $" count:{scaleCount}" : string.Empty)
                        + $" cost:{costBefore}->{source.CurrentCost} level:{levelBefore}->{source.CurrentLevel} "
                        + $"card:{source.Data.cardName}(id:{source.Data.id})");
                    return;
                }

                break;
        }

        ApplyEffect(source, ownerType, effect);
    }

    private static List<CardController> CollectHandControllers(CardGameRule rule)
    {
        List<CardController> list = new List<CardController>();
        if (rule?.HandScrollContent == null)
        {
            return list;
        }

        RectTransform hand = rule.HandScrollContent;
        for (int i = 0; i < hand.childCount; i++)
        {
            CardController cc = hand.GetChild(i).GetComponent<CardController>();
            if (cc != null)
            {
                list.Add(cc);
            }
        }

        return list;
    }

    private EffectActivationContext BuildActivationContext(PlayerType ownerType, CardController sourceCard)
    {
        return new EffectActivationContext(
            ownerType,
            sourceCard,
            playerBattleZoneCards,
            enemyBattleZoneCards,
            CollectHandControllers(cardGameRule),
            CollectHandControllers(enemyCardGameRule),
            isOwnerTurn: ownerType == currentPlayerType,
            observedCards: GetActiveObservedCardsForActivation(),
            ownerTrashCardIds: cardGameRule.GetTrashCardIds(),
            opponentTrashCardIds: enemyCardGameRule.GetTrashCardIds(),
            priorChainDealtDamage: GetEffectChainDealtDamage());
    }

    /// <summary>OnAttack 効果の発動条件（搭乗パイロット等）評価用。攻撃ユニットの Mount 情報を明示する。</summary>
    private EffectActivationContext BuildOnAttackActivationContext(PlayerType ownerType, CardController attacker)
    {
        CardController host = attacker;
        CardController pilot = attacker != null ? attacker.MountedPilot : null;
        return new EffectActivationContext(
            ownerType,
            attacker,
            playerBattleZoneCards,
            enemyBattleZoneCards,
            CollectHandControllers(cardGameRule),
            CollectHandControllers(enemyCardGameRule),
            isOwnerTurn: ownerType == currentPlayerType,
            mountHostUnit: host,
            mountedPilot: pilot,
            observedCards: GetActiveObservedCardsForActivation(),
            ownerTrashCardIds: cardGameRule.GetTrashCardIds(),
            opponentTrashCardIds: enemyCardGameRule.GetTrashCardIds(),
            priorChainDealtDamage: GetEffectChainDealtDamage());
    }

    private EffectActivationContext BuildPilotMountActivationContext(
        PlayerType ownerType,
        CardController sourceCard,
        CardController hostUnit,
        CardController pilot)
    {
        return new EffectActivationContext(
            ownerType,
            sourceCard,
            playerBattleZoneCards,
            enemyBattleZoneCards,
            CollectHandControllers(cardGameRule),
            CollectHandControllers(enemyCardGameRule),
            isOwnerTurn: ownerType == currentPlayerType,
            mountHostUnit: hostUnit,
            mountedPilot: pilot,
            observedCards: GetActiveObservedCardsForActivation(),
            ownerTrashCardIds: cardGameRule.GetTrashCardIds(),
            opponentTrashCardIds: enemyCardGameRule.GetTrashCardIds(),
            priorChainDealtDamage: GetEffectChainDealtDamage());
    }

    private void RefreshAllHandsConditionalOnHandAuto()
    {
        RefreshHandConditionalOnHandAuto(PlayerType.Player);
        RefreshHandConditionalOnHandAuto(PlayerType.Enemy);
    }

    private void RefreshHandConditionalOnHandAuto(PlayerType side)
    {
        CardGameRule rule = side == PlayerType.Player ? cardGameRule : enemyCardGameRule;
        RectTransform hand = rule?.HandScrollContent;
        if (hand == null)
        {
            return;
        }

        for (int i = 0; i < hand.childCount; i++)
        {
            CardController cc = hand.GetChild(i).GetComponent<CardController>();
            ApplyConditionalOnHandAutoPassiveToCard(cc, side);
        }
    }

    private static string MakeOnHandAutoPassiveSourceKey(CardController handCard, int timedBlockIndex)
    {
        return $"OnHandAutoPassive:{handCard.GetEntityId()}:{timedBlockIndex}";
    }

    private void ApplyConditionalOnHandAutoPassiveToCard(CardController cc, PlayerType ownerType)
    {
        if (cc == null || cc.Data == null || cc.Data.timedEffects == null)
        {
            return;
        }

        EffectActivationContext activationContext = BuildActivationContext(ownerType, cc);
        for (int bi = 0; bi < cc.Data.timedEffects.Count; bi++)
        {
            TimedEffectData timed = cc.Data.timedEffects[bi];
            if (timed == null || !timed.HasResolvedEffects())
            {
                continue;
            }

            if (!timed.IsHandConditionalPassiveBlock())
            {
                continue;
            }

            string key = MakeOnHandAutoPassiveSourceKey(cc, bi);
            cc.RemoveStatModifiersBySource(key);
            if (!EffectActivationEvaluator.AreTimedConditionsMet(timed, activationContext))
            {
                continue;
            }

            IReadOnlyList<EffectData> resolvedHandPassive = timed.GetResolvedEffects();
            for (int ei = 0; ei < resolvedHandPassive.Count; ei++)
            {
                EffectData effect = resolvedHandPassive[ei];
                if (effect == null)
                {
                    continue;
                }

                ApplyEffectForOnHandAuto(cc, ownerType, effect, key);
            }
        }
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

        if (timing == EffectTiming.OnPlayed)
        {
            TriggerOnPlayedEffects(sourceCard, ownerType, null);
            return;
        }

        if (timing == EffectTiming.OnDestroyed || timing == EffectTiming.OnUnitDestroyed)
        {
            TriggerOnDestroyedEffects(sourceCard, ownerType, null);
            return;
        }

        if (timing == EffectTiming.OnEnemyUnitDestroyed)
        {
            return;
        }

        if (timing == EffectTiming.OnObservedUnitTrigger)
        {
            return;
        }

        if (timing == EffectTiming.OnPilotMounted || timing == EffectTiming.OnLink)
        {
            return;
        }

        EffectActivationContext activationContext = timing == EffectTiming.OnAttack
            ? BuildOnAttackActivationContext(ownerType, sourceCard)
            : BuildActivationContext(ownerType, sourceCard);

        for (int i = 0; i < sourceCard.Data.timedEffects.Count; i++)
        {
            TimedEffectData timed = sourceCard.Data.timedEffects[i];
            if (timed == null || timed.timing != timing || !timed.HasResolvedEffects())
            {
                continue;
            }

            if (!EffectActivationEvaluator.AreTimedConditionsMet(timed, activationContext))
            {
                continue;
            }

            IReadOnlyList<EffectData> resolvedTimed = timed.GetResolvedEffects();
            for (int j = 0; j < resolvedTimed.Count; j++)
            {
                EffectData effect = resolvedTimed[j];
                if (effect == null)
                {
                    continue;
                }
                if (timing == EffectTiming.OnAttack && effect.target.IsOpponentUnitTarget())
                {
                    // Enemy unit target effects are resolved before attack target decision.
                    continue;
                }

                if (timing == EffectTiming.OnAttack
                    && IsOnAttackNonCombatEffect(effect)
                    && HasOnAttackPreCombatEffectsBeenApplied(sourceCard))
                {
                    continue;
                }

                if (effect.HasEffectActivationConditions()
                    && !EffectActivationEvaluator.AreAllConditionsMet(
                        effect.effectActivationConditions,
                        activationContext))
                {
                    continue;
                }

                if (EffectRequiresManualUnitSelection(effect))
                {
                    continue;
                }

                ApplyEffect(sourceCard, ownerType, effect);
            }
        }
    }

    /// <summary>パイロット搭乗時（OnPilotMounted）。ホストユニットの設定に従い片方のみ／両方・順序を解決。</summary>
    private void TriggerOnPilotMountedEffects(
        CardController hostUnit,
        CardController pilot,
        PlayerType ownerType,
        System.Action onComplete)
    {
        if (hostUnit == null || pilot == null || hostUnit.Data == null || pilot.Data == null)
        {
            onComplete?.Invoke();
            return;
        }

        UnitLinkExtensions.ResolveOnPilotMountedExecutionPlan(
            hostUnit.Data,
            out bool resolveUnit,
            out bool resolvePilot,
            out bool unitFirst);

        List<TimedEffectData> unitBlocks = resolveUnit
            ? CollectMountTimedBlocks(hostUnit, ownerType, hostUnit, pilot, EffectTiming.OnPilotMounted)
            : new List<TimedEffectData>();
        List<TimedEffectData> pilotBlocks = resolvePilot
            ? CollectMountTimedBlocks(pilot, ownerType, hostUnit, pilot, EffectTiming.OnPilotMounted)
            : new List<TimedEffectData>();

        if (unitBlocks.Count == 0 && pilotBlocks.Count == 0)
        {
            onComplete?.Invoke();
            return;
        }

        Debug.Log(
            $"[OnPilotMounted] 開始: {pilot.Data.cardName} → {hostUnit.Data.cardName} "
            + $"source:{hostUnit.Data.pilotMountOnPilotMountedSource} order:{hostUnit.Data.pilotMountOnPilotMountedOrder} "
            + $"unitBlocks:{unitBlocks.Count} pilotBlocks:{pilotBlocks.Count}");

        void RunUnitThenPilot()
        {
            RunMountTimedBlocks(hostUnit, ownerType, unitBlocks, 0, () =>
            {
                RunMountTimedBlocks(pilot, ownerType, pilotBlocks, 0, FinishPilotMountChain);
            }, hostUnit, pilot);
        }

        void RunPilotThenUnit()
        {
            RunMountTimedBlocks(pilot, ownerType, pilotBlocks, 0, () =>
            {
                RunMountTimedBlocks(hostUnit, ownerType, unitBlocks, 0, FinishPilotMountChain);
            }, hostUnit, pilot);
        }

        void FinishPilotMountChain()
        {
            _pilotMountEffectHostUnit = null;
            EndEffectChainObservationScope();
            RefreshAllFieldOwnerTurnPassives();
            onComplete?.Invoke();
        }

        _pilotMountEffectHostUnit = hostUnit;
        BeginEffectChainObservationScope();
        if (unitFirst)
        {
            RunUnitThenPilot();
        }
        else
        {
            RunPilotThenUnit();
        }
    }

    /// <summary>Link 条件を満たす搭乗時（OnLink）。ホストの pilotMount 設定に従いユニット／パイロット双方を解決。</summary>
    private void TriggerOnLinkEffects(
        CardController hostUnit,
        CardController pilot,
        PlayerType ownerType,
        System.Action onComplete)
    {
        if (hostUnit == null || pilot == null || hostUnit.Data == null || pilot.Data == null)
        {
            onComplete?.Invoke();
            return;
        }

        if (!UnitLinkExtensions.HasValidLinkPilot(hostUnit.Data, pilot))
        {
            onComplete?.Invoke();
            return;
        }

        UnitLinkExtensions.ResolveOnPilotMountedExecutionPlan(
            hostUnit.Data,
            out bool resolveUnit,
            out bool resolvePilot,
            out bool unitFirst);

        List<TimedEffectData> unitBlocks = resolveUnit
            ? CollectMountTimedBlocks(hostUnit, ownerType, hostUnit, pilot, EffectTiming.OnLink)
            : new List<TimedEffectData>();
        List<TimedEffectData> pilotBlocks = resolvePilot
            ? CollectMountTimedBlocks(pilot, ownerType, hostUnit, pilot, EffectTiming.OnLink)
            : new List<TimedEffectData>();

        if (unitBlocks.Count == 0 && pilotBlocks.Count == 0)
        {
            onComplete?.Invoke();
            return;
        }

        Debug.Log(
            $"[OnLink] 開始: {pilot.Data.cardName} → {hostUnit.Data.cardName} "
            + $"source:{hostUnit.Data.pilotMountOnPilotMountedSource} order:{hostUnit.Data.pilotMountOnPilotMountedOrder} "
            + $"unitBlocks:{unitBlocks.Count} pilotBlocks:{pilotBlocks.Count}");

        void RunUnitThenPilot()
        {
            RunMountTimedBlocks(hostUnit, ownerType, unitBlocks, 0, () =>
            {
                RunMountTimedBlocks(pilot, ownerType, pilotBlocks, 0, FinishOnLinkChain);
            }, hostUnit, pilot);
        }

        void RunPilotThenUnit()
        {
            RunMountTimedBlocks(pilot, ownerType, pilotBlocks, 0, () =>
            {
                RunMountTimedBlocks(hostUnit, ownerType, unitBlocks, 0, FinishOnLinkChain);
            }, hostUnit, pilot);
        }

        void FinishOnLinkChain()
        {
            _pilotMountEffectHostUnit = null;
            EndEffectChainObservationScope();
            RefreshAllFieldOwnerTurnPassives();
            onComplete?.Invoke();
        }

        _pilotMountEffectHostUnit = hostUnit;
        BeginEffectChainObservationScope();
        if (unitFirst)
        {
            RunUnitThenPilot();
        }
        else
        {
            RunPilotThenUnit();
        }
    }

    private List<TimedEffectData> CollectMountTimedBlocks(
        CardController sourceCard,
        PlayerType ownerType,
        CardController hostUnit,
        CardController pilot,
        EffectTiming mountTiming)
    {
        List<TimedEffectData> blocks = new List<TimedEffectData>();
        if (sourceCard == null || sourceCard.Data == null || sourceCard.Data.timedEffects == null)
        {
            return blocks;
        }

        EffectActivationContext activationContext =
            BuildPilotMountActivationContext(ownerType, sourceCard, hostUnit, pilot);
        string timingLabel = mountTiming == EffectTiming.OnLink ? "OnLink" : "OnPilotMounted";
        for (int i = 0; i < sourceCard.Data.timedEffects.Count; i++)
        {
            TimedEffectData timed = sourceCard.Data.timedEffects[i];
            if (timed == null || !IsMountResolutionBlock(timed, mountTiming))
            {
                continue;
            }

            if (!timed.ShouldDeferActivationToRunTime()
                && !EffectActivationEvaluator.AreTimedConditionsMet(timed, activationContext))
            {
                Debug.Log(
                    $"[{timingLabel}] 条件未達: {sourceCard.Data.cardName}(id:{sourceCard.Data.id}) block:{i}");
                continue;
            }

            blocks.Add(timed);
        }

        return blocks;
    }

    private static bool IsMountResolutionBlock(TimedEffectData timed, EffectTiming mountTiming)
    {
        return mountTiming == EffectTiming.OnLink
            ? timed.IsOnLinkResolutionBlock()
            : timed.IsOnPilotMountedResolutionBlock();
    }

    private void RunMountTimedBlocks(
        CardController sourceCard,
        PlayerType ownerType,
        List<TimedEffectData> blocks,
        int blockIndex,
        System.Action onComplete,
        CardController mountHostUnit = null,
        CardController mountPilot = null)
    {
        if (blocks == null || blockIndex >= blocks.Count)
        {
            onComplete?.Invoke();
            return;
        }

        TimedEffectData block = blocks[blockIndex];
        if (block.IsFieldOwnerTurnStatPassiveBlock())
        {
            RunMountTimedBlocks(
                sourceCard,
                ownerType,
                blocks,
                blockIndex + 1,
                onComplete,
                mountHostUnit,
                mountPilot);
            return;
        }

        EffectActivationContext activationContext = mountHostUnit != null || mountPilot != null
            ? BuildPilotMountActivationContext(ownerType, sourceCard, mountHostUnit ?? sourceCard, mountPilot)
            : BuildActivationContext(ownerType, sourceCard);
        if (!CanRunTimedBlockAtChainTime(block, activationContext, "MountChain"))
        {
            RunMountTimedBlocks(
                sourceCard,
                ownerType,
                blocks,
                blockIndex + 1,
                onComplete,
                mountHostUnit,
                mountPilot);
            return;
        }

        TryExecuteOnPlayedEffectChain(
            sourceCard,
            ownerType,
            block.GetResolvedEffects(),
            0,
            () => RunMountTimedBlocks(
                sourceCard,
                ownerType,
                blocks,
                blockIndex + 1,
                onComplete,
                mountHostUnit,
                mountPilot));
    }

    /// <summary>場に出した時（OnPlayed）。条件付きブロック内の効果を順に解決し、敵ユニット選択が必要なら UI を出す。</summary>
    private void TriggerOnPlayedEffects(CardController sourceCard, PlayerType ownerType, System.Action onComplete)
    {
        if (sourceCard == null || sourceCard.Data == null || sourceCard.Data.timedEffects == null)
        {
            onComplete?.Invoke();
            return;
        }

        EffectActivationContext activationContext = BuildActivationContext(ownerType, sourceCard);
        List<TimedEffectData> blocks = new List<TimedEffectData>();
        for (int i = 0; i < sourceCard.Data.timedEffects.Count; i++)
        {
            TimedEffectData timed = sourceCard.Data.timedEffects[i];
            if (timed == null || !timed.IsOnFieldPlayedResolutionBlock())
            {
                continue;
            }

            if (!timed.ShouldDeferActivationToRunTime()
                && !EffectActivationEvaluator.AreTimedConditionsMet(timed, activationContext))
            {
                Debug.Log(
                    $"[OnPlayed] 条件未達のためスキップ: {sourceCard.Data.cardName}(id:{sourceCard.Data.id}) block:{i}");
                continue;
            }

            blocks.Add(timed);
        }

        if (blocks.Count == 0)
        {
            Debug.Log(
                $"[OnPlayed] 解決対象ブロックなし: {sourceCard.Data.cardName}(id:{sourceCard.Data.id}) "
                + $"(手札パッシブ専用・効果未設定・発動条件未達のいずれか)");
            onComplete?.Invoke();
            return;
        }

        Debug.Log(
            $"[OnPlayed] 開始: {sourceCard.Data.cardName}(id:{sourceCard.Data.id}) blocks:{blocks.Count}");
        BeginEffectChainObservationScope();
        RunOnPlayedTimedBlocks(sourceCard, ownerType, blocks, 0, () =>
        {
            EndEffectChainObservationScope();
            onComplete?.Invoke();
        });
    }

    /// <summary>破壊時（OnDestroyed / OnUnitDestroyed）。条件付きブロック内の効果を順に解決する。</summary>
    private void TriggerOnDestroyedEffects(CardController sourceCard, PlayerType ownerType, System.Action onComplete)
    {
        if (sourceCard == null || sourceCard.Data == null || sourceCard.Data.timedEffects == null)
        {
            onComplete?.Invoke();
            return;
        }

        EffectActivationContext activationContext = BuildActivationContext(ownerType, sourceCard);
        List<TimedEffectData> blocks = new List<TimedEffectData>();
        for (int i = 0; i < sourceCard.Data.timedEffects.Count; i++)
        {
            TimedEffectData timed = sourceCard.Data.timedEffects[i];
            if (timed == null || !timed.IsOnUnitDestroyedResolutionBlock())
            {
                continue;
            }

            if (!EffectActivationEvaluator.AreTimedConditionsMet(timed, activationContext))
            {
                Debug.Log(
                    $"[OnDestroyed] 条件未達のためスキップ: {sourceCard.Data.cardName}(id:{sourceCard.Data.id}) block:{i}");
                continue;
            }

            blocks.Add(timed);
        }

        if (blocks.Count == 0)
        {
            onComplete?.Invoke();
            return;
        }

        Debug.Log(
            $"[OnDestroyed] 開始: {sourceCard.Data.cardName}(id:{sourceCard.Data.id}) blocks:{blocks.Count}");
        RunOnDestroyedTimedBlocks(sourceCard, ownerType, blocks, 0, onComplete);
    }

    private void RunOnDestroyedTimedBlocks(
        CardController sourceCard,
        PlayerType ownerType,
        List<TimedEffectData> blocks,
        int blockIndex,
        System.Action onComplete)
    {
        if (blocks == null || blockIndex >= blocks.Count)
        {
            onComplete?.Invoke();
            return;
        }

        TimedEffectData block = blocks[blockIndex];
        TryExecuteOnDestroyedEffectChain(
            sourceCard,
            ownerType,
            block.GetResolvedEffects(),
            0,
            () => RunOnDestroyedTimedBlocks(sourceCard, ownerType, blocks, blockIndex + 1, onComplete));
    }

    private void TryExecuteOnDestroyedEffectChain(
        CardController sourceCard,
        PlayerType ownerType,
        IReadOnlyList<EffectData> effects,
        int index,
        System.Action onDone)
    {
        if (effects == null || index >= effects.Count)
        {
            onDone?.Invoke();
            return;
        }

        EffectData effect = effects[index];
        if (effect == null)
        {
            TryExecuteOnDestroyedEffectChain(sourceCard, ownerType, effects, index + 1, onDone);
            return;
        }

        if (EffectRequiresManualUnitSelection(effect))
        {
            List<CardController> candidates = ResolveSelectableEffectTargets(sourceCard, ownerType, effect);
            if (candidates.Count == 0)
            {
                TryExecuteOnDestroyedEffectChain(sourceCard, ownerType, effects, index + 1, onDone);
                return;
            }

            if (ownerType == PlayerType.Enemy)
            {
                EnemyAiEffectPickContext pickCtx = BuildEnemyAiEffectPickContext(ownerType, sourceCard, null, null);
                CardController picked = PickEnemyAiEffectTarget(effect, pickCtx, candidates);
                if (picked != null)
                {
                    ApplyEffectToSpecificTargets(sourceCard, ownerType, effect, new List<CardController> { picked });
                }

                TryExecuteOnDestroyedEffectChain(sourceCard, ownerType, effects, index + 1, onDone);
                return;
            }

            Debug.Log(
                $"[OnDestroyed] 手動対象選択は破壊解決中未対応のためスキップ ({effect.FormatEffectSelectionSummary()})。");
            TryExecuteOnDestroyedEffectChain(sourceCard, ownerType, effects, index + 1, onDone);
            return;
        }

        ApplyEffectRespectingLookAsync(
            sourceCard,
            ownerType,
            effect,
            () => TryExecuteOnDestroyedEffectChain(sourceCard, ownerType, effects, index + 1, onDone));
    }

    /// <summary>
    /// このカードが敵ユニットを破壊した時。キルしたカード（destroyedBy）自身の OnEnemyUnitDestroyed のみ解決する。
    /// </summary>
    private void TriggerOnEnemyUnitDestroyedEffects(
        CardController destroyedUnit,
        PlayerType destroyedOwner,
        CardController destroyedBy,
        System.Action onComplete)
    {
        if (!TryResolveEnemyUnitKillContext(destroyedUnit, destroyedOwner, destroyedBy, out CardController killer, out PlayerType killerOwner))
        {
            onComplete?.Invoke();
            return;
        }

        if (!HasEffectTiming(killer.Data, EffectTiming.OnEnemyUnitDestroyed))
        {
            onComplete?.Invoke();
            return;
        }

        EffectActivationContext activationContext = BuildActivationContext(killerOwner, killer);
        List<TimedEffectData> blocks = new List<TimedEffectData>();
        for (int i = 0; i < killer.Data.timedEffects.Count; i++)
        {
            TimedEffectData timed = killer.Data.timedEffects[i];
            if (timed == null || !timed.IsOnEnemyUnitDestroyedResolutionBlock())
            {
                continue;
            }

            if (!EffectActivationEvaluator.AreTimedConditionsMet(timed, activationContext))
            {
                continue;
            }

            blocks.Add(timed);
        }

        if (blocks.Count == 0)
        {
            onComplete?.Invoke();
            return;
        }

        Debug.Log(
            $"[OnEnemyUnitDestroyed] キル:{killer.Data.cardName}(id:{killer.Data.id}) "
            + $"→ 破壊:{destroyedUnit.Data.cardName}(id:{destroyedUnit.Data.id}) blocks:{blocks.Count}");
        RunOnEnemyUnitDestroyedTimedBlocks(killer, killerOwner, blocks, 0, onComplete);
    }

    private void RunOnEnemyUnitDestroyedTimedBlocks(
        CardController sourceCard,
        PlayerType beneficiary,
        List<TimedEffectData> blocks,
        int blockIndex,
        System.Action onComplete)
    {
        if (blocks == null || blockIndex >= blocks.Count)
        {
            onComplete?.Invoke();
            return;
        }

        TimedEffectData block = blocks[blockIndex];
        TryExecuteOnEnemyUnitDestroyedEffectChain(
            sourceCard,
            beneficiary,
            block.GetResolvedEffects(),
            0,
            () => RunOnEnemyUnitDestroyedTimedBlocks(sourceCard, beneficiary, blocks, blockIndex + 1, onComplete));
    }

    private void TryExecuteOnEnemyUnitDestroyedEffectChain(
        CardController sourceCard,
        PlayerType beneficiary,
        IReadOnlyList<EffectData> effects,
        int index,
        System.Action onDone)
    {
        if (effects == null || index >= effects.Count)
        {
            onDone?.Invoke();
            return;
        }

        EffectData effect = effects[index];
        if (effect == null)
        {
            TryExecuteOnEnemyUnitDestroyedEffectChain(sourceCard, beneficiary, effects, index + 1, onDone);
            return;
        }

        if (EffectRequiresManualUnitSelection(effect))
        {
            List<CardController> candidates = ResolveSelectableEffectTargets(sourceCard, beneficiary, effect);
            if (candidates.Count == 0)
            {
                TryExecuteOnEnemyUnitDestroyedEffectChain(sourceCard, beneficiary, effects, index + 1, onDone);
                return;
            }

            if (beneficiary == PlayerType.Enemy)
            {
                EnemyAiEffectPickContext pickCtx = BuildEnemyAiEffectPickContext(beneficiary, sourceCard, null, null);
                CardController picked = PickEnemyAiEffectTarget(effect, pickCtx, candidates);
                if (picked != null)
                {
                    ApplyEffectToSpecificTargets(sourceCard, beneficiary, effect, new List<CardController> { picked });
                }

                TryExecuteOnEnemyUnitDestroyedEffectChain(sourceCard, beneficiary, effects, index + 1, onDone);
                return;
            }

            Debug.Log(
                $"[OnEnemyUnitDestroyed] 手動対象選択は未対応のためスキップ ({effect.FormatEffectSelectionSummary()})。");
            TryExecuteOnEnemyUnitDestroyedEffectChain(sourceCard, beneficiary, effects, index + 1, onDone);
            return;
        }

        ApplyEffectRespectingLookAsync(
            sourceCard,
            beneficiary,
            effect,
            () => TryExecuteOnEnemyUnitDestroyedEffectChain(sourceCard, beneficiary, effects, index + 1, onDone));
    }

    private void RunOnPlayedTimedBlocks(
        CardController sourceCard,
        PlayerType ownerType,
        List<TimedEffectData> blocks,
        int blockIndex,
        System.Action onComplete)
    {
        if (blocks == null || blockIndex >= blocks.Count)
        {
            onComplete?.Invoke();
            return;
        }

        TimedEffectData block = blocks[blockIndex];
        EffectActivationContext activationContext = BuildActivationContext(ownerType, sourceCard);
        if (!CanRunTimedBlockAtChainTime(block, activationContext, "OnPlayed"))
        {
            RunOnPlayedTimedBlocks(sourceCard, ownerType, blocks, blockIndex + 1, onComplete);
            return;
        }

        TryExecuteOnPlayedEffectChain(
            sourceCard,
            ownerType,
            block.GetResolvedEffects(),
            0,
            () => RunOnPlayedTimedBlocks(sourceCard, ownerType, blocks, blockIndex + 1, onComplete));
    }

    private void TryExecuteOnPlayedEffectChain(
        CardController sourceCard,
        PlayerType ownerType,
        IReadOnlyList<EffectData> effects,
        int index,
        System.Action onDone)
    {
        if (effects == null || index >= effects.Count)
        {
            onDone?.Invoke();
            return;
        }

        EffectData effect = effects[index];
        if (effect == null)
        {
            TryExecuteOnPlayedEffectChain(sourceCard, ownerType, effects, index + 1, onDone);
            return;
        }

        EffectActivationContext activationContext = BuildActivationContext(ownerType, sourceCard);
        if (!ShouldApplyChainedEffect(effect, activationContext, "EffectChain"))
        {
            TryExecuteOnPlayedEffectChain(sourceCard, ownerType, effects, index + 1, onDone);
            return;
        }

        if (TryExecutePriorChainPickedTargetEffect(
            sourceCard,
            ownerType,
            effect,
            () => TryExecuteOnPlayedEffectChain(sourceCard, ownerType, effects, index + 1, onDone)))
        {
            return;
        }

        if (effect.type == EffectType.DeployUnit && effect.RequiresDeployUnitZoneSelection())
        {
            ApplyDeployUnitEffect(
                sourceCard,
                ownerType,
                effect,
                () => TryExecuteOnPlayedEffectChain(sourceCard, ownerType, effects, index + 1, onDone));
            return;
        }

        if (EffectRequiresManualHandSelection(effect))
        {
            TryExecuteManualHandSelectionEffect(
                sourceCard,
                ownerType,
                effect,
                () => TryExecuteOnPlayedEffectChain(sourceCard, ownerType, effects, index + 1, onDone));
            return;
        }

        if (EffectRequiresManualUnitSelection(effect))
        {
            TryExecuteManualUnitSelectionEffect(
                sourceCard,
                ownerType,
                effect,
                null,
                () => TryExecuteOnPlayedEffectChain(sourceCard, ownerType, effects, index + 1, onDone));
            return;
        }

        if (IsFieldWideUnitDamageEffect(effect))
        {
            TryApplyFieldWideDamageWithPreviewAsync(
                sourceCard,
                ownerType,
                effect,
                () => TryExecuteOnPlayedEffectChain(sourceCard, ownerType, effects, index + 1, onDone));
            return;
        }

        ApplyEffectRespectingLookAsync(
            sourceCard,
            ownerType,
            effect,
            () => TryExecuteOnPlayedEffectChain(sourceCard, ownerType, effects, index + 1, onDone));
    }

    private void ApplyUnitDamageAndTrackChain(CardController targetUnit, int damageAmount)
    {
        if (targetUnit == null || damageAmount <= 0)
        {
            return;
        }

        int hpBefore = targetUnit.CurrentHp;
        targetUnit.ApplyDamage(damageAmount);
        if (targetUnit.CurrentHp < hpBefore)
        {
            MarkEffectChainDealtDamage();
        }
    }

    private void TryExecuteManualUnitSelectionEffect(
        CardController sourceCard,
        PlayerType ownerType,
        EffectData effect,
        CardController attackingUnitInAttackFlow,
        System.Action onDone)
    {
        if (effect == null)
        {
            onDone?.Invoke();
            return;
        }

        List<CardController> candidates = ResolveSelectableEffectTargets(sourceCard, ownerType, effect);
        if (candidates.Count == 0)
        {
            Debug.Log(
                $"[Effect] 選択可能な対象がありません ({effect.FormatEffectSelectionSummary()})。");
            onDone?.Invoke();
            return;
        }

        if (ownerType == PlayerType.Enemy)
        {
            EnemyAiEffectPickContext pickCtx = BuildEnemyAiEffectPickContext(ownerType, sourceCard, null, null);
            if (effect.selectionMode.IsMultipleUnitPickMode())
            {
                List<CardController> aiPicks = PickEnemyAiEffectTargets(effect, pickCtx, candidates);
                if (aiPicks.Count > 0)
                {
                    ApplyEffectToSpecificTargets(sourceCard, ownerType, effect, aiPicks);
                }
            }
            else
            {
                CardController picked = PickEnemyAiEffectTarget(effect, pickCtx, candidates);
                if (picked != null)
                {
                    ApplyEffectToSpecificTargets(sourceCard, ownerType, effect, new List<CardController> { picked });
                }
            }

            onDone?.Invoke();
            return;
        }

        if (effect.selectionMode.IsMultipleUnitPickMode())
        {
            OpenManualMultiUnitTargetSelectionUI(
                sourceCard,
                ownerType,
                effect,
                candidates,
                attackingUnitInAttackFlow,
                selected =>
                {
                    if (selected != null && selected.Count > 0)
                    {
                        ApplyEffectToSpecificTargets(sourceCard, ownerType, effect, selected);
                    }

                    onDone?.Invoke();
                });
            return;
        }

        bool forceSelectionUi = effect.type == EffectType.GrantAttackFlag;
        if (!forceSelectionUi && candidates.Count == 1)
        {
            ApplyEffectToSpecificTargets(sourceCard, ownerType, effect, candidates);
            onDone?.Invoke();
            return;
        }

        OpenManualUnitTargetSelectionUI(
            sourceCard,
            ownerType,
            effect,
            candidates,
            attackingUnitInAttackFlow,
            picked =>
            {
                if (picked != null)
                {
                    ApplyEffectToSpecificTargets(sourceCard, ownerType, effect, new List<CardController> { picked });
                }

                onDone?.Invoke();
            });
    }

    private void OpenManualUnitTargetSelectionUI(
        CardController source,
        PlayerType ownerType,
        EffectData effect,
        List<CardController> candidates,
        CardController attackingUnitInAttackFlow,
        System.Action<CardController> onPicked)
    {
        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            onPicked?.Invoke(null);
            return;
        }

        DestroyActiveOnActionPopupIfAny();
        GameObject root = new GameObject(
            "ManualUnitTargetSelect",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        activeOnActionPopupRoot = root;
        isOnActionPopupOpen = true;
        root.transform.SetParent(canvas.transform, false);
        root.transform.SetAsLastSibling();
        root.SetFullSize();
        Image dim = root.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.55f);
        dim.raycastTarget = true;

        TextMeshProUGUI title = root.CreateChildTextCustom("ManualUnitTargetTitle", UIAnchor.TopCenter, 720, 48);
        title.text = FormatManualUnitSelectionTitle(effect, attackingUnitInAttackFlow);
        title.color = Color.white;
        title.fontSize = 24;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -20f);

        if (effect != null)
        {
            TextMeshProUGUI summary = root.CreateChildTextCustom("ManualUnitTargetSummary", UIAnchor.TopCenter, 720, 32);
            summary.text = effect.FormatEffectSelectionSummary();
            summary.color = new Color(0.9f, 0.9f, 0.9f);
            summary.fontSize = 18;
            summary.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -58f);
        }

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
                sourceRt.anchoredPosition = new Vector2(0f, -98f);
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
        scrollRt.anchoredPosition = new Vector2(0f, -290f);
        scrollGo.ConfigureGridCellFromViewportHeight(0.78f, 56f);
        ScrollRect sr = scrollGo.GetComponent<ScrollRect>();
        RectTransform content = sr != null ? sr.content : null;

        bool resolved = false;
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
            statLabel.text = effect != null && effect.type == EffectType.GrantAttackFlag
                ? $"AP:{candidate.CurrentPower} HP:{candidate.CurrentHp} 攻撃:{(candidate.AttackFlgState == AttackFlg.True ? "可" : "不可")}"
                : $"AP:{candidate.CurrentPower} HP:{candidate.CurrentHp}";
            statLabel.fontSize = 14;
            statLabel.color = Color.white;
            statLabel.alignment = TextAlignmentOptions.Center;

            Button btn = go.GetComponent<Button>();
            if (btn == null)
            {
                btn = go.AddComponent<Button>();
            }

            CardController pickedRef = candidate;
            btn.onClick.AddListener(() =>
            {
                if (resolved)
                {
                    return;
                }

                resolved = true;
                Destroy(root);
                activeOnActionPopupRoot = null;
                isOnActionPopupOpen = false;
                onPicked?.Invoke(pickedRef);
            });
        }

        Button cancel = root.CreateChildButton("キャンセル");
        RectTransform cancelRt = cancel.GetComponent<RectTransform>();
        cancelRt.sizeDelta = new Vector2(160f, 44f);
        cancelRt.anchorMin = new Vector2(0.5f, 0f);
        cancelRt.anchorMax = new Vector2(0.5f, 0f);
        cancelRt.pivot = new Vector2(0.5f, 0f);
        cancelRt.anchoredPosition = new Vector2(0f, 36f);
        cancel.onClick.AddListener(() =>
        {
            if (resolved)
            {
                return;
            }

            resolved = true;
            Destroy(root);
            activeOnActionPopupRoot = null;
            isOnActionPopupOpen = false;
            onPicked?.Invoke(null);
        });
    }

    private static readonly Color ManualMultiSelectHighlightColor = new Color(1f, 0.45f, 0.45f, 1f);

    private void OpenManualMultiUnitTargetSelectionUI(
        CardController source,
        PlayerType ownerType,
        EffectData effect,
        List<CardController> candidates,
        CardController attackingUnitInAttackFlow,
        System.Action<List<CardController>> onConfirmed)
    {
        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            onConfirmed?.Invoke(new List<CardController>());
            return;
        }

        DestroyActiveOnActionPopupIfAny();
        GameObject root = new GameObject(
            "ManualMultiUnitTargetSelect",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        activeOnActionPopupRoot = root;
        isOnActionPopupOpen = true;
        root.transform.SetParent(canvas.transform, false);
        root.transform.SetAsLastSibling();
        root.SetFullSize();
        Image dim = root.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.55f);
        dim.raycastTarget = true;

        TextMeshProUGUI title = root.CreateChildTextCustom("ManualMultiUnitTargetTitle", UIAnchor.TopCenter, 720, 48);
        title.text = FormatManualUnitSelectionTitle(effect, attackingUnitInAttackFlow);
        title.color = Color.white;
        title.fontSize = 24;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -20f);

        if (effect != null)
        {
            TextMeshProUGUI summary = root.CreateChildTextCustom("ManualMultiUnitTargetSummary", UIAnchor.TopCenter, 720, 32);
            string rangeLabel = effect.FormatSelectCountRangeLabel();
            summary.text = string.IsNullOrEmpty(rangeLabel)
                ? "カードをタップで選択（赤＝対象）→ OK で確定"
                : $"カードをタップで選択（赤＝対象）{rangeLabel} → OK で確定";
            summary.color = new Color(0.9f, 0.9f, 0.9f);
            summary.fontSize = 18;
            summary.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -58f);
        }

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
                sourceRt.anchoredPosition = new Vector2(0f, -98f);
            }

            CardController preview = sourceCardGo.GetComponent<CardController>();
            preview?.SetUp(source.Data, _ => { });
            Button sourceBlocker = sourceCardGo.GetComponent<Button>();
            if (sourceBlocker != null)
            {
                sourceBlocker.interactable = false;
            }
        }

        GameObject scrollGo = root.CreateGridScrollView(680, 360, UIAnchor.TopCenter);
        RectTransform scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.anchoredPosition = new Vector2(0f, -290f);
        scrollGo.ConfigureGridCellFromViewportHeight(0.78f, 56f);
        ScrollRect sr = scrollGo.GetComponent<ScrollRect>();
        RectTransform content = sr != null ? sr.content : null;

        List<CardController> selected = new List<CardController>();
        bool resolved = false;
        int selectMin = effect != null ? effect.GetSelectMinCount() : 1;
        int selectMax = effect != null ? effect.GetSelectMaxCount(candidates.Count) : candidates.Count;

        void CloseWithSelection(List<CardController> picks)
        {
            if (resolved)
            {
                return;
            }

            resolved = true;
            Destroy(root);
            activeOnActionPopupRoot = null;
            isOnActionPopupOpen = false;
            onConfirmed?.Invoke(picks ?? new List<CardController>());
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            CardController candidate = candidates[i];
            if (content == null || candidate == null || candidate.Data == null || CardImagePrefab == null)
            {
                continue;
            }

            GameObject go = Instantiate(CardImagePrefab, content);
            CardController cc = go.GetComponent<CardController>();
            cc?.SetUp(candidate.Data, _ => { });

            TextMeshProUGUI statLabel = go.CreateChildTextCustom(
                "TargetStat",
                UIAnchor.BottomCenter,
                100,
                28);
            statLabel.text = $"AP:{candidate.CurrentPower} HP:{candidate.CurrentHp}";
            statLabel.fontSize = 14;
            statLabel.color = Color.white;
            statLabel.alignment = TextAlignmentOptions.Center;

            Button btn = go.GetComponent<Button>() ?? go.AddComponent<Button>();
            Image baseImage = go.GetComponent<Image>();
            Color original = baseImage != null ? baseImage.color : Color.white;
            CardController pickedRef = candidate;
            btn.onClick.AddListener(() =>
            {
                if (resolved)
                {
                    return;
                }

                if (selected.Contains(pickedRef))
                {
                    selected.Remove(pickedRef);
                    if (baseImage != null)
                    {
                        baseImage.color = original;
                    }
                }
                else
                {
                    if (selected.Count >= selectMax)
                    {
                        Debug.Log($"効果対象は最大{selectMax}体まで選択できます。");
                        return;
                    }

                    selected.Add(pickedRef);
                    if (baseImage != null)
                    {
                        baseImage.color = ManualMultiSelectHighlightColor;
                    }
                }
            });
        }

        Button okBtn = root.CreateChildButton("OK");
        RectTransform okRt = okBtn.GetComponent<RectTransform>();
        okRt.sizeDelta = new Vector2(160f, 44f);
        okRt.anchorMin = new Vector2(0.5f, 0f);
        okRt.anchorMax = new Vector2(0.5f, 0f);
        okRt.pivot = new Vector2(0.5f, 0f);
        okRt.anchoredPosition = new Vector2(-90f, 36f);
        TextMeshProUGUI okLabel = okBtn.GetComponentInChildren<TextMeshProUGUI>();
        if (okLabel != null)
        {
            okLabel.text = "OK";
        }

        okBtn.onClick.AddListener(() =>
        {
            if (selected.Count < selectMin)
            {
                Debug.Log($"効果対象を{selectMin}体以上選択してください。");
                return;
            }

            CloseWithSelection(new List<CardController>(selected));
        });

        Button cancel = root.CreateChildButton("キャンセル");
        RectTransform cancelRt = cancel.GetComponent<RectTransform>();
        cancelRt.sizeDelta = new Vector2(160f, 44f);
        cancelRt.anchorMin = new Vector2(0.5f, 0f);
        cancelRt.anchorMax = new Vector2(0.5f, 0f);
        cancelRt.pivot = new Vector2(0.5f, 0f);
        cancelRt.anchoredPosition = new Vector2(90f, 36f);
        cancel.onClick.AddListener(() => CloseWithSelection(new List<CardController>()));
    }

    private List<CardController> PickEnemyAiEffectTargets(
        EffectData effect,
        EnemyAiEffectPickContext pickCtx,
        List<CardController> candidates)
    {
        List<CardController> picks = new List<CardController>();
        if (candidates == null || candidates.Count == 0)
        {
            return picks;
        }

        if (effect != null && effect.type == EffectType.Damage)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                CardController candidate = candidates[i];
                if (candidate != null && candidate.CurrentHp > 0)
                {
                    picks.Add(candidate);
                }
            }

            return picks;
        }

        if (effect != null && effect.selectionMode.IsMultipleUnitPickMode())
        {
            int min = effect.GetSelectMinCount();
            int max = effect.GetSelectMaxCount(candidates.Count);
            List<CardController> ranked = new List<CardController>(candidates);
            ranked.Sort((a, b) => ComputeEnemyAiUnitThreatScore(b.CurrentPower, b.CurrentHp)
                .CompareTo(ComputeEnemyAiUnitThreatScore(a.CurrentPower, a.CurrentHp)));
            int pickCount = Mathf.Clamp(ranked.Count, min, max);
            if (ranked.Count < min)
            {
                return picks;
            }

            for (int i = 0; i < pickCount; i++)
            {
                picks.Add(ranked[i]);
            }

            return picks;
        }

        CardController single = PickEnemyAiEffectTarget(effect, pickCtx, candidates);
        if (single != null)
        {
            picks.Add(single);
        }

        return picks;
    }

    private static bool EffectRequiresManualUnitSelection(EffectData effect)
    {
        if (effect == null)
        {
            return false;
        }

        if (effect.type.RequiresManualUnitSelection())
        {
            return true;
        }

        return IsEffectTargetRequiringUnitSelection(effect.target)
            && effect.selectionMode.RequiresManualUnitPick();
    }

    private static void FilterTargetsByUnitCondition(
        List<CardController> targets,
        EffectData effect,
        CardController sourceCard = null)
    {
        if (targets == null || effect == null || !effect.HasTargetUnitFilter())
        {
            return;
        }

        if (effect.autoSelectLowestUnitStat && effect.type == EffectType.ReturnUnitToDeckBottom)
        {
            return;
        }

        for (int i = targets.Count - 1; i >= 0; i--)
        {
            if (!effect.MatchesTargetUnitFilter(targets[i], sourceCard))
            {
                targets.RemoveAt(i);
            }
        }
    }

    private static void FilterSelectableEffectTargets(List<CardController> targets, EffectData effect)
    {
        if (targets == null || effect == null)
        {
            return;
        }

        for (int i = targets.Count - 1; i >= 0; i--)
        {
            if (!effect.MatchesSelectableBattleZoneTarget(targets[i]))
            {
                targets.RemoveAt(i);
            }
        }
    }

    private void ApplyEffect(CardController sourceCard, PlayerType ownerType, EffectData effect)
    {
        if (TryApplyAttackActiveEnemyUnitMarker(sourceCard, ownerType, effect))
        {
            BeginOnlineEffectSyncBatch(ownerType);
            FlushOnlineEffectSyncBatch();
            SyncAllResourceViewsFromRule();
            return;
        }

        if (EffectRequiresManualUnitSelection(effect))
        {
            Debug.LogWarning(
                $"[Effect] Skipped auto-apply for manual unit selection (type:{effect.type} target:{effect.target} cardId:{sourceCard?.Data?.id}).");
            return;
        }

        if (EffectRequiresManualHandSelection(effect))
        {
            Debug.LogWarning(
                $"[Effect] Skipped auto-apply for manual hand selection (type:{effect.type} cardId:{sourceCard?.Data?.id}).");
            return;
        }

        if (ShouldRevealDrawnCards(effect, ownerType))
        {
            Debug.LogWarning(
                $"[Effect] Skipped sync apply for reveal draw (cardId:{sourceCard?.Data?.id}). Use effect chain async path.");
            return;
        }

        if (effect.type == EffectType.DeployUnit && effect.RequiresDeployUnitZoneSelection())
        {
            Debug.LogWarning(
                $"[Effect] Skipped sync apply for DeployUnit zone selection (cardId:{sourceCard?.Data?.id}).");
            return;
        }

        if (effect.type == EffectType.AddSelfToHand)
        {
            int addCount = ResolveEffectMagnitude(effect, ownerType, sourceCard);
            ApplyAddSelfToHandEffect(sourceCard, ownerType, effect, addCount > 0 ? addCount : 1);
            BeginOnlineEffectSyncBatch(ownerType);
            FlushOnlineEffectSyncBatch();
            SyncAllResourceViewsFromRule();
            return;
        }

        if (effect.type == EffectType.MarkObservedUnit)
        {
            Debug.LogWarning(
                $"[Effect] MarkObservedUnit は手動選択後に適用してください (cardId:{sourceCard?.Data?.id})。");
            return;
        }

        int magnitude = ResolveEffectMagnitude(effect, ownerType, sourceCard);
        if (magnitude == 0
            && !effect.type.UsesTargetCountValue()
            && effect.type != EffectType.DeployUnit
            && effect.type != EffectType.GrantAttackFlag
            && effect.type != EffectType.AddSelfToHand
            && effect.type != EffectType.MarkObservedUnit)
        {
            return;
        }

        List<CardController> targets = ResolveEffectTargets(sourceCard, ownerType, effect);
        BeginOnlineEffectSyncBatch(ownerType);
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

            case EffectType.Look:
                ApplyLookEffect(sourceCard, ownerType, effect, null);
                break;

            case EffectType.MillTopToTrash:
                ApplyMillTopToTrashEffect(sourceCard, ownerType, effect);
                break;

            case EffectType.ExileFromDeck:
                ApplyExileFromDeckEffect(sourceCard, ownerType, effect);
                break;

            case EffectType.ExileFromTrash:
                ApplyExileFromTrashEffect(sourceCard, ownerType, effect);
                break;

            case EffectType.AddToHandFromLooked:
                Debug.LogWarning(
                    $"[Effect] AddToHandFromLooked は OnLook 専用です (cardId:{sourceCard?.Data?.id})。");
                break;

            case EffectType.ReturnLookedRemainderToDeckTop:
            case EffectType.ShuffleLookedRemainderToDeckBottom:
            case EffectType.ChooseLookedRemainderDisposition:
                Debug.LogWarning(
                    $"[Effect] {effect.type} は OnLook 専用です (cardId:{sourceCard?.Data?.id})。");
                break;

            case EffectType.AddShieldToHand:
                ApplyAddShieldToHandEffect(sourceCard, ownerType, effect, magnitude);
                break;

            case EffectType.AddSelfToHand:
                ApplyAddSelfToHandEffect(sourceCard, ownerType, effect, magnitude);
                break;

            case EffectType.DeployShieldFromHand:
                ApplyDeployShieldFromHandEffect(sourceCard, ownerType, effect, magnitude);
                break;

            case EffectType.DeployBase:
                ApplyDeployBaseEffect(
                    sourceCard,
                    ownerType,
                    effect,
                    magnitude,
                    burstDeployBasePreferSourceCard);
                break;

            case EffectType.Damage:
                for (int i = 0; i < targets.Count; i++)
                {
                    CardController targetUnit = targets[i];
                    int damageAmount = ResolveEffectDamageAmount(magnitude, targetUnit);
                    int hpBefore = targetUnit.CurrentHp;
                    bool isCloseCombat = IsCloseCombatCard(sourceCard);
                    Debug.Log(
                        $"[EffectDamage][LocalBefore] closeCombat:{isCloseCombat} owner:{ownerType} "
                        + $"source:{FormatEffectDamageSourceDebugSnap(sourceCard)} "
                        + $"target:{FormatEffectDamageUnitDebugSnap(targetUnit)} "
                        + $"rawMagnitude:{magnitude} resolvedDamage:{damageAmount}");
                    ApplyUnitDamageAndTrackChain(targetUnit, damageAmount);
                    Debug.Log(
                        $"[EffectDamage][LocalAfter] closeCombat:{isCloseCombat} owner:{ownerType} "
                        + $"source:{FormatEffectDamageSourceDebugSnap(sourceCard)} "
                        + $"target:{FormatEffectDamageUnitDebugSnap(targetUnit)} "
                        + $"HP:{hpBefore}->{targetUnit.CurrentHp} willTrash:{targetUnit.CurrentHp <= 0}");
                    QueueOnlineUnitDamage(targetUnit);
                    PlayerType targetOwner = ResolveCardOwner(targetUnit.transform);
                    if (targetUnit.CurrentHp <= 0)
                    {
                        Debug.Log(
                            $"[EffectDamage][LocalDestroyQueue] closeCombat:{isCloseCombat} "
                            + $"target:{FormatEffectDamageUnitDebugSnap(targetUnit)}");
                        TryLogAttackBlockCloseCombatTrioDestroy("ApplyEffectSync_Damage", targetUnit, sourceCard);
                        NotifyAttackFlowParticipantRemovedDuringOnAction(targetUnit);
                        QueueOnlineUnitDestroy(targetUnit);
                        SendCardToTrash(targetUnit, targetOwner, ResolveUnitKillSourceForTrash(sourceCard, targetUnit));
                    }
                }
                if (effect.target == TargetType.EnemyPlayer || effect.target == TargetType.SelfPlayer)
                {
                    Gundam2024RuleScript.PlayerSide targetSide = effect.target == TargetType.EnemyPlayer
                        ? ToRuleSide(ownerType == PlayerType.Player ? PlayerType.Enemy : PlayerType.Player)
                        : ToRuleSide(ownerType);
                    ApplyEffectDamageToPlayerArea(targetSide, magnitude);
                }
                Debug.Log($"[Effect] Damage {magnitude} target:{effect.target} by cardId:{sourceCard.Data.id}");
                break;

            case EffectType.Buff:
            case EffectType.Debuff:
                int sign = effect.type == EffectType.Buff ? 1 : -1;
                int signedValue = sign * magnitude;
                string modifierSourceKey = ResolveUnitStatModifierSourceKey(sourceCard);
                for (int i = 0; i < targets.Count; i++)
                {
                    ApplyStatEffect(targets[i], signedValue, effect.statTarget, effect.duration, modifierSourceKey);
                    QueueOnlineUnitStat(targets[i], signedValue, effect.statTarget, effect.duration, modifierSourceKey);
                }
                TryRegisterPilotMountAllyFieldAura(sourceCard, ownerType, effect, signedValue);
                Debug.Log($"[Effect] {effect.type} {magnitude} target:{effect.target} stat:{effect.statTarget} by cardId:{sourceCard.Data.id}");
                break;

            case EffectType.BlockRedirect:
                // BlockRedirect は戦闘フロー分岐で解釈するため、ここでは何もしない。
                Debug.Log($"[Effect] BlockRedirect marker by cardId:{sourceCard.Data.id}");
                break;

            case EffectType.HighMobility:
                // HighMobility は攻撃フロー分岐で解釈するため、ここでは何もしない。
                Debug.Log($"[Effect] HighMobility marker by cardId:{sourceCard.Data.id}");
                break;

            case EffectType.AttackActiveEnemyUnit:
                // 付与処理は TryApplyAttackActiveEnemyUnitMarker で行う。
                Debug.Log($"[Effect] AttackActiveEnemyUnit marker by cardId:{sourceCard.Data.id}");
                break;

            case EffectType.Suppress:
                // 制圧は TryResolveShieldAttackStrikeDamage でのみ解決する。
                break;

            case EffectType.Bounce:
                ApplyBounceEffect(effect, targets);
                break;
            case EffectType.ReturnUnitToDeckBottom:
                ApplyReturnUnitToDeckBottomEffect(effect, targets);
                break;
            case EffectType.Rest:
                ApplyRestEffect(effect, targets);
                break;
            case EffectType.Activate:
                ApplyActivateEffect(effect, ownerType, targets);
                break;
            case EffectType.Destroy:
                ApplyDestroyEffect(sourceCard, ownerType, effect, targets);
                break;

            case EffectType.DeployUnit:
                ApplyDeployUnitEffect(sourceCard, ownerType, effect);
                break;

            case EffectType.GrantAttackFlag:
                ApplyGrantAttackFlagEffect(effect, ownerType, targets);
                break;
        }

        FlushOnlineEffectSyncBatch();
        SyncAllResourceViewsFromRule();
    }

    private static void ApplyStatEffect(CardController target, int signedValue, EffectStatTarget statTarget, EffectDuration duration, string statModifierSourceKey = null)
    {
        int powerDelta = 0;
        int hpDelta = 0;
        int costDelta = 0;
        int levelDelta = 0;
        int effectDamageDelta = 0;
        int effectDamageImmunityDelta = 0;
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
            case EffectStatTarget.EffectDamage:
                effectDamageDelta = signedValue;
                break;
            case EffectStatTarget.EffectDamageImmunity:
                effectDamageImmunityDelta = signedValue > 0 ? 1 : (signedValue < 0 ? -1 : 0);
                break;
            default:
                powerDelta = signedValue;
                hpDelta = signedValue;
                costDelta = signedValue;
                levelDelta = signedValue;
                break;
        }
        target.AddEffectStatBonus(
            powerDelta,
            hpDelta,
            costDelta,
            levelDelta,
            duration,
            statModifierSourceKey,
            effectDamageDelta,
            effectDamageImmunityDelta);
    }

    /// <summary>
    /// 効果ダメージ（EffectType.Damage 等）の実際の与ダメージ量。戦闘交換には使わない。
    /// 無効化・修飾は effectDamageTarget 自身のレイヤーのみ適用する。
    /// </summary>
    private int ResolveEffectDamageAmount(int baseMagnitude, CardController effectDamageTarget = null)
    {
        if (effectDamageTarget != null && effectDamageTarget.HasEffectDamageImmunity)
        {
            return 0;
        }

        int modifier = effectDamageTarget != null ? effectDamageTarget.CurrentEffectDamageModifier : 0;
        return Mathf.Max(0, baseMagnitude + modifier);
    }

    private int ResolveEffectDamageAmountForVirtualPlayerLog(
        int baseMagnitude,
        List<VirtualPlayerUnitSnap> workingPlayerOverrides,
        CardController effectDamageTarget = null)
    {
        if (effectDamageTarget != null)
        {
            VirtualPlayerUnitSnap snap = workingPlayerOverrides != null
                ? FindPlayerVirtualSnap(workingPlayerOverrides, effectDamageTarget)
                : null;
            if (snap != null ? snap.EffectDamageImmunityCount > 0 : effectDamageTarget.HasEffectDamageImmunity)
            {
                return 0;
            }

            int modifier = snap != null ? snap.EffectDamageMod : effectDamageTarget.CurrentEffectDamageModifier;
            return Mathf.Max(0, baseMagnitude + modifier);
        }

        return Mathf.Max(0, baseMagnitude);
    }

    private List<CardController> ResolveEffectTargets(
        CardController sourceCard,
        PlayerType ownerType,
        EffectData effect)
    {
        if (effect == null)
        {
            return new List<CardController>();
        }

        IReadOnlyList<CardFeatureData> requiredFeatures = effect.GetTargetFeatures();
        List<CardController> allies = ownerType == PlayerType.Player ? playerBattleZoneCards : enemyBattleZoneCards;
        List<CardController> enemies = ownerType == PlayerType.Player ? enemyBattleZoneCards : playerBattleZoneCards;
        List<CardController> result = new List<CardController>();

        switch (effect.target)
        {
            case TargetType.Self:
                if (sourceCard != null)
                {
                    result.Add(sourceCard);
                }
                break;
            case TargetType.AllyUnit:
                AddFirstAliveUnit(allies, result, null, requiredFeatures);
                break;
            case TargetType.AllyOtherUnit:
                AddFirstAliveUnit(allies, result, sourceCard, requiredFeatures);
                break;
            case TargetType.EnemyUnit:
                if (effect.autoSelectLowestUnitStat)
                {
                    AddAllAliveUnits(enemies, result, null, requiredFeatures);
                }
                else
                {
                    AddFirstAliveUnit(enemies, result, null, requiredFeatures);
                }
                break;
            case TargetType.RestEnemyUnit:
                AddFirstAliveRestUnit(enemies, result, requiredFeatures);
                break;
            case TargetType.AllyAllUnits:
                AddAllAliveUnits(allies, result, null, requiredFeatures);
                break;
            case TargetType.EnemyAllUnits:
                AddAllAliveUnits(enemies, result, null, requiredFeatures);
                break;
        }

        FilterTargetsByUnitCondition(result, effect, sourceCard);
        FilterSelectableEffectTargets(result, effect);
        if (effect.type == EffectType.Rest)
        {
            FilterOutAlreadyRestedUnits(result);
        }

        if (effect.type == EffectType.Activate)
        {
            FilterOutNonRestedUnits(result);
        }

        FilterToLowestStatTiedUnitsIfNeeded(result, effect);

        return result;
    }

    private static void AddFirstAliveUnit(
        List<CardController> source,
        List<CardController> result,
        CardController exclude = null,
        IReadOnlyList<CardFeatureData> requiredFeatures = null)
    {
        for (int i = 0; i < source.Count; i++)
        {
            CardController c = source[i];
            if (c == null || c == exclude || c.Data == null || !c.Data.IsUnitLike() || c.CurrentHp <= 0)
            {
                continue;
            }

            if (!MatchesRequiredFeatures(c.Data, requiredFeatures))
            {
                continue;
            }

            result.Add(c);
            return;
        }
    }

    private static void AddFirstAliveRestUnit(
        List<CardController> source,
        List<CardController> result,
        IReadOnlyList<CardFeatureData> requiredFeatures = null)
    {
        for (int i = 0; i < source.Count; i++)
        {
            CardController c = source[i];
            if (c == null || c.Data == null || !c.Data.IsUnitLike() || c.CurrentHp <= 0 || !c.IsRestState)
            {
                continue;
            }

            if (!MatchesRequiredFeatures(c.Data, requiredFeatures))
            {
                continue;
            }

            result.Add(c);
            return;
        }
    }

    private static void AddAllAliveUnits(
        List<CardController> source,
        List<CardController> result,
        CardController exclude = null,
        IReadOnlyList<CardFeatureData> requiredFeatures = null)
    {
        for (int i = 0; i < source.Count; i++)
        {
            CardController c = source[i];
            if (c == null || c == exclude || c.Data == null || !c.Data.IsUnitLike() || c.CurrentHp <= 0)
            {
                continue;
            }

            if (!MatchesRequiredFeatures(c.Data, requiredFeatures))
            {
                continue;
            }

            result.Add(c);
        }
    }

    private static bool MatchesRequiredFeatures(CardData card, IReadOnlyList<CardFeatureData> requiredFeatures)
    {
        if (requiredFeatures == null || requiredFeatures.Count == 0)
        {
            return true;
        }

        if (card == null)
        {
            return false;
        }

        if (card.HasAnyFeature(requiredFeatures))
        {
            return true;
        }

        for (int i = 0; i < requiredFeatures.Count; i++)
        {
            CardFeatureData required = requiredFeatures[i];
            if (required != null && card.HasFeatureId(required.id))
            {
                return true;
            }
        }

        return false;
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

    /// <summary>AttackActiveEnemyUnit のランタイム付与を解除（UntilEndOfTurn / UntilEndOfBattle）。</summary>
    private void ClearAttackActiveEnemyGrants(EffectDuration duration)
    {
        if (duration != EffectDuration.UntilEndOfTurn && duration != EffectDuration.UntilEndOfBattle)
        {
            return;
        }

        ClearAttackActiveEnemyGrantsOnZone(playerBattleZoneCards, duration);
        ClearAttackActiveEnemyGrantsOnZone(enemyBattleZoneCards, duration);
    }

    private static void ClearAttackActiveEnemyGrantsOnZone(List<CardController> zone, EffectDuration duration)
    {
        if (zone == null)
        {
            return;
        }

        for (int i = 0; i < zone.Count; i++)
        {
            CardController unit = zone[i];
            if (unit == null)
            {
                continue;
            }

            if (duration == EffectDuration.UntilEndOfTurn)
            {
                unit.ClearAttackActiveEnemyUntilEndOfTurnGrants();
            }
            else if (duration == EffectDuration.UntilEndOfBattle)
            {
                unit.ClearAttackActiveEnemyUntilEndOfBattleGrants();
            }
        }
    }

    /// <summary>AttackActiveEnemyUnit マーカーを解決（UntilEndOfTurn 等はここでランタイム付与）。</summary>
    private bool TryApplyAttackActiveEnemyUnitMarker(
        CardController sourceCard,
        PlayerType ownerType,
        EffectData effect)
    {
        if (effect == null || effect.type != EffectType.AttackActiveEnemyUnit)
        {
            return false;
        }

        if (effect.duration == EffectDuration.Permanent)
        {
            return true;
        }

        CardController grantHost = ResolveAttackActiveEnemyGrantHost(sourceCard);
        if (grantHost == null || grantHost.Data == null || !grantHost.Data.IsUnitLike())
        {
            Debug.LogWarning(
                $"[AttackActiveEnemyUnit] 付与先ユニットを解決できません source:{sourceCard?.Data?.cardName} owner:{ownerType}");
            return true;
        }

        if (effect.duration == EffectDuration.UntilEndOfTurn)
        {
            grantHost.AddAttackActiveEnemyUntilEndOfTurnGrant(effect);
            Debug.Log(
                $"[AttackActiveEnemyUnit] UntilEndOfTurn 付与: {grantHost.Data.cardName} "
                + $"filter:{effect.FormatTargetUnitFilterDescription()} "
                + $"(source:{sourceCard.Data?.cardName} owner:{ownerType})");
        }
        else if (effect.duration == EffectDuration.UntilEndOfBattle)
        {
            grantHost.AddAttackActiveEnemyUntilEndOfBattleGrant(effect);
            Debug.Log(
                $"[AttackActiveEnemyUnit] UntilEndOfBattle 付与: {grantHost.Data.cardName} "
                + $"filter:{effect.FormatTargetUnitFilterDescription()} "
                + $"(source:{sourceCard.Data?.cardName} owner:{ownerType})");
        }

        return true;
    }

    private static CardController ResolveAttackActiveEnemyGrantHost(CardController sourceCard)
    {
        if (sourceCard == null || sourceCard.Data == null)
        {
            return null;
        }

        if (sourceCard.Data.IsUnitLike())
        {
            return sourceCard;
        }

        if (sourceCard.Data.type == Type.Pilot && sourceCard.MountedUnit != null)
        {
            return sourceCard.MountedUnit;
        }

        return sourceCard;
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
    /// ターン終了時の OnAction。非ターンプレイヤー→ターンプレイヤー→交互。両者 ActionEnd で終了。
    /// </summary>
    private bool TryRunTurnEndOnActionPhases(PlayerType endingTurnSide, System.Action onComplete)
    {
        PlayerType nonTurnSide = OpponentSide(endingTurnSide);
        BeginActionStepSession(
            endingTurnSide,
            nonTurnSide,
            isAttackContext: false,
            defenderContext: "turn end:enemy-action",
            attackerContext: "turn end:player-action",
            attackingUnit: null,
            onComplete);
        return true;
    }

    /// <summary>
    /// 攻撃フロー後半：ブロック応答後の OnAction。防御側（非ターンプレイヤー）→攻撃側→交互。
    /// </summary>
    private bool TryRunAttackOnActionPhasesAfterBlock(
        PlayerType defenderSide,
        PlayerType attackerSide,
        System.Action onComplete,
        CardController attackingUnitInAttackFlow = null)
    {
        string defenderContext = defenderSide == PlayerType.Player
            ? "attack:player-action"
            : "attack:enemy-action";
        string attackerContext = attackerSide == PlayerType.Player
            ? "attack:player-action"
            : "attack:enemy-action";

        BeginActionStepSession(
            attackerSide,
            defenderSide,
            isAttackContext: true,
            defenderContext,
            attackerContext,
            attackingUnitInAttackFlow,
            onComplete);
        return true;
    }

    private bool TryRunAttackActionSteps(
        PlayerType defenderSide,
        PlayerType attackerSide,
        System.Action onComplete,
        CardController attackingUnitInAttackFlow = null)
    {
        if (enableShieldAttackFlowDebugLog)
        {
            Debug.Log(
                $"[AttackFlow] OnAction order: defender action step → attacker action step "
                + $"(defender:{defenderSide} attacker:{attackerSide})");
        }

        return TryRunAttackOnActionPhasesAfterBlock(
            defenderSide,
            attackerSide,
            onComplete,
            attackingUnitInAttackFlow);
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
        public int EffectDamageMod;
        public int EffectDamageImmunityCount;
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
        public bool IsRest;
        public int EffectDamageMod;
        public int EffectDamageImmunityCount;
    }

    private List<VirtualBattleUnitSnap> BuildFullBattleVirtualSnapshot()
    {
        List<VirtualBattleUnitSnap> list = new List<VirtualBattleUnitSnap>();
        if (playerBattleZoneCards != null)
        {
            for (int i = 0; i < playerBattleZoneCards.Count; i++)
            {
                CardController c = playerBattleZoneCards[i];
                if (c == null || c.Data == null || !c.Data.IsUnitLike())
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
                    IsRest = c.IsRestState,
                    EffectDamageMod = c.CurrentEffectDamageModifier,
                    EffectDamageImmunityCount = c.CurrentEffectDamageImmunityCount,
                });
            }
        }

        if (enemyBattleZoneCards != null)
        {
            for (int i = 0; i < enemyBattleZoneCards.Count; i++)
            {
                CardController c = enemyBattleZoneCards[i];
                if (c == null || c.Data == null || !c.Data.IsUnitLike())
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
                    IsRest = c.IsRestState,
                    EffectDamageMod = c.CurrentEffectDamageModifier,
                    EffectDamageImmunityCount = c.CurrentEffectDamageImmunityCount,
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
            if (c == null || c.Data == null || !c.Data.IsUnitLike())
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
                EffectDamageMod = c.CurrentEffectDamageModifier,
                EffectDamageImmunityCount = c.CurrentEffectDamageImmunityCount,
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
                EffectDamageMod = s.EffectDamageMod,
                EffectDamageImmunityCount = s.EffectDamageImmunityCount,
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
            case EffectStatTarget.EffectDamage:
                snap.EffectDamageMod += signedValue;
                break;
            case EffectStatTarget.EffectDamageImmunity:
                snap.EffectDamageImmunityCount = Mathf.Max(0, snap.EffectDamageImmunityCount + (signedValue > 0 ? 1 : signedValue < 0 ? -1 : 0));
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
                IsRest = s.IsRest,
                EffectDamageMod = s.EffectDamageMod,
                EffectDamageImmunityCount = s.EffectDamageImmunityCount,
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
            case EffectStatTarget.EffectDamage:
                snap.EffectDamageMod += signedValue;
                break;
            case EffectStatTarget.EffectDamageImmunity:
                snap.EffectDamageImmunityCount = Mathf.Max(0, snap.EffectDamageImmunityCount + (signedValue > 0 ? 1 : signedValue < 0 ? -1 : 0));
                break;
            default:
                snap.Ap = Mathf.Max(0, snap.Ap + signedValue);
                snap.Hp = Mathf.Max(0, snap.Hp + signedValue);
                break;
        }
    }

    private static int ResolveVirtualEffectDamageAmount(
        int baseMagnitude,
        List<VirtualBattleUnitSnap> working,
        VirtualBattleUnitSnap effectDamageTarget = null)
    {
        if (effectDamageTarget != null && effectDamageTarget.EffectDamageImmunityCount > 0)
        {
            return 0;
        }

        int modifier = effectDamageTarget != null ? effectDamageTarget.EffectDamageMod : 0;
        return Mathf.Max(0, baseMagnitude + modifier);
    }

    private static void ApplyVirtualBattleEffectToTargetsOnSnaps(
        List<VirtualBattleUnitSnap> working,
        EffectData effect,
        List<CardController> targets,
        int magnitude,
        CardController sourceCard = null)
    {
        if (working == null || effect == null || targets == null)
        {
            return;
        }

        if (magnitude == 0)
        {
            return;
        }

        if (effect.type == EffectType.Draw || effect.type == EffectType.Look || effect.type == EffectType.AddToHandFromLooked
            || effect.type == EffectType.ReturnLookedRemainderToDeckTop
            || effect.type == EffectType.ShuffleLookedRemainderToDeckBottom
            || effect.type == EffectType.ChooseLookedRemainderDisposition
            || effect.type == EffectType.MillTopToTrash
            || effect.type == EffectType.ExileFromDeck
            || effect.type == EffectType.ExileFromTrash
            || effect.type == EffectType.BlockRedirect || effect.type == EffectType.HighMobility
            || effect.type == EffectType.AttackActiveEnemyUnit
            || effect.type == EffectType.AddShieldToHand || effect.type == EffectType.AddSelfToHand || effect.type == EffectType.DeployShieldFromHand
            || effect.type == EffectType.DeployBase
            || effect.type == EffectType.Suppress)
        {
            return;
        }

        if (effect.type == EffectType.Bounce)
        {
            int limit = effect.value > 0 ? effect.value : targets.Count;
            int removed = 0;
            for (int i = 0; i < targets.Count && removed < limit; i++)
            {
                CardController t = targets[i];
                if (t == null)
                {
                    continue;
                }

                VirtualBattleUnitSnap snap = FindBattleVirtualSnap(working, t);
                if (snap != null && working.Remove(snap))
                {
                    removed++;
                }
            }

            return;
        }

        if (effect.type == EffectType.Rest)
        {
            int limit = effect.value > 0 ? effect.value : targets.Count;
            int rested = 0;
            for (int i = 0; i < targets.Count && rested < limit; i++)
            {
                CardController t = targets[i];
                if (t == null)
                {
                    continue;
                }

                VirtualBattleUnitSnap snap = FindBattleVirtualSnap(working, t);
                if (snap == null || snap.IsRest)
                {
                    continue;
                }

                snap.IsRest = true;
                rested++;
            }

            return;
        }

        if (effect.type == EffectType.Destroy)
        {
            int limit = effect.value > 0 ? effect.value : targets.Count;
            int removed = 0;
            for (int i = 0; i < targets.Count && removed < limit; i++)
            {
                CardController t = targets[i];
                if (t == null)
                {
                    continue;
                }

                VirtualBattleUnitSnap snap = FindBattleVirtualSnap(working, t);
                if (snap != null && working.Remove(snap))
                {
                    removed++;
                }
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

            VirtualBattleUnitSnap snap = FindBattleVirtualSnap(working, t);
            if (snap == null)
            {
                continue;
            }

            switch (effect.type)
            {
                case EffectType.Damage:
                {
                    int damageAmount = ResolveVirtualEffectDamageAmount(magnitude, working, snap);
                    snap.Hp = Mathf.Max(0, snap.Hp - damageAmount);
                    break;
                }
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
        if (!EnableVerboseBattleDebugLogs)
        {
            return;
        }

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
        if (combatDefender == null || combatDefender.Data == null || !combatDefender.Data.IsUnitLike())
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
        if (!EnableVerboseBattleDebugLogs)
        {
            return;
        }

        if (command == null || command.Data == null || effect == null || hypotheticalEnemyTarget == null
            || hypotheticalEnemyTarget.Data == null)
        {
            return;
        }

        string patternLabel = FormatHypothesisPatternLetterLabel(candidateIndex);
        int targetSlot = TryGetUnitBattleZoneSlotIndex(hypotheticalEnemyTarget);

        List<VirtualBattleUnitSnap> before = BuildFullBattleVirtualSnapshot();
        List<VirtualBattleUnitSnap> after = CloneVirtualBattleSnaps(before);
        int hypotheticalMagnitude = ResolveEffectMagnitude(effect, commandOwnerSide, command);
        ApplyVirtualBattleEffectToTargetsOnSnaps(
            after,
            effect,
            new List<CardController> { hypotheticalEnemyTarget },
            hypotheticalMagnitude,
            command);
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
        if (!EnableVerboseBattleDebugLogs)
        {
            return;
        }

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
            EffectData enemyPick = fx.Find(e => e != null && EffectRequiresManualUnitSelection(e));
            approxBranchRows += enemyPick != null
                ? ResolveSelectableEffectTargets(c, PlayerType.Enemy, enemyPick).Count
                : 1;
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

            EffectData enemyTargetEffect = onActionEffects.Find(e => e != null && EffectRequiresManualUnitSelection(e));
            if (enemyTargetEffect != null)
            {
                List<CardController> playerSideTargets = ResolveSelectableEffectTargets(
                    cmd,
                    PlayerType.Enemy,
                    enemyTargetEffect);
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
        if (!EnableVerboseBattleDebugLogs)
        {
            return;
        }

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

            if (eff.type == EffectType.Draw || eff.type == EffectType.Look || eff.type == EffectType.AddToHandFromLooked
                || eff.type == EffectType.ReturnLookedRemainderToDeckTop
                || eff.type == EffectType.ShuffleLookedRemainderToDeckBottom
                || eff.type == EffectType.ChooseLookedRemainderDisposition
                || eff.type == EffectType.MillTopToTrash
                || eff.type == EffectType.ExileFromDeck
                || eff.type == EffectType.ExileFromTrash
                || eff.type == EffectType.BlockRedirect || eff.type == EffectType.HighMobility
                || eff.type == EffectType.AttackActiveEnemyUnit)
            {
                trace.Append('[').Append(ei).Append(':').Append(eff.type).Append(" skip] ");
                continue;
            }

            int magnitude = ResolveEffectMagnitude(eff, commandOwnerSide, command);
            if (magnitude == 0 && !eff.type.UsesTargetCountValue())
            {
                continue;
            }

            List<CardController> targets = ResolveEffectTargets(command, commandOwnerSide, eff);
            if (targets == null || targets.Count == 0)
            {
                trace.Append('[').Append(ei).Append(":noTargets] ");
                continue;
            }

            ApplyVirtualBattleEffectToTargetsOnSnaps(working, eff, targets, magnitude, command);
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
        if (!EnableVerboseBattleDebugLogs)
        {
            return;
        }

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
        if (!EnableVerboseBattleDebugLogs)
        {
            return;
        }

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

            int magnitude = ResolveEffectMagnitude(effect, commandOwner, commandCard);
            switch (effect.type)
            {
                case EffectType.BlockRedirect:
                    notes.Append("[BlockRedirect] ");
                    continue;
                case EffectType.HighMobility:
                    notes.Append("[HighMobility] ");
                    continue;
                case EffectType.AttackActiveEnemyUnit:
                    notes.Append("[AttackActiveEnemyUnit] ");
                    continue;
                case EffectType.Draw:
                    notes.Append("[Draw ").Append(magnitude).Append("] ");
                    continue;
                case EffectType.AddShieldToHand:
                    notes.Append("[AddShieldToHand ").Append(magnitude).Append("] ");
                    continue;
                case EffectType.AddSelfToHand:
                    notes.Append("[AddSelfToHand] ");
                    continue;
                case EffectType.DeployShieldFromHand:
                    notes.Append("[DeployShieldFromHand ").Append(magnitude).Append("] ");
                    continue;
                case EffectType.DeployBase:
                    notes.Append("[DeployBase ").Append(magnitude).Append("] ");
                    continue;
                case EffectType.Suppress:
                    notes.Append("[Suppress] ");
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
                    List<CardController> dmgTargets = ResolveEffectTargets(commandCard, commandOwner, effect);
                    for (int ti = 0; ti < dmgTargets.Count; ti++)
                    {
                        CardController dmgTarget = dmgTargets[ti];
                        VirtualPlayerUnitSnap snap = FindPlayerVirtualSnap(working, dmgTarget);
                        if (snap != null)
                        {
                            int damageAmount = ResolveEffectDamageAmountForVirtualPlayerLog(magnitude, working, dmgTarget);
                            snap.Hp = Mathf.Max(0, snap.Hp - damageAmount);
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
                    List<CardController> statTargets = ResolveEffectTargets(commandCard, commandOwner, effect);
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
        if (!EnableVerboseBattleDebugLogs)
        {
            return;
        }

        if (commandCard == null
            || commandCard.Data == null
            || focusUnit == null
            || focusUnit.Data == null
            || !focusUnit.Data.IsUnitLike())
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
                EffectDamageMod = focusUnit.CurrentEffectDamageModifier,
                EffectDamageImmunityCount = focusUnit.CurrentEffectDamageImmunityCount,
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

            int magnitude = ResolveEffectMagnitude(effect, commandOwner, commandCard);
            switch (effect.type)
            {
                case EffectType.BlockRedirect:
                    notes.Append("[BlockRedirect] ");
                    continue;
                case EffectType.HighMobility:
                    notes.Append("[HighMobility] ");
                    continue;
                case EffectType.AttackActiveEnemyUnit:
                    notes.Append("[AttackActiveEnemyUnit] ");
                    continue;
                case EffectType.Draw:
                    notes.Append("[Draw ").Append(magnitude).Append("] ");
                    continue;
                case EffectType.AddShieldToHand:
                    notes.Append("[AddShieldToHand ").Append(magnitude).Append("] ");
                    continue;
                case EffectType.AddSelfToHand:
                    notes.Append("[AddSelfToHand] ");
                    continue;
                case EffectType.DeployShieldFromHand:
                    notes.Append("[DeployShieldFromHand ").Append(magnitude).Append("] ");
                    continue;
                case EffectType.DeployBase:
                    notes.Append("[DeployBase ").Append(magnitude).Append("] ");
                    continue;
                case EffectType.Suppress:
                    notes.Append("[Suppress] ");
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
                    List<CardController> dmgTargets = ResolveEffectTargets(commandCard, commandOwner, effect);
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
                            int damageAmount = ResolveEffectDamageAmountForVirtualPlayerLog(magnitude, working, focusUnit);
                            snap.Hp = Mathf.Max(0, snap.Hp - damageAmount);
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
                    List<CardController> statTargets = ResolveEffectTargets(commandCard, commandOwner, effect);
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

        if (!attackNow.Data.IsUnitLike() || !blockNow.Data.IsUnitLike())
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
            if (attackHighlightUnit != null && c == attackHighlightUnit && c.Data.IsUnitLike())
            {
                sb.Append("[ユニットナウ]");
            }

            if (blockHighlightUnit != null && c == blockHighlightUnit && c.Data.IsUnitLike())
            {
                sb.Append("[ブロックナウ]");
            }

            int ap = c.CurrentPower;
            int hp = c.CurrentHp;
            if (c.Data.IsUnitLike() && virtualSnaps != null)
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
            if (c.Data.IsUnitLike() && virtualSnaps != null)
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
        if (!EnableVerboseBattleDebugLogs)
        {
            return;
        }

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
        if (!EnableVerboseBattleDebugLogs)
        {
            return;
        }

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
                if (t == null || t.Data == null || !t.Data.IsUnitLike())
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
            if (c == null || c.Data == null || !c.Data.IsUnitLike())
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
            if (c == null || c.Data == null || !c.Data.IsUnitLike())
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
            if (c == null || c.Data == null || !c.Data.IsUnitLike())
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
            if (c == null || c.Data == null || !c.Data.IsUnitLike())
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
                if (c != null && c.Data != null && c.Data.IsUnitLike() && c.Data.id == cardId)
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
                if (c != null && c.Data != null && c.Data.IsUnitLike() && c.Data.id == cardId)
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
        if (!EnableVerboseBattleDebugLogs)
        {
            return;
        }

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
                if (aft == null || aft.Data == null || !aft.Data.IsUnitLike())
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
        if (!EnableVerboseBattleDebugLogs)
        {
            Debug.Log(
                $"[BoardCompact] ctx:{context} side:{activeSide} turn:{currentPlayerType} "
                + $"P:{CountAliveUnitsOnZone(playerBattleZoneCards)} E:{CountAliveUnitsOnZone(enemyBattleZoneCards)}");
            return;
        }

        System.Text.StringBuilder sb = new System.Text.StringBuilder(768);
        sb.AppendLine("[BoardSnapshot]");
        AppendBoardStateSnapshotLines(sb, context, activeSide, attackingUnitInAttackFlow);
        Debug.Log(sb.ToString());
    }

    private static int CountAliveUnitsOnZone(List<CardController> zone)
    {
        if (zone == null)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < zone.Count; i++)
        {
            CardController c = zone[i];
            if (c != null && c.Data != null && c.Data.IsUnitLike() && c.CurrentHp > 0)
            {
                count++;
            }
        }

        return count;
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
            if (attackHighlightUnit != null && c == attackHighlightUnit && c.Data.IsUnitLike())
            {
                sb.Append("[ユニットナウ]");
            }

            if (blockHighlightUnit != null && c == blockHighlightUnit && c.Data.IsUnitLike())
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
                if (uc == null || uc.Data == null || !uc.Data.IsUnitLike())
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
        }

        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            return false;
        }

        LogFullBoardSnapshotForCommandTiming(context, side, attackingUnitInAttackFlow);
        _onlineOnActionActiveContext = context;

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

        bool hasSelectableCards = onActionSelectableSources.Count > 0;
        bool useAlternatingActionStepUi = IsActionStepSessionActive
            || IsOnlineBattle();
        string roleLabel = GetActionStepThinkSubtitle(side, context);
        TextMeshProUGUI title = root.CreateChildTextCustom("OnActionCommandTitle", UIAnchor.TopCenter, 720, 48);
        title.text = hasSelectableCards
            ? $"Action Step — {roleLabel}"
            : $"Action Step — {roleLabel} (no playable cards)";
        title.color = Color.white;
        title.fontSize = 24;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -24f);

        HashSet<CardController> selectedSet = new HashSet<CardController>();
        List<CardController> selectedCommands = new List<CardController>();

        if (hasSelectableCards)
        {
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

            for (int i = 0; i < onActionSelectableSources.Count; i++)
            {
                CardController command = onActionSelectableSources[i];
                if (content == null || command == null || command.Data == null)
                {
                    continue;
                }

                string typeLabel = command.Data.type == Type.Command ? "Command" : "Unit";
                bool alreadyUsedInActionStep = useAlternatingActionStepUi
                    && IsActionStepCardUsedForSide(side, command);
                AppendSelectableCommandCardToGrid(
                    content,
                    command,
                    typeLabel,
                    selectedSet,
                    alreadyUsedInActionStep);
            }
        }

        void finishUi(ActionStepPassKind passKind)
        {
            if (!IsOnlineBattle())
            {
                LogAttackOnActionDecisionWithBoard(
                    passKind == ActionStepPassKind.ActionEnd ? "ActionEnd" : "Pass",
                    context,
                    side,
                    attackingUnitInAttackFlow);
            }

            if (useAlternatingActionStepUi)
            {
                if (IsActionStepSessionActive)
                {
                    if (passKind == ActionStepPassKind.Pass && selectedCommands.Count > 0)
                    {
                        ExecuteOnActionCommandQueue(
                            side,
                            selectedCommands,
                            0,
                            () => ResolveActionStepUi(side, passKind, root),
                            attackingUnitInAttackFlow);
                        return;
                    }

                    ResolveActionStepUi(side, passKind, root);
                    return;
                }

                _onlineOnActionActiveContext = null;
                isOnActionPopupOpen = false;
                activeOnActionPopupRoot = null;
                Destroy(root);

                int requestId = _pendingOnlineOnActionRequestId > 0
                    ? _pendingOnlineOnActionRequestId
                    : _onlineOnActionResponseRequestId;

                if (passKind == ActionStepPassKind.Pass && selectedCommands.Count > 0)
                {
                    ExecuteOnActionCommandQueue(
                        side,
                        selectedCommands,
                        0,
                        () =>
                        {
                            SendOnlineActionStepResolution(requestId, side, passKind);
                            onStepDone?.Invoke();
                        },
                        attackingUnitInAttackFlow);
                    return;
                }

                SendOnlineActionStepResolution(requestId, side, passKind);
                onStepDone?.Invoke();
                return;
            }

            if (passKind == ActionStepPassKind.Pass && selectedCommands.Count > 0)
            {
                ExecuteOnActionCommandQueue(
                    side,
                    selectedCommands,
                    0,
                    () =>
                    {
                        _onlineOnActionActiveContext = null;
                        isOnActionPopupOpen = false;
                        activeOnActionPopupRoot = null;
                        Destroy(root);
                        onStepDone?.Invoke();
                    },
                    attackingUnitInAttackFlow);
                return;
            }

            _onlineOnActionActiveContext = null;
            isOnActionPopupOpen = false;
            activeOnActionPopupRoot = null;
            Destroy(root);
            onStepDone?.Invoke();
        }

        Button confirmBtn = root.CreateChildButton("Confirm");
        RectTransform confirmRt = confirmBtn.GetComponent<RectTransform>();
        confirmRt.sizeDelta = new Vector2(160f, 48f);
        confirmRt.anchorMin = new Vector2(0.5f, 0f);
        confirmRt.anchorMax = new Vector2(0.5f, 0f);
        confirmRt.pivot = new Vector2(0.5f, 0f);
        confirmRt.anchoredPosition = new Vector2(-190f, 36f);
        TextMeshProUGUI confirmLabel = confirmBtn.GetComponentInChildren<TextMeshProUGUI>();
        if (confirmLabel != null)
        {
            confirmLabel.text = "Confirm";
        }

        SetActionStepButtonInteractable(confirmBtn, hasSelectableCards);
        confirmBtn.onClick.AddListener(() =>
        {
            if (!hasSelectableCards)
            {
                return;
            }

            selectedCommands.Clear();
            selectedCommands.AddRange(selectedSet);
            if (selectedCommands.Count == 0)
            {
                Debug.Log("OnAction: Select at least one card.");
                return;
            }

            finishUi(ActionStepPassKind.Pass);
        });

        Button cancelBtn = root.CreateChildButton("Cancel");
        RectTransform cancelRt = cancelBtn.GetComponent<RectTransform>();
        cancelRt.sizeDelta = new Vector2(160f, 48f);
        cancelRt.anchorMin = new Vector2(0.5f, 0f);
        cancelRt.anchorMax = new Vector2(0.5f, 0f);
        cancelRt.pivot = new Vector2(0.5f, 0f);
        cancelRt.anchoredPosition = new Vector2(0f, 36f);
        TextMeshProUGUI cancelLabel = cancelBtn.GetComponentInChildren<TextMeshProUGUI>();
        if (cancelLabel != null)
        {
            cancelLabel.text = "Cancel";
        }

        SetActionStepButtonInteractable(cancelBtn, hasSelectableCards);
        cancelBtn.onClick.AddListener(() =>
        {
            if (!hasSelectableCards)
            {
                return;
            }

            selectedCommands.Clear();
            finishUi(ActionStepPassKind.Pass);
        });

        Button actionEndBtn = root.CreateChildButton("ActionEnd");
        RectTransform actionEndRt = actionEndBtn.GetComponent<RectTransform>();
        actionEndRt.sizeDelta = new Vector2(160f, 48f);
        actionEndRt.anchorMin = new Vector2(0.5f, 0f);
        actionEndRt.anchorMax = new Vector2(0.5f, 0f);
        actionEndRt.pivot = new Vector2(0.5f, 0f);
        actionEndRt.anchoredPosition = new Vector2(190f, 36f);
        TextMeshProUGUI actionEndLabel = actionEndBtn.GetComponentInChildren<TextMeshProUGUI>();
        if (actionEndLabel != null)
        {
            actionEndLabel.text = "ActionEnd";
        }

        actionEndBtn.onClick.AddListener(() =>
        {
            selectedCommands.Clear();
            finishUi(ActionStepPassKind.ActionEnd);
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

        EffectData manualTargetEffect = null;
        for (int i = 0; i < onActionEffects.Count; i++)
        {
            EffectData e = onActionEffects[i];
            if (e != null && EffectRequiresManualUnitSelection(e))
            {
                manualTargetEffect = e;
                break;
            }
        }

        if (manualTargetEffect != null)
        {
            OpenOnActionUnitTargetSelection(
                side,
                command,
                manualTargetEffect,
                onDone,
                attackingUnitInAttackFlow,
                commandQueueIndex,
                commandQueueCount);
            return;
        }

        EffectData applied = onActionEffects[0];
        List<CardController> resolvedBeforeApply = ResolveEffectTargets(command, side, applied);
        StartCoroutine(ExecuteOnActionDirectEffectAfterPreview(
            side,
            command,
            applied,
            resolvedBeforeApply,
            attackingUnitInAttackFlow,
            commandQueueIndex,
            commandQueueCount,
            onDone));
    }

    private IEnumerator ExecuteOnActionDirectEffectAfterPreview(
        PlayerType side,
        CardController command,
        EffectData applied,
        List<CardController> resolvedBeforeApply,
        CardController attackingUnitInAttackFlow,
        int commandQueueIndex,
        int commandQueueCount,
        System.Action onDone)
    {
        yield return ShowCommandUsePreviewCoroutine(command, attackingUnitInAttackFlow, resolvedBeforeApply, null);

        if (!TryConsumeResourceForCommandPlay(side, command, "OnAction"))
        {
            LogCommandUseResultWithBoard(
                "OnAction_Failed_InsufficientResource",
                side,
                command,
                attackingUnitInAttackFlow,
                commandQueueIndex,
                commandQueueCount,
                "phase:before_apply_direct cost not consumed");
            onDone?.Invoke();
            yield break;
        }

        TryNotifyLocalOnActionCommandUsed(command, side);
        MarkActionStepCardUsed(side, command);
        string consumedSummary = $"{command.Data.cardName}(id:{command.Data.id})";
        string effectDetail =
            $"consumed:{consumedSummary}|firstEffect:{applied.type} target:{applied.target} value:{applied.value}";
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

    private void OpenOnActionUnitTargetSelection(
        PlayerType side,
        CardController command,
        EffectData effect,
        System.Action onDone,
        CardController attackingUnitInAttackFlow = null,
        int commandQueueIndex = -1,
        int commandQueueCount = -1)
    {
        List<CardController> candidates = ResolveSelectableEffectTargets(command, side, effect);
        if (candidates.Count == 0)
        {
            Debug.Log($"OnAction: 選択可能な対象ユニットがいません ({effect?.FormatEffectSelectionSummary()}).");
            LogCommandUseResultWithBoard(
                "OnAction_Skipped_NoUnitTargets",
                side,
                command,
                attackingUnitInAttackFlow,
                commandQueueIndex,
                commandQueueCount,
                "reason:ResolveSelectableEffectTargets empty");
            onDone?.Invoke();
            return;
        }

        if (command != null && command.Data != null && effect != null)
        {
            System.Text.StringBuilder openPatternTable = new System.Text.StringBuilder(320);
            openPatternTable.Append("[OnActionHypotheticalBoard] phase:EnumerateHypotheticalPicks candidates:").Append(candidates.Count)
                .Append(" cmdId:").Append(command.Data.id).Append(" effect:").Append(effect.type).Append(" target:").Append(effect.target)
                .Append(' ').Append(FormatBlockRedirectProbeInline(side)).AppendLine();
            for (int pi = 0; pi < candidates.Count; pi++)
            {
                CardController eu = candidates[pi];
                if (eu?.Data == null)
                {
                    continue;
                }

                int es = TryGetUnitBattleZoneSlotIndex(eu);
                openPatternTable.Append("  patternRow:").Append(FormatHypothesisPatternLetterLabel(pi)).Append(" → target:")
                    .Append(eu.Data.cardName).Append("(id:").Append(eu.Data.id).Append(") zoneSlotIndex:#").Append(es).AppendLine();
            }

            Debug.Log(openPatternTable.ToString());
            for (int hi = 0; hi < candidates.Count; hi++)
            {
                LogOnActionHypotheticalBoardForEnemyPick(
                    command,
                    side,
                    effect,
                    candidates[hi],
                    hi,
                    attackingUnitInAttackFlow,
                    commandQueueIndex,
                    commandQueueCount);
            }
        }

        bool isAttackContext = attackingUnitInAttackFlow != null && attackingUnitInAttackFlow.Data != null;
        string title = FormatManualUnitSelectionTitle(effect, attackingUnitInAttackFlow);
        string effectSummary = effect != null ? effect.FormatEffectSelectionSummary() : string.Empty;
        CardController blockRedirectUnit = isAttackContext
            && attackFlowBlockRedirectEngaged
            && IsCardControllerInstanceValid(attackFlowBlockRedirectUnit)
            ? attackFlowBlockRedirectUnit
            : null;

        OpenCommandWithTargetsSelectionUI(
            title,
            effectSummary,
            command,
            candidates,
            attackingUnitInAttackFlow,
            picked =>
            {
                if (!TryConsumeResourceForCommandPlay(side, command, "OnAction"))
                {
                    LogCommandUseResultWithBoard(
                        "OnAction_Failed_InsufficientResource",
                        side,
                        command,
                        attackingUnitInAttackFlow,
                        commandQueueIndex,
                        commandQueueCount,
                        "phase:unit_target_ui cost not consumed");
                    onDone?.Invoke();
                    return;
                }

                TryNotifyLocalOnActionCommandUsed(command, side, picked);
                MarkActionStepCardUsed(side, command);
                string consumedSummary = command.Data != null ? $"{command.Data.cardName}(id:{command.Data.id})" : "?";
                string detail =
                    $"consumed:{consumedSummary}|effect:{effect.type} target:{effect.target} value:{effect.value}|picked:{picked.Data.cardName}(id:{picked.Data.id})";
                List<UnitStatSnapForCommandLog> beforeSnapsPick = SnapUnitStatsForOnActionCommandLog(new List<CardController> { picked });

                if (IsCloseCombatCard(command) && attackFlowBlockRedirectFromShieldStrike)
                {
                    LogArgamaShieldBlockCloseCombatDebug(
                        "CloseCombatOnActionPick",
                        $"side:{side} {detail} redirectBlocker:{FormatUnitDebugSnap(attackFlowBlockRedirectUnit)} "
                        + $"pickedIsBlocker:{picked == attackFlowBlockRedirectUnit}",
                        attackFlowAttackerUnit,
                        attackFlowBlockRedirectUnit,
                        command);
                }

                ApplyEffectToSpecificTargets(command, side, effect, new List<CardController> { picked });
                if (attackingUnitInAttackFlow != null && picked == attackingUnitInAttackFlow)
                {
                    Debug.Log(
                        $"[OnActionUnitTarget] effect applied to attacking unit — strikeAP after command:{GetUnitStrikeDamagePower(picked)} "
                        + $"(card:{picked.Data.cardName})");
                }

                LogOnActionCommandAppliedToUnitsBattleOutcome(command, side, effect, "OnAction_AfterApplyUnitTarget", beforeSnapsPick);
                FinalizeOnActionSourceCard(command, side);
                List<CardController> pickedForEval = BuildOnActionUnitTargetListAfterApply(new List<CardController> { picked });
                LogCommandUseResultWithBoard(
                    "OnAction_AfterApplyUnitTarget",
                    side,
                    null,
                    attackingUnitInAttackFlow,
                    commandQueueIndex,
                    commandQueueCount,
                    detail,
                    pickedForEval);
                onDone?.Invoke();
            },
            onDone,
            blockRedirectUnit);
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

        Button mainBtn = filterPanel.CreateChildButton(FormatOnMainActivationButtonLabel(cardController, ownerType));
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

    private bool TryAddOnRestSelfActivateButton(
        GameObject filterPanel,
        CardController cardController,
        PlayerType ownerType,
        float anchoredY)
    {
        if (filterPanel == null || cardController == null || cardController.Data == null)
        {
            return false;
        }

        if (!CanActivateOnRestBySelf(ownerType, cardController))
        {
            return false;
        }

        Button restBtn = filterPanel.CreateChildButton("レストして効果発動");
        RectTransform restRt = restBtn.GetComponent<RectTransform>();
        restRt.sizeDelta = new Vector2(320f, 50f);
        restRt.anchoredPosition = new Vector2(0f, anchoredY);
        CardController source = cardController;
        restBtn.onClick.AddListener(() =>
        {
            TryActivateOnRestBySelf(ownerType, source);
            Destroy(filterPanel);
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
            Debug.Log("OnMain: 現在は発動できません（ターン/フェイズ/リソース/条件/使用回数）。");
            onDone?.Invoke();
            return;
        }

        List<OnMainExecutableBlock> blocks = CollectExecutableOnMainBlocks(side, source);
        if (blocks.Count == 0)
        {
            onDone?.Invoke();
            return;
        }

        TryExecuteOnMainBlocks(side, source, blocks, 0, onDone);
    }

    private void TryExecuteOnMainBlocks(
        PlayerType side,
        CardController source,
        List<OnMainExecutableBlock> blocks,
        int blockIndex,
        System.Action onDone)
    {
        if (blocks == null || blockIndex >= blocks.Count)
        {
            onDone?.Invoke();
            return;
        }

        OnMainExecutableBlock entry = blocks[blockIndex];
        TimedEffectData timed = entry.Timed;
        bool deferPayment = NeedsDeferredOnMainPayment(timed, side, source);
        if (!deferPayment)
        {
            if (!TryFinalizeOnMainPaidActivation(new PaidActivationBlockContext(side, source, timed, entry.BlockIndex)))
            {
                TryExecuteOnMainBlocks(side, source, blocks, blockIndex + 1, onDone);
                return;
            }
        }
        else
        {
            BeginOnMainPaidBlock(side, source, timed, entry.BlockIndex);
        }

        bool trashHandCardAfter = IsOnMainActivatedFromHand(source, side);
        BeginEffectChainObservationScope();
        EffectActivationContext chainActivationContext = BuildOnMainChainActivationContext(side, source);
        TryExecuteOnMainEffectChain(
            side,
            source,
            timed.GetResolvedEffects(),
            0,
            true,
            chainActivationContext,
            () =>
            {
                EndEffectChainObservationScope();
                ClearOnMainPaidBlock();
                if (trashHandCardAfter)
                {
                    FinalizeOnMainSourceCard(source, side);
                }

                TryExecuteOnMainBlocks(side, source, blocks, blockIndex + 1, onDone);
            });
    }

    private EffectActivationContext BuildOnMainChainActivationContext(PlayerType side, CardController source)
    {
        EffectActivationContext context = BuildActivationContext(side, source);
        IReadOnlyList<CardController> ownerZone = side == PlayerType.Player
            ? context.PlayerBattleZone
            : context.EnemyBattleZone;
        int ownerAliveUnits = EffectActivationEvaluator.CountAliveUnitsInZone(ownerZone);
        return context.WithFrozenOwnerBattleAliveUnitCount(ownerAliveUnits);
    }

    private void TryExecuteOnMainEffectChain(
        PlayerType side,
        CardController source,
        IReadOnlyList<EffectData> effects,
        int index,
        bool activationCostAlreadyPaid,
        EffectActivationContext chainActivationContext,
        System.Action onDone)
    {
        if (effects == null || index >= effects.Count)
        {
            onDone?.Invoke();
            return;
        }

        EffectData effect = effects[index];
        if (effect == null)
        {
            TryExecuteOnMainEffectChain(
                side, source, effects, index + 1, activationCostAlreadyPaid, chainActivationContext, onDone);
            return;
        }

        EffectActivationContext activationContext = chainActivationContext ?? BuildActivationContext(side, source);
        if (!ShouldApplyChainedEffect(effect, activationContext, "OnMain"))
        {
            TryExecuteOnMainEffectChain(
                side, source, effects, index + 1, activationCostAlreadyPaid, chainActivationContext, onDone);
            return;
        }

        if (TryExecutePriorChainPickedTargetEffect(
            source,
            side,
            effect,
            () => TryExecuteOnMainEffectChain(
                side, source, effects, index + 1, activationCostAlreadyPaid, chainActivationContext, onDone)))
        {
            return;
        }

        if (EffectRequiresManualHandSelection(effect))
        {
            List<CardController> handCandidates = CollectSelectableHandCards(ResolveHandDiscardOwner(side, effect));
            if (handCandidates.Count == 0)
            {
                Debug.Log("OnMain: 捨てる手札がありません (DiscardFromHand)。");
                TryExecuteOnMainEffectChain(
                    side, source, effects, index + 1, activationCostAlreadyPaid, chainActivationContext, onDone);
                return;
            }

            TryExecuteManualHandSelectionEffect(
                source,
                side,
                effect,
                () => TryExecuteOnMainEffectChain(
                    side, source, effects, index + 1, activationCostAlreadyPaid, chainActivationContext, onDone));
            return;
        }

        if (EffectRequiresManualUnitSelection(effect))
        {
            List<CardController> candidates = ResolveSelectableEffectTargets(source, side, effect);
            if (candidates.Count == 0)
            {
                Debug.Log($"OnMain: 選択可能な対象がありません (target:{effect.target})。");
                TryExecuteOnMainEffectChain(
                    side, source, effects, index + 1, activationCostAlreadyPaid, chainActivationContext, onDone);
                return;
            }

            if (side == PlayerType.Enemy)
            {
                EnemyAiEffectPickContext pickCtx = BuildEnemyAiEffectPickContext(side, source, null, null);
                CardController picked = PickEnemyAiEffectTarget(effect, pickCtx, candidates);
                if (picked != null)
                {
                    ApplyEffectToSpecificTargets(source, side, effect, new List<CardController> { picked });
                }

                TryExecuteOnMainEffectChain(
                    side, source, effects, index + 1, activationCostAlreadyPaid, chainActivationContext, onDone);
                return;
            }

            OpenOnMainTargetSelectionUI(
                side,
                source,
                effect,
                candidates,
                effects,
                index,
                activationCostAlreadyPaid,
                chainActivationContext,
                onDone);
            return;
        }

        ApplyEffectRespectingLookAsync(
            source,
            side,
            effect,
            () => TryExecuteOnMainEffectChain(
                side, source, effects, index + 1, activationCostAlreadyPaid, chainActivationContext, onDone));
    }

    private static bool IsEffectTargetRequiringUnitSelection(TargetType targetType)
    {
        return targetType == TargetType.EnemyUnit
            || targetType == TargetType.RestEnemyUnit
            || targetType == TargetType.AllyUnit
            || targetType == TargetType.AllyOtherUnit;
    }

    private List<CardController> ResolveSelectableEffectTargets(
        CardController sourceCard,
        PlayerType ownerType,
        EffectData effect)
    {
        if (effect == null)
        {
            return new List<CardController>();
        }

        IReadOnlyList<CardFeatureData> requiredFeatures = effect.GetTargetFeatures();
        List<CardController> allies = ownerType == PlayerType.Player ? playerBattleZoneCards : enemyBattleZoneCards;
        List<CardController> result = new List<CardController>();

        switch (effect.target)
        {
            case TargetType.Self:
                if (sourceCard != null
                    && sourceCard.Data != null
                    && sourceCard.Data.IsUnitLike()
                    && sourceCard.CurrentHp > 0
                    && IsCardOnBattleZone(sourceCard)
                    && MatchesRequiredFeatures(sourceCard.Data, requiredFeatures))
                {
                    result.Add(sourceCard);
                }
                break;
            case TargetType.AllyUnit:
                AddAllAliveUnits(allies, result, null, requiredFeatures);
                EnsureAllyUnitSelfInCandidateList(sourceCard, result, requiredFeatures);
                break;
            case TargetType.AllyOtherUnit:
                AddAllAliveUnits(allies, result, sourceCard, requiredFeatures);
                break;
            case TargetType.EnemyUnit:
                AddAllAliveUnits(GetAliveEnemyUnits(ownerType), result, null, requiredFeatures);
                break;
            case TargetType.RestEnemyUnit:
                AddAllAliveUnits(GetAliveRestEnemyUnitsForOwner(ownerType), result, null, requiredFeatures);
                break;
        }

        FilterTargetsByUnitCondition(result, effect, sourceCard);
        FilterSelectableEffectTargets(result, effect);
        if (effect.type == EffectType.Rest)
        {
            FilterOutAlreadyRestedUnits(result);
        }

        if (effect.type == EffectType.Activate)
        {
            FilterOutNonRestedUnits(result);
        }

        return result;
    }

    private static string FormatManualUnitSelectionTitle(EffectData effect, CardController attackingUnitInAttackFlow)
    {
        if (effect == null)
        {
            return "対象を選択";
        }

        bool isAttackContext = attackingUnitInAttackFlow != null && attackingUnitInAttackFlow.Data != null;
        if (effect.type == EffectType.Bounce)
        {
            return isAttackContext
                ? $"バウンス — 手札に戻すユニットを選択（{attackingUnitInAttackFlow.Data.cardName} 攻撃中）"
                : "バウンス — 手札に戻すユニットを選択";
        }

        if (effect.type == EffectType.Rest)
        {
            return isAttackContext
                ? $"REST — 対象ユニットを選択（{attackingUnitInAttackFlow.Data.cardName} 攻撃中）"
                : "REST — 対象ユニットを選択";
        }

        if (effect.type == EffectType.Activate)
        {
            string blockerHint = effect.filterTargetIsBlocker ? "（ブロッカー・RESTのみ）" : "（RESTのみ）";
            return isAttackContext
                ? $"ACTIVE化 — 対象ユニットを選択{blockerHint}（{attackingUnitInAttackFlow.Data.cardName} 攻撃中）"
                : $"ACTIVE化 — 対象ユニットを選択{blockerHint}";
        }

        if (effect.type == EffectType.MarkObservedUnit)
        {
            return $"監視対象ユニットを選択{effect.FormatSelectCountRangeLabel()}";
        }

        if (effect.type == EffectType.GrantAttackFlag)
        {
            string featureLabel = effect.FormatTargetFeaturesLabel("/");
            string typeLabel = effect.filterByTargetCardType ? effect.targetCardType.ToString() : string.Empty;
            string filterHint = string.IsNullOrEmpty(featureLabel)
                ? string.Empty
                : $"（{featureLabel}";
            if (!string.IsNullOrEmpty(typeLabel))
            {
                filterHint += string.IsNullOrEmpty(filterHint) ? $"（{typeLabel}" : $"・{typeLabel}";
            }

            if (!string.IsNullOrEmpty(filterHint))
            {
                filterHint += "・AttackFlg=OFF のみ）";
            }
            else
            {
                filterHint = "（AttackFlg=OFF のユニットのみ）";
            }

            return isAttackContext
                ? $"アタック可能にする味方ユニットを選択{filterHint}（{attackingUnitInAttackFlow.Data.cardName} 攻撃中）"
                : $"アタック可能にする味方ユニットを選択{filterHint}";
        }

        if (effect.type == EffectType.Damage && effect.selectionMode.IsMultipleUnitPickMode())
        {
            if (effect.target.IsOpponentUnitTarget())
            {
                return "相手ユニットを選択（1ダメージ）";
            }

            if (effect.target.IsAllyUnitPickTarget())
            {
                return "自分のユニットを選択（1ダメージ）";
            }
        }

        if (effect.target == TargetType.AllyUnit)
        {
            return isAttackContext
                ? $"味方ユニット1体を選択（自身または他ユニット・{attackingUnitInAttackFlow.Data.cardName} 攻撃中）"
                : "味方ユニット1体を選択（自身または他ユニット）";
        }

        if (effect.target == TargetType.AllyOtherUnit)
        {
            return isAttackContext
                ? $"味方ユニット1体を選択（自身以外・{attackingUnitInAttackFlow.Data.cardName} 攻撃中）"
                : "味方ユニット1体を選択（自身以外）";
        }

        return isAttackContext
            ? $"OnAction — 対象を選択（攻撃中: {attackingUnitInAttackFlow.Data.cardName}）"
            : "OnAction — 対象を選択";
    }

    private static bool IsValidAllyUnitSelfTarget(
        CardController sourceCard,
        IReadOnlyList<CardFeatureData> requiredFeatures)
    {
        return sourceCard != null
            && sourceCard.Data != null
            && sourceCard.Data.IsUnitLike()
            && sourceCard.CurrentHp > 0
            && MatchesRequiredFeatures(sourceCard.Data, requiredFeatures);
    }

    private void EnsureAllyUnitSelfInCandidateList(
        CardController sourceCard,
        List<CardController> result,
        IReadOnlyList<CardFeatureData> requiredFeatures)
    {
        if (result == null
            || !IsValidAllyUnitSelfTarget(sourceCard, requiredFeatures)
            || !IsCardOnBattleZone(sourceCard)
            || result.Contains(sourceCard))
        {
            return;
        }

        result.Insert(0, sourceCard);
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

    private void OpenOnPlayedTargetSelectionUI(
        CardController source,
        PlayerType ownerType,
        EffectData effect,
        List<CardController> candidates,
        IReadOnlyList<EffectData> allEffects,
        int effectIndex,
        System.Action onDone)
    {
        OpenManualUnitTargetSelectionUI(
            source,
            ownerType,
            effect,
            candidates,
            null,
            picked =>
            {
                if (picked != null)
                {
                    ApplyEffectToSpecificTargets(source, ownerType, effect, new List<CardController> { picked });
                }

                TryExecuteOnPlayedEffectChain(source, ownerType, allEffects, effectIndex + 1, onDone);
            });
    }

    private void OpenOnMainTargetSelectionUI(
        PlayerType side,
        CardController source,
        EffectData effect,
        List<CardController> candidates,
        IReadOnlyList<EffectData> allEffects,
        int effectIndex,
        bool activationCostAlreadyPaid,
        EffectActivationContext chainActivationContext,
        System.Action onDone)
    {
        string effectSummary = effect != null
            ? $"OnMain {effect.type} / {effect.target} / 値:{effect.value}"
            : "OnMain";
        OpenCommandWithTargetsSelectionUI(
            FormatManualUnitSelectionTitle(effect, null),
            effectSummary,
            source,
            candidates,
            null,
            picked =>
            {
                ApplyEffectToSpecificTargets(source, side, effect, new List<CardController> { picked });
                TryExecuteOnMainEffectChain(
                    side,
                    source,
                    allEffects,
                    effectIndex + 1,
                    activationCostAlreadyPaid,
                    chainActivationContext,
                    onDone);
            },
            onDone);
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

        return CollectExecutableOnMainBlocks(ownerType, card).Count > 0;
    }

    private static List<EffectData> GetEffectsByTiming(CardData data, EffectTiming timing)
    {
        return TimedEffectResolver.CollectEffectsByTiming(data, timing);
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
            if (side == PlayerType.Player)
            {
                string playKind = HasEffectTiming(source.Data, EffectTiming.OnMain) ? "CommandOnMain" : "CommandOnAction";
                RecordEnemyAiObservedPlayerCardPlay(source, playKind);
            }

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
        return gundamRule.CanPlayCardWithAnyEx(
            ToRuleSide(ownerType),
            card.CurrentLevel,
            card.CurrentCost);
    }

    private static bool HasEffectTiming(CardData data, EffectTiming timing)
    {
        return TimedEffectResolver.HasEffectTiming(data, timing);
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
        ShowGoHomeConfirmDialog(resultText);
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
