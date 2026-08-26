using UnityEngine;

/// <summary>
/// 突破（Breach）：敵ユニットを戦闘ダメージで破壊したとき、相手シールドエリアへダメージ。
/// 配備ベース → EXベース → シールド1枚の順（効果ダメージと同じ優先）。
/// 公式: 「バトルダメージで相手のユニットを破壊したとき」のみ（配備時効果破壊などでは発動しない）。
/// </summary>
public partial class BattleGameMain
{
    /// <summary>
    /// 敵ユニット撃破時の突破。オーナーのターンのみ。戦闘ダメージ破壊時のみ。
    /// </summary>
    private void TryTriggerBreachOnEnemyUnitDestroyed(
        CardController killer,
        PlayerType killerOwner,
        PlayerType destroyedOwner,
        bool destroyedByBattleDamage)
    {
        if (!destroyedByBattleDamage)
        {
            return;
        }

        if (killer == null || killer.Data == null || !killer.Data.IsUnitLike())
        {
            return;
        }

        if (killerOwner != currentPlayerType && !_applyingRemoteBattleAction)
        {
            return;
        }

        if (killerOwner == destroyedOwner)
        {
            return;
        }

        int breachAmount = killer.GetBreachAmount();
        if (breachAmount <= 0)
        {
            return;
        }

        Gundam2024RuleScript.PlayerSide targetSide = ToRuleSide(destroyedOwner);
        Debug.Log(
            $"[Breach] {killer.Data.cardName}(id:{killer.Data.id}) amount:{breachAmount} → side:{targetSide}");
        // 配備ベース → EXベース → シールド。オンライン同期も効果ダメージ経路と共通。
        ApplyEffectDamageToPlayerArea(targetSide, breachAmount, killer);
    }
}
