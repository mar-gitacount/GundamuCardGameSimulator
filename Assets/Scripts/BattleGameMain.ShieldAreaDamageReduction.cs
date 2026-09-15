using UnityEngine;

/// <summary>
/// 味方シールドエリアが相手から受ける効果ダメージの軽減（ST11-006 シャンブロ等）。
/// </summary>
public partial class BattleGameMain
{
    /// <summary>Player 側シールドエリアへの敵効果ダメージ軽減量（当ターン中）。</summary>
    private int _playerShieldAreaEnemyEffectDamageReduction;

    /// <summary>Enemy 側シールドエリアへの敵効果ダメージ軽減量（当ターン中）。</summary>
    private int _enemyShieldAreaEnemyEffectDamageReduction;

    /// <summary>味方シールドエリアの敵効果ダメージ軽減を value 分積み上げる（UntilEndOfTurn）。</summary>
    private void GrantShieldAreaEnemyEffectDamageReduction(PlayerType ownerType, int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        if (ownerType == PlayerType.Player)
        {
            _playerShieldAreaEnemyEffectDamageReduction += amount;
        }
        else
        {
            _enemyShieldAreaEnemyEffectDamageReduction += amount;
        }

        Debug.Log(
            $"[ShieldAreaDmgReduction] grant +{amount} → {ownerType} "
            + $"(now:{GetShieldAreaEnemyEffectDamageReduction(ownerType)})");
    }

    /// <summary>指定陣営のシールドエリア敵効果ダメージ軽減をクリアする。</summary>
    private void ClearShieldAreaEnemyEffectDamageReduction(PlayerType ownerType)
    {
        int before = GetShieldAreaEnemyEffectDamageReduction(ownerType);
        if (ownerType == PlayerType.Player)
        {
            _playerShieldAreaEnemyEffectDamageReduction = 0;
        }
        else
        {
            _enemyShieldAreaEnemyEffectDamageReduction = 0;
        }

        if (before > 0)
        {
            Debug.Log($"[ShieldAreaDmgReduction] cleared {ownerType} (was:{before})");
        }
    }

    private int GetShieldAreaEnemyEffectDamageReduction(PlayerType ownerType)
    {
        return ownerType == PlayerType.Player
            ? Mathf.Max(0, _playerShieldAreaEnemyEffectDamageReduction)
            : Mathf.Max(0, _enemyShieldAreaEnemyEffectDamageReduction);
    }

    /// <summary>
    /// 相手からの効果ダメージなら軽減を適用する。0 以下ならダメージなし。
    /// </summary>
    private int ApplyShieldAreaEnemyEffectDamageReductionIfNeeded(
        Gundam2024RuleScript.PlayerSide targetSide,
        int magnitude,
        CardController sourceUnit)
    {
        if (magnitude <= 0)
        {
            return 0;
        }

        PlayerType targetOwner = targetSide == Gundam2024RuleScript.PlayerSide.Player
            ? PlayerType.Player
            : PlayerType.Enemy;
        int reduction = GetShieldAreaEnemyEffectDamageReduction(targetOwner);
        if (reduction <= 0)
        {
            return magnitude;
        }

        if (sourceUnit != null)
        {
            PlayerType sourceOwner = ResolveCardOwner(sourceUnit.transform);
            if (sourceOwner == targetOwner)
            {
                return magnitude;
            }
        }

        int reduced = Mathf.Max(0, magnitude - reduction);
        Debug.Log(
            $"[ShieldAreaDmgReduction] {targetOwner} {magnitude} → {reduced} "
            + $"(reduction:{reduction} source:{(sourceUnit?.Data != null ? sourceUnit.Data.cardName : "-")})");
        return reduced;
    }
}
