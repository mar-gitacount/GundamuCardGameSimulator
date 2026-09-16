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
        int reduction = ResolveCurrentShambloShieldAreaDamageReduction(targetOwner);
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

    /// <summary>
    /// ST11-006 は常時条件なので、付与時の盤面ではなくダメージ解決時の盤面を参照する。
    /// シャンブロ自身を含む〔水中〕が2体以上なら、生存シャンブロ1体につき5軽減。
    /// </summary>
    private int ResolveCurrentShambloShieldAreaDamageReduction(PlayerType targetOwner)
    {
        if (currentPlayerType == targetOwner)
        {
            return 0;
        }

        System.Collections.Generic.List<CardController> units =
            targetOwner == PlayerType.Player ? playerBattleZoneCards : enemyBattleZoneCards;
        if (units == null)
        {
            return 0;
        }

        int aquaticCount = 0;
        int shambloCount = 0;
        for (int i = 0; i < units.Count; i++)
        {
            CardController unit = units[i];
            if (unit == null || unit.Data == null || unit.CurrentHp <= 0 || !unit.Data.IsUnitLike())
            {
                continue;
            }

            unit.Data.EnsureFeaturesResolved();
            if (unit.Data.HasFeatureId(76))
            {
                aquaticCount++;
            }

            if (unit.Data.id == 1000643)
            {
                shambloCount++;
            }
        }

        return aquaticCount >= 2 ? shambloCount * 5 : 0;
    }
}
