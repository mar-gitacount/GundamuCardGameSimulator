using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ST12-011 ミリアルド・ピースクラフト（カード名「ゼクス・マーキス」としても扱う）。
/// 【起動・アクション】【ターン1回】【強制】
/// このユニットとバトルしている、ダメージを受けている相手のユニット1つに 2 ダメージ。
/// </summary>
public partial class BattleGameMain
{
    private const int MilliardoPeacecraftCardId = 1000664;

    private static bool IsMilliardoPeacecraftCard(CardController source)
    {
        return source?.Data != null && source.Data.id == MilliardoPeacecraftCardId;
    }

    /// <summary>アクション UI オープン前に交戦ペアを攻撃フローへ載せる。</summary>
    private void PrepareOnActionCombatContextForUi(CardController attackingUnitInAttackFlow)
    {
        if (attackingUnitInAttackFlow != null && attackingUnitInAttackFlow.CurrentHp > 0)
        {
            attackFlowAttackerUnit = attackingUnitInAttackFlow;
            pendingUnitAttackAttacker = attackingUnitInAttackFlow;
            AssignBattleInstanceIdIfNeeded(attackingUnitInAttackFlow);
        }

        SyncActionStepCombatPairToAttackFlow();

        if (_actionStepSession != null && _actionStepSession.IsAttackContext)
        {
            if ((attackFlowAttackerUnit == null || attackFlowAttackerUnit.CurrentHp <= 0)
                && _actionStepSession.AttackingUnit != null
                && _actionStepSession.AttackingUnit.CurrentHp > 0)
            {
                attackFlowAttackerUnit = _actionStepSession.AttackingUnit;
                pendingUnitAttackAttacker = _actionStepSession.AttackingUnit;
            }

            if (attackFlowBlockRedirectUnit == null
                && (attackFlowDeclaredDefenderUnit == null || attackFlowDeclaredDefenderUnit.CurrentHp <= 0)
                && _actionStepSession.DefendingUnit != null
                && _actionStepSession.DefendingUnit.CurrentHp > 0)
            {
                attackFlowDeclaredDefenderUnit = _actionStepSession.DefendingUnit;
            }
        }
    }

    private void SyncActionStepCombatPairToAttackFlow()
    {
        if (_actionStepSession == null || !_actionStepSession.IsAttackContext)
        {
            return;
        }

        if ((attackFlowAttackerUnit == null || attackFlowAttackerUnit.CurrentHp <= 0)
            && _actionStepSession.AttackingUnit != null
            && _actionStepSession.AttackingUnit.CurrentHp > 0)
        {
            attackFlowAttackerUnit = _actionStepSession.AttackingUnit;
            pendingUnitAttackAttacker = _actionStepSession.AttackingUnit;
        }

        if (attackFlowBlockRedirectUnit == null
            && (attackFlowDeclaredDefenderUnit == null || attackFlowDeclaredDefenderUnit.CurrentHp <= 0)
            && _actionStepSession.DefendingUnit != null
            && _actionStepSession.DefendingUnit.CurrentHp > 0)
        {
            attackFlowDeclaredDefenderUnit = _actionStepSession.DefendingUnit;
        }
    }

    private void ResolveMilliardoActionCombatPair(
        out CardController attacker,
        out CardController defender)
    {
        SyncActionStepCombatPairToAttackFlow();

        attacker = attackFlowAttackerUnit != null && attackFlowAttackerUnit.CurrentHp > 0
            ? attackFlowAttackerUnit
            : null;
        if (attacker == null
            && pendingUnitAttackAttacker != null
            && pendingUnitAttackAttacker.CurrentHp > 0)
        {
            attacker = pendingUnitAttackAttacker;
        }

        if (attacker == null
            && _actionStepSession != null
            && _actionStepSession.IsAttackContext
            && _actionStepSession.AttackingUnit != null
            && _actionStepSession.AttackingUnit.CurrentHp > 0)
        {
            attacker = _actionStepSession.AttackingUnit;
        }

        // ブロック確定後のみブロッカーを最終相手にする（未確定の残り参照で被攻撃時に外れるのを防ぐ）
        defender = attackFlowBlockRedirectEngaged
            && attackFlowBlockRedirectUnit != null
            && attackFlowBlockRedirectUnit.Data != null
            && attackFlowBlockRedirectUnit.Data.IsUnitLike()
            && attackFlowBlockRedirectUnit.CurrentHp > 0
            ? attackFlowBlockRedirectUnit
            : null;
        if (defender == null
            && attackFlowDeclaredDefenderUnit != null
            && attackFlowDeclaredDefenderUnit.Data != null
            && attackFlowDeclaredDefenderUnit.Data.IsUnitLike()
            && attackFlowDeclaredDefenderUnit.CurrentHp > 0)
        {
            defender = attackFlowDeclaredDefenderUnit;
        }

        if (defender == null
            && _actionStepSession != null
            && _actionStepSession.IsAttackContext
            && _actionStepSession.DefendingUnit != null
            && _actionStepSession.DefendingUnit.Data != null
            && _actionStepSession.DefendingUnit.Data.IsUnitLike()
            && _actionStepSession.DefendingUnit.CurrentHp > 0)
        {
            defender = _actionStepSession.DefendingUnit;
        }
    }

