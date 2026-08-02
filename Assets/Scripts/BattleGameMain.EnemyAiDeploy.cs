using System.Collections.Generic;
using UnityEngine;

/// <summary>エネミー手札からのユニット複数配備と、OnAction 用リソース温存。</summary>
public partial class BattleGameMain
{
    private const int EnemyAiPlayerBoardThreatForOnActionReserve = 8;
    private const int EnemyAiMinDeployUnitBenefit = 1;
    private const int EnemyAiForceDeployOnlyHandPlayBenefit = 0;

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
            if (unit == null || unit.Data == null || !unit.Data.IsUnitLike())
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
        bool forcedUnitAttack = HasForcedEnemyAttackTarget(PlayerType.Enemy, attacker);
        bool canShieldOrDirect = !forcedUnitAttack
            && !attacker.CannotDirectAttackPlayerOrShield()
            && (canAttackShield || canDirectAttack);
        bool canAttackUnit = GetEnemyUnitAttackTargets(PlayerType.Enemy, attacker).Count > 0;
        return canShieldOrDirect || canAttackUnit;
    }

    private bool TryEnemyDeployBestUnitFromHand(EnemyAiDeployResourceBudget reserve)
    {
        Gundam2024RuleScript.PlayerSide side = Gundam2024RuleScript.PlayerSide.Enemy;
        EnemyAiDeployResourceBudget noReserve = default;
        List<CardController> candidatesWithoutReserve = CollectEnemyDeployableUnitsFromHand(side, noReserve);
        bool forceOnlyHandDeploy = ShouldEnemyForceDeployUnitAsOnlyHandPlay(candidatesWithoutReserve);
        EnemyAiDeployResourceBudget effectiveReserve = forceOnlyHandDeploy ? noReserve : reserve;
        List<CardController> candidates = forceOnlyHandDeploy
            ? candidatesWithoutReserve
            : CollectEnemyDeployableUnitsFromHand(side, reserve);
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

        if (forceOnlyHandDeploy)
        {
            CardController forced = PickEnemyForceDeployUnitCandidate(candidates);
            if (forced != null)
            {
                best = forced;
                bestBenefit = ScoreEnemyDeployUnitVirtualBenefit(forced);
            }
        }

        if (best == null)
        {
            return false;
        }

        int requiredBenefit = forceOnlyHandDeploy
            ? EnemyAiForceDeployOnlyHandPlayBenefit
            : EnemyAiMinDeployUnitBenefit;
        if (bestBenefit < requiredBenefit)
        {
            return false;
        }

        Gundam2024RuleScript.PlayerState payState = side == Gundam2024RuleScript.PlayerSide.Player
            ? gundamRule.Player
            : gundamRule.Enemy;
        int exToUse = Gundam2024RuleScript.GetExNeededForCost(payState, best.CurrentCost);
        if (!TryPayHandDeployCost(side, best, exToUse))
        {
            return false;
        }

        SendCardToField(best, PlayerType.Enemy, enemyCardGameRule);
        SyncResourceViewsFromRule(side);
        Debug.Log(
            forceOnlyHandDeploy
                ? $"[Enemy] ユニット配備(手札の唯一の行動): {best.Data.cardName}(lv:{best.CurrentLevel} cost:{best.CurrentCost} benefit:{bestBenefit} "
                  + $"enemyLv:{gundamRule.Enemy.TotalLevel} res:{gundamRule.Enemy.resource} reserveLeft:{effectiveReserve.ResourceToKeep})"
                : $"[Enemy] ユニット配備: {best.Data.cardName}(lv:{best.CurrentLevel} cost:{best.CurrentCost} benefit:{bestBenefit} "
                  + $"enemyLv:{gundamRule.Enemy.TotalLevel} res:{gundamRule.Enemy.resource} reserveLeft:{effectiveReserve.ResourceToKeep})");
        return true;
    }

    /// <summary>手札にユニット配備以外の有用行動が無いときは配備を強制する（場の攻撃可否は見ない）。</summary>
    private bool ShouldEnemyForceDeployUnitAsOnlyHandPlay(List<CardController> deployableUnits)
    {
        if (deployableUnits == null || deployableUnits.Count == 0)
        {
            return false;
        }

        return !EnemyAiHasOtherUsefulHandPlaysBesidesUnitDeploy();
    }

    private bool EnemyAiHasOtherUsefulHandPlaysBesidesUnitDeploy()
    {
        if (EnemyAiCanExecuteUsefulOnMainFromHand())
        {
            return true;
        }

        EnemyAiDeployResourceBudget noReserve = default;
        return EnemyAiHasBeneficialPilotMountFromHand(noReserve);
    }

    private bool EnemyAiHasBeneficialPilotMountFromHand(EnemyAiDeployResourceBudget reserve)
    {
        List<CardController> pilots = CollectEnemyMountablePilotsFromHand(reserve);
        List<CardController> units = GetMountableUnits(PlayerType.Enemy);
        if (pilots.Count == 0 || units.Count == 0)
        {
            return false;
        }

        for (int pi = 0; pi < pilots.Count; pi++)
        {
            CardController pilot = pilots[pi];
            for (int ui = 0; ui < units.Count; ui++)
            {
                CardController unit = units[ui];
                if (unit == null || !unit.CanMountPilot())
                {
                    continue;
                }

                if (ScoreEnemyPilotMountVirtualBenefit(pilot, unit, reserve) >= EnemyAiMinPilotMountBenefit)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private CardController PickEnemyForceDeployUnitCandidate(List<CardController> candidates)
    {
        if (candidates == null || candidates.Count == 0)
        {
            return null;
        }

        CardController best = null;
        int bestBenefit = int.MinValue;
        bool bestHasDeployDamageArchetype = false;
        for (int i = 0; i < candidates.Count; i++)
        {
            CardController unit = candidates[i];
            if (unit == null || unit.Data == null)
            {
                continue;
            }

            int benefit = ScoreEnemyDeployUnitVirtualBenefit(unit);
            bool hasArchetype = CardHasOnPlayedSelfHarmAndOpponentDamagePattern(unit.Data);
            if (best == null
                || benefit > bestBenefit
                || (benefit == bestBenefit && hasArchetype && !bestHasDeployDamageArchetype))
            {
                best = unit;
                bestBenefit = benefit;
                bestHasDeployDamageArchetype = hasArchetype;
            }
        }

        return best;
    }

    /// <summary>OnPlayed に相手へのダメージと自傷（味方/自身）ダメージの両方を含むカード設計か。</summary>
    private static bool CardHasOnPlayedSelfHarmAndOpponentDamagePattern(CardData data)
    {
        if (data == null || data.timedEffects == null)
        {
            return false;
        }

        bool damagesOpponent = false;
        bool damagesSelfOrAlly = false;
        for (int i = 0; i < data.timedEffects.Count; i++)
        {
            TimedEffectData timed = data.timedEffects[i];
            if (timed == null || timed.timing != EffectTiming.OnPlayed || !timed.HasResolvedEffects())
            {
                continue;
            }

            IReadOnlyList<EffectData> resolved = timed.GetResolvedEffects();
            for (int j = 0; j < resolved.Count; j++)
            {
                EffectData effect = resolved[j];
                if (effect == null || effect.type != EffectType.Damage)
                {
                    continue;
                }

                if (effect.target == TargetType.EnemyUnit
                    || effect.target == TargetType.EnemyAllUnits
                    || effect.target == TargetType.RestEnemyUnit
                    || effect.target == TargetType.EnemyPlayer)
                {
                    damagesOpponent = true;
                }

                if (effect.target == TargetType.Self
                    || effect.target == TargetType.AllyUnit
                    || effect.target == TargetType.AllyOtherUnit
                    || effect.target == TargetType.AllyAllUnits
                    || effect.target == TargetType.SelfPlayer)
                {
                    damagesSelfOrAlly = true;
                }
            }
        }

        return damagesOpponent && damagesSelfOrAlly;
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
            if (cc == null || cc.Data == null || !cc.Data.IsUnitLike())
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
        if (!gundamRule.CanPlayCardWithAnyEx(side, card.CurrentLevel, cost))
        {
            return false;
        }

        int exNeeded = Gundam2024RuleScript.GetExNeededForCost(state, cost);
        return state.resource >= (cost - exNeeded) + keep;
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

        int afterScore = ScoreEnemyAiBoardWithOnPlayedDeployEffects(unit, after);
        return afterScore - beforeScore;
    }

    /// <summary>ユニット配備候補の OnPlayed を仮想適用した盤面スコア（配備 AI 用）。</summary>
    private int ScoreEnemyAiBoardWithOnPlayedDeployEffects(
        CardController deployCandidate,
        List<VirtualBattleUnitSnap> fieldSnaps)
    {
        int score = ComputeEnemyAiFieldAdvantageScore(fieldSnaps)
            + ScoreEnemyAiDefensiveZoneValue(Gundam2024RuleScript.PlayerSide.Enemy, null);
        if (deployCandidate == null)
        {
            return score;
        }

        List<VirtualBattleUnitSnap> afterEffects = CloneVirtualBattleSnaps(fieldSnaps);
        ApplyEnemyAiDeployOnPlayedEffectsToVirtualSnaps(afterEffects, deployCandidate, PlayerType.Enemy);
        int effectBenefit = ComputeEnemyAiFieldAdvantageScore(afterEffects)
            - ComputeEnemyAiFieldAdvantageScore(fieldSnaps);
        if (EnemyAiVirtualPlanHasPlayerUnitKill(afterEffects)
            && !EnemyAiVirtualPlanHasPlayerUnitKill(fieldSnaps))
        {
            effectBenefit += EnemyAiOnRestBonusNewKill;
        }

        return score + effectBenefit;
    }

    private void ApplyEnemyAiDeployOnPlayedEffectsToVirtualSnaps(
        List<VirtualBattleUnitSnap> working,
        CardController deployingUnit,
        PlayerType ownerType)
    {
        if (working == null || deployingUnit == null)
        {
            return;
        }

        List<EffectData> effects = CollectEnemyAiResolvedTimedEffects(
            deployingUnit,
            ownerType,
            EffectTiming.OnPlayed);
        if (effects.Count == 0)
        {
            return;
        }

        EnemyAiEffectPickContext pickCtx = BuildEnemyAiEffectPickContext(ownerType, deployingUnit, null, null);
        for (int i = 0; i < effects.Count; i++)
        {
            EffectData effect = effects[i];
            if (effect == null)
            {
                continue;
            }

            if (effect.type == EffectType.Damage
                && (effect.target == TargetType.EnemyPlayer || effect.target == TargetType.SelfPlayer))
            {
                continue;
            }

            int magnitude = ResolveEffectMagnitude(effect, ownerType, deployingUnit);
            if (magnitude == 0 && !effect.type.UsesTargetCountValue())
            {
                continue;
            }

            List<CardController> targets;
            if (EffectRequiresManualUnitSelection(effect))
            {
                List<CardController> candidates = ResolveVirtualDeploySelectableEffectTargets(
                    working,
                    deployingUnit,
                    ownerType,
                    effect);
                targets = PickEnemyAiEffectTargets(effect, pickCtx, candidates, singleOnly: true);
            }
            else
            {
                targets = ResolveVirtualDeployEffectTargets(working, deployingUnit, ownerType, effect);
            }

            if (targets == null || targets.Count == 0)
            {
                continue;
            }

            ApplyVirtualBattleEffectToTargetsOnSnaps(working, effect, targets, magnitude, deployingUnit);
        }
    }

    private List<CardController> ResolveVirtualDeployEffectTargets(
        List<VirtualBattleUnitSnap> working,
        CardController sourceCard,
        PlayerType ownerType,
        EffectData effect)
    {
        if (effect == null)
        {
            return new List<CardController>();
        }

        IReadOnlyList<CardFeatureData> requiredFeatures = effect.GetTargetFeatures();
        List<CardController> result = new List<CardController>();
        switch (effect.target)
        {
            case TargetType.Self:
                if (IsVirtualDeployUnitTarget(working, sourceCard, requiredFeatures))
                {
                    result.Add(sourceCard);
                }

                break;
            case TargetType.AllyUnit:
                AddVirtualDeployAliveUnits(working, ownerType, result, null, requiredFeatures);
                EnsureVirtualDeploySelfCandidate(working, sourceCard, result, requiredFeatures);
                break;
            case TargetType.AllyOtherUnit:
                AddVirtualDeployAliveUnits(working, ownerType, result, sourceCard, requiredFeatures);
                break;
            case TargetType.EnemyUnit:
                AddVirtualDeployAliveUnits(
                    working,
                    ownerType == PlayerType.Player ? PlayerType.Enemy : PlayerType.Player,
                    result,
                    null,
                    requiredFeatures);
                break;
            case TargetType.RestEnemyUnit:
                AddVirtualDeployAliveRestUnits(
                    working,
                    ownerType == PlayerType.Player ? PlayerType.Enemy : PlayerType.Player,
                    result,
                    requiredFeatures);
                break;
            case TargetType.AllyAllUnits:
                AddVirtualDeployAliveUnits(working, ownerType, result, null, requiredFeatures);
                break;
            case TargetType.EnemyAllUnits:
                AddVirtualDeployAliveUnits(
                    working,
                    ownerType == PlayerType.Player ? PlayerType.Enemy : PlayerType.Player,
                    result,
                    null,
                    requiredFeatures);
                break;
        }

        FilterTargetsByUnitCondition(result, effect, sourceCard);
        if (effect.type == EffectType.Rest)
        {
            FilterOutAlreadyRestedUnits(result);
        }

        return result;
    }

    private List<CardController> ResolveVirtualDeploySelectableEffectTargets(
        List<VirtualBattleUnitSnap> working,
        CardController sourceCard,
        PlayerType ownerType,
        EffectData effect)
    {
        return ResolveVirtualDeployEffectTargets(working, sourceCard, ownerType, effect);
    }

    private static bool IsVirtualDeployUnitTarget(
        List<VirtualBattleUnitSnap> working,
        CardController unit,
        IReadOnlyList<CardFeatureData> requiredFeatures)
    {
        if (unit == null || unit.Data == null || !unit.Data.IsUnitLike())
        {
            return false;
        }

        VirtualBattleUnitSnap snap = FindBattleVirtualSnap(working, unit);
        if (snap == null || snap.Hp <= 0)
        {
            return false;
        }

        return MatchesRequiredFeatures(unit.Data, requiredFeatures);
    }

    private static void EnsureVirtualDeploySelfCandidate(
        List<VirtualBattleUnitSnap> working,
        CardController sourceCard,
        List<CardController> result,
        IReadOnlyList<CardFeatureData> requiredFeatures)
    {
        if (result == null
            || !IsVirtualDeployUnitTarget(working, sourceCard, requiredFeatures)
            || result.Contains(sourceCard))
        {
            return;
        }

        result.Insert(0, sourceCard);
    }

    private static void AddVirtualDeployAliveUnits(
        List<VirtualBattleUnitSnap> working,
        PlayerType ownerType,
        List<CardController> result,
        CardController exclude,
        IReadOnlyList<CardFeatureData> requiredFeatures)
    {
        if (working == null || result == null)
        {
            return;
        }

        for (int i = 0; i < working.Count; i++)
        {
            VirtualBattleUnitSnap snap = working[i];
            if (snap == null || snap.FieldOwner != ownerType || snap.Controller == null)
            {
                continue;
            }

            CardController unit = snap.Controller;
            if (unit == exclude || unit.Data == null || !unit.Data.IsUnitLike() || snap.Hp <= 0)
            {
                continue;
            }

            if (!MatchesRequiredFeatures(unit.Data, requiredFeatures))
            {
                continue;
            }

            if (!result.Contains(unit))
            {
                result.Add(unit);
            }
        }
    }

    private static void AddVirtualDeployAliveRestUnits(
        List<VirtualBattleUnitSnap> working,
        PlayerType ownerType,
        List<CardController> result,
        IReadOnlyList<CardFeatureData> requiredFeatures)
    {
        if (working == null || result == null)
        {
            return;
        }

        for (int i = 0; i < working.Count; i++)
        {
            VirtualBattleUnitSnap snap = working[i];
            if (snap == null || snap.FieldOwner != ownerType || snap.Controller == null || !snap.IsRest)
            {
                continue;
            }

            CardController unit = snap.Controller;
            if (unit.Data == null || !unit.Data.IsUnitLike() || snap.Hp <= 0)
            {
                continue;
            }

            if (!MatchesRequiredFeatures(unit.Data, requiredFeatures))
            {
                continue;
            }

            if (!result.Contains(unit))
            {
                result.Add(unit);
            }
        }
    }
}
