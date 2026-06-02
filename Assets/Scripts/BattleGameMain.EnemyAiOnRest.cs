using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// エネミー AI：OnRest / OnBaseDeployed を仮想盤面で評価し、有利なときだけ実行する。
/// </summary>
public partial class BattleGameMain
{
    private const int EnemyAiOnRestMinPlanBenefitToExecute = 1;
    private const int EnemyAiOnRestBonusNewKill = 45;
    private const int EnemyAiOnBaseDeployedMinPlanBenefit = 1;

    /// <summary>攻撃ループ前に、シミュ上で得になる OnRest があれば1件実行する。</summary>
    private bool TryEnemyExecuteScoredOnRestBeforeAttacks()
    {
        if (isMatchFinished || currentPlayerType != PlayerType.Enemy || gundamRule == null)
        {
            return false;
        }

        List<CardController> candidates = CollectEnemyAiOnRestCandidates();
        if (candidates.Count == 0)
        {
            return false;
        }

        List<VirtualBattleUnitSnap> baselineSnaps = BuildFullBattleVirtualSnapshot();
        int baselinePlan = ScoreEnemyAiTurnAttackPlan(baselineSnaps);
        bool baselineHasKill = EnemyAiVirtualPlanHasPlayerUnitKill(baselineSnaps);

        CardController best = null;
        int bestBenefit = int.MinValue;
        int bestAfterPlan = int.MinValue;
        bool bestEnablesNewKill = false;

        for (int i = 0; i < candidates.Count; i++)
        {
            CardController candidate = candidates[i];
            if (candidate == null || candidate.Data == null)
            {
                continue;
            }

            List<VirtualBattleUnitSnap> afterSnaps = CloneVirtualBattleSnaps(baselineSnaps);
            ApplyEnemyAiTimedEffectsToVirtualSnaps(afterSnaps, candidate, PlayerType.Enemy, EffectTiming.OnRest);
            int afterPlan = ScoreEnemyAiTurnAttackPlan(afterSnaps);
            bool afterHasKill = EnemyAiVirtualPlanHasPlayerUnitKill(afterSnaps);
            bool newKill = afterHasKill && !baselineHasKill;
            int benefit = afterPlan - baselinePlan;
            if (newKill)
            {
                benefit += EnemyAiOnRestBonusNewKill;
            }

            if (benefit > bestBenefit)
            {
                bestBenefit = benefit;
                best = candidate;
                bestAfterPlan = afterPlan;
                bestEnablesNewKill = newKill;
            }
        }

        if (best == null || !ShouldEnemyAiExecuteOnRestBySimulation(bestBenefit, bestEnablesNewKill))
        {
            Debug.Log(
                $"[EnemyAI] OnRest skip (no beneficial activation) baselinePlan:{baselinePlan} "
                + $"bestBenefit:{bestBenefit} min:{EnemyAiOnRestMinPlanBenefitToExecute}");
            return false;
        }

        Debug.Log(
            $"[EnemyAI] OnRest execute {best.Data.cardName}(id:{best.Data.id}) "
            + $"baselinePlan:{baselinePlan} afterPlan:{bestAfterPlan} benefit:{bestBenefit} newKill:{bestEnablesNewKill}");
        EnemyAiActivateOnRestSync(best);
        return true;
    }

    private static bool ShouldEnemyAiExecuteOnRestBySimulation(int benefit, bool enablesNewKill)
    {
        if (enablesNewKill)
        {
            return true;
        }

        return benefit >= EnemyAiOnRestMinPlanBenefitToExecute;
    }

