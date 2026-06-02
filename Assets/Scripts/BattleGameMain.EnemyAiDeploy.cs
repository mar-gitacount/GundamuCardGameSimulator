using System.Collections.Generic;
using UnityEngine;

/// <summary>エネミー手札からのユニット複数配備と、OnAction 用リソース温存。</summary>
public partial class BattleGameMain
{
    private const int EnemyAiPlayerBoardThreatForOnActionReserve = 8;
    private const int EnemyAiMinDeployUnitBenefit = 1;

    private struct EnemyAiDeployResourceBudget
    {
        public int ResourceToKeep;
        public CardController ReservedCommand;
        public int ReservedCommandSimScore;
    }

    /// <summary>コストが許せる限りユニットを配備する。温存分を除いたリソースで実行。</summary>
    private int TryEnemyDeployAllAffordableUnitsFromHand()
    {
        EnemyAiDeployResourceBudget reserve = ComputeEnemyAiOnActionResourceReserve();
        if (reserve.ReservedCommand != null)
        {
            Debug.Log(
                $"[EnemyAI] Deploy reserve {reserve.ResourceToKeep} resource for OnAction "
                + $"{reserve.ReservedCommand.Data.cardName}(id:{reserve.ReservedCommand.Data.id}) sim:{reserve.ReservedCommandSimScore}");
        }

        int deployed = 0;
        while (TryEnemyDeployBestUnitFromHand(reserve))
        {
            deployed++;
        }

        if (deployed > 0)
        {
            Debug.Log($"[EnemyAI] Deployed {deployed} unit(s) from hand this main phase (reserve:{reserve.ResourceToKeep}).");
        }

        return deployed;
    }

    private EnemyAiDeployResourceBudget ComputeEnemyAiOnActionResourceReserve()
    {
        EnemyAiDeployResourceBudget budget = default;
        if (gundamRule == null || enemyCardGameRule == null)
        {
            return budget;
        }

        List<CardController> commands = CollectEligibleEnemyHandCommandsForEnemyAiSim();
        if (commands.Count == 0 || !EnemyAiPlayerBoardPresentsOnActionOpportunities())
        {
            return budget;
        }

        CardController bestCmd = null;
        int bestScore = int.MinValue;
        for (int i = 0; i < commands.Count; i++)
        {
            CardController cmd = commands[i];
            if (cmd == null || cmd.Data == null)
            {
                continue;
            }

            int score = ScoreEnemyOnActionCommandForDeployPhaseReserve(cmd);
            if (score > bestScore)
            {
                bestScore = score;
                bestCmd = cmd;
            }
        }

        if (bestCmd == null || bestScore < EnemyAiHandCommandMinScoreToExecute)
        {
            return budget;
        }

        budget.ResourceToKeep = bestCmd.CurrentCost;
        budget.ReservedCommand = bestCmd;
        budget.ReservedCommandSimScore = bestScore;
        return budget;
    }

    /// <summary>プレイヤー盤面に OnAction で触れる状況があるか（REST ユニット・脅威・今ターン攻撃可能ユニット）。</summary>
    private bool EnemyAiPlayerBoardPresentsOnActionOpportunities()
    {
        if (GetEnemyAiRestTargets(PlayerType.Enemy).Count > 0)
        {
            return true;
        }

        if (GetEnemyAttackReadyUnitsThisTurn().Count > 0)
        {
            return true;
        }

        List<VirtualBattleUnitSnap> snaps = BuildFullBattleVirtualSnapshot();
        return SumPlayerFieldThreat(snaps) >= EnemyAiPlayerBoardThreatForOnActionReserve;
    }

    private int ScoreEnemyOnActionCommandForDeployPhaseReserve(CardController command)
    {
        int best = int.MinValue;
        List<CardController> attackers = GetEnemyAttackReadyUnitsThisTurn();
        for (int i = 0; i < attackers.Count; i++)
        {
            CardController attacker = attackers[i];
            if (attacker == null || attacker.Data == null)
            {
                continue;
            }

            if (!EnemyAiAttackerCanStrikePlayerThisTurn(attacker))
            {
                continue;
            }

            int score = ScoreEnemyHandCommandByEffectSimulation(command, EffectTiming.OnAction, attacker);
            if (score > best)
            {
                best = score;
            }
        }

        List<CardController> restTargets = GetEnemyAiRestTargets(PlayerType.Enemy);
        if (restTargets.Count > 0)
        {
            int generic = ScoreEnemyHandCommandByEffectSimulation(command, EffectTiming.OnAction, null);
            if (generic > best)
            {
                best = generic;
            }
        }

        return best;
    }

    private List<CardController> GetEnemyAttackReadyUnitsThisTurn()
    {
        List<CardController> list = new List<CardController>();
        if (enemyBattleZoneCards == null)
        {
            return list;
        }

        for (int i = 0; i < enemyBattleZoneCards.Count; i++)
        {
            CardController unit = enemyBattleZoneCards[i];
            if (unit == null || unit.Data == null || unit.Data.type != Type.Unit)
            {
                continue;
            }

            if (unit.AttackFlgState != AttackFlg.True)
            {
                continue;
            }

            list.Add(unit);
        }

        return list;
    }

