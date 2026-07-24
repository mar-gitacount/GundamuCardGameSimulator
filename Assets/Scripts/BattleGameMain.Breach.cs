using UnityEngine;

/// <summary>
/// 突破（Breach）：敵ユニット破壊時に相手シールドエリアへダメージ。
/// 配備ベース → EXベース → シールド1枚の順（効果ダメージと同じ優先）。
/// 戦闘破壊・効果ダメージ破壊の両方で発動。
/// </summary>
public partial class BattleGameMain
{
    /// <summary>
    /// 敵ユニット撃破時の突破。オーナーのターンのみ。
    /// </summary>
    private void TryTriggerBreachOnEnemyUnitDestroyed(
        CardController killer,
        PlayerType killerOwner,
        PlayerType destroyedOwner)
    {
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
        ApplyEffectDamageToPlayerArea(targetSide, breachAmount);
    }
}