    private List<CardController> CollectEnemyAiOnRestCandidates()
    {
        List<CardController> list = new List<CardController>();
        CardGameRule rule = enemyCardGameRule;
        if (rule == null)
        {
            return list;
        }

        TryAddEnemyAiOnRestCandidate(list, GetDeployedBaseForRuleSide(Gundam2024RuleScript.PlayerSide.Enemy));
        if (rule.BaseSlotContent != null)
        {
            for (int i = 0; i < rule.BaseSlotContent.childCount; i++)
            {
                TryAddEnemyAiOnRestCandidate(list, rule.BaseSlotContent.GetChild(i).GetComponent<CardController>());
            }
        }

        if (rule.ShieldCardsContent != null)
        {
            for (int i = 0; i < rule.ShieldCardsContent.childCount; i++)
            {
                CardController shieldCard = rule.ShieldCardsContent.GetChild(i).GetComponent<CardController>();
                if (!IsVisibleBaseInShieldZone(shieldCard))
                {
                    continue;
                }

                TryAddEnemyAiOnRestCandidate(list, shieldCard);
            }
        }

        for (int i = 0; i < enemyBattleZoneCards.Count; i++)
        {
            TryAddEnemyAiOnRestCandidate(list, enemyBattleZoneCards[i]);
        }

        return list;
    }

    private void TryAddEnemyAiOnRestCandidate(List<CardController> list, CardController card)
    {
        if (card == null || list.Contains(card))
        {
            return;
        }

        if (!EnemyAiCanActivateOnRest(card))
        {
            return;
        }

        list.Add(card);
    }

    private bool EnemyAiCanActivateOnRest(CardController source)
    {
        if (source == null || source.Data == null || !HasEffectTiming(source.Data, EffectTiming.OnRest))
        {
            return false;
        }

        if (!CanUseOnRestAtCardLocation(source, PlayerType.Enemy))
        {
            return false;
        }

        if (source.IsRestState)
        {
            return false;
        }

        int turnIndex = gundamRule != null ? gundamRule.TurnIndex : -1;
        return !onRestActivatedTurnByCard.TryGetValue(source, out int activatedTurn) || activatedTurn != turnIndex;
    }

    private void EnemyAiActivateOnRestSync(CardController source)
    {
        if (source == null)
        {
            return;
        }

        source.SetAttackFlg(AttackFlg.False);
        source.SetUnitRestVisual(true);
        if (gundamRule != null)
        {
            onRestActivatedTurnByCard[source] = gundamRule.TurnIndex;
        }

        ApplyEnemyTimedEffectsSync(source, PlayerType.Enemy, EffectTiming.OnRest);
        SyncAllResourceViewsFromRule();
    }

    private List<EffectData> CollectEnemyAiResolvedTimedEffects(
        CardController source,
        PlayerType ownerType,
        EffectTiming timing)
    {
        List<EffectData> result = new List<EffectData>();
        if (source == null || source.Data == null || source.Data.timedEffects == null)
        {
            return result;
        }

        EffectActivationContext activationContext = BuildActivationContext(ownerType, source);
        for (int i = 0; i < source.Data.timedEffects.Count; i++)
        {
            TimedEffectData timed = source.Data.timedEffects[i];
            if (timed == null || timed.timing != timing || !timed.HasResolvedEffects())
            {
                continue;
            }

            if (!EffectActivationEvaluator.AreTimedConditionsMet(timed, activationContext))
            {
                continue;
            }

            IReadOnlyList<EffectData> resolved = timed.GetResolvedEffects();
            for (int j = 0; j < resolved.Count; j++)
            {
                if (resolved[j] != null)
                {
                    result.Add(resolved[j]);
                }
            }
        }

        return result;
    }

    private void ApplyEnemyTimedEffectsSync(CardController source, PlayerType ownerType, EffectTiming timing)
    {
        List<EffectData> effects = CollectEnemyAiResolvedTimedEffects(source, ownerType, timing);
        if (effects.Count == 0)
        {
            return;
        }

        EnemyAiEffectPickContext pickCtx = BuildEnemyAiEffectPickContext(ownerType, source, null, null);
        for (int i = 0; i < effects.Count; i++)
        {
            EffectData effect = effects[i];
            if (effect == null)
            {
                continue;
            }

            if (EffectRequiresManualUnitSelection(effect))
            {
                List<CardController> candidates = ResolveSelectableEffectTargets(source, ownerType, effect);
                CardController picked = PickEnemyAiEffectTarget(effect, pickCtx, candidates);
                if (picked != null)
                {
                    ApplyEffectToSpecificTargets(
                        source,
                        ownerType,
                        effect,
                        new List<CardController> { picked });
                }

                continue;
            }

            ApplyEffect(source, ownerType, effect);
        }
    }

