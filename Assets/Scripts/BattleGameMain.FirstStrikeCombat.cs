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
        bool attackerFirstStrike = attacker != null && attacker.HasFirstStrike();
        bool defenderFirstStrike = defender != null && defender.HasFirstStrike();

        int attackerHpBefore = attacker != null ? attacker.CurrentHp : 0;
        int defenderHpBefore = defender != null ? defender.CurrentHp : 0;

        ResolveUnitVsUnitCombatHpExchange(
            attackerHpBefore,
            defenderHpBefore,
            attackerStrike,
            defenderStrike,
            attackerFirstStrike,
            defenderFirstStrike,
            out int attackerHpAfter,
            out int defenderHpAfter);

        if (attackerFirstStrike && !defenderFirstStrike && defenderHpAfter <= 0)
        {
            Debug.Log(
                $"[FirstStrike] {attacker?.Data?.cardName} 先制撃破 → {defender?.Data?.cardName} "
                + $"HP:{defenderHpBefore}->{defenderHpAfter}（反撃なし）");
        }

        int defenderDamage = defenderHpBefore - defenderHpAfter;
        int attackerDamage = attackerHpBefore - attackerHpAfter;
        if (defenderDamage > 0)
        {
            defender?.ApplyDamage(defenderDamage);
        }

        if (attackerDamage > 0)
        {
            attacker?.ApplyDamage(attackerDamage);
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
