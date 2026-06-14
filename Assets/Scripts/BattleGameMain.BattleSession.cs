using UnityEngine;

/// <summary>バトルセッションの破棄・再開（ホーム復帰後のやり直し用）。</summary>
public partial class BattleGameMain
{
    public void TeardownBattleSessionForMainMenu()
    {
        StopAllCoroutines();
        CloseAllBattleMenuOverlays();
        DestroyActiveOnActionPopupIfAny();
        DestroyActiveAttackFlowDebugPanelIfAny();
        DetachPersistentButtonsBeforeFieldClear();
        ClearBattleFieldPanels();
        DestroyTransientBattleCanvasUi();
        ClearBattleRuntimeCollections();
        ResetBattleFlowFlags();
    }

    public void RestartBattleFromBeginning()
    {
        TeardownBattleSessionForMainMenu();
        RecreateRuleEngines();
        StartCoroutine(BattleSetupCoroutine());
    }

    private void RecreateRuleEngines()
    {
        if (gundamRule != null)
        {
            gundamRule.OnShieldDamaged -= OnGundamShieldDamaged;
        }

        cardGameRule = new CardGameRule();
        enemyCardGameRule = new CardGameRule();
        gundamRule = new Gundam2024RuleScript();
        gundamRule.OnShieldDamaged += OnGundamShieldDamaged;
        RegisterBaseProtectionCallbacks();
    }

    private void DetachPersistentButtonsBeforeFieldClear()
    {
        Transform keepParent = PlayerFieldPanel != null ? PlayerFieldPanel.transform.parent : null;
        if (keepParent == null)
        {
            Canvas canvas = ResolveBattleCanvas();
            keepParent = canvas != null ? canvas.transform : null;
        }

        if (keepParent == null)
        {
            return;
        }

        if (EndTurnButton != null)
        {
            EndTurnButton.transform.SetParent(keepParent, false);
            EndTurnButton.gameObject.SetActive(false);
        }

        if (battleMenuButton != null)
        {
            Destroy(battleMenuButton.gameObject);
            battleMenuButton = null;
        }
    }

    private void ClearBattleFieldPanels()
    {
        DestroyAllDirectChildren(PlayerFieldPanel);
        DestroyAllDirectChildren(EnemyPlayerFieldPanel);
    }

    private void DestroyTransientBattleCanvasUi()
    {
        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            return;
        }

        Transform canvasTransform = canvas.transform;
        for (int i = canvasTransform.childCount - 1; i >= 0; i--)
        {
            Transform child = canvasTransform.GetChild(i);
            if (IsPersistentBattleCanvasChild(child.gameObject))
            {
                continue;
            }

            Destroy(child.gameObject);
        }
    }

    private bool IsPersistentBattleCanvasChild(GameObject child)
    {
        if (child == null)
        {
            return true;
        }

        if (PlayerFieldPanel != null && child == PlayerFieldPanel)
        {
            return true;
        }

        if (EnemyPlayerFieldPanel != null && child == EnemyPlayerFieldPanel)
        {
            return true;
        }

        if (EndTurnButton != null && child == EndTurnButton.gameObject)
        {
            return true;
        }

        return child.GetComponent<BattleGameMain>() != null;
    }

    private static void DestroyAllDirectChildren(GameObject root)
    {
        if (root == null)
        {
            return;
        }

        Transform t = root.transform;
        for (int i = t.childCount - 1; i >= 0; i--)
        {
            Destroy(t.GetChild(i).gameObject);
        }
    }

    private void ClearBattleRuntimeCollections()
    {
        playerBattleZoneCards.Clear();
        enemyBattleZoneCards.Clear();
        playerHandCards.Clear();
        enemyHandCards.Clear();
        onHandAutoProcessing.Clear();
        pendingShieldBreakBatches.Clear();
        copyCardController = null;
        pendingUnitAttackAttacker = null;
        pendingOnAttackEffectResolvedAttacker = null;
    }

    private void ResetBattleFlowFlags()
    {
        isMatchFinished = false;
        isEndTurnFlowRunning = false;
        isOnActionPopupOpen = false;
        isShieldBreakFlowOpen = false;
        shieldBreakQueueRunning = false;
        isAttackedSidePanelOpen = false;
        isActionThinkPauseOpen = false;
        isShieldAttackResolving = false;
        isTurnPhaseSequenceRunning = false;
        isEnemyMainPhaseCoroutineRunning = false;
        blockShieldFlowDuringShieldAttack = false;
        deferredShieldBlockRedirectWait = false;
        shieldStrikeAbortedAfterBlockInterrupt = false;
        blockExchangeCancelledForCurrentAttack = false;
        burstDeployBasePreferSourceCard = false;
        burstEffectResolutionDepth = 0;
        ClearAttackFlowContext();
        currentPhase = BattlePhase.StartTurn;
    }

    private void DestroyActiveAttackFlowDebugPanelIfAny()
    {
        if (activeAttackFlowDebugPanelRoot != null)
        {
            Destroy(activeAttackFlowDebugPanelRoot);
            activeAttackFlowDebugPanelRoot = null;
        }
    }
}