    /// <summary>ホスト or 交戦ユニットにこのパイロットが乗っていれば同一扱い。</summary>
    private bool IsMilliardoHostInCombatUnit(CardController milliardoSource, CardController combatUnit)
    {
        if (milliardoSource == null || combatUnit == null || combatUnit.CurrentHp <= 0)
        {
            return false;
        }

        CardController host = ResolveEffectSourceBattleHost(milliardoSource);
        if (host != null && host.CurrentHp > 0 && IsSameBattleUnit(host, combatUnit))
        {
            return true;
        }

        CardController mounted = combatUnit.MountedPilot;
        if (mounted == null)
        {
            return false;
        }

        if (ReferenceEquals(mounted, milliardoSource))
        {
            return true;
        }

        return milliardoSource.BattleInstanceId > 0
            && mounted.BattleInstanceId == milliardoSource.BattleInstanceId;
    }

    private CardController ResolveMilliardoForcedDamageTarget(CardController milliardoSource)
    {
        if (!IsMilliardoPeacecraftCard(milliardoSource))
        {
            return null;
        }

        // シールド攻撃のみ（ユニットへのブロックリダイレクトなし）では発動不可
        if (attackFlowStrikeKind == AttackFlowStrikeKind.Shield
            && !attackFlowBlockRedirectEngaged)
        {
            return null;
        }

        ResolveMilliardoActionCombatPair(out CardController attacker, out CardController defender);
        if (attacker == null || defender == null)
        {
            return null;
        }

        // 防衛側がユニットでない（シールド宣言のまま等）場合は不可
        if (defender.Data == null || !defender.Data.IsUnitLike())
        {
            return null;
        }

        CardController host = ResolveEffectSourceBattleHost(milliardoSource);
        if (host == null || host.CurrentHp <= 0 || !host.Data.IsUnitLike())
        {
            return null;
        }

        CardController battlingEnemy = null;
        if (IsMilliardoHostInCombatUnit(milliardoSource, attacker))
        {
            battlingEnemy = defender;
        }
        else if (IsMilliardoHostInCombatUnit(milliardoSource, defender))
        {
            battlingEnemy = attacker;
        }
        else
        {
            return null;
        }

        if (battlingEnemy.Data == null
            || !battlingEnemy.Data.IsUnitLike()
            || battlingEnemy.CurrentHp <= 0
            || !battlingEnemy.IsDamagedForWhileDamagedEffects())
        {
            return null;
        }

        return battlingEnemy;
    }

    /// <summary>【ターン1回】キーは搭乗ホストユニット（パイロット単体ではなくユニット単位）。</summary>
    private CardController ResolveMilliardoOncePerTurnKeyCard(CardController milliardoSource)
    {
        if (!IsMilliardoPeacecraftCard(milliardoSource))
        {
            return milliardoSource;
        }

        CardController host = ResolveEffectSourceBattleHost(milliardoSource);
        return host != null ? host : milliardoSource;
    }

    private bool CanActivateMilliardoPeacecraftOnAction(CardController source)
    {
        return ResolveMilliardoForcedDamageTarget(source) != null;
    }

    private bool TryApplyMilliardoPeacecraftOnActionDamage(
        CardController milliardoSource,
        PlayerType side,
        EffectData effect)
    {
        if (!IsMilliardoPeacecraftCard(milliardoSource) || effect == null || effect.type != EffectType.Damage)
        {
            return false;
        }

        CardController target = ResolveMilliardoForcedDamageTarget(milliardoSource);
        if (target == null || target.CurrentHp <= 0)
        {
            return false;
        }

        int damageAmount = effect.value > 0 ? effect.value : 2;
        damageAmount = ResolveEffectDamageAmount(damageAmount, target, side);
        if (damageAmount <= 0)
        {
            return false;
        }

        bool nestedBatch = _onlineEffectSyncActive;
        if (!nestedBatch)
        {
            BeginOnlineEffectSyncBatch(side);
        }

        List<EffectDestroyWatcher> destroyWatchers = CollectEffectDestroyWatchers();
        ApplyUnitDamageAndTrackChain(target, damageAmount);
        QueueOnlineUnitDamage(target, ResolveUnitKillSourceForTrash(milliardoSource, target));

        if (target.CurrentHp <= 0)
        {
            NotifyOwnerEffectUnitsDestroyed(
                side,
                milliardoSource,
                destroyWatchers,
                new List<CardController> { target });
            NotifyAttackFlowParticipantRemovedDuringOnAction(target);
            SendCardToTrash(
                target,
                ResolveCardOwner(target.transform),
                ResolveUnitKillSourceForTrash(milliardoSource, target),
                destroyedByEffectDamage: true);
        }

        if (!nestedBatch)
        {
            FlushOnlineEffectSyncBatch();
        }

        SyncAllResourceViewsFromRule();
        return true;
    }
}
