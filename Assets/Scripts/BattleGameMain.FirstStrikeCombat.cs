using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Operation Meteor 先制攻撃の付与とユニット戦ダメージ解決。
/// </summary>
public partial class BattleGameMain
{
    /// <summary>
    /// Operation Meteor パイロット搭乗時、搭乗ターン限定でホスト MS に先制攻撃を付与する。
    /// </summary>
    private void TryGrantOperationMeteorFirstStrikeOnPilotMount(
        CardController hostUnit,
        CardController pilot,
        PlayerType ownerType)
    {
        if (hostUnit == null || hostUnit.Data == null || pilot?.Data == null)
        {
            return;
        }

        // 搭乗者のターンでのみ付与（オンライン相手搭乗の受信同期時は _applyingRemoteBattleAction でミラー）
        if (ownerType != currentPlayerType && !_applyingRemoteBattleAction)
        {
            return;
        }

        if (!pilot.Data.HasOperationMeteorFeature())
        {
            return;
        }

        hostUnit.AddFirstStrikeUntilEndOfTurnGrant();
        Debug.Log(
            $"[OperationMeteor] 先制攻撃付与: {hostUnit.Data.cardName}(id:{hostUnit.Data.id}) "
            + $"pilot:{pilot.Data.cardName}(id:{pilot.Data.id}) turn:{ownerType}");
    }

    private void ClearFirstStrikeGrants(EffectDuration duration)
    {
        if (duration != EffectDuration.UntilEndOfTurn)
        {
            return;
        }

        ClearFirstStrikeGrantsOnZone(playerBattleZoneCards);
        ClearFirstStrikeGrantsOnZone(enemyBattleZoneCards);
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

        if (unit.HasFirstStrike())
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
        bool attackerFirstStrike = UnitHasFirstStrikeInCombat(attacker, defender);
        bool defenderFirstStrike = UnitHasFirstStrikeInCombat(defender, attacker);
        int defenderHpBefore = defender != null ? defender.CurrentHp : 0;
        attackerStrike = Mathf.Max(0, attackerStrike);
        defenderStrike = Mathf.Max(0, defenderStrike);

        // 軽減は ApplyDamage 側。残 HP でキャップした差分を渡すと
        // 「AP7・軽減6・HP5」が 7-6=1 ではなく min(7,5)-6=0 になる。
        if (attackerFirstStrike && !defenderFirstStrike)
        {
            ApplyCombatDamageIfNotImmune(defender, attacker, defenderOwner, attackerOwner, attackerStrike);
            if (defender != null && defender.CurrentHp <= 0)
            {
                Debug.Log(
                    $"[FirstStrike] {attacker?.Data?.cardName} 先制撃破 → {defender?.Data?.cardName} "
                    + $"HP:{defenderHpBefore}->{defender.CurrentHp}（反撃なし）");
                return;
            }

            ApplyCombatDamageIfNotImmune(attacker, defender, attackerOwner, defenderOwner, defenderStrike);
            return;
        }

        if (defenderFirstStrike && !attackerFirstStrike)
        {
            ApplyCombatDamageIfNotImmune(attacker, defender, attackerOwner, defenderOwner, defenderStrike);
            if (attacker != null && attacker.CurrentHp <= 0)
            {
                return;
            }

            ApplyCombatDamageIfNotImmune(defender, attacker, defenderOwner, attackerOwner, attackerStrike);
            return;
        }

        ApplyCombatDamageIfNotImmune(defender, attacker, defenderOwner, attackerOwner, attackerStrike);
        ApplyCombatDamageIfNotImmune(attacker, defender, attackerOwner, defenderOwner, defenderStrike);
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

        bool isTargetOwnerTurn = damageTargetOwner == currentPlayerType;
        if (CardPilotBattleDamageImmunityExtensions.ShouldIgnoreBattleDamageFromAttacker(
                damageTarget,
                damageSource,
                damageTargetOwner,
                damageSourceOwner,
                isTargetOwnerTurn))
        {
            return;
        }

        damageTarget.ApplyDamage(damage);
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
