using System.Collections.Generic;
using UnityEngine;

/// <summary>エネミー手札パイロットの搭乗評価（Link 優遇・搭乗後の仮想手札/攻撃シミュレーション）。</summary>
public partial class BattleGameMain
{
    private const int EnemyAiMinPilotMountBenefit = 1;
    private const int EnemyAiLinkMountBonus = 14;
    private const int EnemyAiMountPlayerHarmWeight = 3;
    private const int EnemyAiMountEnemySelfHarmWeight = 4;

    /// <summary>温存リソースを考慮し、スコアが最も高い搭乗を繰り返す。</summary>
    private int TryEnemyMountAllAffordablePilotsFromHand()
    {
        EnemyAiDeployResourceBudget reserve = ComputeEnemyAiOnActionResourceReserve();
        int mounted = 0;
        while (TryEnemyMountBestPilotFromHand(reserve))
        {
            mounted++;
        }

        if (mounted > 0)
        {
            Debug.Log($"[EnemyAI] Mounted {mounted} pilot(s) from hand (reserve:{reserve.ResourceToKeep}).");
        }

        return mounted;
    }

    private bool TryEnemyMountBestPilotFromHand(EnemyAiDeployResourceBudget reserve)
    {
        List<CardController> pilots = CollectEnemyMountablePilotsFromHand(reserve);
        List<CardController> units = GetMountableUnits(PlayerType.Enemy);
        if (pilots.Count == 0 || units.Count == 0)
        {
            return false;
        }

        CardController bestPilot = null;
        CardController bestUnit = null;
        int bestBenefit = int.MinValue;
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

                int benefit = ScoreEnemyPilotMountVirtualBenefit(pilot, unit, reserve);
                if (benefit > bestBenefit)
                {
                    bestBenefit = benefit;
                    bestPilot = pilot;
                    bestUnit = unit;
                }
            }
        }

        if (bestPilot == null || bestUnit == null || bestBenefit < EnemyAiMinPilotMountBenefit)
        {
            return false;
        }

        if (!TryExecuteEnemyPilotMount(bestPilot, bestUnit, reserve))
        {
            return false;
        }

        Debug.Log(
            $"[EnemyAI] Pilot mount: {bestPilot.Data.cardName} → {bestUnit.Data.cardName} "
            + $"benefit:{bestBenefit} link:{UnitLinkExtensions.MatchesLinkPilot(bestUnit.Data, bestPilot.Data)}");
        return true;
    }

    private List<CardController> CollectEnemyMountablePilotsFromHand(EnemyAiDeployResourceBudget reserve)
    {
        List<CardController> list = new List<CardController>();
        Gundam2024RuleScript.PlayerSide side = Gundam2024RuleScript.PlayerSide.Enemy;
        Gundam2024RuleScript.PlayerState state = GetRuleState(side);
        List<CardController> hand = CollectHandControllers(enemyCardGameRule);
        for (int i = 0; i < hand.Count; i++)
        {
            CardController cc = hand[i];
            if (cc == null || cc.Data == null || cc.Data.type != Type.Pilot)
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

    private bool TryExecuteEnemyPilotMount(
        CardController pilot,
        CardController unit,
        EnemyAiDeployResourceBudget reserve)
    {
        if (pilot == null || unit == null || pilot.Data == null || unit.Data == null)
        {
            return false;
        }

        Gundam2024RuleScript.PlayerSide side = Gundam2024RuleScript.PlayerSide.Enemy;
        Gundam2024RuleScript.PlayerState state = GetRuleState(side);
        if (!CanEnemyAffordHandDeployWithReserve(state, side, pilot, reserve)
            || !TryPayHandDeployCost(side, pilot, 0))
        {
            return false;
        }

        if (enemyHandCards != null)
        {
            enemyHandCards.Remove(pilot.Data);
        }

        if (!unit.TryAttachPilot(pilot))
        {
            return false;
        }

        ApplyUnitAttackFlgFromLink(unit, PlayerType.Enemy);
        TriggerOnPilotMountedEffects(unit, pilot, PlayerType.Enemy, () =>
        {
            TriggerOnPlayedEffects(pilot, PlayerType.Enemy, RefreshAllHandsConditionalOnHandAuto);
        });
        SyncResourceViewsFromRule(side);
        return true;
    }

    /// <summary>搭乗＋OnPlayed 仮想適用後の「打てる手」シミュレーション。プレイヤー被害↑・敵被害↓を優先。</summary>
    private int ScoreEnemyPilotMountVirtualBenefit(
        CardController pilot,
        CardController unit,
        EnemyAiDeployResourceBudget reserve)
    {
        if (pilot == null || pilot.Data == null || unit == null || unit.Data == null)
        {
            return int.MinValue / 2;
        }

        List<VirtualBattleUnitSnap> before = BuildFullBattleVirtualSnapshot();
        int beforeOutlook = ScoreEnemyAiMountPhaseOutlook(before, reserve, null, null, null);
        int playerHpBefore = SumPlayerFieldHp(before);
        int playerThreatBefore = SumPlayerFieldThreat(before);
        int enemyHpBefore = SumEnemyFieldHp(before);

        List<VirtualBattleUnitSnap> after = CloneVirtualBattleSnaps(before);
        ApplyVirtualPilotMountToSnaps(after, pilot, unit);
        ApplyVirtualPilotOnMountedEffects(after, unit, pilot);
        ApplyVirtualPilotOnPlayedEffects(after, pilot, unit);

        int afterOutlook = ScoreEnemyAiMountPhaseOutlook(after, reserve, pilot, unit, pilot);
        int playerHpAfter = SumPlayerFieldHp(after);
        int playerThreatAfter = SumPlayerFieldThreat(after);
        int enemyHpAfter = SumEnemyFieldHp(after);

        int playerHarm = (playerHpBefore - playerHpAfter) * EnemyAiMountPlayerHarmWeight
            + (playerThreatBefore - playerThreatAfter);
        int enemySelfHarm = (enemyHpBefore - enemyHpAfter) * EnemyAiMountEnemySelfHarmWeight;
        int benefit = afterOutlook - beforeOutlook + playerHarm - enemySelfHarm;

        if (UnitLinkExtensions.MatchesLinkPilot(unit.Data, pilot.Data))
        {
            benefit += EnemyAiLinkMountBonus;
        }

        return benefit;
    }

    private static int SumEnemyFieldHp(List<VirtualBattleUnitSnap> snaps)
    {
        int sum = 0;
        if (snaps == null)
        {
            return sum;
        }

        for (int i = 0; i < snaps.Count; i++)
        {
            VirtualBattleUnitSnap s = snaps[i];
            if (s != null && s.FieldOwner == PlayerType.Enemy)
            {
                sum += Mathf.Max(0, s.Hp);
            }
        }

        return sum;
    }

    private void ApplyVirtualPilotMountToSnaps(
        List<VirtualBattleUnitSnap> snaps,
        CardController pilot,
        CardController unit)
    {
        VirtualBattleUnitSnap snap = FindBattleVirtualSnap(snaps, unit);
        if (snap == null || pilot == null || pilot.Data == null)
        {
            return;
        }

        snap.Ap += Mathf.Max(0, pilot.Data.power);
        snap.Hp += Mathf.Max(0, pilot.Data.hp);
    }

    private void ApplyVirtualPilotOnMountedEffects(
        List<VirtualBattleUnitSnap> snaps,
        CardController hostUnit,
        CardController pilot)
    {
        if (snaps == null || hostUnit == null || pilot == null || hostUnit.Data == null)
        {
            return;
        }

        UnitLinkExtensions.ResolveOnPilotMountedExecutionPlan(
            hostUnit.Data,
            out bool resolveUnit,
            out bool resolvePilot,
            out bool unitFirst);

        if (unitFirst)
        {
            if (resolveUnit)
            {
                ApplyVirtualOnPilotMountedForCard(snaps, hostUnit, hostUnit, pilot);
            }

            if (resolvePilot)
            {
                ApplyVirtualOnPilotMountedForCard(snaps, pilot, hostUnit, pilot);
            }
        }
        else
        {
            if (resolvePilot)
            {
                ApplyVirtualOnPilotMountedForCard(snaps, pilot, hostUnit, pilot);
            }

            if (resolveUnit)
            {
                ApplyVirtualOnPilotMountedForCard(snaps, hostUnit, hostUnit, pilot);
            }
        }
    }

    private void ApplyVirtualOnPilotMountedForCard(
        List<VirtualBattleUnitSnap> snaps,
        CardController sourceCard,
        CardController hostUnit,
        CardController pilot)
    {
        if (sourceCard == null || sourceCard.Data == null || sourceCard.Data.timedEffects == null)
        {
            return;
        }

        EffectActivationContext activationContext =
            BuildPilotMountActivationContext(PlayerType.Enemy, sourceCard, hostUnit, pilot);
        List<CardController> restTargets = GetEnemyAiRestTargets(PlayerType.Enemy);
        EnemyAiEffectPickContext pickCtx = BuildEnemyAiEffectPickContext(
            PlayerType.Enemy,
            sourceCard,
            hostUnit,
            restTargets);

        for (int i = 0; i < sourceCard.Data.timedEffects.Count; i++)
        {
            TimedEffectData timed = sourceCard.Data.timedEffects[i];
            if (timed == null || !timed.IsOnPilotMountedResolutionBlock())
            {
                continue;
            }

            if (!EffectActivationEvaluator.AreTimedConditionsMet(timed, activationContext))
            {
                continue;
            }

            List<EffectData> resolved = new List<EffectData>(timed.GetResolvedEffects());
            ApplyEnemyHandCommandVirtualEffects(
                snaps,
                resolved,
                sourceCard,
                PlayerType.Enemy,
                pickCtx);
        }
    }

    private void ApplyVirtualPilotOnPlayedEffects(
        List<VirtualBattleUnitSnap> snaps,
        CardController pilot,
        CardController mountedUnit)
    {
        if (snaps == null || pilot == null || pilot.Data == null || pilot.Data.timedEffects == null)
        {
            return;
        }

        EffectActivationContext activationContext = BuildPilotMountActivationContext(
            PlayerType.Enemy,
            pilot,
            mountedUnit,
            pilot);
        List<CardController> restTargets = GetEnemyAiRestTargets(PlayerType.Enemy);
        EnemyAiEffectPickContext pickCtx = BuildEnemyAiEffectPickContext(
            PlayerType.Enemy,
            pilot,
            mountedUnit,
            restTargets);

        for (int i = 0; i < pilot.Data.timedEffects.Count; i++)
        {
            TimedEffectData timed = pilot.Data.timedEffects[i];
            if (timed == null || !timed.IsOnFieldPlayedResolutionBlock())
            {
                continue;
            }

            if (!EffectActivationEvaluator.AreTimedConditionsMet(timed, activationContext))
            {
                continue;
            }

            List<EffectData> resolved = new List<EffectData>(timed.GetResolvedEffects());
            ApplyEnemyHandCommandVirtualEffects(
                snaps,
                resolved,
                pilot,
                PlayerType.Enemy,
                pickCtx);
        }
    }

    /// <summary>搭乗後の盤面評価：フィールド有利＋最良の仮想攻撃＋温存内 OnAction コマンド。</summary>
    private int ScoreEnemyAiMountPhaseOutlook(
        List<VirtualBattleUnitSnap> snaps,
        EnemyAiDeployResourceBudget reserve,
        CardController linkMountUnit,
        CardController linkMountPilot,
        CardController excludeHandCard)
    {
        if (snaps == null)
        {
            return int.MinValue / 2;
        }

        int score = ComputeEnemyAiFieldAdvantageScore(snaps)
            + ScoreEnemyAiDefensiveZoneValue(Gundam2024RuleScript.PlayerSide.Enemy, null);
        score += ScoreEnemyAiBestVirtualAttackOutlookOnSnaps(snaps, linkMountUnit, linkMountPilot);
        score += ScoreEnemyAiBestOnActionCommandOutlookOnSnaps(snaps, reserve, excludeHandCard, linkMountUnit);
        return score;
    }

    private int ScoreEnemyAiBestVirtualAttackOutlookOnSnaps(
        List<VirtualBattleUnitSnap> snaps,
        CardController linkMountUnit,
        CardController linkMountPilot)
    {
        if (snaps == null || gundamRule == null)
        {
            return 0;
        }

        int best = int.MinValue / 4;
        List<CardController> attackers = CollectEnemyVirtualAttackers(linkMountUnit, linkMountPilot);
        for (int ai = 0; ai < attackers.Count; ai++)
        {
            CardController attacker = attackers[ai];
            if (attacker == null || attacker.Data == null)
            {
                continue;
            }

            List<CardController> attackTargets = GetEnemyUnitAttackTargets(PlayerType.Enemy, attacker);

            VirtualBattleUnitSnap atkSnap = FindBattleVirtualSnap(snaps, attacker);
            if (atkSnap == null)
            {
                continue;
            }

            CardData simPilotData = null;
            if (linkMountUnit == attacker && linkMountPilot != null)
            {
                simPilotData = linkMountPilot.Data;
            }
            else if (attacker.MountedPilot != null)
            {
                simPilotData = attacker.MountedPilot.Data;
            }

            bool canShieldOrDirect = EnemyAiAttackerCanStrikePlayerThisTurn(attacker);
            if (canShieldOrDirect && attackTargets.Count == 0)
            {
                int shieldWeight = gundamRule.CanShowUnitShieldAttackOption(gundamRule.Player, atkSnap.Ap)
                    ? (gundamRule.HasShieldZoneProtection(Gundam2024RuleScript.PlayerSide.Player) ? 8 : 12)
                    : 12;
                int shieldScore = 40 - atkSnap.Ap * shieldWeight;
                if (shieldScore > best)
                {
                    best = shieldScore;
                }
            }

            for (int ti = 0; ti < attackTargets.Count; ti++)
            {
                CardController target = attackTargets[ti];
                if (target == null || target.Data == null)
                {
                    continue;
                }

                List<VirtualBattleUnitSnap> work = CloneVirtualBattleSnaps(snaps);
                ApplyVirtualOnAttackPreCombatEffects(work, attacker, simPilotData, target);
                VirtualBattleUnitSnap atk = FindBattleVirtualSnap(work, attacker);
                VirtualBattleUnitSnap def = FindBattleVirtualSnap(work, target);
                if (atk == null || def == null)
                {
                    continue;
                }

                int sc = ScoreVirtualAttackAgainstPlayerUnit(atk, def);
                if (sc > best)
                {
                    best = sc;
                }
            }
        }

        return best > int.MinValue / 4 ? best : 0;
    }

    private List<CardController> CollectEnemyVirtualAttackers(
        CardController linkMountUnit,
        CardController linkMountPilot)
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

            if (!EnemyAiVirtualUnitCanAttackThisTurn(unit, linkMountUnit, linkMountPilot))
            {
                continue;
            }

            if (!EnemyAiAttackerCanStrikePlayerThisTurn(unit))
            {
                continue;
            }

            list.Add(unit);
        }

        return list;
    }

    private bool EnemyAiVirtualUnitCanAttackThisTurn(
        CardController unit,
        CardController linkMountUnit,
        CardController linkMountPilot)
    {
        if (unit == null)
        {
            return false;
        }

        if (unit.AttackFlgState == AttackFlg.True)
        {
            return true;
        }

        if (linkMountUnit == unit && linkMountPilot != null && linkMountPilot.Data != null
            && UnitLinkExtensions.MatchesLinkPilot(unit.Data, linkMountPilot.Data))
        {
            return true;
        }

        return false;
    }

    private void ApplyVirtualOnAttackPreCombatEffects(
        List<VirtualBattleUnitSnap> working,
        CardController attackerUnit,
        CardData pilotData,
        CardController defenderUnit)
    {
        if (working == null || attackerUnit == null || defenderUnit == null)
        {
            return;
        }

        List<CardController> restTargets = GetEnemyAiRestTargets(PlayerType.Enemy);
        EnemyAiEffectPickContext ctx = BuildEnemyAiEffectPickContext(
            PlayerType.Enemy,
            attackerUnit,
            attackerUnit,
            restTargets);

        ApplyVirtualOnAttackEffectsFromData(working, attackerUnit, attackerUnit.Data, defenderUnit, ctx);
        if (pilotData != null)
        {
            ApplyVirtualOnAttackEffectsFromData(working, attackerUnit, pilotData, defenderUnit, ctx);
        }
        else if (attackerUnit.MountedPilot != null && attackerUnit.MountedPilot.Data != null)
        {
            ApplyVirtualOnAttackEffectsFromData(
                working,
                attackerUnit,
                attackerUnit.MountedPilot.Data,
                defenderUnit,
                ctx);
        }
    }

    private void ApplyVirtualOnAttackEffectsFromData(
        List<VirtualBattleUnitSnap> working,
        CardController sourceCard,
        CardData data,
        CardController defenderUnit,
        EnemyAiEffectPickContext ctx)
    {
        if (working == null || sourceCard == null || data == null || defenderUnit == null)
        {
            return;
        }

        List<EffectData> effects = GetEffectsByTiming(data, EffectTiming.OnAttack);
        if (effects == null || effects.Count == 0)
        {
            return;
        }

        List<EffectData> applicable = new List<EffectData>();
        for (int i = 0; i < effects.Count; i++)
        {
            EffectData effect = effects[i];
            if (effect == null)
            {
                continue;
            }

            if (effect.target == TargetType.EnemyUnit
                || (effect.target == TargetType.RestEnemyUnit && defenderUnit.IsRestState))
            {
                applicable.Add(effect);
                continue;
            }

            if (effect.target == TargetType.EnemyAllUnits)
            {
                applicable.Add(effect);
            }
        }

        if (applicable.Count == 0)
        {
            return;
        }

        ApplyEnemyHandCommandVirtualEffects(working, applicable, sourceCard, PlayerType.Enemy, ctx);
    }

    private int ScoreEnemyAiBestOnActionCommandOutlookOnSnaps(
        List<VirtualBattleUnitSnap> snaps,
        EnemyAiDeployResourceBudget reserve,
        CardController excludeCard,
        CardController preferredAttacker)
    {
        List<CardController> commands = CollectEligibleEnemyHandCommandsForEnemyAiSim();
        if (commands.Count == 0)
        {
            return 0;
        }

        int best = 0;
        for (int i = 0; i < commands.Count; i++)
        {
            CardController cmd = commands[i];
            if (cmd == null || cmd == excludeCard)
            {
                continue;
            }

            Gundam2024RuleScript.PlayerState state = GetRuleState(Gundam2024RuleScript.PlayerSide.Enemy);
            if (!CanEnemyAffordHandDeployWithReserve(state, Gundam2024RuleScript.PlayerSide.Enemy, cmd, reserve))
            {
                continue;
            }

            List<EffectData> effects = GetEffectsByTiming(cmd.Data, EffectTiming.OnAction);
            if (effects == null || effects.Count == 0)
            {
                continue;
            }

            List<CardController> restTargets = GetEnemyAiRestTargets(PlayerType.Enemy);
            EnemyAiEffectPickContext pickCtx = BuildEnemyAiEffectPickContext(
                PlayerType.Enemy,
                cmd,
                preferredAttacker,
                restTargets);

            int score = ScoreEnemyHandCommandBenefitOnSnaps(snaps, cmd, effects, pickCtx, preferredAttacker);
            if (score > best)
            {
                best = score;
            }

            if (preferredAttacker == null && restTargets.Count > 0)
            {
                int generic = ScoreEnemyHandCommandBenefitOnSnaps(snaps, cmd, effects, pickCtx, null);
                if (generic > best)
                {
                    best = generic;
                }
            }
        }

        List<CardController> attackers = CollectEnemyVirtualAttackers(preferredAttacker, excludeCard);
        for (int a = 0; a < attackers.Count; a++)
        {
            CardController attacker = attackers[a];
            if (attacker == null)
            {
                continue;
            }

            for (int i = 0; i < commands.Count; i++)
            {
                CardController cmd = commands[i];
                if (cmd == null || cmd == excludeCard)
                {
                    continue;
                }

                Gundam2024RuleScript.PlayerState state = GetRuleState(Gundam2024RuleScript.PlayerSide.Enemy);
                if (!CanEnemyAffordHandDeployWithReserve(state, Gundam2024RuleScript.PlayerSide.Enemy, cmd, reserve))
                {
                    continue;
                }

                List<EffectData> effects = GetEffectsByTiming(cmd.Data, EffectTiming.OnAction);
                if (effects == null || effects.Count == 0)
                {
                    continue;
                }

                List<CardController> restTargets = GetEnemyAiRestTargets(PlayerType.Enemy);
                EnemyAiEffectPickContext pickCtx = BuildEnemyAiEffectPickContext(
                    PlayerType.Enemy,
                    cmd,
                    attacker,
                    restTargets);
                int score = ScoreEnemyHandCommandBenefitOnSnaps(snaps, cmd, effects, pickCtx, attacker);
                if (score > best)
                {
                    best = score;
                }
            }
        }

        return best;
    }
}
