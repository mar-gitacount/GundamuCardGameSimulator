using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Operation Meteor 先制攻撃の付与とユニット戦ダメージ解決。
/// </summary>
public partial class BattleGameMain
{
    /// <summary>
    /// 〔オペレーション・メテオ〕は特徴名のみ。搭乗だけで《先制攻撃》は付与しない。
    /// （デスサイズ等の「セット時に先制を得る」は各カードの timedEffects で実装する）
    /// 旧実装はヒイロ等のメテオ・パイロット搭乗でも先制が付き、撃破時に反撃が乗らない不具合の原因だった。
    /// </summary>
    private void TryGrantOperationMeteorFirstStrikeOnPilotMount(
        CardController hostUnit,
        CardController pilot,
        PlayerType ownerType)
    {
        // no-op: 特徴〔オペレーション・メテオ〕≠自動先制付与
    }

    private void ClearFirstStrikeGrants(EffectDuration duration)
    {
        if (duration != EffectDuration.UntilEndOfTurn)
        {
            return;
        }

        ClearFirstStrikeGrantsOnZone(playerBattleZoneCards);
        ClearFirstStrikeGrantsOnZone(enemyBattleZoneCards);
        ClearMagicBulletOfDuskFirstStrikeGrants();
    }

    private static void ClearFirstStrikeGrantsOnZone(System.Collections.Generic.List<CardController> zone)
    {
        if (zone == null)
        {
            return;
        }

        for (int i = 0; i < zone.Count; i++)
        {
            zone[i]?.ClearFirstStrikeUntilEndOfTurnGrants();
        }
    }

    /// <summary>
    /// ユニット戦の先制判定。付与マーカーに加え、対戦相手を明示して条件付き《先制攻撃》を評価する。
    /// </summary>
    private bool UnitHasFirstStrikeInCombat(CardController unit, CardController battleOpponent)
    {
        if (unit == null)
        {
            return false;
        }

        if (unit.HasFirstStrike() || UnitHasMagicBulletOfDuskFirstStrike(unit))
        {
            return true;
        }

        return HasOnAttackFirstStrikeEffectActive(unit, battleOpponent);
    }

    /// <summary>
    /// OnAttack の《先制攻撃》効果を、現在のバトル相手・ターン条件付きで評価する（ZnO 等）。
    /// UntilEndOfBattle 付与マーカーは使わず、戦闘時に条件を再評価する。
    /// </summary>
    private bool HasOnAttackFirstStrikeEffectActive(CardController unit, CardController battleOpponent)
    {
        if (unit?.Data == null)
        {
            return false;
        }

        PlayerType owner = ResolveCardOwner(unit.transform);
        EffectActivationContext ctx = BuildOnAttackActivationContext(owner, unit, battleOpponent);
        if (HasOnAttackFirstStrikeInData(unit.Data, ctx))
        {
            return true;
        }

        CardController pilot = unit.MountedPilot;
        if (pilot?.Data == null)
        {
            return false;
        }

        return HasOnAttackFirstStrikeInData(pilot.Data, ctx);
    }

