using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ST13-012 スレッタ・マーキュリー:
/// 「このユニットがアタックしている間、相手のユニットが効果ダメージで破壊されたとき、1枚ドロー」。
/// Close Combat 等で destroyedBy がユニットにならない場合も、攻撃中ユニット＋搭乗パイロットを監視する。
/// </summary>
public partial class BattleGameMain
{
    /// <summary>
    /// 効果ダメージで敵ユニットが破壊されたとき、現在アタック中のユニット／パイロットの
    /// OnEnemyUnitDestroyed（DestroyedByEffectDamage）を解決する。
    /// キル元がアタッカー自身のときは TriggerOnEnemyUnitDestroyedEffects 側で解決済みのためスキップ。
    /// </summary>
    private void TriggerWhileAttackingEffectDamageDestroyWatch(
        CardController destroyedUnit,
        PlayerType destroyedOwner,
        bool destroyedByEffectDamage,
        CardController destroyedBy,
        Action onComplete)
    {
        if (!destroyedByEffectDamage
            || destroyedUnit == null
            || destroyedUnit.Data == null
            || !destroyedUnit.Data.IsUnitLike())
        {
            onComplete?.Invoke();
            return;
        }

        CardController attacker = ResolveCurrentAttackFlowAttackerUnit();
        if (attacker == null || attacker.Data == null || attacker.CurrentHp <= 0)
        {
            onComplete?.Invoke();
            return;
        }

        PlayerType attackerOwner = ResolveCardOwner(attacker.transform);
        if (attackerOwner == destroyedOwner)
        {
            onComplete?.Invoke();
            return;
        }

        // キル元ユニット＝アタッカーなら既存の OnEnemyUnitDestroyed 経路で解決済み
        if (destroyedBy != null
            && destroyedBy.Data != null
            && destroyedBy.Data.IsUnitLike()
            && IsSameBattleUnit(destroyedBy, attacker))
        {
            onComplete?.Invoke();
            return;
        }

        EffectActivationContext activationContext = new EffectActivationContext(
            attackerOwner,
            attacker,
            playerBattleZoneCards,
            enemyBattleZoneCards,
            CollectHandControllers(cardGameRule),
            CollectHandControllers(enemyCardGameRule),
            isOwnerTurn: attackerOwner == currentPlayerType,
            mountHostUnit: attacker,
            mountedPilot: attacker.MountedPilot,
            observedCards: GetActiveObservedCardsForActivation(),
            ownerTrashCardIds: cardGameRule.GetTrashCardIds(),
            opponentTrashCardIds: enemyCardGameRule.GetTrashCardIds(),
            priorChainDealtDamage: GetEffectChainDealtDamage(),
            destroyingCard: destroyedBy,
            hasDestroyingCardOwner: destroyedBy != null,
            destroyingCardOwner: destroyedBy != null
                ? ResolveCardOwner(destroyedBy.transform)
                : default,
            destroyedByBattleDamage: false,
            destroyedByEffectDamage: true,
            sourceAttackingEnemyUnit: IsSourceAttackingEnemyUnit(attacker, allowDestroyedDefender: true),
            sourceAttackingEnemyPlayer: IsSourceAttackingEnemyPlayer(attacker));

        List<TimedEffectData> unitBlocks = new List<TimedEffectData>();
        List<TimedEffectData> pilotBlocks = new List<TimedEffectData>();
        AppendWhileAttackingEffectDamageDestroyBlocks(attacker, activationContext, unitBlocks);
        CardController attackerPilot = attacker.MountedPilot;
        if (attackerPilot != null)
        {
            AppendWhileAttackingEffectDamageDestroyBlocks(attackerPilot, activationContext, pilotBlocks);
        }

        if (unitBlocks.Count == 0 && pilotBlocks.Count == 0)
        {
            onComplete?.Invoke();
            return;
        }

        string pilotName = attackerPilot?.Data != null ? attackerPilot.Data.cardName : "-";
        Debug.Log(
            $"[WhileAttackingEffectDmgDestroy] attacker:{attacker.Data.cardName}(id:{attacker.Data.id}) "
            + $"pilot:{pilotName} destroyed:{destroyedUnit.Data.cardName}(id:{destroyedUnit.Data.id}) "
            + $"unitBlocks:{unitBlocks.Count} pilotBlocks:{pilotBlocks.Count}");

        ResolveUnitPilotEffectOrder(
            attackerOwner,
            attacker,
            attackerPilot,
            unitBlocks,
            pilotBlocks,
            attacker.Data,
            ordered =>
            {
                if (ordered == null || ordered.Count == 0)
                {
                    onComplete?.Invoke();
                    return;
                }

                RunOrderedOnEnemyUnitDestroyedEntries(attacker, attackerOwner, ordered, 0, onComplete);
            });
    }

    private void AppendWhileAttackingEffectDamageDestroyBlocks(
        CardController effectSource,
        EffectActivationContext activationContext,
        List<TimedEffectData> blocks)
    {
        CardData data = effectSource?.Data;
        if (data?.timedEffects == null || blocks == null)
        {
            return;
        }

        for (int i = 0; i < data.timedEffects.Count; i++)
        {
            TimedEffectData timed = data.timedEffects[i];
            if (timed == null || !timed.IsOnEnemyUnitDestroyedResolutionBlock())
            {
                continue;
            }

            if (!TimedHasDestroyedByEffectDamageCondition(timed)
                || !TimedHasSourceIsAttackingCondition(timed))
            {
                continue;
            }

            if (timed.oncePerTurn && HasUsedPaidActivationThisTurn(activationContext.OwnerType, effectSource, i))
            {
                continue;
            }

            if (!EffectActivationEvaluator.AreTimedConditionsMet(timed, activationContext))
            {
                continue;
            }

            blocks.Add(timed);
        }
    }

    private static bool TimedHasDestroyedByEffectDamageCondition(TimedEffectData timed)
    {
        return TimedHasActivationCheckKind(timed, EffectActivationCheckKind.DestroyedByEffectDamage);
    }

    private static bool TimedHasSourceIsAttackingCondition(TimedEffectData timed)
    {
        return TimedHasActivationCheckKind(timed, EffectActivationCheckKind.SourceIsAttacking);
    }

    private static bool TimedHasActivationCheckKind(
        TimedEffectData timed,
        EffectActivationCheckKind kind)
    {
        List<EffectActivationCondition> conditions = timed?.activationConditions;
        if (conditions == null || conditions.Count == 0)
        {
            return false;
        }

        for (int i = 0; i < conditions.Count; i++)
        {
            EffectActivationCondition c = conditions[i];
            if (c != null && c.checkKind == kind)
            {
                return true;
            }
        }

        return false;
    }

    private CardController ResolveCurrentAttackFlowAttackerUnit()
    {
        if (attackFlowAttackerUnit != null
            && attackFlowAttackerUnit.Data != null
            && attackFlowAttackerUnit.Data.IsUnitLike())
        {
            return attackFlowAttackerUnit;
        }

        if (IsActionStepSessionActive
            && _actionStepSession != null
            && _actionStepSession.AttackingUnit != null
            && _actionStepSession.AttackingUnit.Data != null)
        {
            return _actionStepSession.AttackingUnit;
        }

        if (pendingUnitAttackAttacker != null
            && pendingUnitAttackAttacker.Data != null
            && pendingUnitAttackAttacker.Data.IsUnitLike())
        {
            return pendingUnitAttackAttacker;
        }

        return null;
    }
}