    private bool EnemyAiAttackerCanStrikePlayerThisTurn(CardController attacker)
    {
        if (attacker == null || gundamRule == null)
        {
            return false;
        }

        bool canAttackShield = gundamRule.CanShowUnitShieldAttackOption(gundamRule.Player, attacker.CurrentPower);
        bool canDirectAttack = !gundamRule.HasShieldZoneProtection(Gundam2024RuleScript.PlayerSide.Player);
        bool canShieldOrDirect = !attacker.Data.isNotDirectAttack && (canAttackShield || canDirectAttack);
        bool canAttackUnit = GetEnemyAiRestTargets(PlayerType.Enemy).Count > 0;
        return canShieldOrDirect || canAttackUnit;
    }

    private bool TryEnemyDeployBestUnitFromHand(EnemyAiDeployResourceBudget reserve)
    {
        Gundam2024RuleScript.PlayerSide side = Gundam2024RuleScript.PlayerSide.Enemy;
        List<CardController> candidates = CollectEnemyDeployableUnitsFromHand(side, reserve);
        if (candidates.Count == 0)
        {
            return false;
        }

        CardController best = null;
        int bestBenefit = int.MinValue;
        for (int i = 0; i < candidates.Count; i++)
        {
            CardController unit = candidates[i];
            int benefit = ScoreEnemyDeployUnitVirtualBenefit(unit);
            if (benefit > bestBenefit)
            {
                bestBenefit = benefit;
                best = unit;
            }
        }

        if (best == null || bestBenefit < EnemyAiMinDeployUnitBenefit)
        {
            return false;
        }

        if (!TryPayHandDeployCost(side, best, 0))
        {
            return false;
        }

        SendCardToField(best, PlayerType.Enemy, enemyCardGameRule);
        SyncResourceViewsFromRule(side);
        Debug.Log(
            $"[Enemy] ユニット配備: {best.Data.cardName}(lv:{best.CurrentLevel} cost:{best.CurrentCost} benefit:{bestBenefit} "
            + $"enemyLv:{gundamRule.Enemy.TotalLevel} res:{gundamRule.Enemy.resource} reserveLeft:{reserve.ResourceToKeep})");
        return true;
    }

    private List<CardController> CollectEnemyDeployableUnitsFromHand(
        Gundam2024RuleScript.PlayerSide side,
        EnemyAiDeployResourceBudget reserve)
    {
        List<CardController> list = new List<CardController>();
        List<CardController> hand = CollectHandControllers(enemyCardGameRule);
        Gundam2024RuleScript.PlayerState state = GetRuleState(side);
        for (int i = 0; i < hand.Count; i++)
        {
            CardController cc = hand[i];
            if (cc == null || cc.Data == null || cc.Data.type != Type.Unit)
            {
                continue;
            }

            if (!CanEnemyAffordHandDeployWithReserve(state, side, cc, reserve))
            {
                continue;
            }

            list.Add(cc);
        }

        return list;
    }

    private bool CanEnemyAffordHandDeployWithReserve(
        Gundam2024RuleScript.PlayerState state,
        Gundam2024RuleScript.PlayerSide side,
        CardController card,
        EnemyAiDeployResourceBudget reserve)
    {
        if (card == null || card.Data == null || state == null || gundamRule == null)
        {
            return false;
        }

        int cost = card.CurrentCost;
        int keep = Mathf.Max(0, reserve.ResourceToKeep);
        if (state.resource < cost + keep)
        {
            return false;
        }

        return gundamRule.CanPlayCard(side, card.CurrentLevel, cost);
    }

    private int ScoreEnemyDeployUnitVirtualBenefit(CardController unit)
    {
        if (unit == null || unit.Data == null)
        {
            return int.MinValue / 2;
        }

        List<VirtualBattleUnitSnap> before = BuildFullBattleVirtualSnapshot();
        int beforeScore = ComputeEnemyAiFieldAdvantageScore(before)
            + ScoreEnemyAiDefensiveZoneValue(Gundam2024RuleScript.PlayerSide.Enemy, null);

        List<VirtualBattleUnitSnap> after = CloneVirtualBattleSnaps(before);
        after.Add(new VirtualBattleUnitSnap
        {
            Controller = unit,
            FieldOwner = PlayerType.Enemy,
            Slot = after.Count,
            Name = unit.Data.cardName,
            Id = unit.Data.id,
            Hp = unit.CurrentHp > 0 ? unit.CurrentHp : unit.Data.hp,
            Ap = unit.CurrentPower,
        });

        int afterScore = ComputeEnemyAiFieldAdvantageScore(after)
            + ScoreEnemyAiDefensiveZoneValue(Gundam2024RuleScript.PlayerSide.Enemy, null);
        return afterScore - beforeScore;
    }
}