    private static bool HasOnAttackFirstStrikeInData(CardData data, EffectActivationContext ctx)
    {
        if (data?.timedEffects == null || ctx == null)
        {
            return false;
        }

        for (int i = 0; i < data.timedEffects.Count; i++)
        {
            TimedEffectData timed = data.timedEffects[i];
            if (timed == null || timed.timing != EffectTiming.OnAttack || !timed.HasResolvedEffects())
            {
                continue;
            }

            if (!EffectActivationEvaluator.AreTimedConditionsMet(timed, ctx))
            {
                continue;
            }

            IReadOnlyList<EffectData> effects = timed.GetResolvedEffects();
            for (int j = 0; j < effects.Count; j++)
            {
                EffectData effect = effects[j];
                if (effect != null
                    && effect.type == EffectType.FirstStrike
                    && effect.target == TargetType.Self)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// ユニット戦の HP 交換。先制のみの場合は攻撃側→防御側の順。先制撃破時は反撃ダメージなし。
    /// </summary>
    private static void ResolveUnitVsUnitCombatHpExchange(
        int attackerHpBefore,
        int defenderHpBefore,
        int attackerStrike,
        int defenderStrike,
        bool attackerFirstStrike,
        bool defenderFirstStrike,
        out int attackerHpAfter,
        out int defenderHpAfter)
    {
        attackerStrike = Mathf.Max(0, attackerStrike);
        defenderStrike = Mathf.Max(0, defenderStrike);

        if (attackerFirstStrike && !defenderFirstStrike)
        {
            defenderHpAfter = Mathf.Max(0, defenderHpBefore - attackerStrike);
            if (defenderHpAfter <= 0)
            {
                attackerHpAfter = attackerHpBefore;
                return;
            }

            attackerHpAfter = Mathf.Max(0, attackerHpBefore - defenderStrike);
            return;
        }

        if (defenderFirstStrike && !attackerFirstStrike)
        {
            attackerHpAfter = Mathf.Max(0, attackerHpBefore - defenderStrike);
            if (attackerHpAfter <= 0)
            {
                defenderHpAfter = defenderHpBefore;
                return;
            }

            defenderHpAfter = Mathf.Max(0, defenderHpBefore - attackerStrike);
            return;
        }

        defenderHpAfter = Mathf.Max(0, defenderHpBefore - attackerStrike);
        attackerHpAfter = Mathf.Max(0, attackerHpBefore - defenderStrike);
    }

    private void ApplyUnitVsUnitCombatDamageExchange(
        CardController attacker,
        CardController defender,
        PlayerType attackerOwner,
        PlayerType defenderOwner,
        int attackerStrike,
        int defenderStrike)
    {
        attackerStrike = Mathf.Max(0, attackerStrike);
        defenderStrike = Mathf.Max(0, defenderStrike);
        ClearCombatRedirectRecipientOnlineSync();

        int attackerHpBefore = attacker != null ? attacker.CurrentHp : 0;
        int defenderHpBefore = defender != null ? defender.CurrentHp : 0;
        bool attackerFirstStrike = UnitHasFirstStrikeInCombat(attacker, defender);
        bool defenderFirstStrike = UnitHasFirstStrikeInCombat(defender, attacker);

        if (attackerFirstStrike && !defenderFirstStrike)
        {
            ApplyIncomingBattleStrike(defender, attacker, defenderOwner, attackerOwner, attackerStrike);
            if (defender != null && defender.CurrentHp <= 0)
            {
                Debug.Log(
                    $"[FirstStrike] {attacker?.Data?.cardName} 先制撃破 → {defender?.Data?.cardName} "
                    + $"HP:{defenderHpBefore}->{defender.CurrentHp}（反撃なし）");
                RestoreRedirectHostHpIfAbsorbed(attacker, attackerHpBefore);
                RestoreRedirectHostHpIfAbsorbed(defender, defenderHpBefore);
                return;
            }

            ApplyIncomingBattleStrike(attacker, defender, attackerOwner, defenderOwner, defenderStrike);
            RestoreRedirectHostHpIfAbsorbed(attacker, attackerHpBefore);
            RestoreRedirectHostHpIfAbsorbed(defender, defenderHpBefore);
            return;
        }

        if (defenderFirstStrike && !attackerFirstStrike)
        {
            ApplyIncomingBattleStrike(attacker, defender, attackerOwner, defenderOwner, defenderStrike);
            if (attacker != null && attacker.CurrentHp <= 0)
            {
                RestoreRedirectHostHpIfAbsorbed(attacker, attackerHpBefore);
                RestoreRedirectHostHpIfAbsorbed(defender, defenderHpBefore);
                return;
            }

            ApplyIncomingBattleStrike(defender, attacker, defenderOwner, attackerOwner, attackerStrike);
            RestoreRedirectHostHpIfAbsorbed(attacker, attackerHpBefore);
            RestoreRedirectHostHpIfAbsorbed(defender, defenderHpBefore);
            return;
        }

        ApplyIncomingBattleStrike(defender, attacker, defenderOwner, attackerOwner, attackerStrike);
        ApplyIncomingBattleStrike(attacker, defender, attackerOwner, defenderOwner, defenderStrike);
        RestoreRedirectHostHpIfAbsorbed(attacker, attackerHpBefore);
        RestoreRedirectHostHpIfAbsorbed(defender, defenderHpBefore);
    }

    /// <summary>
    /// 戦闘ダメージ1発。GD06-018 は敵のストライクを選んだカードへ移し、本体には入れない。
    /// </summary>
    private void ApplyIncomingBattleStrike(
        CardController intendedTarget,
        CardController damageSource,
        PlayerType intendedOwner,
        PlayerType sourceOwner,
        int damage)
    {
        if (intendedTarget == null || damage <= 0)
        {
            return;
        }

        CardController recipient = ResolveCombatIncomingDamageTarget(intendedTarget);
        if (recipient != null && recipient != intendedTarget)
        {
            Debug.Log(
                $"[GD06-018] 敵ダメージ {damage} を {intendedTarget.Data?.cardName} ではなく "
                + $"{recipient.Data?.cardName}(id:{recipient.Data?.id} type:{recipient.Data?.type}) が受ける");
            ApplyResolvedBattleDamageToRecipient(recipient, intendedTarget, damageSource, damage);
            return;
        }

        ApplyCombatDamageIfNotImmune(intendedTarget, damageSource, intendedOwner, sourceOwner, damage);
    }

    private CardController ResolveCombatIncomingDamageTarget(CardController intended)
    {
        if (intended == null)
        {
            return null;
        }

        bool is018 = IsGd06018QubeleyMkII(intended);
        if (!is018
            && !intended.HasIncomingBattleDamageRedirectThisBattle
            && !IsBattleIncomingRedirectHost(intended)
            && !IsIncomingBattleDamageRedirectHost(intended)
            && !HasActiveIncomingBattleDamageRedirect())
        {
            return intended;
        }

        if (!is018
            && !intended.HasIncomingBattleDamageRedirectThisBattle
            && !IsBattleIncomingRedirectHost(intended)
            && !IsIncomingBattleDamageRedirectHost(intended))
        {
            return intended;
        }

        CardController recipient = ResolveLiveIncomingRedirectRecipientForHost(intended);
        if (recipient != null && recipient != intended && recipient.CurrentHp > 0)
        {
            return recipient;
        }

        List<CardController> lastPicked = GetAliveEffectChainLastPickedTargets();
        for (int i = 0; i < lastPicked.Count; i++)
        {
            CardController picked = lastPicked[i];
            if (picked != null && picked != intended && picked.CurrentHp > 0)
            {
                return picked;
            }
        }

        if (is018 && _gd06018RecipientCardId > 0)
        {
            recipient = FindIncomingRedirectRecipientByCardId(
                _gd06018RecipientCardId,
                _gd06018RecipientIsBase,
                _gd06018Owner,
                intended);
            if (recipient != null && recipient != intended && recipient.CurrentHp > 0)
            {
                return recipient;
            }
        }

        return intended;
    }

    private void RestoreRedirectHostHpIfAbsorbed(CardController unit, int hpBefore)
    {
        if (unit == null || hpBefore <= 0 || unit.CurrentHp >= hpBefore)
        {
            return;
        }

        CardController recipient = ResolveCombatIncomingDamageTarget(unit);
        if (recipient == null || recipient == unit)
        {
            return;
        }

        Debug.Log(
            $"[GD06-018] 本体HPを復元 {unit.Data?.cardName} {unit.CurrentHp}→{hpBefore} "
            + $"(受け替え先:{recipient.Data?.cardName})");
        unit.RestoreHpAfterRedirectAbsorb(hpBefore);
    }

    private void ApplyCombatDamageIfNotImmune(
        CardController damageTarget,
        CardController damageSource,
        PlayerType damageTargetOwner,
        PlayerType damageSourceOwner,
        int damage)
    {
        if (damageTarget == null || damage <= 0)
        {
            return;
        }

        // GD06-018 等: このユニットが受ける戦闘ダメージを、Action で選んだカードへ移す。
        if (TryApplyIncomingBattleDamageRedirectOnCombatHit(damageTarget, damageSource, damage))
        {
            return;
        }

        if (damageTarget.Data != null && damageTarget.Data.IsUnitLike())
        {
            damage += ResolveAllyFeatureOutgoingDamageBonus(damageSourceOwner, damageSource, damageTarget);
        }

        CardController originalTarget = damageTarget;

        if (ShouldIgnoreTheBlueGiantBattleDamage(
                damageTarget,
                damageSource,
                damage))
        {
            return;
        }

        bool isTargetOwnerTurn = damageTargetOwner == currentPlayerType;
        if (CardPilotBattleDamageImmunityExtensions.ShouldIgnoreBattleDamageFromAttacker(
                damageTarget,
                damageSource,
                damageTargetOwner,
                damageSourceOwner,
                isTargetOwnerTurn,
                canConsumeOncePerTurnBlock: (timed, blockIndex) =>
                {
                    if (timed == null || !timed.oncePerTurn)
                    {
                        return true;
                    }

                    if (HasUsedPaidActivationThisTurn(damageTargetOwner, damageTarget, blockIndex))
                    {
                        return false;
                    }

                    MarkPaidActivationUsedThisTurn(damageTargetOwner, damageTarget, blockIndex);
                    return true;
                },
                playerBattleZone: playerBattleZoneCards,
                enemyBattleZone: enemyBattleZoneCards))
        {
            return;
        }

        // 相手ユニットからのバトルダメージ軽減（ロニ・ガーベイ等）
        if (damageSource != null
            && damageSource.Data != null
            && damageSource.Data.IsUnitLike()
            && damageTargetOwner != damageSourceOwner)
        {
            int battleReduction = damageTarget.GetBattleDamageFromEnemyUnitReduction(damageSource);
            if (battleReduction > 0)
            {
                int before = damage;
                damage = Mathf.Max(0, damage - battleReduction);
                Debug.Log(
                    $"[BattleDmgReduction] {damageTarget.Data?.cardName} from {damageSource.Data.cardName} "
                    + $"{before} → {damage} (reduction:{battleReduction})");
            }
        }

        if (damage <= 0)
        {
            return;
        }

        ApplyResolvedBattleDamageToRecipient(damageTarget, originalTarget, damageSource, damage);
    }

    /// <summary>
    /// 戦闘ダメージを受け替え先（ユニット／配備ベース）へ適用し、ベース HP 表示と破壊を処理する。
    /// </summary>
    private void ApplyResolvedBattleDamageToRecipient(
        CardController recipient,
        CardController originalTarget,
        CardController damageSource,
        int damage)
    {
        if (recipient == null || damage <= 0)
        {
            return;
        }

        int hpBefore = recipient.CurrentHp;
        recipient.ApplyDamage(damage);
        NoteCombatRedirectRecipientForOnlineSync(recipient, originalTarget);

        if (recipient.Data != null && recipient.Data.type == Type.Base)
        {
            PlayerType owner = ResolveCardOwner(recipient.transform);
            Gundam2024RuleScript.PlayerSide ruleSide = ToRuleSide(owner);
            NotifyThisBaseReceivedBattleDamageIfNeeded(recipient, hpBefore);
            MarkPendingDefenderDeployedBaseHpForOnlineSync(recipient.CurrentHp);
            RefreshDeployedBaseHpOverlay(recipient);
            SyncBaseZoneHeaderDisplay(ruleSide);
            if (recipient.CurrentHp <= 0)
            {
                MarkPendingDefenderDeployedBaseHpForOnlineSync(0);
                CardGameRule ownerRule = GetCardRuleForRuleSide(ruleSide);
                SendDeployedBaseToTrash(recipient, owner, ownerRule);
                SyncResourceViewsFromRule(ruleSide);
            }

            return;
        }

        if (recipient != originalTarget
            && recipient.Data != null
            && recipient.Data.IsUnitLike()
            && recipient.CurrentHp <= 0)
        {
            PlayerType owner = ResolveCardOwner(recipient.transform);
            SendCardToTrash(recipient, owner, damageSource, destroyedByBattleDamage: true);
        }
    }

    private static void ApplyVirtualUnitVsUnitCombatHpExchange(
        VirtualBattleUnitSnap attacker,
        VirtualBattleUnitSnap defender)
    {
        if (attacker == null || defender == null)
        {
            return;
        }

        ResolveUnitVsUnitCombatHpExchange(
            attacker.Hp,
            defender.Hp,
            attacker.Ap,
            defender.Ap,
            attacker.FirstStrike,
            defender.FirstStrike,
            out int attackerHpAfter,
            out int defenderHpAfter);

        defender.Hp = defenderHpAfter;
        if (!(attacker.FirstStrike && !defender.FirstStrike && defenderHpAfter <= 0))
        {
            attacker.Hp = attackerHpAfter;
        }
    }
}
