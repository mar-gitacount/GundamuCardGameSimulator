using System.Collections.Generic;
using UnityEngine;

/// <summary>エネミーが手札からベースを配備する AI（盤面スコア・他行動不可時）。</summary>
public partial class BattleGameMain
{
    private const int EnemyAiDefensiveZoneExBaseWeight = 8;
    private const int EnemyAiDefensiveZoneShieldWeight = 4;
    private const int EnemyAiDefensiveZoneDeployedBaseFlat = 40;
    private const int EnemyAiDefensiveZoneDeployedBaseHpWeight = 3;

    /// <summary>
    /// このターン他にユニット配備・OnMain・攻撃ができず、配備ベースが無いとき手札ベースを配備する。
    /// </summary>
    private bool TryEnemyDeployBaseWhenIdle()
    {
        if (isMatchFinished || currentPlayerType != PlayerType.Enemy || gundamRule == null || enemyCardGameRule == null)
        {
            return false;
        }

        Gundam2024RuleScript.PlayerSide enemySide = Gundam2024RuleScript.PlayerSide.Enemy;
        if (HasActiveDeployedBaseForRuleSide(enemySide))
        {
            return false;
        }

        if (!EnemyAiHasNoOtherMainPhaseActionsRemaining())
        {
            return false;
        }

        CardController bestBase = null;
        int bestScore = int.MinValue;
        List<CardController> hand = CollectHandControllers(enemyCardGameRule);
        for (int i = 0; i < hand.Count; i++)
        {
            CardController cc = hand[i];
            if (cc == null || cc.Data == null || cc.Data.type != Type.Base)
            {
                continue;
            }

            int score = ScoreEnemyAiBoardWithSimulatedBaseDeploy(cc);
            if (score > bestScore)
            {
                bestScore = score;
                bestBase = cc;
            }
        }

        if (bestBase == null)
        {
            return false;
        }

        Gundam2024RuleScript.PlayerState payState = gundamRule.Enemy;
        int exToUse = Gundam2024RuleScript.GetExNeededForCost(payState, bestBase.CurrentCost);
        if (!TryPayHandDeployCost(enemySide, bestBase, exToUse))
        {
            return false;
        }

        Debug.Log(
            $"[EnemyAI] DeployBase (idle, no other actions) {bestBase.Data.cardName}(id:{bestBase.Data.id}) "
            + $"boardScore:{bestScore} (field+zoneDefense)");
        DeployBaseFromHand(bestBase, PlayerType.Enemy, enemyCardGameRule);
        SyncResourceViewsFromRule(enemySide);
        return true;
    }

    /// <summary>ユニット配備・有用な OnMain・攻撃のいずれもまだ可能か。</summary>
    private bool EnemyAiHasNoOtherMainPhaseActionsRemaining()
    {
        return !EnemyAiCanDeployUnitFromHand()
            && !EnemyAiCanExecuteUsefulOnMainFromHand()
            && !EnemyAiCanMakeAnyAttack();
    }

    private bool EnemyAiCanDeployUnitFromHand()
    {
        if (enemyCardGameRule == null || gundamRule == null)
        {
            return false;
        }

        EnemyAiDeployResourceBudget reserve = ComputeEnemyAiOnActionResourceReserve();
        Gundam2024RuleScript.PlayerSide side = Gundam2024RuleScript.PlayerSide.Enemy;
        return CollectEnemyDeployableUnitsFromHand(side, reserve).Count > 0;
    }

    private bool EnemyAiCanExecuteUsefulOnMainFromHand()
    {
        List<CardController> hand = CollectHandControllers(enemyCardGameRule);
        for (int i = 0; i < hand.Count; i++)
        {
            CardController cc = hand[i];
            if (cc == null || cc.Data == null || cc.Data.type != Type.Command)
            {
                continue;
            }

            if (!CanExecuteOnMainCardNow(PlayerType.Enemy, cc))
            {
                continue;
            }

            int score = ScoreEnemyHandCommandByEffectSimulation(cc, EffectTiming.OnMain, null);
            if (score >= EnemyAiHandCommandMinScoreToExecute)
            {
                return true;
            }
        }

        return false;
    }

    private bool EnemyAiCanMakeAnyAttack()
    {
        if (gundamRule == null || isMatchFinished)
        {
            return false;
        }

        List<CardController> snapshot = new List<CardController>(enemyBattleZoneCards);
        for (int i = 0; i < snapshot.Count; i++)
        {
            CardController unit = snapshot[i];
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
            if (GetEnemyAiRestTargets(PlayerType.Enemy).Count > 0)
            {
                return true;
            }

            if (canShieldOrDirectAttack)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>仮想盤面＋OnBaseDeployed 効果シミュ＋ゾーン防御。</summary>
    private int ScoreEnemyAiBoardWithSimulatedBaseDeploy(CardController baseCandidate)
    {
        List<VirtualBattleUnitSnap> fieldSnaps = BuildFullBattleVirtualSnapshot();
        int withoutEffects = ComputeEnemyAiFieldAdvantageScore(fieldSnaps)
            + ScoreEnemyAiDefensiveZoneValue(Gundam2024RuleScript.PlayerSide.Enemy, baseCandidate);
        int withEffects = ScoreEnemyAiBoardWithOnBaseDeployedEffects(baseCandidate, fieldSnaps);
        if (withEffects - withoutEffects < EnemyAiOnBaseDeployedMinPlanBenefit)
        {
            return withoutEffects;
        }

        return withEffects;
    }

    private int ScoreEnemyAiDefensiveZoneValue(
        Gundam2024RuleScript.PlayerSide side,
        CardController simulatedDeployedBaseOrNull)
    {
        Gundam2024RuleScript.PlayerState state = GetRuleState(side);
        int value = state.exBase * EnemyAiDefensiveZoneExBaseWeight
            + state.shield * EnemyAiDefensiveZoneShieldWeight;

        CardController currentBase = GetDeployedBaseForRuleSide(side);
        if (currentBase != null && currentBase.CurrentHp > 0)
        {
            value += EnemyAiDefensiveZoneDeployedBaseFlat
                + currentBase.CurrentHp * EnemyAiDefensiveZoneDeployedBaseHpWeight;
        }
        else if (simulatedDeployedBaseOrNull != null && simulatedDeployedBaseOrNull.Data != null)
        {
            int hp = simulatedDeployedBaseOrNull.CurrentHp > 0
                ? simulatedDeployedBaseOrNull.CurrentHp
                : simulatedDeployedBaseOrNull.Data.hp;
            value += EnemyAiDefensiveZoneDeployedBaseFlat + hp * EnemyAiDefensiveZoneDeployedBaseHpWeight;
        }

        return value;
    }
}
