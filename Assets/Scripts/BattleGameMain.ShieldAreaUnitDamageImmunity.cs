using UnityEngine;

/// <summary>
/// 味方シールドエリア（実シールド／配備ベース／EXベース）が、
/// 指定 Lv 以下の相手ユニットから受けるダメージを無効化する
/// （ST02-013 穏やかな音色等・このバトル中）。
/// </summary>
public partial class BattleGameMain
{
    /// <summary>Player 側：無効化する相手ユニットの最大 Lv（未付与は -1）。</summary>
    private int _playerShieldAreaImmunityMaxEnemyUnitLevel = -1;

    /// <summary>Enemy 側：無効化する相手ユニットの最大 Lv（未付与は -1）。</summary>
    private int _enemyShieldAreaImmunityMaxEnemyUnitLevel = -1;

    /// <summary>
    /// 味方シールドエリア（シールド／配備ベース／EX）への
    /// 「相手ユニット Lv≤maxLevel」ダメージ無効を付与する（UntilEndOfBattle）。
    /// 複数付与時はより高い閾値（広い範囲）を残す。
    /// </summary>
    private void GrantShieldAreaImmunityFromEnemyUnitLevelOrLess(PlayerType ownerType, int maxEnemyUnitLevel)
    {
        if (maxEnemyUnitLevel < 0)
        {
            return;
        }

        if (ownerType == PlayerType.Player)
        {
            _playerShieldAreaImmunityMaxEnemyUnitLevel =
                Mathf.Max(_playerShieldAreaImmunityMaxEnemyUnitLevel, maxEnemyUnitLevel);
        }
        else
        {
            _enemyShieldAreaImmunityMaxEnemyUnitLevel =
                Mathf.Max(_enemyShieldAreaImmunityMaxEnemyUnitLevel, maxEnemyUnitLevel);
        }

        // オンライン：攻撃側クライアントにも付与を届けないとシールド打撃が通ってしまう
        QueueOnlineShieldAreaUnitDamageImmunity(ownerType, maxEnemyUnitLevel);

        Debug.Log(
            $"[ShieldAreaUnitDmgImmunity] grant Lv≤{maxEnemyUnitLevel} → {ownerType} "
            + $"(now:{GetShieldAreaImmunityMaxEnemyUnitLevel(ownerType)})");
    }

    /// <summary>オンライン受信：送信側視点の付与サイドをローカル視点へ反転して付与する。</summary>
    private void ApplyRemoteShieldAreaUnitDamageImmunity(PlayerType senderGrantedSide, int maxEnemyUnitLevel)
    {
        if (maxEnemyUnitLevel < 0)
        {
            return;
        }

        PlayerType localSide = MirrorOnlineZoneOwner(senderGrantedSide);
        if (localSide == PlayerType.Player)
        {
            _playerShieldAreaImmunityMaxEnemyUnitLevel =
                Mathf.Max(_playerShieldAreaImmunityMaxEnemyUnitLevel, maxEnemyUnitLevel);
        }
        else
        {
            _enemyShieldAreaImmunityMaxEnemyUnitLevel =
                Mathf.Max(_enemyShieldAreaImmunityMaxEnemyUnitLevel, maxEnemyUnitLevel);
        }

        Debug.Log(
            $"[ShieldAreaUnitDmgImmunity] Remote grant Lv≤{maxEnemyUnitLevel} "
            + $"senderSide:{senderGrantedSide} → localSide:{localSide} "
            + $"(now:{GetShieldAreaImmunityMaxEnemyUnitLevel(localSide)})");
    }