    private void ApplyEnemyAiTimedEffectsToVirtualSnaps(
        List<VirtualBattleUnitSnap> working,
        CardController source,
        PlayerType ownerType,
        EffectTiming timing)
    {
        if (working == null || source == null)
        {
            return;
        }

        List<EffectData> effects = CollectEnemyAiResolvedTimedEffects(source, ownerType, timing);
        if (effects.Count == 0)
        {
            return;
        }

        EnemyAiEffectPickContext pickCtx = BuildEnemyAiEffectPickContext(ownerType, source, null, null);
        for (int i = 0; i < effects.Count; i++)
        {
            EffectData effect = effects[i];
            if (effect == null)
            {
                continue;
            }

            int magnitude = ResolveEffectMagnitude(effect, ownerType, source);
            if (magnitude == 0)
            {
                continue;
            }

            List<CardController> targets;
            if (EffectRequiresManualUnitSelection(effect))
            {
                targets = PickEnemyAiEffectTargets(effect, pickCtx, null, singleOnly: true);
            }
            else
            {
                targets = ResolveEffectTargets(source, ownerType, effect);
            }

            if (targets == null || targets.Count == 0)
            {
                continue;
            }

            ApplyVirtualBattleEffectToTargetsOnSnaps(working, effect, targets, magnitude);
        }
    }

    /// <summary>仮想盤面での「このターンの攻撃方針」スコア（高いほど敵に有利）。</summary>
    private int ScoreEnemyAiTurnAttackPlan(List<VirtualBattleUnitSnap> snaps)
    {
        if (snaps == null || gundamRule == null)
        {
            return int.MinValue / 4;
        }

        int score = ComputeEnemyAiFieldAdvantageScore(snaps);
        List<CardController> restTargets = GetEnemyAiRestTargets(PlayerType.Enemy);
        bool playerProtected = gundamRule.HasShieldZoneProtection(Gundam2024RuleScript.PlayerSide.Player);

        List<CardController> snapshot = new List<CardController>(enemyBattleZoneCards);
        for (int i = 0; i < snapshot.Count; i++)
        {
            CardController unit = snapshot[i];
            if (unit == null || unit.Data == null || unit.Data.type != Type.Unit)
            {
                continue;
            }

            if (unit.AttackFlgState != AttackFlg.True)
            {
                continue;
            }

            GetVirtualUnitCombatStats(unit, snaps, out int atkAp, out int atkHp);
            if (atkAp <= 0)
            {
                continue;
            }

            if (restTargets.Count > 0)
            {
                int bestUnitAttack = int.MinValue;
                for (int t = 0; t < restTargets.Count; t++)
                {
                    CardController target = restTargets[t];
                    if (target == null || target.Data == null)
                    {
                        continue;
                    }

                    int exchange = ScoreVirtualUnitAttackExchange(atkAp, atkHp, target, snaps);
                    if (exchange > bestUnitAttack)
                    {
                        bestUnitAttack = exchange;
                    }
                }

                if (bestUnitAttack > int.MinValue / 4)
                {
                    score = Mathf.Max(score, bestUnitAttack);
                }
            }

            if (!unit.Data.isNotDirectAttack && playerProtected)
            {
                int shieldScore = ScoreVirtualShieldAttackHeuristic(atkAp, gundamRule.Player);
                score = Mathf.Max(score, shieldScore);
            }
        }

        return score;
    }

    private static void GetVirtualUnitCombatStats(
        CardController unit,
        List<VirtualBattleUnitSnap> snaps,
        out int ap,
        out int hp)
    {
        ap = unit != null ? unit.CurrentPower : 0;
        hp = unit != null ? unit.CurrentHp : 0;
        if (unit == null || snaps == null)
        {
            return;
        }

        VirtualBattleUnitSnap snap = FindBattleVirtualSnap(snaps, unit);
        if (snap == null)
        {
            return;
        }

        ap = snap.Ap;
        hp = snap.Hp;
    }

