using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro; // これを追加！

public partial class BattleGameMain : MonoBehaviour
{
    private const int PoorlyPlannedOffensiveCardId = 1000650;

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
    /// <summary>ActionStep でカード確定後〜コスト支払い・効果解決が終わるまで。この間はカード選択 UI を差し込まない。</summary>
    private bool _isActionStepCommandResolving;
    private GameObject _activeResourcePaymentOverlay;
    private bool isShieldBreakFlowOpen;
    private bool shieldBreakQueueRunning;
    private readonly Queue<PendingShieldBreakBatch> pendingShieldBreakBatches = new Queue<PendingShieldBreakBatch>();
    private GameObject activeOnActionPopupRoot;
    /// <summary>プレイヤーが手動選択 UI 経由で ApplyEffectToSpecificTargets している間だけ &gt; 0。</summary>
    private int _playerManualUnitSelectionApplyDepth;
    /// <summary>
    /// CannotBeChosenAsAttackTarget（UntilEndOfTurn）付与中の BattleInstanceId。
    /// CardController のランタイムがリセットされても攻撃対象判定が残るようにする。
    /// </summary>
    private readonly HashSet<int> _cannotBeChosenAsAttackUntilEotInstanceIds = new HashSet<int>();
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
    private int _turnPhaseSequenceGeneration;
    /// <summary>エネミー <see cref="EnemyActionCoroutine"/> 実行中。ターン進行を止める。</summary>
    private bool isEnemyMainPhaseCoroutineRunning;
    private bool blockShieldFlowDuringShieldAttack;
    private Gundam2024RuleScript.PlayerSide blockedShieldFlowSide;
    /// <summary>シールド攻撃宣言時点で EX／配備ベースがあったか（OnAttack 後も打撃ロジック用に保持）。</summary>
    private bool _shieldAttackHadExOrBaseAtDeclaration;
    private bool _shieldAttackHadExOrBaseAtDeclarationValid;
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
    /// <summary>武力介入でアタック先を REST〔CB〕へ変えた。OnAction 完了後にユニット戦へ進む。</summary>
    private bool _armedInterventionRedirectPending;
    /// <summary>武力介入のエフェクトバトルを選択直後に解決済み。元のシールド／ユニット打撃は行わない。</summary>
    private bool _armedInterventionCombatResolvedThisAttack;
    /// <summary>
    /// 武力介入後の元アタック（シールド含む）禁止。
    /// ClearAttackFlowContext では消さない（OnAction 完了後のシールド再開を防ぐ）。
    /// </summary>
    private bool _armedInterventionCancelOriginalStrike;
    /// <summary>武力介入後に遅れて届く相手の ShieldAttack を捨てる。</summary>
    private bool _armedInterventionIgnoreRemoteShieldAttack;

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
        ClearAttackFlowContextCore();
    }

    /// <summary>ユニット／シールド攻撃の正常終了：Deferred Activate → セッションリセット → フローコンテキスト消去。</summary>
    private void FinishUnitAttackSessionAndClearFlowContext()
    {
        FlushPendingPostCombatRecovers();
        ApplyDeferredOnAttackTrashReturnActivate();
        ResetOnAttackTrashReturnSession();
        ClearAttackFlowContextCore();
    }

    private void ClearAttackFlowContextCore()
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

        isAttackedSidePanelOpen = false;
        _suppressOnAttackReturnToDeckBottomAfterFailedDiscard = false;
        ClearShieldAttackDeclarationLayerCache();
        _armedInterventionRedirectPending = false;
        _armedInterventionCombatResolvedThisAttack = false;
        // _armedInterventionCancelOriginalStrike は次の新規攻撃宣言まで残す
    }

    private void MarkAttackFlowBlockSelectionResolved()
    {
        attackFlowBlockSelectionResolved = true;
    }

    /// <summary>新しい攻撃宣言の直前に、前回攻撃のブロック／OnAction 再開フラグを消す。</summary>
    private void ResetAttackFlowBlockPassFlagsForNewDeclaration()
    {
        _armedInterventionCancelOriginalStrike = false;
        _armedInterventionCombatResolvedThisAttack = false;
        _armedInterventionIgnoreRemoteShieldAttack = false;
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
        if (attacker != null)
        {
            pendingUnitAttackAttacker = attacker;
            AssignBattleInstanceIdIfNeeded(attacker);
        }

        if (declaredDefenderOrNull != null)
        {
            AssignBattleInstanceIdIfNeeded(declaredDefenderOrNull);
        }

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
        TryRunAttackActionSteps(
            defenderSideForOnAction,
            attackerOwner,
            finishOnAction,
            attacker,
            declaredDefenderOrNull);

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
        // 武力介入：選択直後にバトル済みなら元の打撃は行わない
        if (TryFinishArmedInterventionAlreadyResolvedCombat())
        {
            return;
        }

        if (TryResumeArmedInterventionUnitCombatAfterOnAction(attacker))
        {
            return;
        }

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

        RestBlockerThenNotifyActivated(blocker, syncOnlineRest: true);

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
        FinishUnitAttackSessionAndClearFlowContext();
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
        if (attacker != null)
        {
            pendingUnitAttackAttacker = attacker;
            AssignBattleInstanceIdIfNeeded(attacker);
        }

        if (declaredDefenderOrNull != null)
        {
            AssignBattleInstanceIdIfNeeded(declaredDefenderOrNull);
        }

        if (blockRedirectUnitOrNull != null)
        {
            AssignBattleInstanceIdIfNeeded(blockRedirectUnitOrNull);
        }

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
        CardPilotIdRegistry.EnsureLoaded();
        NamedEffectSetRegistry.Reload();
        ClearOwnEffectDestroyOfOwnUnitHistory();
        InitializeBattleOpponent();
        ResetOnlineBattleInstanceIds();
        isFirstPlayer = DecideTurnOrder();
        PlayerType firstPlayerThisGame = currentPlayerType;

        const int openingHandSize = 5;
        int minDeckTotalForOpening = openingHandSize + OpeningShieldCardCount;

        playerDeckData = DeckSettinObject.Instance.LoadDeckReturn();
        enemyDeckData = DeckSettinObject.Instance.LoadEnemyDeckReturn();
        // オンライン: ランダムマッチ等の開始押下時点デッキを優先（編集中バッファで差し替えない）
        if (IsOnlineBattle()
            && EosOnlineMatchState.TryGetLockedPlayerDeckCards(out Dictionary<int, int> lockedOnlineDeck))
        {
            playerDeckData = lockedOnlineDeck;
            Debug.Log(
                $"[OnlineBattle] Using locked match deck '{EosOnlineMatchState.LockedPlayerDeck?.Title}' "
                + $"cards={lockedOnlineDeck.Count}");
        }

        ConfigureOnlineBattleDecks(ref playerDeckData, ref enemyDeckData);
        ConfigureTestPlayDecks(ref playerDeckData, ref enemyDeckData);
        enemyDeckData = EnsureDeckHasMinimumCardsForOpening(enemyDeckData, playerDeckData, minDeckTotalForOpening, "Enemy");
        playerDeckData = EnsureDeckHasMinimumCardsForOpening(playerDeckData, enemyDeckData, minDeckTotalForOpening, "Player");

        cardGameRule.SetUp(PlayerFieldPanel);
        cardGameRule.CreateShuffledDeck(playerDeckData, GetOnlineDeckSeed(true));
        cardGameRule.ResourcAndLevelTextGet(PlayerresourcePointText, PlayerlevelText, ExresourcePointText);
        // 相手は自分と同じ配置を作り、盤面全体を 180° 回転（向かい合わせ）
        enemyCardGameRule.SetUp(EnemyPlayerFieldPanel);
        enemyCardGameRule.CreateShuffledDeck(enemyDeckData, GetOnlineDeckSeed(false));

        // 自分フィールドを大きめに取り、相手は上スクロールで見る（回転もここで設定）
        RectTransform boardCanvas = ResolveBattleBoardRoot();
        BattleBoardScrollLayout.Apply(
            boardCanvas,
            PlayerFieldPanel != null ? PlayerFieldPanel.GetComponent<RectTransform>() : null,
            EnemyPlayerFieldPanel != null ? EnemyPlayerFieldPanel.GetComponent<RectTransform>() : null);
        // 180° 後に相手リソースのレスト向きを合わせる
        cardGameRule.RefreshResourceBoardFlipState();
        enemyCardGameRule.RefreshResourceBoardFlipState();
        // レイアウト確定後にもう一度（eulerAngles が確定してから）
        Canvas.ForceUpdateCanvases();
        enemyCardGameRule.RefreshResourceBoardFlipState();

        cardGameRule.BindDiscardZoneToggleClick(() =>
        {
            cardGameRule.SetDiscardZoneViewMode(DiscardZoneViewMode.Trash);
            OpenDiscardZoneInspectionPanel(cardGameRule);
        });
        cardGameRule.BindDiscardZoneCountClick(() =>
        {
            cardGameRule.SetDiscardZoneViewMode(DiscardZoneViewMode.Trash);
            OpenDiscardZoneInspectionPanel(cardGameRule);
        });
        cardGameRule.BindExileZoneCountClick(() =>
        {
            cardGameRule.SetDiscardZoneViewMode(DiscardZoneViewMode.Exile);
            OpenDiscardZoneInspectionPanel(cardGameRule);
        });
        enemyCardGameRule.BindDiscardZoneToggleClick(() =>
        {
            enemyCardGameRule.SetDiscardZoneViewMode(DiscardZoneViewMode.Trash);
            OpenDiscardZoneInspectionPanel(enemyCardGameRule);
        });
        enemyCardGameRule.BindDiscardZoneCountClick(() =>
        {
            enemyCardGameRule.SetDiscardZoneViewMode(DiscardZoneViewMode.Trash);
            OpenDiscardZoneInspectionPanel(enemyCardGameRule);
        });
        enemyCardGameRule.BindExileZoneCountClick(() =>
        {
            enemyCardGameRule.SetDiscardZoneViewMode(DiscardZoneViewMode.Exile);
            OpenDiscardZoneInspectionPanel(enemyCardGameRule);
        });
        RegisterOnlineZoneSyncObservers();

        if (!IsTestPlayBattle())
        {
            BindEnemyAiPlayerTrashObservation();
        }

        DeveloperModeAccess.ApplyStartingLevelOverride(gundamRule);
        gundamRule.InitializeGame(
            cardGameRule.GetRemainingCount(),
            enemyCardGameRule.GetRemainingCount(),
            ToRuleSide(firstPlayerThisGame));
        DeveloperModeAccess.ForceBothSidesStartingLevel(gundamRule);

        WithEffectDrawTurnFlagSuppressed(() =>
        {
            for (int i = 0; i < openingHandSize; i++)
            {
                CardAddtoHand(cardGameRule, PlayerType.Player);
            }
        });
        if (ShouldSkipEnemyOpeningHandOnline())
        {
            Debug.Log("[OnlineBattle] Skipped local opponent opening hand. Opponent hand is on their device.");
        }
        else
        {
            WithEffectDrawTurnFlagSuppressed(() =>
            {
                for (int i = 0; i < openingHandSize; i++)
                {
                    CardAddtoHand(enemyCardGameRule, PlayerType.Enemy);
                }
            });
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
        if (IsOnlineBattle())
        {
            NotifyLocalPlayerHandDeckSnapshot();
        }

        int exBasePoints = exBaseData != null ? exBaseData.startingPoints : 3;

        // マリガン：プレイヤーは Yes/No、エネミーは 1/2（オンラインは P2P 同期）
        Canvas canvas = ResolveBattleCanvas();
        if (canvas != null)
        {
            if (IsOnlineBattle())
            {
                yield return RunOnlineMulliganAndBootstrapCoroutine(canvas, openingHandSize, exBasePoints);
            }
            else if (IsTestPlayBattle())
            {
                yield return RunTestPlayDualMulliganCoroutine(canvas, openingHandSize, exBasePoints);
            }
            else
            {
                isMulliganPromptOpen = true;
                bool playerPerformedMulligan = false;
                yield return RunPlayerMulliganDecisionCoroutine(
                    canvas,
                    openingHandSize,
                    value => playerPerformedMulligan = value);
                isMulliganPromptOpen = false;

                if (playerPerformedMulligan)
                {
                    Debug.Log("[マリガン] プレイヤー：マリガン終了。");
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

        DeveloperModeAccess.ForceBothSidesStartingLevel(gundamRule);
        SyncAllResourceViewsFromRule();

        if (IsTestPlayBattle())
        {
            ConfigureTestPlaySandboxToolbar();
            ApplyTestPlayBoardPerspective(currentPlayerType);
        }

        if (DeveloperModeAccess.IsAuthorized)
        {
            ConfigureDeveloperUnlimitedDraw();
        }

        ChangePhase(BattlePhase.StartTurn);

        ConfigureEndTurnButtonInHandPanel();
        if (IsTestPlayBattle())
        {
            ConfigureEndTurnButtonForActiveSide(currentPlayerType);
        }

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

    /// <summary>BattleBoardCanvas / BattleBoardRoot を解決する（スクロール化後も可）。</summary>
    private RectTransform ResolveBattleBoardRoot()
    {
        Transform start = PlayerFieldPanel != null
            ? PlayerFieldPanel.transform
            : (EnemyPlayerFieldPanel != null ? EnemyPlayerFieldPanel.transform : null);
        if (start == null)
        {
            return null;
        }

        Transform p = start;
        while (p != null)
        {
            string n = p.name;
            if (n == "BattleBoardCanvas"
                || n == "BattleBoardRoot"
                || n == "BattleBiardCanvas")
            {
                return p as RectTransform;
            }

            p = p.parent;
        }

        return start.parent as RectTransform;
    }

    /// <summary>手札のカードを山札に戻しシャッフルして、指定枚数ドローし直す。</summary>
    private void PerformMulligan(CardGameRule rule, List<CardData> handList, int drawCount, PlayerType playerType)
    {
        List<int> ids = CollectHandCardIdsFromHandContent(rule);
        ClearHandVisuals(rule, handList);
        rule.ReturnCardIdsToDeckAndShuffle(ids);
        WithEffectDrawTurnFlagSuppressed(() =>
        {
            for (int i = 0; i < drawCount; i++)
            {
                CardAddtoHand(rule, playerType);
            }
        });

        RefreshAllHandsConditionalOnHandAuto();
        rule.RefreshHandCountDisplay();
        if (playerType == PlayerType.Player)
        {
            NotifyLocalPlayerHandDeckSnapshot();
        }
    }

    /// <summary>
    /// 通常は1回だけ、開発者の無制限設定中は Yes のたびに引き直して再表示し、No で終了する。
    /// </summary>
    private IEnumerator RunPlayerMulliganDecisionCoroutine(
        Canvas canvas,
        int openingHandSize,
        System.Action<bool> onComplete)
    {
        bool performedAny = false;
        bool keepPrompting;
        do
        {
            bool? choice = null;
            yield return MulliganPromptCoroutine(
                canvas,
                GameLocale.T(
                    "手札を山札に戻して5枚引き直しますか？（マリガン）",
                    "Do you want to shuffle your hand and draw 5 cards again? (Mulligan)"),
                value => choice = value);

            if (choice != true)
            {
                break;
            }

            PerformMulligan(cardGameRule, playerHandCards, openingHandSize, PlayerType.Player);
            performedAny = true;
            Debug.Log("[マリガン] プレイヤー：実行（手札を山札に戻しシャッフル後、5枚ドロー）。");
            keepPrompting = DeveloperModeAccess.ShouldAllowUnlimitedMulligan;
        }
        while (keepPrompting);

        onComplete?.Invoke(performedAny);
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
        titleTmp.SetLocalizedText(message);
        titleTmp.fontSize = 22;
        titleTmp.alignment = TextAlignmentOptions.Center;
        titleTmp.color = Color.black;
        titleTmp.enableWordWrapping = true;
        RectTransform titleRt = titleObj.GetComponent<RectTransform>();
        titleRt.anchorMin = new Vector2(0f, 0.55f);
        titleRt.anchorMax = new Vector2(1f, 1f);
        titleRt.offsetMin = new Vector2(16f, 0f);
        titleRt.offsetMax = new Vector2(-16f, -12f);

        Button yesButton = panel.CreateChildButton(GameLocale.T("はい", "Yes"));
        RectTransform yesRt = yesButton.GetComponent<RectTransform>();
        yesRt.anchorMin = new Vector2(0.15f, 0.12f);
        yesRt.anchorMax = new Vector2(0.45f, 0.42f);
        yesRt.offsetMin = Vector2.zero;
        yesRt.offsetMax = Vector2.zero;

        Button noButton = panel.CreateChildButton(GameLocale.T("いいえ", "No"));
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
        if (isInHand)
        {
            RefreshAllHandsConditionalOnHandAuto();
        }
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
            if (!IsTestPlayBattle())
            {
                Debug.Log("シールドは裏向きです。破壊されると中身が表示されます。");
                return;
            }

            // TestPlay: 内容を見せて操作メニューへ
            cardController.RevealShieldFace();
        }

        // クリック時にフィルターパネルを表示する処理
        FilterSetParentanvas = GetComponentInParent<Canvas>().rootCanvas;
        RectTransform boardRoot = ResolveBattleBoardRoot();
        Transform filterParent = boardRoot != null
            ? boardRoot
            : FilterSetParentanvas.transform;

        GameObject FilterPanel = Instantiate(FilterPanelPrefab, filterParent);
        ApplyCardFilterPanelSize(
            FilterPanel,
            boardRoot != null ? boardRoot : FilterSetParentanvas.GetComponent<RectTransform>());
        GameObject filterContent = CreateCardFilterScrollContent(FilterPanel);

        // GameObject imageOnlyObj = new GameObject("CopyImage", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        // imageOnlyObj.transform.SetParent(FilterPanel.transform, false);

        // UnityEngine.UI.Image sourceImg = cardController.GetComponent<UnityEngine.UI.Image>();
        // UnityEngine.UI.Image targetImg = imageOnlyObj.GetComponent<UnityEngine.UI.Image>();
        
        
        // targetImg.sprite = sourceImg.sprite; 
        GameObject copy = filterContent.CreateChildImageFrom(cardController.gameObject);
        RectTransform copyRt = copy.GetComponent<RectTransform>();
        if (copyRt != null)
        {
            copyRt.sizeDelta = new Vector2(120f, 168f);
        }

        LayoutElement copyLayout = copy.GetComponent<LayoutElement>();
        if (copyLayout == null)
        {
            copyLayout = copy.AddComponent<LayoutElement>();
        }

        copyLayout.preferredWidth = 120f;
        copyLayout.preferredHeight = 168f;
        copyLayout.minHeight = 120f;
        FilterPanel.SetActive(true);

        if (isInHand && cardController.Data != null)
        {
            TextMeshProUGUI handStatText = filterContent.CreateChildTextCustom(
                "HandStatText",
                UIAnchor.TopCenter,
                320,
                44);
            handStatText.text =
                $"Lv:{cardController.CurrentLevel}  COST:{cardController.CurrentCost}";
            handStatText.fontSize = 28;
            handStatText.color = Color.black;
            RectTransform handStatRt = handStatText.GetComponent<RectTransform>();
            handStatRt.anchoredPosition = new Vector2(0f, -30f);
            handStatText.transform.SetAsLastSibling();
        }

        if (isOnAnyDeployField && cardController.Data != null)
        {
            TextMeshProUGUI battleStatText = filterContent.CreateChildTextCustom("BattleStatText", UIAnchor.TopCenter, 320, 44);
            battleStatText.text = $"AP:{cardController.CurrentPower}  HP:{cardController.CurrentHp}";
            battleStatText.fontSize = 28;
            battleStatText.color = Color.black;
            RectTransform statRt = battleStatText.GetComponent<RectTransform>();
            statRt.anchoredPosition = new Vector2(0f, -30f);
            battleStatText.transform.SetAsLastSibling();

            if (cardController.Data.IsUnitLike() && cardController.MountedPilot != null)
            {
                GameObject pilotCopy = filterContent.CreateChildImageFrom(cardController.MountedPilot.gameObject);
                RectTransform pilotCopyRt = pilotCopy.GetComponent<RectTransform>();
                if (pilotCopyRt != null)
                {
                    pilotCopyRt.anchoredPosition = new Vector2(0f, -120f);
                    pilotCopyRt.localScale = Vector3.one * 0.95f;
                }
                pilotCopy.transform.SetAsLastSibling();
            }
        }

        // close は 480×800 枠の下端に固定（スクロール外）
        var closeButton = FilterPanel.CreateChildButton("close");
        RectTransform closeBtnRect = closeButton.GetComponent<RectTransform>();
        PinFilterCloseButton(closeBtnRect);
        closeButton.onClick.AddListener(() => Destroy(FilterPanel));
    
        // 場のカードはトラッシュ送り操作を可能にする。
        if (isOnField)
        {
            bool canShowUnitAttackMenu = currentPhase == BattlePhase.MainPhase
                && IsActingSideForUi(ownerType)
                && cardController.Data.IsUnitLike()
                && cardController.Data.id != 1000703
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
                bool forcedUnitAttack = HasForcedEnemyAttackTarget(ownerType, cardController);
                bool allowShieldOrDirectAttack = !forcedUnitAttack
                    && !cardController.CannotDirectAttackPlayerOrShield()
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

                    var shieldAttackBtn = filterContent.CreateChildButton(shieldLabel);
                    RectTransform shieldRect = shieldAttackBtn.GetComponent<RectTransform>();
                    shieldRect.sizeDelta = new Vector2(320, 50);
                    shieldRect.anchoredPosition = new Vector2(0, -10);
                    shieldAttackBtn.onClick.AddListener(() =>
                    {
                        TryUnitShieldAttackFromUnit(cardController);
                        Destroy(FilterPanel);
                    });
                }

                var unitAttackBtn = filterContent.CreateChildButton(
                    HasForcedEnemyAttackTarget(ownerType, cardController)
                        ? "Attack Unit (forced target)"
                        : cardController.HasAttackActiveEnemyAbility()
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

                PinFilterCloseButton(closeBtnRect);
            }

            float fieldActionY = canShowUnitAttackMenu ? -130f : -70f;
            if (TryAddOnRestSelfActivateButton(filterContent, cardController, ownerType, fieldActionY))
            {
                fieldActionY -= 60f;
            }

            if (TryAddOnMainEffectApplyButton(filterContent, cardController, ownerType, fieldActionY))
            {
                fieldActionY -= 60f;
            }

            // TestPlay: ユニットは AP永続 / HP / APターン、ベースは HP のみ
            if (IsTestPlayBattle())
            {
                fieldActionY = EmbedTestPlayFieldStatCounters(
                    filterContent,
                    cardController,
                    isInBaseSlot,
                    fieldActionY);
            }

            var trashButton = filterContent.CreateChildButton(
                cardController.Data != null && cardController.Data.IsUnitToken()
                    ? GameLocale.T("消滅させる", "Vanish")
                    : GameLocale.T("トラッシュへ送る", "Send to trash"));
            RectTransform trashBtnRect = trashButton.GetComponent<RectTransform>();
            trashBtnRect.sizeDelta = new Vector2(180, 50);
            trashBtnRect.anchoredPosition = new Vector2(0, fieldActionY);

            // 通常対戦では手動トラッシュ不可（TestPlay のみ操作可）
            bool allowManualTrash = IsTestPlayBattle();
            trashButton.interactable = allowManualTrash;
            if (!allowManualTrash)
            {
                ColorBlock colors = trashButton.colors;
                colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.7f);
                trashButton.colors = colors;
                Image trashImage = trashButton.GetComponent<Image>();
                if (trashImage != null)
                {
                    trashImage.color = new Color(0.45f, 0.45f, 0.45f, 0.85f);
                }

                TextMeshProUGUI trashLabel = trashButton.GetComponentInChildren<TextMeshProUGUI>();
                if (trashLabel != null)
                {
                    trashLabel.color = new Color(0.75f, 0.75f, 0.75f, 0.9f);
                }
            }
            else
            {
                trashButton.onClick.AddListener(() =>
                {
                    SendCardToTrash(cardController, ownerType);
                    Destroy(FilterPanel);
                });
            }

            fieldActionY -= 60f;
            if (IsTestPlayBattle())
            {
                var exileButton = filterContent.CreateChildButton(
                    GameLocale.T("除外する", "Exile"));
                RectTransform exileBtnRect = exileButton.GetComponent<RectTransform>();
                exileBtnRect.sizeDelta = new Vector2(180, 50);
                exileBtnRect.anchoredPosition = new Vector2(0, fieldActionY);
                exileButton.onClick.AddListener(() =>
                {
                    TestPlayExileCardInstance(cardController, ownerType);
                    Destroy(FilterPanel);
                });
                fieldActionY -= 60f;
            }

            if (IsTestPlayBattle())
            {
                fieldActionY = EmbedTestPlayFieldUnitExtraActions(
                    filterContent,
                    cardController,
                    ownerType,
                    fieldActionY);
            }
            else if (cardController.Data != null
                && cardController.Data.type == Type.Base
                && currentPhase == BattlePhase.MainPhase
                && IsActingSideForUi(ownerType))
            {
                float baseActionY = -10f;
                if (TryAddOnMainEffectApplyButton(filterContent, cardController, ownerType, baseActionY))
                {
                    baseActionY -= 60f;
                    PinFilterCloseButton(closeBtnRect);
                    return;
                }
            }

            PinFilterCloseButton(closeBtnRect);
            return;
        }

        // シールドエリア：TestPlay は操作メニュー、通常は OnRest の能動起動を許可
        if (isInShield)
        {
            if (IsTestPlayBattle())
            {
                TryEmbedTestPlayShieldMenu(
                    filterContent,
                    cardController,
                    ownerType,
                    ownerRule,
                    closeBtnRect);
                return;
            }

            if (TryAddOnRestSelfActivateButton(filterContent, cardController, ownerType, -10f))
            {
                PinFilterCloseButton(closeBtnRect);
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

        // 相手ターン中に相手手札を操作させない（TestPlay は両サイド操作可）。
        if (!IsActingSideForUi(ownerType))
        {
            Debug.Log("現在のターンプレイヤーの手札ではありません。");
            Destroy(FilterPanel);
            return;
        }

        if (CanDeployShieldFromHand(cardController))
        {
            float shieldActionY = -10f;
            if (TryAddOnMainEffectApplyButton(filterContent, cardController, ownerType, shieldActionY))
            {
                shieldActionY -= 60f;
                PinFilterCloseButton(closeBtnRect);
            }

            var deployShieldBtn = filterContent.CreateChildButton("Deploy Shield");
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

        float handActionY = -10f;
        bool hasOnMainActivateButton = TryAddOnMainEffectApplyButton(
            filterContent,
            cardController,
            ownerType,
            handActionY);
        if (hasOnMainActivateButton)
        {
            handActionY -= 60f;
            PinFilterCloseButton(closeBtnRect);
        }

        if (DeveloperModeAccess.IsAuthorized
            && !IsTestPlayBattle()
            && isInHand
            && ownerType == PlayerType.Player)
        {
            Button developerDiscardButton = filterContent.CreateChildButton(
                GameLocale.T("手札から捨てる（開発者）", "Discard from hand (Developer)"));
            RectTransform discardRect = developerDiscardButton.GetComponent<RectTransform>();
            discardRect.sizeDelta = new Vector2(320f, 50f);
            discardRect.anchoredPosition = new Vector2(0f, handActionY);
            developerDiscardButton.onClick.AddListener(() =>
            {
                DiscardHandCardInstance(cardController, ownerType);
                Destroy(FilterPanel);
                SyncAllResourceViewsFromRule();
                Debug.Log("[DeveloperMode] Discarded 1 card from hand.");
            });
            handActionY -= 60f;
            PinFilterCloseButton(closeBtnRect);
        }

        if (IsTestPlayBattle())
        {
            TryEmbedTestPlayFreeHandDeployUi(
                filterContent,
                cardController,
                ownerType,
                ownerRule,
                ownerSide,
                closeBtnRect,
                handActionY);
            return;
        }

        // 純コマンドはレベル／コスト不足でもユニット配備 UI へ落とさない
        if (cardController.Data.type == Type.Command)
        {
            if (!hasOnMainActivateButton)
            {
                if (!HasEffectTiming(cardController.Data, EffectTiming.OnMain))
                {
                    Debug.Log("このコマンドはメインフェイズでは発動できません（アクション等で使用）。");
                }
                else
                {
                    Debug.Log("OnMain: 現在は発動できません（ターン/フェイズ/リソース/条件/使用回数）。");
                }
            }

            return;
        }

        if (currentLevel < cardController.CurrentLevel)
        {
            bool allowNormalDeploy = false;
            if (TryOpenHandDeployAlternateCostFlow(
                    FilterPanel,
                    filterContent,
                    cardController,
                    ownerType,
                    ownerRule,
                    ownerSide,
                    closeBtnRect,
                    handActionY,
                    currentLevel,
                    allowNormalDeploy))
            {
                return;
            }

            Debug.Log("レベルが足りません。");
            Destroy(FilterPanel);
            return;
        }

        if (TryOpenHandDeployAlternateCostFlow(
                FilterPanel,
                filterContent,
                cardController,
                ownerType,
                ownerRule,
                ownerSide,
                closeBtnRect,
                handActionY,
                currentLevel,
                allowNormalDeploy: true))
        {
            return;
        }

        if (cardController.Data.IsPilot())
        {
            // CommandPilot: OnMain（コマンド）と搭乗（パイロット）を両立させる。
            // ShowPilotMountTargetButtons は他ボタンを無効化するため、兼用時は「搭乗する」経由で開く。
            bool isCommandPilot = cardController.Data.IsCommand();
            List<CardController> mountTargets = GetMountableUnits(ownerType);

            if (mountTargets.Count == 0)
            {
                if (!isCommandPilot)
                {
                    Debug.Log("パイロットを乗せるユニットがバトルゾーンにいません。");
                }

                // 兼用カードは OnMain のみ残して終了（ユニット配備へ落とさない）
                return;
            }

            int mountCost = CardPairFromHandCost.GetPlayCost(cardController, mountTargets);
            if (!gundamRule.CanPlayCardWithAnyEx(ownerSide, cardController.CurrentLevel, mountCost))
            {
                if (!isCommandPilot)
                {
                    Debug.Log("リソース不足のためパイロットを配備できません。");
                }

                return;
            }

            if (isCommandPilot)
            {
                Button mountBtn = filterContent.CreateChildButton(
                    GameLocale.T("搭乗する（パイロット）", "Mount (Pilot)"));
                RectTransform mountRt = mountBtn.GetComponent<RectTransform>();
                mountRt.sizeDelta = new Vector2(280f, 50f);
                mountRt.anchoredPosition = new Vector2(0f, handActionY);
                PinFilterCloseButton(closeBtnRect);

                mountBtn.onClick.AddListener(() =>
                {
                    BeginPilotMountPaymentOrTargets(
                        filterContent,
                        cardController,
                        ownerType,
                        ownerSide,
                        mountCost);
                });
                return;
            }

            BeginPilotMountPaymentOrTargets(
                filterContent,
                cardController,
                ownerType,
                ownerSide,
                mountCost);
            return;
        }

        if (!gundamRule.CanPlayCardWithAnyEx(ownerSide, cardController.CurrentLevel, cost))
        {
            Debug.Log("リソースポイントが足りません。EXリソースを含めても不足しています。");
            Destroy(FilterPanel);
            return;
        }

        // 配備前に通常／EXリソースを選択して支払う
        PinFilterCloseButton(closeBtnRect);
        EmbedResourcePaymentUI(
            filterContent,
            ownerType,
            cost,
            cardController.CurrentLevel,
            handActionY,
            exToUse =>
            {
                if (cardController.Data.type == Type.Base)
                {
                    if (!TryPayHandDeployCost(ownerSide, cardController, exToUse))
                    {
                        Debug.Log("リソースポイントが足りません！");
                        return;
                    }

                    BeginDeployBaseFromHand(cardController, ownerType, ownerRule);
                    SyncResourceViewsFromRule(ownerSide);
                    Destroy(FilterPanel);
                    return;
                }

                if (!cardController.Data.IsUnitLike())
                {
                    Debug.LogWarning(
                        $"[HandDeploy] フィールド配備対象外のカードです: "
                        + $"{cardController.Data.cardName} type:{cardController.Data.type}");
                    return;
                }

                // 満杯時は支払前に置換／Cancel（Cancel ならコスト未消費）
                DeployToBattleZoneWithCapGate(
                    ownerType,
                    cardController,
                    () =>
                    {
                        if (!TryPayHandDeployCost(ownerSide, cardController, exToUse))
                        {
                            Debug.Log("リソースポイントが足りません！");
                            return;
                        }

                        SendCardToField(cardController, ownerType, ownerRule);
                        SyncResourceViewsFromRule(ownerSide);
                        Destroy(FilterPanel);
                    },
                    () => Destroy(FilterPanel));
            },
            () => Destroy(FilterPanel));
        return;
    }

    private const float CardFilterContentWidth = 480f;
    private const float CardFilterContentHeight = 800f;

    /// <summary>カード操作フィルターを 480×800 で盤面中央に置く。画面より大きい場合は収める。</summary>
    private static void ApplyCardFilterPanelSize(GameObject panel, RectTransform fitParent)
    {
        if (panel == null)
        {
            return;
        }

        RectTransform rt = panel.GetComponent<RectTransform>();
        if (rt == null)
        {
            return;
        }

        if (fitParent != null)
        {
            rt.SetParent(fitParent, false);
        }

        float parentW = fitParent != null && fitParent.rect.width > 1f
            ? fitParent.rect.width
            : CardFilterContentWidth;
        float parentH = fitParent != null && fitParent.rect.height > 1f
            ? fitParent.rect.height
            : CardFilterContentHeight;
        float w = Mathf.Min(CardFilterContentWidth, parentW);
        float h = Mathf.Min(CardFilterContentHeight, parentH);

        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = Vector2.zero;
        rt.localScale = Vector3.one;
        rt.SetAsLastSibling();
    }

    /// <summary>close 以外の操作 UI を 480×800 内で縦スクロールできるようにする。</summary>
    private static GameObject CreateCardFilterScrollContent(GameObject panel)
    {
        if (panel.GetComponent<RectMask2D>() == null)
        {
            panel.AddComponent<RectMask2D>();
        }

        GameObject scrollGo = new GameObject(
            "FilterScroll",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(ScrollRect),
            typeof(RectMask2D));
        scrollGo.transform.SetParent(panel.transform, false);
        Image scrollBg = scrollGo.GetComponent<Image>();
        scrollBg.color = new Color(0f, 0f, 0f, 0.01f);
        scrollBg.raycastTarget = true;

        RectTransform scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.anchorMin = Vector2.zero;
        scrollRt.anchorMax = Vector2.one;
        scrollRt.offsetMin = new Vector2(0f, 56f);
        scrollRt.offsetMax = Vector2.zero;

        GameObject viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewport.transform.SetParent(scrollGo.transform, false);
        RectTransform viewRt = viewport.GetComponent<RectTransform>();
        viewRt.anchorMin = Vector2.zero;
        viewRt.anchorMax = Vector2.one;
        viewRt.offsetMin = Vector2.zero;
        viewRt.offsetMax = Vector2.zero;

        GameObject content = new GameObject(
            "Content",
            typeof(RectTransform),
            typeof(VerticalLayoutGroup),
            typeof(ContentSizeFitter));
        content.transform.SetParent(viewport.transform, false);
        RectTransform contentRt = content.GetComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta = Vector2.zero;

        VerticalLayoutGroup layout = content.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 12, 12);
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect sr = scrollGo.GetComponent<ScrollRect>();
        sr.viewport = viewRt;
        sr.content = contentRt;
        sr.horizontal = false;
        sr.vertical = true;
        sr.movementType = ScrollRect.MovementType.Clamped;
        sr.scrollSensitivity = 24f;

        return content;
    }

    /// <summary>close をフィルター枠の下端中央に固定する。</summary>
    private static void PinFilterCloseButton(RectTransform closeRt)
    {
        if (closeRt == null)
        {
            return;
        }

        closeRt.anchorMin = new Vector2(0.5f, 0f);
        closeRt.anchorMax = new Vector2(0.5f, 0f);
        closeRt.pivot = new Vector2(0.5f, 0f);
        closeRt.sizeDelta = new Vector2(140f, 44f);
        closeRt.anchoredPosition = new Vector2(0f, 8f);
        closeRt.SetAsLastSibling();
    }

    /// <summary>カード操作フィルターを破棄する。</summary>
    private static void DestroyCardFilterOverlay(GameObject panel)
    {
        if (panel != null)
        {
            Destroy(panel);
        }
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
        if (targetType == PlayerType.Player)
        {
            NotifyLocalPlayerHandDeckSnapshot();
        }

        // 効果ドロー（ターン開始・開始手札以外）をコウ・ウラキ条件へ記録する。
        NoteEffectDrawResolved(targetType, 1);

        return drawnCard;
    }
    public bool DecideTurnOrder()
    {
        // 先攻後攻は1回の乱数で決定（isFirstPlayer / currentPlayerType / currentPlayer を矛盾なく同期）
        bool playerGoesFirst;
        if (IsTestPlayBattle())
        {
            playerGoesFirst = true;
        }
        else if (TryOverrideTurnOrderFromOnlineMatch(out bool onlinePlayerGoesFirst))
        {
            playerGoesFirst = onlinePlayerGoesFirst;
        }
        else
        {
            playerGoesFirst = Random.value < 0.5f;
        }

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
                // 手札上限破棄中はドローフェイズ等を開始しない
                if (isEndPhaseHandLimitDiscardInProgress)
                {
                    Debug.LogWarning("[Battle] StartTurn deferred — end-phase hand limit discard still in progress.");
                    return;
                }

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

        int generation = ++_turnPhaseSequenceGeneration;
        isTurnPhaseSequenceRunning = true;
        if (currentPlayerType == PlayerType.Player)
        {
            _onlineLocalTurnSequenceStarted = true;
        }

        try
        {
            if (IsOnlineBattle())
            {
                CloseAllOnlineActionStepUi();
            }

            yield return WaitForShieldBreakFlowCompleteCoroutine(8f);
            if (generation != _turnPhaseSequenceGeneration)
            {
                yield break;
            }

            if (IsTestPlayBattle())
            {
                ApplyTestPlayBoardPerspective(currentPlayerType);
                ExecuteTurnStartCore();
                currentPhase = BattlePhase.MainPhase;
                UpdateEndTurnButtonVisibility();
                ExcuteMainPhase();
                yield break;
            }

            yield return ShowPhasePauseCoroutine(currentPlayerType == PlayerType.Player ? "Player Turn" : "Enemy Turn");
            if (generation != _turnPhaseSequenceGeneration)
            {
                yield break;
            }

            currentPhase = BattlePhase.DrawPhase;
            UpdateEndTurnButtonVisibility();
            yield return ShowPhasePauseCoroutine("Draw Phase");
            if (generation != _turnPhaseSequenceGeneration)
            {
                yield break;
            }

            currentPhase = BattlePhase.ResourcePhase;
            UpdateEndTurnButtonVisibility();
            yield return ShowPhasePauseCoroutine("Resource Phase");
            if (generation != _turnPhaseSequenceGeneration)
            {
                yield break;
            }

            currentPhase = BattlePhase.ActivePhase;
            UpdateEndTurnButtonVisibility();
            yield return ShowPhasePauseCoroutine("Card & Resource Active");
            if (generation != _turnPhaseSequenceGeneration)
            {
                yield break;
            }

            ExecuteTurnStartCore();

            currentPhase = BattlePhase.MainPhase;
            UpdateEndTurnButtonVisibility();
            yield return ShowPhasePauseCoroutine("Main Phase");
            if (generation != _turnPhaseSequenceGeneration)
            {
                yield break;
            }

            ExcuteMainPhase();
        }
        finally
        {
            if (generation == _turnPhaseSequenceGeneration)
            {
                isTurnPhaseSequenceRunning = false;
            }
        }
    }

    private void AbortTurnPhaseSequenceForRemoteTurnSwitch()
    {
        _turnPhaseSequenceGeneration++;
        isTurnPhaseSequenceRunning = false;
    }

    private IEnumerator ExecuteEndTurnWithPhasePauseCoroutine()
    {
        currentPhase = BattlePhase.EndTurn;
        UpdateEndTurnButtonVisibility();
        yield return WaitForBattleFlowIdleBeforeEndTurnCoroutine();

        if (IsLiveShieldBreakInProgress())
        {
            yield return WaitForShieldBreakFlowCompleteCoroutine(45f);
        }

        ClearStaleAttackAndShieldFlagsForEndTurn();
        if (ShouldBlockOnlineLocalPlayDueToOnAction())
        {
            CloseAllOnlineActionStepUi();
        }

        if (!IsTestPlayBattle())
        {
            yield return ShowPhasePauseCoroutine("End Phase");
        }

        // 手札上限破棄・ターン切替・次ターン開始まで完了してからこのコルーチンを終える
        if (isEndTurnFlowRunning || isEnemyMainPhaseCoroutineRunning || IsBattleFlowBlockingTurnProgress())
        {
            Debug.LogWarning(
                $"[OnlineBattle] EndTurn skipped after End Phase pause. endTurnRunning:{isEndTurnFlowRunning} "
                + $"blocking:{IsBattleFlowBlockingTurnProgress()}");
            yield break;
        }

        yield return ExecuteEndTurnCoroutine();
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
        ClearPilotSetBattleDamageKillThisTurn();
        // 前ターンの効果ドロー記録を切る（オンライン相手ターン終了スキップ時の残留防止）。
        ClearAllOwnerDrewWithEffectThisTurn();
        RefreshAllFieldOwnerTurnPassives();

        if(currentPlayerType == PlayerType.Player)
        {
            Debug.Log("プレイヤーのターン開始処理を実行します。");
            // 先攻・後攻に関わらずレベル+1・リソースをレベルに同期してから、ドロー1枚
            gundamRule.SetCurrentTurnPlayer(Gundam2024RuleScript.PlayerSide.Player);
            gundamRule.BeginTurn();
            WithEffectDrawTurnFlagSuppressed(() => CardAddtoHand(cardGameRule, PlayerType.Player));
            SyncResourceViewsFromRule(Gundam2024RuleScript.PlayerSide.Player);
            Debug.Log($"[ドロー] プレイヤーのターン開始ドロー1枚。LV:{gundamRule.Player.level} Resource:{gundamRule.Player.resource}");
            Debug.Log($"プレイヤーの現在のリソースポイント: {gundamRule.Player.resource}");
            PlayerresourcePointText.text = gundamRule.Player.resource.ToString();
            ApplyTurnStartAttackFlgForCurrentPlayer();
            ClearPaidActivationUsesForSide(PlayerType.Player);
            ClearPaidUnitEffectCostThisTurn(PlayerType.Player);
            ClearOwnerEffectDestroyWatchUsesThisTurn();
            TriggerAllTimedEffectsForSide(PlayerType.Player, EffectTiming.OnTurnStart);
            // 相手ターン開始時効果（シャンブロ等）は非ターン側の場ユニットを評価する
            TriggerAllTimedEffectsForSide(PlayerType.Enemy, EffectTiming.OnOpponentTurnStart);
        }
        else
        {
            Debug.Log("エネミーのターン開始処理を実行します。");
            gundamRule.SetCurrentTurnPlayer(Gundam2024RuleScript.PlayerSide.Enemy);
            // オンラインは相手ドローの抽象手札更新もスキップし、HandDeckState を正とする
            gundamRule.BeginTurn(drawCards: !ShouldSkipEnemyDrawOnline());
            if (!ShouldSkipEnemyDrawOnline())
            {
                WithEffectDrawTurnFlagSuppressed(() => CardAddtoHand(enemyCardGameRule, PlayerType.Enemy));
            }
            else
            {
                Debug.Log("[OnlineBattle] Skipped local opponent draw. Opponent draws on their device.");
            }
            SyncResourceViewsFromRule(Gundam2024RuleScript.PlayerSide.Enemy);
            Debug.Log($"[ドロー] エネミーのターン開始ドロー1枚。LV:{gundamRule.Enemy.level} Resource:{gundamRule.Enemy.resource}");
            ApplyTurnStartAttackFlgForCurrentPlayer();
            ClearPaidActivationUsesForSide(PlayerType.Enemy);
            ClearPaidUnitEffectCostThisTurn(PlayerType.Enemy);
            ClearOwnerEffectDestroyWatchUsesThisTurn();
            TriggerAllTimedEffectsForSide(PlayerType.Enemy, EffectTiming.OnTurnStart);
            TriggerAllTimedEffectsForSide(PlayerType.Player, EffectTiming.OnOpponentTurnStart);
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

            yield return TryEnemyDeployAllAffordableUnitsFromHandCoroutine();
            yield return WaitForBattleFlowIdleCoroutine();

            yield return TryEnemyMountAllAffordablePilotsFromHandCoroutine();
            yield return WaitForBattleFlowIdleCoroutine();

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
            Debug.Log($"エネミーの行動が終了しました。shieldAttack:{attacked}");
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

            if (unit.Data.id == 1000703)
            {
                continue;
            }

            if (unit.AttackFlgState != AttackFlg.True)
            {
                continue;
            }

            bool canAttackShield = gundamRule.CanShowUnitShieldAttackOption(gundamRule.Player, unit.CurrentPower);
            bool canDirectAttack = !gundamRule.HasShieldZoneProtection(Gundam2024RuleScript.PlayerSide.Player);
            bool forcedUnitAttack = HasForcedEnemyAttackTarget(PlayerType.Enemy, unit);
            bool canShieldOrDirectAttack = !forcedUnitAttack
                && !unit.CannotDirectAttackPlayerOrShield()
                && (canAttackShield || canDirectAttack);
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

    /// <summary>攻撃者がユニット戦で選べる敵ユニット一覧（通常は REST のみ。アクティブ攻撃効果で ACTIVE も可）。強制攻撃対象がある場合はそのみ。</summary>
    private List<CardController> GetEnemyUnitAttackTargets(PlayerType attackerOwner, CardController attacker)
    {
        List<CardController> legal = CollectEnemyUnitAttackCombatCandidates(attackerOwner, attacker);
        RemoveCannotBeChosenAsAttackTargets(legal);
        return FilterEnemyUnitAttackTargetsByForce(attackerOwner, attacker, legal);
    }

    /// <summary>
    /// ユニット攻撃の戦闘条件を満たす敵（CannotBeChosenAsAttackTarget はまだ除外しない）。
    /// 攻撃対象 UI で保護ユニットをグレイ表示するために使う。
    /// </summary>
    private List<CardController> CollectEnemyUnitAttackCombatCandidates(
        PlayerType attackerOwner,
        CardController attacker)
    {
        List<CardController> enemies = GetAliveEnemyUnits(attackerOwner);
        List<CardController> legal = new List<CardController>(enemies.Count);
        if (attacker == null || !attacker.HasAttackActiveEnemyAbility())
        {
            for (int i = 0; i < enemies.Count; i++)
            {
                CardController enemy = enemies[i];
                if (enemy != null && enemy.IsRestState)
                {
                    legal.Add(enemy);
                }
            }
        }
        else
        {
            for (int i = 0; i < enemies.Count; i++)
            {
                CardController enemy = enemies[i];
                if (enemy == null)
                {
                    continue;
                }

                if (enemy.IsRestState || attacker.CanAttackerTargetActiveEnemy(enemy))
                {
                    legal.Add(enemy);
                }
            }
        }

        return legal;
    }

    /// <summary>
    /// 攻撃者に対し、敵側の ForceEnemyAttackTarget が有効な強制攻撃対象一覧。
    /// 「可能なら」＝通常のユニット攻撃対象として合法な場合のみ。
    /// </summary>
    private List<CardController> CollectForcedEnemyAttackTargets(PlayerType attackerOwner, CardController attacker)
    {
        var forced = new List<CardController>(2);
        if (attacker == null)
        {
            return forced;
        }

        List<CardController> enemies = GetAliveEnemyUnits(attackerOwner);
        EffectActivationContext attackerCtx = BuildOnAttackActivationContext(attackerOwner, attacker);
        for (int i = 0; i < enemies.Count; i++)
        {
            CardController host = enemies[i];
            if (host == null || !CanAttackerTargetEnemyUnitForCombat(attacker, host))
            {
                continue;
            }

            if (DoesUnitPreventBeingChosenAsAttackTarget(host))
            {
                continue;
            }

            if (DoesUnitForceEnemyAttackTarget(host, attacker, attackerOwner, attackerCtx))
            {
                forced.Add(host);
            }
        }

        return forced;
    }

    /// <summary>強制攻撃対象が1体以上あるか（シールド／直接攻撃を封じる判定用）。</summary>
    private bool HasForcedEnemyAttackTarget(PlayerType attackerOwner, CardController attacker)
    {
        return CollectForcedEnemyAttackTargets(attackerOwner, attacker).Count > 0;
    }

    /// <summary>合法候補を、有効な強制攻撃対象があるときそれのみに絞り込む。</summary>
    private List<CardController> FilterEnemyUnitAttackTargetsByForce(
        PlayerType attackerOwner,
        CardController attacker,
        List<CardController> legalTargets)
    {
        List<CardController> forced = CollectForcedEnemyAttackTargets(attackerOwner, attacker);
        if (forced.Count == 0)
        {
            return legalTargets;
        }

        return forced;
    }

    /// <summary>ホスト（本体＋搭乗パイロット）の ForceEnemyAttackTarget が、この攻撃者に対して有効か。</summary>
    private bool DoesUnitForceEnemyAttackTarget(
        CardController host,
        CardController attacker,
        PlayerType attackerOwner,
        EffectActivationContext attackerCtx)
    {
        if (host == null || attacker == null)
        {
            return false;
        }

        var abilities = new List<CardForceEnemyAttackExtensions.ForceEnemyAttackAbility>(2);
        CardForceEnemyAttackExtensions.CollectForceEnemyAttackAbilities(host, abilities);
        if (abilities.Count == 0)
        {
            return false;
        }

        PlayerType hostOwner = ResolveCardOwner(host.transform);
        EffectActivationContext hostCtx = BuildOnAttackActivationContext(hostOwner, host);

        for (int i = 0; i < abilities.Count; i++)
        {
            CardForceEnemyAttackExtensions.ForceEnemyAttackAbility ability = abilities[i];
            if (ability.Effect == null)
            {
                continue;
            }

            if (!EffectActivationEvaluator.AreAllConditionsMet(ability.HostConditions, hostCtx))
            {
                continue;
            }

            if (!EffectActivationEvaluator.AreAllConditionsMet(
                    ability.Effect.effectActivationConditions,
                    attackerCtx ?? BuildOnAttackActivationContext(attackerOwner, attacker)))
            {
                continue;
            }

            return true;
        }

        return false;
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
        // 手札コマンドに加え、場のユニット／搭乗パイロットの【起動・アクション】も含める
        return CollectOnActionSelectableSources(PlayerType.Enemy);
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

            if (!unit.HasBlockerAbility)
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
                .Append(" isBlocker:").Append(ru.HasBlockerAbility)
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
        ClearStaleAttackAndShieldFlagsForEndTurn();
        if (ShouldBlockOnlineLocalPlayDueToOnAction())
        {
            Debug.LogWarning("[OnlineBattle] Stale OnAction wait blocked EndTurn — clearing and retrying.");
            CloseAllOnlineActionStepUi();
        }

        if (isEndTurnFlowRunning || isEnemyMainPhaseCoroutineRunning || IsBattleFlowBlockingTurnProgress())
        {
            Debug.LogWarning(
                $"[OnlineBattle] EndTurn skipped. endTurnRunning:{isEndTurnFlowRunning} "
                + $"liveShield:{IsLiveShieldBreakInProgress()} blocking:{IsBattleFlowBlockingTurnProgress()}");
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

        if (IsLiveShieldBreakInProgress())
        {
            Debug.Log("[OnlineBattle] End Turn ignored — shield break / attack handshake still running.");
            return;
        }

        ClearStaleAttackAndShieldFlagsForEndTurn();
        if (ShouldBlockOnlineLocalPlayDueToOnAction())
        {
            Debug.LogWarning("[OnlineBattle] Clearing stale OnAction wait so EndTurn can proceed.");
            CloseAllOnlineActionStepUi();
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
            || isEndPhaseHandLimitDiscardInProgress
            || _isActionStepCommandResolving
            || _activeResourcePaymentOverlay != null
            || _activeOnActionCommandRevealRoot != null
            || _activeHandDiscardRevealRoot != null
            || isAttackedSidePanelOpen
            || isActionThinkPauseOpen
            || isMulliganPromptOpen
            || isMulliganThinkPauseOpen
            || isOnlineEffectThinkPauseOpen
            || isOnlineDiscardThinkPauseOpen
            || isOnlineOpponentCardConfirmWaitOpen
            || ShouldBlockOnlineLocalPlayDueToOnAction()
            || isOnlineShieldBreakThinkPauseOpen
            || isShieldBreakFlowOpen
            || shieldBreakQueueRunning
            || isShieldAttackResolving
            || attackFlowStrikeKind != AttackFlowStrikeKind.None
            || pendingUnitAttackAttacker != null
            || pendingOnAttackEffectResolvedAttacker != null;
    }

    /// <summary>シールド破壊の握手・バースト解決が実処理中か。残留フラグとは区別する。</summary>
    private bool IsLiveShieldBreakInProgress()
    {
        return isShieldAttackResolving
            || _pendingOnlineShieldBreakRequestId > 0
            || isOnlineShieldBreakThinkPauseOpen
            || shieldBreakQueueRunning
            || pendingShieldBreakBatches.Count > 0;
    }

    /// <summary>
    /// 攻撃／シールド破壊が終わったあとに残ったフラグだけ消す。
    /// 進行中の握手は絶対に切らない（切ると End 不能とフリーズになる）。
    /// </summary>
    private void ClearStaleAttackAndShieldFlagsForEndTurn()
    {
        if (IsLiveShieldBreakInProgress())
        {
            return;
        }

        if (attackFlowStrikeKind != AttackFlowStrikeKind.None
            || pendingUnitAttackAttacker != null
            || pendingOnAttackEffectResolvedAttacker != null
            || attackFlowAttackerUnit != null
            || attackFlowDeclaredDefenderUnit != null)
        {
            Debug.LogWarning("[Battle] Clearing leftover attack-flow flags before End Turn.");
        }

        pendingUnitAttackAttacker = null;
        pendingOnAttackEffectResolvedAttacker = null;
        // ターン終了 OnAction に前回攻撃の攻防ペアが残ると GD06-001 被攻撃ドローが誤発火する。
        ClearAttackFlowContext();

        if (isShieldBreakFlowOpen)
        {
            ForceClearStaleShieldBreakStateForTurnProgress();
        }
    }

    private IEnumerator WaitForBattleFlowIdleCoroutine()
    {
        while (IsBattleFlowBlockingTurnProgress())
        {
            yield return null;
        }
    }

    /// <summary>
    /// ターン終了直前の Idle 待ち。OnAction 相手待ちの残留などで永久停止しないよう上限を設ける。
    /// </summary>
    private IEnumerator WaitForBattleFlowIdleBeforeEndTurnCoroutine()
    {
        const float idleTimeoutSeconds = 8f;
        float deadline = Time.realtimeSinceStartup + idleTimeoutSeconds;
        while (IsBattleFlowBlockingTurnProgress() && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }

        if (!IsBattleFlowBlockingTurnProgress())
        {
            yield break;
        }

        Debug.LogWarning(
            $"[OnlineBattle] Battle flow still blocking before end turn after {idleTimeoutSeconds}s — clear stale waits.");
        if (ShouldBlockOnlineLocalPlayDueToOnAction())
        {
            CloseAllOnlineActionStepUi();
        }

        // それでも攻撃フロー等で残る場合はこれ以上待たずターン終了へ進む。
        float softDeadline = Time.realtimeSinceStartup + 2f;
        while (IsBattleFlowBlockingTurnProgress() && Time.realtimeSinceStartup < softDeadline)
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
        try
        {
            yield return WaitForShieldBreakFlowCompleteCoroutine(8f);
            if (IsLiveShieldBreakInProgress())
            {
                Debug.LogWarning("[OnlineBattle] End turn aborted — shield break handshake still running.");
                yield break;
            }

            ClearStaleAttackAndShieldFlagsForEndTurn();
            // 前回の OnAction 待ちフラグ残留で Idle 待ちが永久化しないよう、タイムアウト付きで待つ。
            yield return WaitForBattleFlowIdleBeforeEndTurnCoroutine();
            pendingUnitAttackAttacker = null;
            pendingOnAttackEffectResolvedAttacker = null;
            PlayerType endingTurnSide = currentPlayerType;
            bool waitingForClose = false;
            bool startedOnActionStep = TryRunTurnEndOnActionPhases(endingTurnSide, () => waitingForClose = false);
            if (startedOnActionStep)
            {
                waitingForClose = true;
                const float turnEndActionStepTimeoutSeconds = 45f;
                float actionStepDeadline = Time.realtimeSinceStartup + turnEndActionStepTimeoutSeconds;
                yield return new WaitUntil(() =>
                    !waitingForClose || Time.realtimeSinceStartup >= actionStepDeadline);
                if (waitingForClose)
                {
                    ForceCompleteActionStepSessionForTimeout(
                        $"turn end action step exceeded {turnEndActionStepTimeoutSeconds}s");
                    waitingForClose = false;
                }
            }

            ApplyTurnEndRepairForAllInPlayUnits();
            ClearTemporaryTurnEndRepairBonusesForAllInPlay();
            // アクション → リペアのあと、手札上限（10枚）まで捨てる（捨て札は相手に公開）
            yield return CoEnforceEndPhaseHandSizeLimit(endingTurnSide);
            TriggerAllTimedEffectsForSide(endingTurnSide, EffectTiming.OnTurnEnd);
            // ターン終了時は盤面全体の「ターン終了で切れる補正」を解除する。
            ClearTimedStatModifiersForAllInPlayCards(EffectDuration.UntilEndOfTurn);
            ClearAttackActiveEnemyGrants(EffectDuration.UntilEndOfTurn);
            ClearNotDirectAttackGrants(EffectDuration.UntilEndOfTurn);
            ClearCannotBeChosenAsAttackGrants(EffectDuration.UntilEndOfTurn);
            ClearFirstStrikeGrants(EffectDuration.UntilEndOfTurn);
            ClearHighMobilityUntilEndOfTurnGrantsForAllInPlayUnits();
            ClearBattleDamageImmunityUntilEndOfTurnGrantsForAllInPlayUnits();
            ClearBreachUntilEndOfTurnGrantsForAllInPlayUnits();
            ClearSuppressUntilEndOfTurnGrantsForAllInPlayUnits();
            ClearCopiedKeywordsUntilEndOfTurnForAllInPlayUnits();
            ClearPreventAllyDestroyByEnemyEffectUntilEot();
            ClearShieldAreaEnemyEffectDamageReduction(OpponentSide(endingTurnSide));
            ClearOwnerSpecialMoveCommandActivatedThisTurn(endingTurnSide);
            ClearOwnerActivatedResourceByEffectThisTurn(endingTurnSide);
            ClearAllOwnerDrewWithEffectThisTurn();
            ClearObservedUnitWatches();
            ForceClearEffectChainObservationScope();
            DumpTurnResourceUsageLogs(endingTurnSide, "end turn");
            NotifyLocalPlayerEndedTurn();

            // プレイヤーとエネミーのターンを切り替える
            currentPlayerType = (currentPlayerType == PlayerType.Player) ? PlayerType.Enemy : PlayerType.Player;
            if (currentPlayerType != PlayerType.Player)
            {
                _onlineLocalTurnSequenceStarted = false;
            }
            if (IsTestPlayBattle())
            {
                ApplyTestPlayBoardPerspective(currentPlayerType);
            }

            ExpireUntilEndOfBattleModifiersAtTurnStart();
            RefreshAllFieldOwnerTurnPassives();
            AdvanceRuleToNextTurnStart();
            UpdateEndTurnButtonVisibility();

            Debug.Log("エンドフェイズの処理を実行します。");
            ChangePhase(BattlePhase.StartTurn);
        }
        finally
        {
            isEndTurnFlowRunning = false;
        }
    }

    // Update is called once per frame
    void Update()
    {
        SyncLiveHandCountDisplays();
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

        // 未登録 UI の破棄はシールド破壊コミット後専用。通常 Sync では枚数合わせのみ行う
        // （破壊処理中のカードやバースト配備元を誤って消さない）。
        gundamRule.SyncShieldCountFromZone(side, rule.GetShieldZoneCardCount());
    }

    private void SyncResourceViewsFromRule(Gundam2024RuleScript.PlayerSide side)
    {
        CardGameRule targetRule = side == Gundam2024RuleScript.PlayerSide.Player ? cardGameRule : enemyCardGameRule;
        Gundam2024RuleScript.PlayerState state = side == Gundam2024RuleScript.PlayerSide.Player ? gundamRule.Player : gundamRule.Enemy;
        if (targetRule == null || state == null)
        {
            return;
        }

        // 相手盤 180° 時もレスト向きがずれないよう、毎回フリップ状態を取り直してから描画する
        targetRule.RefreshResourceBoardFlipState();
        targetRule.ApplyExternalResourceState(state.TotalLevel, state.resource, state.exResource);
        ReconcileShieldStateWithZone(side);
        int shieldDisplayCount = targetRule.GetShieldZoneCardCount();
        targetRule.SetShieldCountDisplay(shieldDisplayCount);
        SyncBaseZoneHeaderDisplay(side);
        targetRule.RefreshHandCountDisplay();

        if (side == Gundam2024RuleScript.PlayerSide.Player)
        {
            if (PlayerlevelText != null)
            {
                PlayerlevelText.text = $"LV:{state.TotalLevel}";
            }

            if (PlayerresourcePointText != null)
            {
                PlayerresourcePointText.text = state.resource.ToString();
            }

            if (ExresourcePointText != null)
            {
                ExresourcePointText.text = state.exResource.ToString();
            }
        }
    }

    /// <summary>
    /// リソース支払い・戻し・配置など state 変更後の共通処理。
    /// ローカルUIを即更新し、自分側ならオンライン相手へ残リソースを送る（トークンレスト表示）。
    /// </summary>
    private void AfterLocalResourceChanged(Gundam2024RuleScript.PlayerSide side)
    {
        SyncResourceViewsFromRule(side);
        if (side == Gundam2024RuleScript.PlayerSide.Player)
        {
            NotifyLocalPlayerResourceSnapshotAfterCost();
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
    /// amount が正なら増加、負なら減少。どのカード効果からも呼べる。
    /// </summary>
    public void ApplyCardEffectExResourceDelta(PlayerType target, int amount)
    {
        if (amount == 0 || gundamRule == null)
        {
            return;
        }

        Gundam2024RuleScript.PlayerSide side = ToRuleSide(target);
        gundamRule.AddExResource(side, amount);
        SyncResourceViewsFromRule(side);
        NotifyLocalExResourceDeltaIfNeeded(target, amount);
        Debug.Log($"[Effect] AddExResource delta:{amount} target:{target} side:{side}");

        // 効果で EX を減らす場合は「除外」として監視を保留（呼び出し側または遅延 Flush で解決）
        if (amount < 0)
        {
            EnqueueExResourceRemoved(target, -amount);
            StartCoroutine(FlushPendingExResourceRemovedAndApReducedWatchesCoroutine());
        }
    }

    /// <summary>
    /// <see cref="EffectType.AddExResource"/> を解決する。
    /// value が 0 以下なら 1 枚追加。target で付与先プレイヤーを決める。
    /// </summary>
    private void ApplyAddExResourceEffect(
        CardController sourceCard,
        PlayerType ownerType,
        EffectData effect)
    {
        if (effect == null)
        {
            return;
        }

        int amount = ResolveEffectMagnitude(effect, ownerType, sourceCard);
        if (amount <= 0)
        {
            amount = 1;
        }

        PlayerType targetPlayer = ResolveAddExResourceTargetPlayer(ownerType, effect.target);
        Gundam2024RuleScript.PlayerState beforeState = targetPlayer == PlayerType.Player
            ? gundamRule.Player
            : gundamRule.Enemy;
        int beforeEx = beforeState != null ? beforeState.exResource : 0;
        ApplyCardEffectExResourceDelta(targetPlayer, amount);
        Gundam2024RuleScript.PlayerState afterState = targetPlayer == PlayerType.Player
            ? gundamRule.Player
            : gundamRule.Enemy;
        int afterEx = afterState != null ? afterState.exResource : 0;
        int placed = afterEx - beforeEx;
        if (placed > 0)
        {
            EnqueueExResourcePlaced(targetPlayer, placed);
            StartCoroutine(FlushPendingExResourcePlacedWatchesCoroutine());
        }

        Debug.Log(
            $"[Effect] AddExResource requested:{amount} actual:+{afterEx - beforeEx} "
            + $"(before:{beforeEx} after:{afterEx} max:{gundamRule.MaxExResource}) "
            + $"to:{targetPlayer} by cardId:{sourceCard?.Data?.id} owner:{ownerType}");
    }

    private static PlayerType ResolveAddExResourceTargetPlayer(PlayerType ownerType, TargetType target)
    {
        if (target == TargetType.EnemyPlayer)
        {
            return ownerType == PlayerType.Player ? PlayerType.Enemy : PlayerType.Player;
        }

        // SelfPlayer / Self / 未指定などは効果オーナー側
        return ownerType;
    }

    /// <summary>
    /// <see cref="EffectType.DeployExBase"/>：自分のシールドエリアに EXベースを1つ配備する。
    /// ベース枠に既存のベース（カード／EX）がある場合はルール上1枚制限のため先に破壊／除去し、
    /// その後ポイントを上限（既定3）まで満たす。
    /// </summary>
    private void ApplyDeployExBaseEffect(
        CardController sourceCard,
        PlayerType ownerType,
        EffectData effect)
    {
        if (effect == null || gundamRule == null)
        {
            return;
        }

        PlayerType targetPlayer = ResolveAddExResourceTargetPlayer(ownerType, effect.target);
        Gundam2024RuleScript.PlayerSide side = ToRuleSide(targetPlayer);
        Gundam2024RuleScript.PlayerState state = GetRuleState(side);
        if (state == null)
        {
            return;
        }

        CardGameRule ownerRule = GetCardRuleForRuleSide(side);
        bool replacedBaseLayer = HadActiveBaseLayerBeforeDeploy(targetPlayer, ownerRule);
        // 出資者等：既存ベースカードがあればトラッシュし、EX 数値もいったんクリアしてから配備する
        DestroyExistingBaseLayerBeforeDeploy(targetPlayer, ownerRule, incomingCard: null);

        int cap = ResolveEffectMagnitude(effect, ownerType, sourceCard);
        if (cap <= 0)
        {
            cap = exBaseData != null && exBaseData.startingPoints > 0
                ? exBaseData.startingPoints
                : 3;
        }

        int before = state.exBase;
        // 既存ベース除去後は新規 EX ベースとして上限まで満たす（加算ではない）
        int after = Mathf.Clamp(cap, 0, cap);
        gundamRule.SetExBasePoints(side, after);
        SyncBaseZoneHeaderDisplay(side);
        NotifyLocalDeployExBaseSynced(targetPlayer);

        Debug.Log(
            $"[Effect] DeployExBase fill-to-cap:{cap} exBase:{before}->{after} to:{targetPlayer} "
            + $"replacedBase:{replacedBaseLayer} by cardId:{sourceCard?.Data?.id} owner:{ownerType}");
    }

    /// <summary>SendCardToTrash の非同期パイプライン（OnDestroyed / Look UI 等）が未完了の件数。</summary>
    private int _pendingSendToTrashPipelines;

    /// <summary>
    /// アクションタイミングのコマンド了承 UI 表示中は破壊時効果（Look／手札回収等）を保留する。
    /// OK 押下でホールド解除 → 保留分をフラッシュする（レイテンシー）。
    /// </summary>
    private int _onDestroyedLatencyHoldCount;

    private readonly List<System.Action> _deferredOnDestroyedResolutions = new List<System.Action>();

    /// <summary>
    /// 破壊時 Look・コマンド確認など、プレイヤー選択 UI が開いている／トラッシュ解決中か。
    /// この間は非選択側の進行を止める。
    /// </summary>
    private bool HasBlockingChoiceOrTrashUi()
    {
        // isOnActionPopupOpen はコマンド確認中も true になるため、Wait ではトラッシュ／Look／公開 UI だけ見る
        // ただしシールド破壊公開・バースト Yes/No は攻撃再開より必ず先に完了させる
        return _pendingSendToTrashPipelines > 0
            || _deferredOnDestroyedResolutions.Count > 0
            || HasPendingRemoteOnDestroyedResolution
            || _pendingOnlineOpponentUnitPickRequestId > 0
            || _unitPilotEffectOrderUiOpen
            || _effectChoiceUiOpen
            || _battleZoneCapUiOpen
            || isOnlineDiscardThinkPauseOpen
            || _activeLookDeckPopupRoot != null
            || _activeHandDiscardRevealRoot != null
            || _activeOnActionCommandRevealRoot != null
            || IsShieldBreakBurstUiPending()
            || _apReducedWatchResolving
            || _effectDamageTakenWatchResolving
            || _argamaAfterDamageStepResolving;
    }

    private IEnumerator WaitUntilBlockingChoiceOrTrashUiCleared(float timeoutSeconds = -1f)
    {
        // SendCardToTrash が同フレームで同期完了する場合も、Look 開始を1フレ待つ
        yield return null;
        if (timeoutSeconds <= 0f)
        {
            yield return new WaitUntil(() => !HasBlockingChoiceOrTrashUi());
            yield break;
        }

        float deadline = Time.realtimeSinceStartup + timeoutSeconds;
        while (HasBlockingChoiceOrTrashUi() && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }

        if (HasBlockingChoiceOrTrashUi())
        {
            Debug.LogWarning(
                $"[Battle] Blocking choice/trash UI still open after {timeoutSeconds}s — continuing attack flow.");
        }
    }

    private void BeginOnDestroyedLatencyHold()
    {
        _onDestroyedLatencyHoldCount++;
    }

    private void EndOnDestroyedLatencyHold()
    {
        if (_onDestroyedLatencyHoldCount <= 0)
        {
            return;
        }

        _onDestroyedLatencyHoldCount--;
        if (_onDestroyedLatencyHoldCount == 0)
        {
            FlushDeferredOnDestroyedResolutions();
        }
    }

    private void FlushDeferredOnDestroyedResolutions()
    {
        if (_deferredOnDestroyedResolutions.Count == 0)
        {
            return;
        }

        List<System.Action> pending = new List<System.Action>(_deferredOnDestroyedResolutions);
        _deferredOnDestroyedResolutions.Clear();
        Debug.Log($"[OnDestroyed][Latency] flush deferred resolutions count:{pending.Count}");
        for (int i = 0; i < pending.Count; i++)
        {
            pending[i]?.Invoke();
        }
    }

    /// <summary>
    /// コマンド了承 OK 待ち中は破壊時効果を保留し、OK 後に Look／回収を実行する。
    /// </summary>
    private void RunOrDeferOnDestroyedEffects(
        CardController cardController,
        PlayerType ownerType,
        System.Action onComplete)
    {
        if (ShouldSkipAutomaticEffectsInTestPlay())
        {
            onComplete?.Invoke();
            return;
        }

        if (_onDestroyedLatencyHoldCount > 0)
        {
            string cardName = cardController != null && cardController.Data != null
                ? cardController.Data.cardName
                : "(null)";
            Debug.Log(
                $"[OnDestroyed][Latency] defer until command ack OK: {cardName} hold:{_onDestroyedLatencyHoldCount}");
            _deferredOnDestroyedResolutions.Add(
                () => TriggerOnDestroyedEffects(cardController, ownerType, onComplete, destroyedBy: null));
            return;
        }

        TriggerOnDestroyedEffects(cardController, ownerType, onComplete);
    }

    /// <summary>ユニット破壊時、搭乗パイロットと双方に効果があれば順番選択してから解決する。</summary>
    private void RunOrDeferUnitAndPilotOnDestroyedEffects(
        CardController unit,
        CardController detachedPilot,
        PlayerType ownerType,
        System.Action onComplete,
        CardController destroyedBy = null,
        bool destroyedUnitWasLinked = false)
    {
        if (_onDestroyedLatencyHoldCount > 0)
        {
            string cardName = unit != null && unit.Data != null ? unit.Data.cardName : "(null)";
            Debug.Log(
                $"[OnDestroyed][Latency] defer unit+pilot until command ack OK: {cardName} "
                + $"hold:{_onDestroyedLatencyHoldCount}");
            _deferredOnDestroyedResolutions.Add(
                () => TriggerUnitAndPilotOnDestroyedEffects(
                    unit,
                    detachedPilot,
                    ownerType,
                    onComplete,
                    destroyedBy,
                    destroyedUnitWasLinked));
            return;
        }

        TriggerUnitAndPilotOnDestroyedEffects(
            unit,
            detachedPilot,
            ownerType,
            onComplete,
            destroyedBy,
            destroyedUnitWasLinked);
    }

    private void TriggerUnitAndPilotOnDestroyedEffects(
        CardController unit,
        CardController detachedPilot,
        PlayerType ownerType,
        System.Action onComplete,
        CardController destroyedBy = null,
        bool destroyedUnitWasLinked = false)
    {
        List<TimedEffectData> unitBlocks = CollectOnDestroyedTimedBlocks(
            unit,
            ownerType,
            destroyedBy,
            detachedPilot,
            destroyedHostUnit: null,
            destroyedUnitWasLinkedHint: destroyedUnitWasLinked);
        List<TimedEffectData> pilotBlocks = CollectOnDestroyedTimedBlocks(
            detachedPilot,
            ownerType,
            destroyedBy,
            detachedMountedPilot: null,
            destroyedHostUnit: unit,
            destroyedUnitWasLinkedHint: destroyedUnitWasLinked);

        if (unitBlocks.Count == 0 && pilotBlocks.Count == 0)
        {
            onComplete?.Invoke();
            return;
        }

        ResolveUnitPilotEffectOrder(
            ownerType,
            unit,
            detachedPilot,
            unitBlocks,
            pilotBlocks,
            unit != null ? unit.Data : null,
            ordered =>
            {
                if (ordered == null || ordered.Count == 0)
                {
                    onComplete?.Invoke();
                    return;
                }

                RunOrderedOnDestroyedEntries(ownerType, ordered, 0, onComplete);
            });
    }

    private void RunOrderedOnDestroyedEntries(
        PlayerType ownerType,
        List<UnitPilotEffectOrderEntry> ordered,
        int index,
        System.Action onComplete)
    {
        if (ordered == null || index >= ordered.Count)
        {
            onComplete?.Invoke();
            return;
        }

        UnitPilotEffectOrderEntry entry = ordered[index];
        if (entry == null || entry.Source == null || entry.Blocks == null || entry.Blocks.Count == 0)
        {
            RunOrderedOnDestroyedEntries(ownerType, ordered, index + 1, onComplete);
            return;
        }

        Debug.Log(
            $"[OnDestroyed] 順番解決 #{index + 1}: "
            + $"{(entry.Source.Data != null ? entry.Source.Data.cardName : "?")} "
            + $"blocks:{entry.Blocks.Count}");
        RunOnDestroyedTimedBlocks(
            entry.Source,
            ownerType,
            entry.Blocks,
            0,
            () => RunOrderedOnDestroyedEntries(ownerType, ordered, index + 1, onComplete));
    }

    private void SendCardToTrash(
        CardController cardController,
        PlayerType ownerType,
        CardController destroyedBy = null,
        bool destroyedByBattleDamage = false,
        bool destroyedByEffectDamage = false)
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
        _pendingSendToTrashPipelines++;

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

        CardController detachedPilot = null;
        bool destroyedUnitWasLinked = false;
        if (cardController.Data.IsUnitLike() && cardController.MountedPilot != null)
        {
            // パイロット切り離し前にリンク状態を確定（OnEnemyUnitDestroyed 条件用）
            destroyedUnitWasLinked = UnitLinkExtensions.HasValidLinkPilot(
                cardController.Data,
                cardController.MountedPilot);
            detachedPilot = cardController.DetachMountedPilotWithoutDestroy();
            _onDestroyedPendingDetachedPilot = detachedPilot;
        }

        // 突破は戦闘ダメージ破壊時のみ（公式 Breach）。配備時効果破壊などでは発動しない。
        if (destroyedByBattleDamage
            && TryResolveEnemyUnitKillContext(
                cardController,
                ownerType,
                destroyedBy,
                out CardController breachKiller,
                out PlayerType breachKillerOwner))
        {
            TryTriggerBreachOnEnemyUnitDestroyed(
                breachKiller,
                breachKillerOwner,
                ownerType,
                destroyedByBattleDamage: true);
        }

        void ContinueAfterOwnerOnDestroyed()
        {
            // 破壊時効果解決後に搭乗パイロットを墓地へ（OnDestroyed は再実行しない）
            if (detachedPilot != null)
            {
                if (IsCardInOwnerHand(detachedPilot, ownerType))
                {
                    _onDestroyedPendingDetachedPilot = null;
                }
                else
                {
                    FinishSendCardToTrash(detachedPilot, ownerType);
                    _onDestroyedPendingDetachedPilot = null;
                }

                detachedPilot = null;
            }

            TriggerOnEnemyUnitDestroyedEffects(
                cardController,
                ownerType,
                destroyedBy,
                destroyedByBattleDamage,
                destroyedByEffectDamage,
                destroyedUnitWasLinked,
                () =>
            {
                TriggerWhileAttackingEffectDamageDestroyWatch(
                    cardController,
                    ownerType,
                    destroyedByEffectDamage,
                    destroyedBy,
                    () =>
                {
                NotifyDeployedBaseOnAllyEnemyUnitDestroyed(
                    cardController,
                    ownerType,
                    destroyedBy,
                    destroyedByBattleDamage,
                    () =>
                {
                NotifyAllyEnemyUnitDestroyedWatch(
                    cardController,
                    ownerType,
                    destroyedBy,
                    destroyedByBattleDamage,
                    destroyedByEffectDamage,
                    () =>
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
                        () => CompleteSendCardToTrashPipeline(cardController, ownerType),
                        destroyedByBattleDamage);
                }
                else
                {
                    CompleteSendCardToTrashPipeline(cardController, ownerType);
                }
                });
                });
                });
            });
        }

        if (ShouldDelegateOnDestroyedToRemoteOwner(ownerType))
        {
            // オンラインの相手所有カードは、選択権を持つ相手クライアントで破壊時効果を解決する。
            ContinueAfterOwnerOnDestroyed();
            return;
        }

        // 自分が破壊時効果を解決するあいだ、相手に effectthink を出して待たせる
        int remoteThinkRequestId = BeginOnlineRemoteEffectThinkForLocalOnDestroyed(
            cardController,
            detachedPilot,
            ownerType);
        RunOrDeferUnitAndPilotOnDestroyedEffects(
            cardController,
            detachedPilot,
            ownerType,
            () =>
            {
                EndOnlineRemoteEffectThinkForLocalOnDestroyed(remoteThinkRequestId);
                ContinueAfterOwnerOnDestroyed();
            },
            destroyedBy,
            destroyedUnitWasLinked);
    }

    private void CompleteSendCardToTrashPipeline(CardController cardController, PlayerType ownerType)
    {
        // OnDestroyed 中に AddSelfToHand 等で既に場から外れている場合は再トラッシュしない
        if (cardController != null && unitsPendingSendToTrash.Contains(cardController))
        {
            FinishSendCardToTrash(cardController, ownerType);
        }

        _pendingSendToTrashPipelines = Mathf.Max(0, _pendingSendToTrashPipelines - 1);
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
            playerHandCards.Add(card.Data);
            NotifyLocalPlayerHandDeckSnapshotAfterHandListChange();
        }
        else
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
        // バースト一時ユニットは手札へ戻す前に印刷タイプ（パイロット等）へ復帰
        unit.TryRestoreFromTemporaryBurstBattleUnit();
        unit.ResetRuntimeStatsFromData();
        unit.CleanupUnitBattleMountVisuals();
        unit.SetAttackFlg(AttackFlg.False);
        unit.SetUnitRestVisual(false);
        unit.RevealShieldFace();

        unit.transform.SetParent(rule.HandScrollContent, false);
        rule.ApplyHandZoneLayoutToCard(unit);

        RegisterCardInHandLists(unit, ownerType);
        TriggerOnHandAutoEffects(unit, ownerType, skipHandZoneCheck: true);
        RefreshAllHandsConditionalOnHandAuto();
        RefreshAllFieldOwnerTurnPassives();
        rule.RefreshHandCountDisplay();
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
        rule.ApplyHandZoneLayoutToCard(card);

        RegisterCardInHandLists(card, ownerType);
        TriggerOnHandAutoEffects(card, ownerType, skipHandZoneCheck: true);
        rule.RefreshHandCountDisplay();
        return true;
    }

    /// <summary>直近の Bounce 適用体数（「そうしたなら」後続判定用）。</summary>
    private int _lastBounceAppliedCount;

    private void ApplyBounceEffect(EffectData effect, List<CardController> targets)
    {
        _lastBounceAppliedCount = 0;
        Debug.Log($"[Effect] Bounce applied:{effect.value} target:{effect.target}");
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

            // 攻撃フロー参加者の除去は場から外す前に通知する
            NotifyAttackFlowParticipantRemovedDuringOnAction(target);
            // オンライン同期は場から外す前にキュー（zoneIndex / instanceId を保持するため）。
            QueueOnlineUnitBounce(target);
            // バトルゾーンから手札へ戻すおそらく、発動側の処理。
            if (TryReturnBattleUnitToHand(target))
            {
                applied++;
            }
        }

        _lastBounceAppliedCount = applied;
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

        int limit = ResolveRestEffectApplyLimit(effect, targets.Count);
        int applied = 0;
        for (int i = 0; i < targets.Count && applied < limit; i++)
        {
            CardController liveTarget = ResolveLiveBattleZoneUnit(targets[i]);
            if (liveTarget == null
                || liveTarget.Data == null
                || !liveTarget.Data.IsUnitLike()
                || !IsCardOnBattleZone(liveTarget))
            {
                continue;
            }

            if (TryApplyRestToUnit(liveTarget))
            {
                QueueOnlineUnitRest(liveTarget);
                applied++;
            }
        }

        if (applied > 0)
        {
            Debug.Log($"[Effect] Rest applied:{applied} target:{effect.target}");
        }
    }

    /// <summary>
    /// Rest の適用体数上限。複数手動選択時は選んだ体数すべて REST（value は単体選択時の上限）。
    /// </summary>
    private static int ResolveRestEffectApplyLimit(EffectData effect, int selectedCount)
    {
        if (effect == null || selectedCount <= 0)
        {
            return 0;
        }

        if (effect.selectionMode.IsMultipleUnitPickMode())
        {
            return selectedCount;
        }

        return effect.value > 0 ? Mathf.Min(effect.value, selectedCount) : selectedCount;
    }

    /// <summary>手動選択 UI 後もバトルゾーン上の現行インスタンスへ解決する。</summary>
    private CardController ResolveLiveBattleZoneUnit(CardController unit)
    {
        if (unit == null)
        {
            return null;
        }

        if (IsCardOnBattleZone(unit))
        {
            return unit;
        }

        int instanceId = unit.BattleInstanceId;
        if (instanceId <= 0)
        {
            return unit;
        }

        if (playerBattleZoneCards != null)
        {
            for (int i = 0; i < playerBattleZoneCards.Count; i++)
            {
                CardController candidate = playerBattleZoneCards[i];
                if (candidate != null && candidate.BattleInstanceId == instanceId)
                {
                    return candidate;
                }
            }
        }

        if (enemyBattleZoneCards != null)
        {
            for (int i = 0; i < enemyBattleZoneCards.Count; i++)
            {
                CardController candidate = enemyBattleZoneCards[i];
                if (candidate != null && candidate.BattleInstanceId == instanceId)
                {
                    return candidate;
                }
            }
        }

        return unit;
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
            if (TryPreventEnemyEffectDestroy(target, ownerType))
            {
                continue;
            }

            TryLogAttackBlockCloseCombatTrioDestroy("ApplyDestroyEffect", target, sourceCard);
            NotifyBlockRedirectUnitRemovedDuringAttackFlow(target);
            // OnDestroyed 条件用: 敵キルでなくても破壊効果の発動元ユニットを破壊者として渡す
            CardController destroyedBy = ResolveUnitKillSourceForTrash(sourceCard, target);
            if (destroyedBy == null
                && sourceCard != null
                && sourceCard.Data != null
                && sourceCard.Data.IsUnitLike())
            {
                destroyedBy = sourceCard;
            }

            QueueOnlineUnitDestroy(target, destroyedBy, byCardEffect: true);
            // 場から外れる前に通知する。破壊される監視カード自身（Alpha Azieru 等）を
            // 生存中の効果源として解決できるようにするため、SendCardToTrash より先に呼ぶ。
            // 戦闘ダメージ破壊は通らない。効果ダメージ破壊はダメージ処理側で通知する。
            NotifyOwnerEffectUnitDestroyed(
                ownerType,
                sourceCard,
                CollectEffectDestroyWatchers(),
                targetOwner);
            SendCardToTrash(target, targetOwner, destroyedBy);
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
        if (unit == null || unit.Data == null || unit.CurrentHp <= 0)
        {
            return false;
        }

        // ユニットに加え、ベース（Axis 等の OnMain コスト）も REST 可。
        if (!unit.Data.IsUnitLike() && unit.Data.type != Type.Base)
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
        cardController.SetBattleStatOverlayVisible(true);
    }

    private void SendCardToField(CardController cardController, PlayerType ownerType, CardGameRule ownerRule)
    {
        if (cardController == null || ownerRule == null)
        {
            return;
        }

        // コマンドは場に出さず、効果発動後にトラッシュへ送る（誤配備防止）
        if (cardController.Data != null
            && cardController.Data.IsCommand()
            && !cardController.Data.IsUnitLike())
        {
            Debug.LogWarning(
                $"[SendCardToField] コマンドはフィールド配備しません: "
                + $"{cardController.Data.cardName}(id:{cardController.Data.id})");
            return;
        }

        if (cardController.Data != null
            && cardController.Data.IsUnitLike()
            && !_applyingRemoteBattleAction
            && IsBattleZoneAtCapacity(ownerType))
        {
            Debug.LogWarning(
                $"[SendCardToField] バトルゾーン満杯のため配備拒否: "
                + $"{cardController.Data.cardName} → {ownerType}");
            return;
        }

        if (ownerType == PlayerType.Player)
        {
            RecordEnemyAiObservedPlayerCardPlay(cardController, "DeployUnit");
        }

        cardController.transform.SetParent(ownerRule.PlayerDeployPanel, false);
        if (!ownerRule.TryPlaceUnitInBattleZone(cardController))
        {
            Debug.LogWarning(
                $"[SendCardToField] バトル枠への配置に失敗: "
                + $"{cardController.Data?.cardName} → {ownerType}");
            return;
        }

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
            cardController.SetDeployedFromTrash(false);
        }

        StartCoroutine(TriggerOnPlayedEffectsAfterDeployCoroutine(cardController, ownerType));

        ownerRule.RefreshHandCountDisplay();
        if (ownerType == PlayerType.Player)
        {
            NotifyLocalPlayCardDeployed(cardController);
            NotifyLocalPlayerHandDeckSnapshot();
        }
    }

    /// <summary>手札 UI 閉鎖後に OnPlayed を解決（選択 UI が確実に表示されるよう1フレーム遅延）。</summary>
    private IEnumerator TriggerOnPlayedEffectsAfterDeployCoroutine(CardController sourceCard, PlayerType ownerType)
    {
        yield return null;
        bool finished = false;
        TriggerOnPlayedEffects(sourceCard, ownerType, () => finished = true);
        yield return new WaitUntil(() => finished);

        bool watchFinished = false;
        NotifyAllyUnitDeployed(ownerType, sourceCard, () => watchFinished = true);
        yield return new WaitUntil(() => watchFinished);

        // 配備コストで EX を使っていた場合、カード解決後にキャリバーン等の監視を解決
        yield return FlushPendingExResourceRemovedAndApReducedWatchesCoroutine();

        RefreshAllFieldOwnerTurnPassives();
        RefreshAllHandsConditionalOnHandAuto();
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

    /// <summary>コスト0（Corin Nander の Lv5〔ディアナ・カウンター〕減免等）は支払い UI を出さず搭乗先選択へ進む。</summary>
    private void BeginPilotMountPaymentOrTargets(
        GameObject filterPanel,
        CardController pilotCard,
        PlayerType ownerType,
        Gundam2024RuleScript.PlayerSide ownerSide,
        int cost)
    {
        if (cost <= 0)
        {
            ShowPilotMountTargetButtons(filterPanel, pilotCard, ownerType, ownerSide, 0, exToUse: 0);
            return;
        }

        ShowPilotMountResourcePaymentThenTargets(filterPanel, pilotCard, ownerType, ownerSide, cost);
    }

    private void ShowPilotMountResourcePaymentThenTargets(
        GameObject filterPanel,
        CardController pilotCard,
        PlayerType ownerType,
        Gundam2024RuleScript.PlayerSide ownerSide,
        int cost)
    {
        if (filterPanel == null || pilotCard == null)
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

        EmbedResourcePaymentUI(
            filterPanel,
            ownerType,
            cost,
            pilotCard.CurrentLevel,
            anchorY: -10f,
            exToUse =>
            {
                ClearEmbeddedResourcePaymentUi(filterPanel);
                ShowPilotMountTargetButtons(filterPanel, pilotCard, ownerType, ownerSide, cost, exToUse);
            },
            () => DestroyCardFilterOverlay(filterPanel));
    }

    private void ShowPilotMountExConfirmThenTargets(
        GameObject filterPanel,
        CardController pilotCard,
        PlayerType ownerType,
        Gundam2024RuleScript.PlayerSide ownerSide,
        int cost,
        int requiredEx)
    {
        // 互換: 旧 Yes/No EX 確認は支払い UI に統合
        _ = requiredEx;
        ShowPilotMountResourcePaymentThenTargets(filterPanel, pilotCard, ownerType, ownerSide, cost);
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

        List<CardController> targets = CardPairFromHandCost.FilterMountTargets(
            pilotCard,
            GetMountableUnits(ownerType),
            cost);
        if (targets.Count == 0)
        {
            Debug.Log("搭乗可能なユニットがありません。");
            return;
        }

        TextMeshProUGUI title = filterPanel.CreateChildTextCustom("PilotTargetTitle", UIAnchor.TopCenter, 460, 40);
        title.SetLocalizedText("搭乗先ユニットを選択", "Select Unit to mount");
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
                if (!TryPayHandDeployCost(ownerSide, pilotCard, exToUse, costOverride: cost))
                {
                    Debug.Log("リソース不足でパイロットを搭乗できません。");
                    return;
                }

                if (ownerType == PlayerType.Player)
                {
                    playerHandCards.Remove(pilotCard.Data);
                    NotifyLocalPlayerHandDeckSnapshotAfterHandListChange();
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

                ApplyOnPilotMountedAllyBuffsDirect(target, pilotCard, ownerType);

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
                TryGrantOperationMeteorFirstStrikeOnPilotMount(target, pilotCard, ownerType);
                SyncResourceViewsFromRule(ownerSide);
                // 搭乗先決定 UI を即閉じ、クリックが敵選択 UI に漏れないようにする
                DestroyCardFilterOverlay(filterPanel);
                StartCoroutine(CoResolvePilotMountChainAfterAttachCoroutine(
                    target,
                    pilotCard,
                    ownerType,
                    ownerSide));
            });
        }
    }

    /// <summary>
    /// 搭乗直後の効果チェーン（OnPilotMounted → AllyPilotMount 監視 → OnLink → OnPlayed）を
    /// 手動選択 UI 完了まで待ってからフィルターを閉じる。
    /// </summary>
    private IEnumerator CoResolvePilotMountChainAfterAttachCoroutine(
        CardController hostUnit,
        CardController pilotCard,
        PlayerType ownerType,
        Gundam2024RuleScript.PlayerSide ownerSide)
    {
        yield return null;

        // オンライン：搭乗確認待ちオーバーレイの裏で【セット時】選択 UI が押せないのを防ぐ
        if (IsOnlineBattle() && ownerType == PlayerType.Player && !_applyingRemoteBattleAction)
        {
            yield return CoWaitPendingOnlineOpponentCardConfirmIfNeeded();
        }

        bool pilotMountEffectsDone = false;
        TriggerOnPilotMountedEffects(hostUnit, pilotCard, ownerType, () => pilotMountEffectsDone = true);
        yield return new WaitUntil(() => pilotMountEffectsDone);

        bool postMountChainDone = false;
        TriggerOnLinkEffects(hostUnit, pilotCard, ownerType, () =>
        {
            TriggerOnPlayedEffects(pilotCard, ownerType, () =>
            {
                RefreshAllHandsConditionalOnHandAuto();
                StartCoroutine(FlushPendingExResourceRemovedAndApReducedWatchesCoroutine());
                RefreshAllFieldOwnerTurnPassives();
                postMountChainDone = true;
            });
        });
        yield return new WaitUntil(() => postMountChainDone);
        SyncResourceViewsFromRule(ownerSide);
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
        List<CardController> opponentBattleZone = side == PlayerType.Player
            ? enemyBattleZoneCards
            : playerBattleZoneCards;

        // 相手場のジ・O 等：レスト中の最低 Lv ユニット（同値すべて・UnitToken 含む）はスタートで起こさない
        bool preventLowestRest = CardPreventStartPhaseActiveExtensions
            .HasPreventOpponentStartPhaseActiveLowestRestAbility(opponentBattleZone);
        int lowestRestedLevel = preventLowestRest
            ? CardPreventStartPhaseActiveExtensions.FindLowestRestedUnitLevel(battleZone)
            : -1;

        Debug.Log(
            side == PlayerType.Player
                ? "[TurnStart] プレイヤー：ユニットをアクティブ化、レスト中のベース/シールドを起こす"
                : "[TurnStart] エネミー：ユニットをアクティブ化、レスト中のベース/シールドを起こす");
        if (preventLowestRest && lowestRestedLevel >= 0)
        {
            Debug.Log(
                $"[TurnStart] PreventOpponentStartPhaseActiveLowestRest — skip rested Lv.{lowestRestedLevel}");
        }

        for (int i = 0; i < battleZone.Count; i++)
        {
            CardController c = battleZone[i];
            if (c == null || c.Data == null || !c.Data.IsUnitLike())
            {
                continue;
            }

            if (c.TryConsumeSkipNextStartPhaseActiveGrant() && c.IsRestState)
            {
                Debug.Log(
                    $"[TurnStart] Skip next-start active: {c.Data.cardName}(id:{c.Data.id}) Lv:{c.CurrentLevel}");
                continue;
            }

            if (CardPreventStartPhaseActiveExtensions.ShouldSkipStartPhaseActive(
                    c, lowestRestedLevel, preventLowestRest && lowestRestedLevel >= 0))
            {
                Debug.Log(
                    $"[TurnStart] Skip active: {c.Data.cardName}(id:{c.Data.id}) Lv:{c.CurrentLevel}");
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

        if (cardGameRule != null
            && ((cardGameRule.PlayerDeployPanel != null && cardTransform.IsChildOf(cardGameRule.PlayerDeployPanel))
                || (cardGameRule.HandScrollContent != null && cardTransform.IsChildOf(cardGameRule.HandScrollContent))
                || (cardGameRule.ShieldCardsContent != null && cardTransform.IsChildOf(cardGameRule.ShieldCardsContent))
                || (cardGameRule.BaseSlotContent != null && cardTransform.IsChildOf(cardGameRule.BaseSlotContent))))
        {
            return PlayerType.Player;
        }

        if (enemyCardGameRule != null
            && ((enemyCardGameRule.PlayerDeployPanel != null && cardTransform.IsChildOf(enemyCardGameRule.PlayerDeployPanel))
                || (enemyCardGameRule.HandScrollContent != null && cardTransform.IsChildOf(enemyCardGameRule.HandScrollContent))
                || (enemyCardGameRule.ShieldCardsContent != null && cardTransform.IsChildOf(enemyCardGameRule.ShieldCardsContent))
                || (enemyCardGameRule.BaseSlotContent != null && cardTransform.IsChildOf(enemyCardGameRule.BaseSlotContent))))
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
        FlushPendingPostCombatRecovers();
        CommitBlockerRestIfBlockWasCommitted();
        CommitAttackerRestIfAttackWasDeclared();
        FinishDeferredShieldAttackBlockFlow();
        pendingUnitAttackAttacker = null;
        pendingOnAttackEffectResolvedAttacker = null;
        ResetOnAttackTrashReturnSession();
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
        if (TryAbortRemainingAttackAfterArmedIntervention())
        {
            return true;
        }

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
        ClearShieldAttackDeclarationLayerCache();
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

        // ブロック未解決のまま OnAttack 再開から来た場合はブロック UI をスキップしない。
        // （skip=true だとアクションが先に走り、Sazabi 等の効果がブロックを飛ばす）
        if (attackFlowStrikeKind != AttackFlowStrikeKind.None
            && (attackFlowBlockSelectionResolved || attackFlowPostBlockPassOnActionDone))
        {
            Debug.Log("[AttackFlow] Resume after block resolved — skip re-showing block UI.");
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
        if (TryAbortRemainingAttackAfterArmedIntervention())
        {
            return;
        }

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

        // 宣言済み攻撃の解決では CannotBeChosenAsAttackTarget を見ない。
        // （Action で防御側を保護しても、そのバトルは中断せず処理する）

        ResolveUnitVsUnitCombatStrikePowers(
            attacker,
            attackerOwner,
            defender,
            out int attackerPowerForCombat,
            out int defenderPowerForCombat);

        // ストライク値は上で確定済み。「このバトル中」修飾は交換後（attack finish）まで残し、
        // OnAction の AP+1 等が盤面表示・与ダメージ確認に反映されるようにする。

        int defenderHpBeforeExchange = defender.CurrentHp;
        int attackerHpBeforeExchange = attacker.CurrentHp;

        ApplyUnitVsUnitCombatDamageExchange(
            attacker,
            defender,
            attackerOwner,
            defenderOwner,
            attackerPowerForCombat,
            defenderPowerForCombat);

        FlushPendingPostCombatRecovers();

        int defenderHpAfterExchange = defender.CurrentHp;
        int attackerHpAfterExchange = attacker.CurrentHp;

        TryResolveOnDealtBattleDamageEffects(
            attacker,
            attackerOwner,
            defender,
            attackerPowerForCombat,
            defenderHpBeforeExchange,
            defenderHpAfterExchange);

        defenderHpAfterExchange = defender.CurrentHp;
        attackerHpAfterExchange = attacker.CurrentHp;

        NotifyLocalUnitAttackResolved(
            attacker,
            defender,
            attackerHpAfterExchange,
            defenderHpAfterExchange);

        if (defender.CurrentHp <= 0)
        {
            SendCardToTrash(defender, defenderOwner, attacker, destroyedByBattleDamage: true);
        }

        if (attacker.CurrentHp <= 0)
        {
            SendCardToTrash(attacker, attackerOwner);
        }

        StartCoroutine(CoFinishUnitVsUnitCombatAfterTrashUi(attacker, attackerOwner));
    }

    private IEnumerator CoFinishUnitVsUnitCombatAfterTrashUi(
        CardController attacker,
        PlayerType attackerOwner)
    {
        yield return WaitUntilBlockingChoiceOrTrashUiCleared();

        pendingUnitAttackAttacker = null;
        pendingOnAttackEffectResolvedAttacker = null;
        ClearEndOfBattleCombatModifiers("unit vs unit attack finish");
        DumpTurnResourceUsageLogs(attackerOwner, "unit vs unit attack");
        SyncAllResourceViewsFromRule();
        LogAttackPostBattleFieldCompact(attacker, attackerOwner);
        FinishUnitAttackSessionAndClearFlowContext();
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

    private bool IsValidEnemyUnitAttackTarget(
        CardController attacker,
        CardController target,
        PlayerType attackerOwner,
        bool enforceCannotBeChosenAsAttackTarget = true)
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

        if (!IsUnitAliveOnAnyDeployField(target))
        {
            return false;
        }

        // 新規のアタック先選択時のみ。宣言済みバトルの続行では見ない。
        if (enforceCannotBeChosenAsAttackTarget && DoesUnitPreventBeingChosenAsAttackTarget(target))
        {
            return false;
        }

        List<CardController> forced = CollectForcedEnemyAttackTargets(attackerOwner, attacker);
        if (forced.Count > 0)
        {
            return forced.Contains(target);
        }

        return true;
    }

    /// <summary>アタック先に選べないユニットを候補から除外する。</summary>
    private void RemoveCannotBeChosenAsAttackTargets(List<CardController> targets)
    {
        if (targets == null || targets.Count == 0)
        {
            return;
        }

        for (int i = targets.Count - 1; i >= 0; i--)
        {
            if (DoesUnitPreventBeingChosenAsAttackTarget(targets[i]))
            {
                targets.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// ホスト（本体＋搭乗パイロット）の CannotBeChosenAsAttackTarget が有効か。
    /// 【セット中】・味方〔特徴〕体数などは timed.activationConditions で評価する。
    /// UntilEndOfTurn 付与（ST11-014 等）も対象。
    /// </summary>
    private bool DoesUnitPreventBeingChosenAsAttackTarget(CardController host)
    {
        if (host == null)
        {
            return false;
        }

        if (host.HasCannotBeChosenAsAttackUntilEndOfTurnGrant)
        {
            return true;
        }

        if (host.BattleInstanceId > 0
            && _cannotBeChosenAsAttackUntilEotInstanceIds.Contains(host.BattleInstanceId))
        {
            return true;
        }

        var abilities = new List<CardCannotBeChosenAsAttackExtensions.CannotBeChosenAsAttackAbility>(2);
        CardCannotBeChosenAsAttackExtensions.CollectCannotBeChosenAsAttackAbilities(host, abilities);
        if (abilities.Count == 0)
        {
            return false;
        }

        PlayerType hostOwner = ResolveCardOwner(host.transform);
        EffectActivationContext hostCtx = BuildOnAttackActivationContext(hostOwner, host);

        for (int i = 0; i < abilities.Count; i++)
        {
            CardCannotBeChosenAsAttackExtensions.CannotBeChosenAsAttackAbility ability = abilities[i];
            if (ability.Effect == null)
            {
                continue;
            }

            if (!EffectActivationEvaluator.AreAllConditionsMet(ability.HostConditions, hostCtx))
            {
                continue;
            }

            return true;
        }

        return false;
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
        if (!IsActingSideForUi(attackerOwner))
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
            CardController attacker = pendingUnitAttackAttacker;
            pendingUnitAttackAttacker = null;
            BeginUnitAttackAfterTargetDeclared(
                attacker,
                clicked,
                attackerOwner,
                defenderOwner);
            return true;
        }

        if (clickedOnAnyField)
        {
            if (clicked != null && DoesUnitPreventBeingChosenAsAttackTarget(clicked))
            {
                Debug.Log("That unit cannot be chosen as an attack target this turn.");
            }
            else
            {
                Debug.Log("Only REST enemy units can be selected as attack targets (unless attacker has AttackActiveEnemyUnit).");
            }

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

        List<CardController> combatCandidates = CollectEnemyUnitAttackCombatCandidates(attackerOwner, attacker);
        combatCandidates = FilterEnemyUnitAttackTargetsByForce(attackerOwner, attacker, combatCandidates);

        List<CardController> selectable = new List<CardController>(combatCandidates.Count);
        List<CardController> blocked = new List<CardController>(2);
        for (int i = 0; i < combatCandidates.Count; i++)
        {
            CardController unit = combatCandidates[i];
            if (unit == null)
            {
                continue;
            }

            if (DoesUnitPreventBeingChosenAsAttackTarget(unit))
            {
                blocked.Add(unit);
            }
            else
            {
                selectable.Add(unit);
            }
        }

        if (selectable.Count == 0 && blocked.Count == 0)
        {
            Debug.Log(HasForcedEnemyAttackTarget(attackerOwner, attacker)
                ? "Forced attack target is not attackable."
                : attacker.HasAttackActiveEnemyAbility()
                    ? "No enemy units to attack."
                    : "No REST enemy units to attack.");
            return;
        }

        List<CardController> displayUnits = BuildUnitPickDisplayList(selectable, blocked);
        ApplyAttackTargetFieldGrayOut(combatCandidates, blocked);

        GameObject root = new GameObject("AttackEnemySelect", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        root.transform.SetParent(canvas.transform, false);
        root.transform.SetAsLastSibling();
        root.SetFullSize();
        Image bg = root.GetComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.5f);
        bg.raycastTarget = true;

        TextMeshProUGUI title = root.CreateChildTextCustom("AttackEnemyTitle", UIAnchor.TopCenter, 620, 48);
        if (selectable.Count == 0)
        {
            title.SetLocalizedText(
                "アタック先に選べるユニットがありません",
                "No unit can be chosen as attack target");
        }
        else if (blocked.Count > 0)
        {
            title.SetLocalizedText(
                "アタック先の敵ユニットを選択（グレーは選択不可）",
                "Select enemy unit to attack (grayed = locked)");
        }
        else if (HasForcedEnemyAttackTarget(attackerOwner, attacker))
        {
            title.text = "Select forced attack target";
        }
        else if (attacker.HasAttackActiveEnemyAbility())
        {
            title.text = "Select enemy unit to attack (REST or ACTIVE)";
        }
        else
        {
            title.text = "Select REST enemy unit to attack";
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
        if (content == null)
        {
            ClearAttackTargetFieldGrayOut(combatCandidates);
            Destroy(root);
            return;
        }

        for (int i = 0; i < displayUnits.Count; i++)
        {
            CardController unit = displayUnits[i];
            if (unit == null || unit.Data == null)
            {
                continue;
            }

            bool isBlocked = IsUnitInList(blocked, unit);
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
            string protectLabel = isBlocked
                ? GameLocale.T(" 選択不可", " Locked")
                : string.Empty;
            statText.text =
                $"AP:{unit.CurrentPower} HP:{unit.CurrentHp} {(unit.IsRestState ? "REST" : "ACTIVE")}{protectLabel}";
            statText.fontSize = 14;
            statText.color = Color.white;
            statText.alignment = TextAlignmentOptions.Center;

            Button btn = cardItem.GetComponent<Button>();
            if (btn == null)
            {
                btn = cardItem.AddComponent<Button>();
            }

            ApplyUnitPickCardGrayedOut(cardItem, isBlocked);
            btn.interactable = !isBlocked;
            if (isBlocked)
            {
                continue;
            }

            CardController selectedUnit = unit;
            btn.onClick.AddListener(() =>
            {
                ClearAttackTargetFieldGrayOut(combatCandidates);
                Destroy(root);

                PlayerType defenderOwner = ResolveCardOwner(selectedUnit.transform);
                pendingUnitAttackAttacker = null;
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
            ClearAttackTargetFieldGrayOut(combatCandidates);
            pendingUnitAttackAttacker = null;
            pendingOnAttackEffectResolvedAttacker = null;
            Destroy(root);
        });
    }

    /// <summary>アタック先選択中、場の保護ユニットを半透明にする。</summary>
    private static void ApplyAttackTargetFieldGrayOut(
        List<CardController> combatCandidates,
        List<CardController> blocked)
    {
        if (combatCandidates == null)
        {
            return;
        }

        for (int i = 0; i < combatCandidates.Count; i++)
        {
            CardController unit = combatCandidates[i];
            if (unit == null)
            {
                continue;
            }

            bool isBlocked = IsUnitInList(blocked, unit);
            ApplyUnitPickCardGrayedOut(unit.gameObject, isBlocked);
        }
    }

    private static void ClearAttackTargetFieldGrayOut(List<CardController> combatCandidates)
    {
        if (combatCandidates == null)
        {
            return;
        }

        for (int i = 0; i < combatCandidates.Count; i++)
        {
            CardController unit = combatCandidates[i];
            if (unit == null)
            {
                continue;
            }

            ApplyUnitPickCardGrayedOut(unit.gameObject, false);
        }
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

    /// <summary>搭乗パイロットとユニット双方に敵対象 OnAttack がある場合の解決順（選択結果）。</summary>
    private List<CardController> _pendingOnAttackEnemyEffectSourceOrder;

    /// <summary>OnAttack 敵対象効果のソース順。プレイヤー選択があればそれを優先。</summary>
    private List<CardController> BuildOnAttackEnemyEffectSources(CardController attacker)
    {
        if (_pendingOnAttackEnemyEffectSourceOrder != null)
        {
            return new List<CardController>(_pendingOnAttackEnemyEffectSourceOrder);
        }

        // フォールバック（AI／片方のみ）: 従来どおりパイロット優先
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

    private List<TimedEffectData> CollectOnAttackEnemyTargetBlocksForSource(
        CardController sourceCard,
        CardController attacker,
        PlayerType attackerOwner)
    {
        List<TimedEffectData> blocks = new List<TimedEffectData>();
        if (sourceCard?.Data?.timedEffects == null || attacker == null)
        {
            return blocks;
        }

        EffectActivationContext activationContext = BuildOnAttackActivationContext(attackerOwner, attacker);
        for (int i = 0; i < sourceCard.Data.timedEffects.Count; i++)
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

            IReadOnlyList<EffectData> resolved = timed.GetResolvedEffects();
            bool relevant = false;
            for (int j = 0; j < resolved.Count; j++)
            {
                EffectData effect = resolved[j];
                if (effect == null)
                {
                    continue;
                }

                if (effect.target.IsOpponentUnitTarget() || effect.type.UsesTargetCountValue())
                {
                    // バトルダメージ付与後解決（Exia Repair 等）は戦闘前チェーンに載せない
                    if (effect.resolveAfterDealtBattleDamage)
                    {
                        continue;
                    }

                    relevant = true;
                    break;
                }
            }

            if (relevant)
            {
                blocks.Add(timed);
            }
        }

        return blocks;
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

        _pendingOnAttackEnemyEffectSourceOrder = null;
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

        bool isStartCursor = cursor.SourceIndex == 0 && cursor.TimedIndex == 0 && cursor.EffectIndex == 0;
        if (isStartCursor && _pendingOnAttackEnemyEffectSourceOrder == null)
        {
            List<TimedEffectData> unitBlocks = CollectOnAttackEnemyTargetBlocksForSource(
                attacker,
                attacker,
                attackerOwner);
            CardController pilot = attacker.MountedPilot;
            List<TimedEffectData> pilotBlocks = CollectOnAttackEnemyTargetBlocksForSource(
                pilot,
                attacker,
                attackerOwner);
            if (unitBlocks.Count > 0 && pilotBlocks.Count > 0)
            {
                ResolveUnitPilotEffectOrder(
                    attackerOwner,
                    attacker,
                    pilot,
                    unitBlocks,
                    pilotBlocks,
                    attacker.Data,
                    ordered =>
                    {
                        if (ordered == null || ordered.Count == 0)
                        {
                            _pendingOnAttackEnemyEffectSourceOrder = null;
                            onResolved?.Invoke();
                            return;
                        }

                        _pendingOnAttackEnemyEffectSourceOrder = new List<CardController>(ordered.Count);
                        for (int i = 0; i < ordered.Count; i++)
                        {
                            if (ordered[i]?.Source != null)
                            {
                                _pendingOnAttackEnemyEffectSourceOrder.Add(ordered[i].Source);
                            }
                        }

                        if (!TryOpenOnAttackEnemySelectionPanelCore(
                            attacker,
                            attackerOwner,
                            attackedTarget,
                            () =>
                            {
                                _pendingOnAttackEnemyEffectSourceOrder = null;
                                onResolved?.Invoke();
                            },
                            default))
                        {
                            _pendingOnAttackEnemyEffectSourceOrder = null;
                            onResolved?.Invoke();
                        }
                    },
                    autoPilotFirst: true);
                return true;
            }
        }

        return TryOpenOnAttackEnemySelectionPanelCore(
            attacker,
            attackerOwner,
            attackedTarget,
            () =>
            {
                if (isStartCursor)
                {
                    _pendingOnAttackEnemyEffectSourceOrder = null;
                }

                onResolved?.Invoke();
            },
            cursor);
    }

    private bool TryOpenOnAttackEnemySelectionPanelCore(
        CardController attacker,
        PlayerType attackerOwner,
        CardController attackedTarget,
        System.Action onResolved,
        OnAttackEnemyEffectCursor cursor)
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

                    // Destroy 等は RequiresManualUnitSelection が true のため、
                    // 攻撃対象限定（Exia Repair 含む）を手動選択より先に処理する。
                    if (effect.selectionMode.IsAttackedTargetOnlyMode())
                    {
                        // バトルダメージ付与後に解決する効果は戦闘前ではスキップする。
                        if (effect.resolveAfterDealtBattleDamage)
                        {
                            continue;
                        }

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
                        FilterTargetsByUnitCondition(singleTarget, effect, sourceCard, attackerOwner);
                        if (singleTarget.Count == 0)
                        {
                            continue;
                        }

                        System.Action applyAttackedTarget = () =>
                            ApplyEffectToSpecificTargets(sourceCard, attackerOwner, effect, singleTarget);
                        if (attackerOwner == PlayerType.Player)
                        {
                            InvokePlayerManualUnitSelectionCallback(applyAttackedTarget);
                        }
                        else
                        {
                            applyAttackedTarget();
                        }
                        continue;
                    }

                    if (EffectRequiresManualUnitSelection(effect))
                    {
                        List<CardController> bounceCandidates = ResolveSelectableEffectTargets(
                            sourceCard,
                            attackerOwner,
                            effect);
                        if (bounceCandidates.Count == 0)
                        {
                            continue;
                        }

                        if (TryResolveOnAttackManualUnitPick(
                            sourceCard,
                            attacker,
                            attackerOwner,
                            effect,
                            bounceCandidates,
                            stepResolved,
                            onResolved,
                            attackedTarget))
                        {
                            return true;
                        }

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

                    var enemyBlocked = new List<CardController>();
                    CollectOnAttackDestroyBlockedUnits(effect, attacker, attackedTarget, enemyBlocked);
                    RemoveUnitsFromList(enemyUnits, enemyBlocked);
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
                        stepResolved,
                        onSkipRemainingEffects: null,
                        chooserSide: PlayerType.Player,
                        blockedUnits: enemyBlocked);
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>OnAttack 手動ユニット選択。相手選択は UI（オンラインは相手クライアント）で行い、Destroy 後は OnDestroyed 完了まで待つ。</summary>
    private bool TryResolveOnAttackManualUnitPick(
        CardController sourceCard,
        CardController attacker,
        PlayerType attackerOwner,
        EffectData effect,
        List<CardController> candidates,
        System.Action stepResolved,
        System.Action onAllComplete = null,
        CardController attackedTarget = null)
    {
        if (sourceCard == null || effect == null || candidates == null)
        {
            return false;
        }

        var blocked = new List<CardController>();
        CollectOnAttackDestroyBlockedUnits(effect, attacker, attackedTarget, blocked);
        List<CardController> selectable = new List<CardController>(candidates);
        RemoveUnitsFromList(selectable, blocked);
        if (selectable.Count == 0)
        {
            return false;
        }

        PlayerType chooser = ResolveOnAttackEffectChooser(attackerOwner, effect);

        // オンライン：相手が選ぶ場合は相手クライアントの UI へ委譲し、自分は effectthink で待つ
        if (chooser == PlayerType.Enemy
            && IsOnlineBattle()
            && !_applyingRemoteBattleAction
            && TryBeginOnlineOpponentUnitPick(
                sourceCard,
                attacker,
                attackerOwner,
                effect,
                selectable,
                stepResolved,
                onAllComplete,
                blocked))
        {
            return true;
        }

        // ローカル UI（プレイヤー選択、またはオフライン時の相手選択）
        OpenEnemyUnitEffectSelectionUI(
            sourceCard,
            attacker,
            attackerOwner,
            effect,
            selectable,
            stepResolved,
            onAllComplete,
            chooser,
            blocked);
        return true;
    }

    private static PlayerType ResolveOnAttackEffectChooser(PlayerType effectOwner, EffectData effect)
    {
        if (effect != null && effect.opponentChoosesTarget)
        {
            return effectOwner == PlayerType.Player ? PlayerType.Enemy : PlayerType.Player;
        }

        return effectOwner;
    }

    /// <summary>Destroy は OnDestroyed（Look 等）完了まで待ってから次へ。解決側には effectthink を出さない。</summary>
    private void ContinueOnAttackAfterAppliedEffect(
        CardController attackUnit,
        EffectData effect,
        System.Action onResolved)
    {
        bool waitTriggeredWatch = HasPendingLocalApReducedWatch
            || HasPendingLocalEffectDamageTakenWatch
            || _apReducedWatchFlushRunning
            || _effectDamageTakenWatchFlushRunning
            || HasPendingRemoteOnDestroyedResolution;
        if ((effect != null && effect.type == EffectType.Destroy) || waitTriggeredWatch)
        {
            StartCoroutine(CoContinueOnAttackAfterDestroyPipeline(attackUnit, onResolved, waitTriggeredWatch));
            return;
        }

        pendingOnAttackEffectResolvedAttacker = attackUnit;
        onResolved?.Invoke();
    }

    private IEnumerator CoContinueOnAttackAfterDestroyPipeline(
        CardController attackUnit,
        System.Action onResolved,
        bool waitApReduced = false)
    {
        // 破壊時 Look／回収、および相手効果による AP 減少割り込みが終わるまで待つ。
        // effectthink は「待たされている側」だけに出す（PrepareOnlineOnDestroyedWait / OpponentUnitPick / EffectThinkWait）。
        if (waitApReduced)
        {
            yield return FlushPendingApReducedByOpponentWatchCoroutine();
            yield return FlushPendingEffectDamageTakenFromOpponentWatchCoroutine();
        }

        yield return WaitUntilBlockingChoiceOrTrashUiCleared();

        if (attackUnit == null
            || !IsCardControllerInstanceValid(attackUnit)
            || attackUnit.Data == null
            || !attackUnit.Data.IsUnitLike()
            || attackUnit.CurrentHp <= 0
            || !IsCardOnBattleZone(attackUnit))
        {
            Debug.Log("[OnAttack] 攻撃ユニットが破壊されたため攻撃を中断します。");
            pendingUnitAttackAttacker = null;
            pendingOnAttackEffectResolvedAttacker = null;
            CancelPendingUnitAttackFlow();
            yield break;
        }

        pendingOnAttackEffectResolvedAttacker = attackUnit;
        onResolved?.Invoke();
    }

    private void OpenEnemyUnitEffectSelectionUI(
        CardController effectSourceCard,
        CardController attackingUnit,
        PlayerType attackerOwner,
        EffectData effect,
        List<CardController> enemyUnits,
        System.Action onResolved = null,
        System.Action onSkipRemainingEffects = null,
        PlayerType chooserSide = PlayerType.Player,
        List<CardController> blockedUnits = null)
    {
        CardController attackUnit = attackingUnit ?? pendingUnitAttackAttacker ?? effectSourceCard;

        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            pendingOnAttackEffectResolvedAttacker = attackUnit;
            onResolved?.Invoke();
            return;
        }

        List<CardController> selectable = enemyUnits != null
            ? new List<CardController>(enemyUnits)
            : new List<CardController>();
        List<CardController> blocked = blockedUnits != null
            ? new List<CardController>(blockedUnits)
            : new List<CardController>();
        RemoveUnitsFromList(selectable, blocked);
        List<CardController> displayUnits = BuildUnitPickDisplayList(selectable, blocked);

        GameObject root = new GameObject("OnAttackEffectSelect", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        activeOnActionPopupRoot = root;
        isOnActionPopupOpen = true;
        root.transform.SetParent(canvas.transform, false);
        root.transform.SetAsLastSibling();
        root.SetFullSize();
        Image bg = root.GetComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.5f);
        bg.raycastTarget = true;

        TextMeshProUGUI title = root.CreateChildTextCustom("EffectSelectTitle", UIAnchor.TopCenter, 720, 48);
        title.text = FormatOnAttackUnitSelectionTitle(effect, chooserSide);
        title.color = Color.white;
        title.fontSize = 22;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -24f);

        GameObject scrollGo = root.CreateGridScrollView(620, 420, UIAnchor.TopCenter);
        RectTransform scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.anchoredPosition = new Vector2(0f, -80f);
        scrollGo.ConfigureGridCellFromViewportHeight(0.78f, 56f);
        ScrollRect sr = scrollGo.GetComponent<ScrollRect>();
        RectTransform content = sr != null ? sr.content : null;

        List<CardController> selected = new List<CardController>();
        for (int i = 0; i < displayUnits.Count; i++)
        {
            CardController unit = displayUnits[i];
            if (content == null || unit == null)
            {
                continue;
            }

            bool isBlocked = IsUnitInList(blocked, unit);
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
            if (isBlocked)
            {
                statText.text =
                    $"AP:{unit.CurrentPower} HP:{unit.CurrentHp} {(unit.IsRestState ? "REST" : "ACTIVE")} | Unavailable";
            }
            else
            {
                string scoreLabel = FormatOnAttackEnemyTargetScoreLabelEnglish(
                    attackingUnit,
                    unit,
                    attackerOwner,
                    effectSourceCard,
                    effect);
                statText.text = string.IsNullOrEmpty(scoreLabel)
                    ? $"AP:{unit.CurrentPower} HP:{unit.CurrentHp} {(unit.IsRestState ? "REST" : "ACTIVE")}"
                    : $"AP:{unit.CurrentPower} HP:{unit.CurrentHp} {(unit.IsRestState ? "REST" : "ACTIVE")} | {scoreLabel}";
            }

            statText.fontSize = 14;
            statText.color = Color.white;
            statText.alignment = TextAlignmentOptions.Center;

            Button btn = cardItem.GetComponent<Button>();
            if (btn == null)
            {
                btn = cardItem.AddComponent<Button>();
            }

            ApplyUnitPickCardGrayedOut(cardItem, isBlocked);
            if (isBlocked)
            {
                if (itemCc != null && unit.Data != null)
                {
                    itemCc.SetUp(unit.Data, _ => { });
                }

                continue;
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
                    System.Action applyPick = () => ApplyEffectToSpecificTargets(
                        effectSourceCard,
                        attackerOwner,
                        effect,
                        new List<CardController> { unit });
                    // 相手選択（chooser=Enemy）でもローカル UI で選んだ結果なので、
                    // Player 効果の手動適用ガードを通すため必ずコールバック経由にする。
                    InvokePlayerManualUnitSelectionCallback(applyPick);
                    Debug.Log(
                        $"[OnAttack] 効果対象を選択 ({effectSourceCard?.Data?.cardName} → {unit.Data?.cardName}) "
                        + $"chooser:{chooserSide}");
                    ReleaseOnActionPopupState(root);
                    Destroy(root);
                    ContinueOnAttackAfterAppliedEffect(attackUnit, effect, onResolved);
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

                System.Action applyPicks = () =>
                {
                    ApplyEffectToSpecificTargets(effectSourceCard, attackerOwner, effect, selected);
                    Debug.Log(
                        $"[OnAttack] 効果対象を複数選択 ({effectSourceCard?.Data?.cardName})。");
                    ReleaseOnActionPopupState(root);
                    Destroy(root);
                    ContinueOnAttackAfterAppliedEffect(attackUnit, effect, onResolved);
                };
                InvokePlayerManualUnitSelectionCallback(applyPicks);
            });
        }

        // 任意効果の Cancel＝効果スキップして攻撃続行。必須の Damage / Destroy は Cancel で攻撃中断しない。
        bool optionalSkip = effect != null
            && effect.optionalPlayerConfirm
            && !effect.opponentChoosesTarget
            && chooserSide == attackerOwner;
        bool showCancel = optionalSkip;

        if (showCancel)
        {
            Button cancel = root.CreateChildButton("Cancel");
            RectTransform cancelRt = cancel.GetComponent<RectTransform>();
            cancelRt.sizeDelta = new Vector2(180f, 46f);
            cancelRt.anchoredPosition = new Vector2(100f, 48f);
            cancel.onClick.AddListener(() =>
            {
                ReleaseOnActionPopupState(root);
                Destroy(root);

                if (optionalSkip)
                {
                    Debug.Log(
                        $"[OnAttack] 任意効果を Cancel（スキップ）: {effectSourceCard?.Data?.cardName} "
                        + $"effect:{effect?.type}");
                    pendingOnAttackEffectResolvedAttacker = attackUnit;
                    if (onSkipRemainingEffects != null)
                    {
                        onSkipRemainingEffects.Invoke();
                    }
                    else
                    {
                        onResolved?.Invoke();
                    }

                    return;
                }

                pendingUnitAttackAttacker = null;
                pendingOnAttackEffectResolvedAttacker = null;
                CancelPendingUnitAttackFlow();
            });
        }
    }

    private static string FormatOnAttackUnitSelectionTitle(EffectData effect, PlayerType chooserSide)
    {
        if (effect == null)
        {
            return "Select effect target unit";
        }

        if (effect.type == EffectType.Damage)
        {
            return "Damage — Choose an enemy Unit";
        }

        if (effect.type == EffectType.Bounce)
        {
            return "Bounce — Choose a Unit to return to hand";
        }

        if (effect.type == EffectType.Rest)
        {
            return "REST — Choose a Unit";
        }

        if (effect.type == EffectType.Activate)
        {
            return effect.filterTargetIsBlocker
                ? "Activate — Choose a Blocker (REST only)"
                : "Activate — Choose a Unit (REST only)";
        }

        if (effect.type == EffectType.Destroy)
        {
            string optionalSuffix = effect.optionalPlayerConfirm && !effect.opponentChoosesTarget
                ? " (Cancel to skip)"
                : string.Empty;

            if (effect.opponentChoosesTarget)
            {
                return chooserSide == PlayerType.Player
                    ? "Destroy — Choose one of your Units (attacked Unit unavailable)"
                    : "Destroy — Opponent chooses a Unit (attacked Unit unavailable)";
            }

            if (effect.target.IsAllyUnitPickTarget())
            {
                return chooserSide == PlayerType.Player
                    ? "Destroy — Choose one of your Units (attacker unavailable)" + optionalSuffix
                    : "Destroy — Choose an opponent Unit" + optionalSuffix;
            }

            return "Destroy — Choose an enemy Unit" + optionalSuffix;
        }

        if (effect.type == EffectType.EffectBattle)
        {
            return effect.opponentChoosesTarget
                ? GameLocale.T(
                    "最後の勝利者 — バトルを行う自分のユニットを選択",
                    "The Final Victor — Choose your Unit for battle")
                : GameLocale.T(
                    "エフェクトバトル — 相手ユニットを選択",
                    "Effect Battle — Choose an enemy Unit");
        }

        if (effect.type == EffectType.ReturnUnitToDeckBottom)
        {
            return "Return to bottom of deck (tokens are destroyed · Lv0)";
        }

        return effect.target == TargetType.RestEnemyUnit
            ? "Select REST enemy unit"
            : "Select effect target unit";
    }

    /// <summary>OnAttack 敵選択 UI：効果ダメージ量などを英語ラベルで返す（Providence 等）。</summary>
    private string FormatOnAttackEnemyTargetScoreLabelEnglish(
        CardController attacker,
        CardController target,
        PlayerType attackerOwner,
        CardController effectSource,
        EffectData effect)
    {
        if (effect == null || effect.type != EffectType.Damage || target == null)
        {
            return string.Empty;
        }

        CardController magnitudeSource = effectSource ?? attacker;
        if (magnitudeSource == null)
        {
            return string.Empty;
        }

        int magnitude = ResolveEffectMagnitude(effect, attackerOwner, magnitudeSource);
        int damage = ResolveEffectDamageAmount(magnitude, target, attackerOwner);
        if (damage > 0)
        {
            damage += ResolveAllyFeatureOutgoingDamageBonus(attackerOwner, magnitudeSource, target);
        }

        if (damage <= 0)
        {
            return "Pts:0";
        }

        bool kill = damage >= target.CurrentHp;
        return kill ? $"Pts:{damage} (Kill)" : $"Pts:{damage}";
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

    private void InvokePlayerManualUnitSelectionCallback(System.Action callback)
    {
        _playerManualUnitSelectionApplyDepth++;
        try
        {
            callback?.Invoke();
        }
        finally
        {
            _playerManualUnitSelectionApplyDepth--;
        }
    }

    /// <summary>
    /// オンライン相手搭乗ミラー等、リモート適用中は AllyPilotMount をローカル解決しない。
    /// （ミラー側 AI 自動選択 → EffectSync で相手盤面へ誤デバフ、を防ぐ）
    /// </summary>
    private bool ShouldRunAllyPilotMountWatchEffects(PlayerType ownerType)
    {
        if (_applyingRemoteBattleAction)
        {
            Debug.Log(
                $"[AllyPilotMountWatch] skip remote mirror (owner:{ownerType})");
            return false;
        }

        return true;
    }

    /// <summary>
    /// 手動ユニット選択 UI を出すべき操作側か（TestPlay 両サイド／ローカル Player）。
    /// ownerType=Enemy のオンライン受信ミラーは false（AI 自動選択禁止）。
    /// </summary>
    private bool RequiresInteractiveManualUnitSelectionUi(PlayerType effectOwnerType)
    {
        if (_applyingRemoteBattleAction)
        {
            return false;
        }

        if (IsTestPlayBattle())
        {
            return IsActingSideForUi(effectOwnerType);
        }

        if (IsOnlineBattle())
        {
            // 相手ターンの防御側誘発（Argama 等）もローカル Player が選ぶ。
            return effectOwnerType == PlayerType.Player;
        }

        return effectOwnerType == PlayerType.Player;
    }

    /// <summary>
    /// プレイヤー操作時に UI なしで ApplyEffectToSpecificTargets してはいけない効果か。
    /// UsePriorChainPickedTarget / 攻撃対象限定の非手動選択タイプのみ自動適用を許可する。
    /// </summary>
    private static bool ShouldBlockPlayerManualEffectWithoutSelectionUi(EffectData effect)
    {
        if (effect == null)
        {
            return false;
        }

        if (effect.selectionMode == EffectSelectionMode.UsePriorChainPickedTarget)
        {
            return false;
        }

        // 攻撃対象限定は戦闘中の相手が自動確定するため選択 UI 不要
        // （selectionMode が明示的に AttackedTargetOnly のカードのみ。ST12-011 は専用経路）
        if (effect.selectionMode.IsAttackedTargetOnlyMode())
        {
            return false;
        }

        return EffectRequiresManualUnitSelection(effect);
    }

    private void ApplyEffectToSpecificTargets(CardController sourceCard, PlayerType ownerType, EffectData effect, List<CardController> targets)
    {
        if (sourceCard?.Data?.id == PoorlyPlannedOffensiveCardId
            && effect != null
            && effect.type == EffectType.Bounce)
        {
            List<CardController> validTargets = new List<CardController>();
            if (targets != null)
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    CardController target = targets[i];
                    if (IsPoorlyPlannedOffensiveTarget(target, ownerType))
                    {
                        validTargets.Add(target);
                    }
                }
            }

            targets = validTargets;
            if (targets.Count == 0)
            {
                _lastBounceAppliedCount = 0;
                Debug.Log("[ST11-013] REST・現在HP3以下の相手ユニットがないため、バウンス不成立。");
                return;
            }
        }

        if (ownerType == PlayerType.Player
            && ShouldBlockPlayerManualEffectWithoutSelectionUi(effect)
            && _playerManualUnitSelectionApplyDepth <= 0)
        {
            Debug.LogWarning(
                $"[Effect] Blocked player manual unit effect without selection UI "
                + $"(type:{effect.type} target:{effect.target} cardId:{sourceCard?.Data?.id} "
                + $"targets:{targets?.Count ?? 0}).");
            return;
        }

        if (IsMagicBulletOfDuskCard(sourceCard?.Data))
        {
            GrantMagicBulletOfDuskFirstStrikeToTargets(targets);
            if (targets != null && targets.Count > 0)
            {
                SetEffectChainLastPickedTargets(targets);
            }

            BeginOnlineEffectSyncBatch(ownerType);
            FlushOnlineEffectSyncBatch();
            SyncAllResourceViewsFromRule();
            return;
        }

        if (IsTheBlueGiantCard(sourceCard?.Data))
        {
            GrantTheBlueGiantImmunityToTargets(targets);
            if (targets != null && targets.Count > 0)
            {
                SetEffectChainLastPickedTargets(targets);
            }

            BeginOnlineEffectSyncBatch(ownerType);
            FlushOnlineEffectSyncBatch();
            SyncAllResourceViewsFromRule();
            return;
        }

        if (effect != null && effect.type == EffectType.AttackActiveEnemyUnit)
        {
            if (effect.IsAttackActiveEnemyAllyGrant())
            {
                ApplyAttackActiveEnemyGrantsToTargets(effect, ownerType, targets);
            }
            else
            {
                TryApplyAttackActiveEnemyUnitMarker(sourceCard, ownerType, effect);
            }

            if (targets != null && targets.Count > 0)
            {
                SetEffectChainLastPickedTargets(targets);
            }

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

        if (effect != null && effect.type == EffectType.CannotBeChosenAsAttackTarget)
        {
            BeginOnlineEffectSyncBatch(ownerType);
            if (TryApplyCannotBeChosenAsAttackTargetMarker(effect, targets))
            {
                SetEffectChainLastPickedTargets(targets);
            }

            FlushOnlineEffectSyncBatch();
            SyncAllResourceViewsFromRule();
            return;
        }

        if (effect != null && effect.type == EffectType.PreventNextStartPhaseActive)
        {
            BeginOnlineEffectSyncBatch(ownerType);
            if (TryApplyPreventNextStartPhaseActiveMarker(effect, targets))
            {
                SetEffectChainLastPickedTargets(targets);
            }

            FlushOnlineEffectSyncBatch();
            SyncAllResourceViewsFromRule();
            return;
        }

        if (TryApplyFirstStrikeMarker(effect, targets))
        {
            SetEffectChainLastPickedTargets(targets);
            BeginOnlineEffectSyncBatch(ownerType);
            FlushOnlineEffectSyncBatch();
            SyncAllResourceViewsFromRule();
            return;
        }

        if (TryApplyHighMobilityMarker(effect, targets))
        {
            SetEffectChainLastPickedTargets(targets);
            BeginOnlineEffectSyncBatch(ownerType);
            FlushOnlineEffectSyncBatch();
            SyncAllResourceViewsFromRule();
            return;
        }

        if (TryApplyBattleDamageImmunityFromLowApEnemyMarker(effect, targets))
        {
            SetEffectChainLastPickedTargets(targets);
            BeginOnlineEffectSyncBatch(ownerType);
            FlushOnlineEffectSyncBatch();
            SyncAllResourceViewsFromRule();
            return;
        }

        if (TryApplyGrantBreachMarker(effect, targets, sourceCard))
        {
            SetEffectChainLastPickedTargets(targets);
            BeginOnlineEffectSyncBatch(ownerType);
            FlushOnlineEffectSyncBatch();
            SyncAllResourceViewsFromRule();
            return;
        }

        if (TryApplyIncomingBattleDamageRedirectMarker(effect, sourceCard, targets, ownerType))
        {
            SetEffectChainLastPickedTargets(targets);
            BeginOnlineEffectSyncBatch(ownerType);
            FlushOnlineEffectSyncBatch();
            SyncAllResourceViewsFromRule();
            return;
        }

        if (TryApplyGrantTurnEndRepair(effect, targets))
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

        if (effect.type == EffectType.PreventAllyDestroyByEnemyEffect)
        {
            BeginOnlineEffectSyncBatch(ownerType);
            GrantPreventAllyDestroyByEnemyEffect(ownerType);
            FlushOnlineEffectSyncBatch();
            SyncAllResourceViewsFromRule();
            Debug.Log(
                $"[Effect] PreventAllyDestroyByEnemyEffect until EOT by cardId:{sourceCard?.Data?.id} owner:{ownerType}");
            return;
        }

        if (effect.type == EffectType.GrantShieldAreaImmunityFromEnemyUnitLevelOrLess)
        {
            int maxLevel = ResolveEffectMagnitude(effect, ownerType, sourceCard);
            if (maxLevel <= 0)
            {
                maxLevel = effect.value > 0 ? effect.value : 4;
            }

            BeginOnlineEffectSyncBatch(ownerType);
            GrantShieldAreaImmunityFromEnemyUnitLevelOrLess(ownerType, maxLevel);
            FlushOnlineEffectSyncBatch();
            SyncAllResourceViewsFromRule();
            return;
        }

        if (effect.type == EffectType.GrantShieldAreaEnemyEffectDamageReduction)
        {
            int reduction = ResolveEffectMagnitude(effect, ownerType, sourceCard);
            if (reduction <= 0)
            {
                reduction = effect.value > 0 ? effect.value : 5;
            }

            BeginOnlineEffectSyncBatch(ownerType);
            GrantShieldAreaEnemyEffectDamageReduction(ownerType, reduction);
            FlushOnlineEffectSyncBatch();
            SyncAllResourceViewsFromRule();
            return;
        }

        if (effect.type == EffectType.CopyKeywordsFromTrashUnit)
        {
            Debug.LogWarning(
                $"[Effect] CopyKeywordsFromTrashUnit は非同期チェーン経由で解決してください (cardId:{sourceCard?.Data?.id})。");
            return;
        }

        int magnitude = ResolveEffectMagnitude(effect, ownerType, sourceCard);
        if (magnitude == 0
            && !effect.type.UsesTargetCountValue()
            && effect.type != EffectType.GrantAttackFlag
            && effect.type != EffectType.MarkObservedUnit
            && effect.type != EffectType.EffectBattle
            && effect.type != EffectType.PreventAllyDestroyByEnemyEffect
            && effect.type != EffectType.CopyKeywordsFromTrashUnit
            && effect.type != EffectType.DeployExBase)
        {
            return;
        }

        bool nestedBatch = _onlineEffectSyncActive;
        if (!nestedBatch)
        {
            BeginOnlineEffectSyncBatch(ownerType);
        }

        if (effect.type == EffectType.Draw)
        {
            CardGameRule rule = ownerType == PlayerType.Player ? cardGameRule : enemyCardGameRule;
            int drawn = 0;
            for (int i = 0; i < magnitude; i++)
            {
                CardAddtoHand(rule, ownerType);
                drawn++;
            }

            NoteEffectDrawResolved(ownerType, drawn);

            if (!nestedBatch)
            {
                FlushOnlineEffectSyncBatch();
            }

            return;
        }

        if (effect.type == EffectType.AddExResource)
        {
            ApplyAddExResourceEffect(sourceCard, ownerType, effect);
            if (!nestedBatch)
            {
                FlushOnlineEffectSyncBatch();
            }

            SyncAllResourceViewsFromRule();
            return;
        }

        if (effect.type == EffectType.DeployExBase)
        {
            ApplyDeployExBaseEffect(sourceCard, ownerType, effect);
            if (!nestedBatch)
            {
                FlushOnlineEffectSyncBatch();
            }

            SyncAllResourceViewsFromRule();
            return;
        }

        if (effect.type == EffectType.RestResource)
        {
            ApplyRestResourceEffect(sourceCard, ownerType, effect);
            if (!nestedBatch)
            {
                FlushOnlineEffectSyncBatch();
            }

            SyncAllResourceViewsFromRule();
            return;
        }

        if (effect.type == EffectType.ActivateResource)
        {
            ApplyActivateResourceEffect(sourceCard, ownerType, effect);
            if (!nestedBatch)
            {
                FlushOnlineEffectSyncBatch();
            }

            SyncAllResourceViewsFromRule();
            return;
        }

        List<CardController> pendingEffectDamageTrash = effect.type == EffectType.Damage
            ? new List<CardController>()
            : null;
        // 効果ダメージ（G-fred の全体ダメージや近接戦闘など）で HP が 0 になった破壊も
        // 「カード効果による破壊」として扱うため、ダメージ適用前に監視カードを確保する。
        List<EffectDestroyWatcher> effectDamageDestroyWatchers = effect.type == EffectType.Damage
            ? CollectEffectDestroyWatchers()
            : null;

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
                    int damageAmount = ResolveEffectDamageAmount(magnitude, t, ownerType);
                    if (damageAmount > 0)
                    {
                        damageAmount += ResolveAllyFeatureOutgoingDamageBonus(ownerType, sourceCard, t);
                    }

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
                    int effectDmgTakenRequestId = NotifyOpponentEffectDamageTakenIfNeeded(
                        sourceCard,
                        ownerType,
                        t,
                        hpBefore);
                    QueueOnlineUnitDamage(
                        t,
                        ResolveUnitKillSourceForTrash(sourceCard, t),
                        effectDmgTakenRequestId > 0 ? 1 : 0,
                        effectDmgTakenRequestId);
                    if (t.CurrentHp <= 0)
                    {
                        Debug.Log(
                            $"[EffectDamage][LocalDestroyQueue] closeCombat:{isCloseCombat} "
                            + $"target:{FormatEffectDamageUnitDebugSnap(t)}");
                        pendingEffectDamageTrash?.Add(t);
                    }

                    if (logCloseCombat)
                    {
                        LogArgamaShieldBlockCloseCombatDebug(
                            "CloseCombatAfterDamage",
                            $"targetAfter:{FormatUnitDebugSnap(t)} willNotifyAndTrash:{t.CurrentHp <= 0}",
                            blocker: t,
                            effectSource: sourceCard);
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
                    int signedValue = -magnitude;
                    ApplyStatEffect(t, signedValue, effect.statTarget, effect.duration, modifierSourceKey);
                    int apReducedRequestId = NotifyApReducedByOpponentIfNeeded(
                        sourceCard,
                        ownerType,
                        t,
                        effect,
                        signedValue);
                    QueueOnlineUnitStat(
                        t,
                        signedValue,
                        effect.statTarget,
                        effect.duration,
                        modifierSourceKey,
                        apReducedRequestId > 0 ? 1 : 0,
                        apReducedRequestId);
                    break;
                }
                case EffectType.BlockRedirect:
                    // BlockRedirect は戦闘フロー分岐で解釈するため、ここでは何もしない。
                    break;
                case EffectType.IncreaseAllyFeatureDamageToEnemyUnits:
                    // 常在オーラ。与ダメージ加算は ResolveAllyFeatureOutgoingDamageBonus で解釈する。
                    break;
                case EffectType.HighMobility:
                    // HighMobility は攻撃フロー分岐で解釈するため、ここでは何もしない。
                    break;
                case EffectType.BattleDamageImmunityFromLowApEnemy:
                    TryApplyBattleDamageImmunityFromLowApEnemyMarker(effect, targets);
                    break;
                case EffectType.AttackActiveEnemyUnit:
                    // AttackActiveEnemyUnit は攻撃対象判定で解釈するため、ここでは何もしない。
                    break;
                case EffectType.ForceEnemyAttackTarget:
                    // ForceEnemyAttackTarget は攻撃対象判定で解釈するため、ここでは何もしない。
                    break;
                case EffectType.CannotBeChosenAsAttackTarget:
                    TryApplyCannotBeChosenAsAttackTargetMarker(effect, targets);
                    break;
                case EffectType.PreventNextStartPhaseActive:
                    TryApplyPreventNextStartPhaseActiveMarker(effect, targets);
                    break;
                case EffectType.GrantShieldAreaEnemyEffectDamageReduction:
                    GrantShieldAreaEnemyEffectDamageReduction(
                        ownerType,
                        magnitude > 0 ? magnitude : 5);
                    break;
                case EffectType.GrantShieldAreaImmunityFromEnemyUnitLevelOrLess:
                    GrantShieldAreaImmunityFromEnemyUnitLevelOrLess(
                        ownerType,
                        magnitude > 0 ? magnitude : 4);
                    break;
                case EffectType.AllyEnemyEffectDamageImmunity:
                    break;
                case EffectType.ThisDeployedBaseImmunityFromEnemyNonTokenUnitLevelOrLess:
                    break;
                case EffectType.ReturnFromHandToDeckBottom:
                    break;
                case EffectType.PreventOpponentStartPhaseActiveLowestRestUnits:
                    break;
                case EffectType.Bounce:
                    break;
                case EffectType.Rest:
                    break;
                case EffectType.Destroy:
                    break;
            }
        }

        // ダメージ専用の早期 Flush。Bounce/Rest 等より先に呼ぶとバッチが閉じ、同期キューが捨てられる。
        if (effect.type == EffectType.Damage)
        {
            FlushOnlineEffectSyncBatchAfterDamageQueue(nestedBatch);

            if (pendingEffectDamageTrash != null && pendingEffectDamageTrash.Count > 0)
            {
                // 破壊されたユニットが場から外れる前に通知する（監視カード自身の破壊も対象にするため）
                NotifyOwnerEffectUnitsDestroyed(
                    ownerType,
                    sourceCard,
                    effectDamageDestroyWatchers,
                    pendingEffectDamageTrash);
                for (int i = 0; i < pendingEffectDamageTrash.Count; i++)
                {
                    CardController t = pendingEffectDamageTrash[i];
                    if (t == null)
                    {
                        continue;
                    }

                    TryLogAttackBlockCloseCombatTrioDestroy("ApplyEffect_Damage", t, sourceCard);
                    NotifyAttackFlowParticipantRemovedDuringOnAction(t);
                    SendCardToTrash(
                        t,
                        ResolveCardOwner(t.transform),
                        ResolveUnitKillSourceForTrash(sourceCard, t),
                        destroyedByEffectDamage: true);
                }
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
        else if (effect.type == EffectType.EffectBattle)
        {
            ApplyEffectBattleToTargets(sourceCard, ownerType, targets);
        }
        else if (effect.type == EffectType.GrantAttackFlag)
        {
            ApplyGrantAttackFlagEffect(effect, ownerType, targets);
        }
        else if (effect.type == EffectType.Activate)
        {
            ApplyActivateEffect(effect, ownerType, targets);
        }
        else if (effect.type == EffectType.RecoverHp)
        {