    private void ClearShieldAreaImmunityFromEnemyUnitLevelOrLess()
    {
        if (_playerShieldAreaImmunityMaxEnemyUnitLevel < 0
            && _enemyShieldAreaImmunityMaxEnemyUnitLevel < 0)
        {
            return;
        }

        Debug.Log(
            "[ShieldAreaUnitDmgImmunity] cleared "
            + $"(player:{_playerShieldAreaImmunityMaxEnemyUnitLevel} "
            + $"enemy:{_enemyShieldAreaImmunityMaxEnemyUnitLevel})");
        _playerShieldAreaImmunityMaxEnemyUnitLevel = -1;
        _enemyShieldAreaImmunityMaxEnemyUnitLevel = -1;
    }

    private int GetShieldAreaImmunityMaxEnemyUnitLevel(PlayerType ownerType)
    {
        return ownerType == PlayerType.Player
            ? _playerShieldAreaImmunityMaxEnemyUnitLevel
            : _enemyShieldAreaImmunityMaxEnemyUnitLevel;
    }

    /// <summary>
    /// 相手ユニット（Lv≤付与閾値）からのシールドエリアダメージなら true（無効化対象）。
    /// sourceOwnerOverride があるときは階層解決より優先（攻撃フロー中の誤判定防止）。
    /// </summary>
    private bool ShouldIgnoreShieldAreaDamageFromEnemyUnit(
        Gundam2024RuleScript.PlayerSide targetSide,
        CardController sourceUnit,
        PlayerType? sourceOwnerOverride = null)
    {
        CardController unitSource = ResolveUnitSourceForShieldAreaDamage(sourceUnit);
        if (unitSource == null || unitSource.Data == null || !unitSource.Data.IsUnitLike())
        {
            return false;
        }

        PlayerType targetOwner = targetSide == Gundam2024RuleScript.PlayerSide.Player
            ? PlayerType.Player
            : PlayerType.Enemy;
        int maxLevel = GetShieldAreaImmunityMaxEnemyUnitLevel(targetOwner);
        if (maxLevel < 0)
        {
            return false;
        }

        PlayerType sourceOwner = sourceOwnerOverride
            ?? ResolveUnitOwnerForShieldAreaDamageSource(unitSource);
        if (sourceOwner == targetOwner)
        {
            return false;
        }

        // 実効 Lv（ランタイム補正込み）。印刷 Lv のみだとデバフ後が判定から外れる
        int unitLevel = unitSource.CurrentLevel;
        bool ignore = unitLevel <= maxLevel;
        if (ignore)
        {
            Debug.Log(
                $"[ShieldAreaUnitDmgImmunity] ignore damage to {targetOwner} from "
                + $"{unitSource.Data.cardName}(id:{unitSource.Data.id}) "
                + $"Lv:{unitLevel}≤{maxLevel} (printed:{unitSource.Data.level})");
        }
        else
        {
            Debug.Log(
                $"[ShieldAreaUnitDmgImmunity] NOT ignore — {unitSource.Data.cardName} "
                + $"Lv:{unitLevel}>{maxLevel} target:{targetOwner}");
        }

        return ignore;
    }

    /// <summary>ダメージ元ユニットの陣営。バトルゾーン所属を優先し、攻撃フロー所有者にフォールバック。</summary>
    private PlayerType ResolveUnitOwnerForShieldAreaDamageSource(CardController unitSource)
    {
        if (unitSource == null)
        {
            return currentPlayerType;
        }

        if (playerBattleZoneCards != null && playerBattleZoneCards.Contains(unitSource))
        {
            return PlayerType.Player;
        }

        if (enemyBattleZoneCards != null && enemyBattleZoneCards.Contains(unitSource))
        {
            return PlayerType.Enemy;
        }

        if (attackFlowStrikeKind != AttackFlowStrikeKind.None
            && attackFlowAttackerUnit != null
            && ReferenceEquals(attackFlowAttackerUnit, unitSource))
        {
            return attackFlowAttackerOwner;
        }

        if (pendingUnitAttackAttacker != null
            && ReferenceEquals(pendingUnitAttackAttacker, unitSource))
        {
            return attackFlowAttackerOwner;
        }

        return ResolveCardOwner(unitSource.transform);
    }
}