    private static int ScoreVirtualUnitAttackExchange(
        int attackerAp,
        int attackerHp,
        CardController playerTarget,
        List<VirtualBattleUnitSnap> snaps)
    {
        if (playerTarget == null || playerTarget.Data == null)
        {
            return int.MinValue / 4;
        }

        GetVirtualUnitCombatStats(playerTarget, snaps, out int defAp, out int defHp);
        int score = defAp * 2 - defHp;
        int playerHpAfter = Mathf.Max(0, defHp - attackerAp);
        int enemyHpAfter = Mathf.Max(0, attackerHp - defAp);
        if (playerHpAfter <= 0)
        {
            score += EnemyAiAttackScoreBonusRawKillPlayer;
        }

        if (enemyHpAfter <= 0 && playerHpAfter > 0)
        {
            score -= EnemyAiAttackScorePenaltyOneSidedEnemyDeath;
        }

        return score;
    }

    private static int ScoreVirtualShieldAttackHeuristic(int attackerAp, Gundam2024RuleScript.PlayerState defender)
    {
        if (defender == null || attackerAp <= 0)
        {
            return int.MinValue / 4;
        }

        int score = 22 + attackerAp * 2;
        if (defender.exBase > 0)
        {
            score += Mathf.Min(attackerAp * 3, 24);
        }
        else if (defender.shield > 0)
        {
            score += 18;
        }

        return score;
    }

    private bool EnemyAiVirtualPlanHasPlayerUnitKill(List<VirtualBattleUnitSnap> snaps)
    {
        if (snaps == null)
        {
            return false;
        }

        List<CardController> restTargets = GetEnemyAiRestTargets(PlayerType.Enemy);
        if (restTargets.Count == 0)
        {
            return false;
        }

        List<CardController> snapshot = new List<CardController>(enemyBattleZoneCards);
        for (int i = 0; i < snapshot.Count; i++)
        {
            CardController unit = snapshot[i];
            if (unit == null || unit.Data == null || unit.Data.type != Type.Unit)
            {
                continue;
            }

            if (unit.AttackFlgState != AttackFlg.True)
            {
                continue;
            }

            GetVirtualUnitCombatStats(unit, snaps, out int atkAp, out _);
            if (atkAp <= 0)
            {
                continue;
            }

            for (int t = 0; t < restTargets.Count; t++)
            {
                CardController target = restTargets[t];
                if (target == null)
                {
                    continue;
                }

                GetVirtualUnitCombatStats(target, snaps, out _, out int defHp);
                if (defHp > 0 && defHp <= atkAp)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>ベース配備候補の OnBaseDeployed を仮想適用した盤面スコア（配備 AI 用）。</summary>
    private int ScoreEnemyAiBoardWithOnBaseDeployedEffects(
        CardController baseCandidate,
        List<VirtualBattleUnitSnap> fieldSnaps)
    {
        int score = ComputeEnemyAiFieldAdvantageScore(fieldSnaps);
        score += ScoreEnemyAiDefensiveZoneValue(Gundam2024RuleScript.PlayerSide.Enemy, baseCandidate);

        if (baseCandidate == null)
        {
            return score;
        }

        List<VirtualBattleUnitSnap> afterEffects = CloneVirtualBattleSnaps(fieldSnaps);
        ApplyEnemyAiTimedEffectsToVirtualSnaps(
            afterEffects,
            baseCandidate,
            PlayerType.Enemy,
            EffectTiming.OnBaseDeployed);
        int afterField = ComputeEnemyAiFieldAdvantageScore(afterEffects);
        int effectBenefit = afterField - ComputeEnemyAiFieldAdvantageScore(fieldSnaps);
        if (effectBenefit < 0)
        {
            effectBenefit = 0;
        }

        if (EnemyAiVirtualPlanHasPlayerUnitKill(afterEffects)
            && !EnemyAiVirtualPlanHasPlayerUnitKill(fieldSnaps))
        {
            effectBenefit += EnemyAiOnRestBonusNewKill;
        }

        return score + effectBenefit;
    }
}
