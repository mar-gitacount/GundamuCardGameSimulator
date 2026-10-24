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

    /// <summary>Player 側：無効化する相手ユニットの最大 AP（未付与は -1）。</summary>
    private int _playerShieldAreaImmunityMaxEnemyUnitAp = -1;

    /// <summary>Enemy 側：無効化する相手ユニットの最大 AP（未付与は -1）。</summary>
    private int _enemyShieldAreaImmunityMaxEnemyUnitAp = -1;

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

    /// <summary>
    /// 味方シールドエリアへの「相手ユニット AP≤maxAp」ダメージ無効を付与する（UntilEndOfBattle）。
    /// </summary>
    private void GrantShieldAreaImmunityFromEnemyUnitApOrLess(PlayerType ownerType, int maxEnemyUnitAp)
    {
        if (maxEnemyUnitAp < 0)
        {
            return;
        }

        if (ownerType == PlayerType.Player)
        {
            _playerShieldAreaImmunityMaxEnemyUnitAp =
                Mathf.Max(_playerShieldAreaImmunityMaxEnemyUnitAp, maxEnemyUnitAp);
        }
        else
        {
            _enemyShieldAreaImmunityMaxEnemyUnitAp =
                Mathf.Max(_enemyShieldAreaImmunityMaxEnemyUnitAp, maxEnemyUnitAp);
        }

        QueueOnlineShieldAreaUnitDamageImmunityAp(ownerType, maxEnemyUnitAp);

        Debug.Log(
            $"[ShieldAreaUnitDmgImmunity] grant AP≤{maxEnemyUnitAp} → {ownerType} "
            + $"(now:{GetShieldAreaImmunityMaxEnemyUnitAp(ownerType)})");
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

    /// <summary>オンライン受信：AP 閾値のシールドエリア無効をローカル視点へ反転して付与する。</summary>
    private void ApplyRemoteShieldAreaUnitDamageImmunityAp(PlayerType senderGrantedSide, int maxEnemyUnitAp)
    {
        if (maxEnemyUnitAp < 0)
        {
            return;
        }

        PlayerType localSide = MirrorOnlineZoneOwner(senderGrantedSide);
        if (localSide == PlayerType.Player)
        {
            _playerShieldAreaImmunityMaxEnemyUnitAp =
                Mathf.Max(_playerShieldAreaImmunityMaxEnemyUnitAp, maxEnemyUnitAp);
        }
        else
        {
            _enemyShieldAreaImmunityMaxEnemyUnitAp =
                Mathf.Max(_enemyShieldAreaImmunityMaxEnemyUnitAp, maxEnemyUnitAp);
        }

        Debug.Log(
            $"[ShieldAreaUnitDmgImmunity] Remote grant AP≤{maxEnemyUnitAp} "
            + $"senderSide:{senderGrantedSide} → localSide:{localSide} "
            + $"(now:{GetShieldAreaImmunityMaxEnemyUnitAp(localSide)})");
    }

    private void ClearShieldAreaImmunityFromEnemyUnitLevelOrLess()
    {
        if (_playerShieldAreaImmunityMaxEnemyUnitLevel < 0
            && _enemyShieldAreaImmunityMaxEnemyUnitLevel < 0
            && _playerShieldAreaImmunityMaxEnemyUnitAp < 0
            && _enemyShieldAreaImmunityMaxEnemyUnitAp < 0)
        {
            return;
        }

        Debug.Log(
            "[ShieldAreaUnitDmgImmunity] cleared "
            + $"(playerLv:{_playerShieldAreaImmunityMaxEnemyUnitLevel} "
            + $"enemyLv:{_enemyShieldAreaImmunityMaxEnemyUnitLevel} "
            + $"playerAp:{_playerShieldAreaImmunityMaxEnemyUnitAp} "
            + $"enemyAp:{_enemyShieldAreaImmunityMaxEnemyUnitAp})");
        _playerShieldAreaImmunityMaxEnemyUnitLevel = -1;
        _enemyShieldAreaImmunityMaxEnemyUnitLevel = -1;
        _playerShieldAreaImmunityMaxEnemyUnitAp = -1;
        _enemyShieldAreaImmunityMaxEnemyUnitAp = -1;
    }

    private int GetShieldAreaImmunityMaxEnemyUnitLevel(PlayerType ownerType)
    {
        return ownerType == PlayerType.Player
            ? _playerShieldAreaImmunityMaxEnemyUnitLevel
            : _enemyShieldAreaImmunityMaxEnemyUnitLevel;
    }

    private int GetShieldAreaImmunityMaxEnemyUnitAp(PlayerType ownerType)
    {
        return ownerType == PlayerType.Player
            ? _playerShieldAreaImmunityMaxEnemyUnitAp
            : _enemyShieldAreaImmunityMaxEnemyUnitAp;
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
        int maxApEarly = GetShieldAreaImmunityMaxEnemyUnitAp(targetOwner);
        if (maxLevel < 0 && maxApEarly < 0)
        {
            return false;
        }

        PlayerType sourceOwner = sourceOwnerOverride
            ?? ResolveUnitOwnerForShieldAreaDamageSource(unitSource);
        if (sourceOwner == targetOwner)
        {
            return false;
        }

        // 実効 Lv / AP（ランタイム補正込み）。印刷値のみだとデバフ後が判定から外れる
        int unitLevel = unitSource.CurrentLevel;
        int unitAp = unitSource.CurrentPower;
        int maxAp = GetShieldAreaImmunityMaxEnemyUnitAp(targetOwner);
        bool ignoreByLevel = maxLevel >= 0 && unitLevel <= maxLevel;
        bool ignoreByAp = maxAp >= 0 && unitAp <= maxAp;
        bool ignore = ignoreByLevel || ignoreByAp;
        if (ignore)
        {
            Debug.Log(
                $"[ShieldAreaUnitDmgImmunity] ignore damage to {targetOwner} from "
                + $"{unitSource.Data.cardName}(id:{unitSource.Data.id}) "
                + $"Lv:{unitLevel}≤{maxLevel} AP:{unitAp}≤{maxAp} "
                + $"(printed Lv:{unitSource.Data.level} AP:{unitSource.Data.power})");
        }
        else
        {
            Debug.Log(
                $"[ShieldAreaUnitDmgImmunity] NOT ignore — {unitSource.Data.cardName} "
                + $"Lv:{unitLevel} AP:{unitAp} maxLv:{maxLevel} maxAp:{maxAp} target:{targetOwner}");
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
