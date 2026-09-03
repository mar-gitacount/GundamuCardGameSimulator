using UnityEngine;

/// <summary>
/// Kindhearted 等：このターン、発動者の味方ユニットは相手の「効果破壊」（EffectType.Destroy）では破壊されない。
/// 効果ダメージ・戦闘ダメージによる破壊は対象外。
/// Player / Enemy それぞれ独立にシールドを持ち、相手側の発動も正しく検知する。
/// </summary>
public partial class BattleGameMain
{
    private bool _playerPreventAllyDestroyByEnemyEffectUntilEot;
    private bool _enemyPreventAllyDestroyByEnemyEffectUntilEot;

    private bool HasPreventAllyDestroyByEnemyEffect(PlayerType allyOwner)
    {
        return allyOwner == PlayerType.Player
            ? _playerPreventAllyDestroyByEnemyEffectUntilEot
            : _enemyPreventAllyDestroyByEnemyEffectUntilEot;
    }

    private void GrantPreventAllyDestroyByEnemyEffect(PlayerType ownerType)
    {
        if (ownerType == PlayerType.Player)
        {
            _playerPreventAllyDestroyByEnemyEffectUntilEot = true;
        }
        else
        {
            _enemyPreventAllyDestroyByEnemyEffectUntilEot = true;
        }

        // 送信側視点の付与サイドを同期（受信側はミラーする）
        QueueOnlinePreventAllyDestroyByEnemyEffect(ownerType);
        Debug.Log($"[PreventEnemyEffectDestroy] Granted until EOT for owner:{ownerType}");
    }

    /// <summary>オンライン受信：送信側視点の付与サイドをローカル視点へ反転して付与する。</summary>
    private void ApplyRemotePreventAllyDestroyByEnemyEffect(PlayerType senderGrantedSide)
    {
        PlayerType localSide = MirrorOnlineZoneOwner(senderGrantedSide);
        if (localSide == PlayerType.Player)
        {
            _playerPreventAllyDestroyByEnemyEffectUntilEot = true;
        }
        else
        {
            _enemyPreventAllyDestroyByEnemyEffectUntilEot = true;
        }

        Debug.Log(
            $"[PreventEnemyEffectDestroy] Remote grant until EOT "
            + $"senderSide:{senderGrantedSide} → localSide:{localSide}");
    }

    private void ClearPreventAllyDestroyByEnemyEffectUntilEot()
    {
        if (!_playerPreventAllyDestroyByEnemyEffectUntilEot && !_enemyPreventAllyDestroyByEnemyEffectUntilEot)
        {
            return;
        }

        _playerPreventAllyDestroyByEnemyEffectUntilEot = false;
        _enemyPreventAllyDestroyByEnemyEffectUntilEot = false;
        Debug.Log("[PreventEnemyEffectDestroy] Cleared (end of turn)");
    }

    /// <summary>バトルゾーン所属からユニットの陣営を決める（階層解決のフォールバック付き）。</summary>
    private PlayerType ResolveUnitOwnerForDestroyPrevent(CardController target)
    {
        if (target == null)
        {
            return currentPlayerType;
        }

        if (playerBattleZoneCards != null && playerBattleZoneCards.Contains(target))
        {
            return PlayerType.Player;
        }

        if (enemyBattleZoneCards != null && enemyBattleZoneCards.Contains(target))
        {
            return PlayerType.Enemy;
        }

        return ResolveCardOwner(target.transform);
    }

    /// <summary>
    /// 相手の効果破壊（Destroy）を防ぐ。true なら破壊をスキップ。
    /// effectSourceOwner は破壊効果の発動側。効果ダメージ／戦闘破壊では呼ばない。
    /// </summary>
    private bool TryPreventEnemyEffectDestroy(CardController target, PlayerType effectSourceOwner)
    {
        if (target == null || target.Data == null || !target.Data.IsUnitLike())
        {
            return false;
        }

        PlayerType targetOwner = ResolveUnitOwnerForDestroyPrevent(target);
        // 自分の効果で自分のユニットを壊す（コスト破壊等）は許可
        if (targetOwner == effectSourceOwner)
        {
            return false;
        }

        // 対象オーナー側が Kindhearted 等を発動していれば防止（相手側の付与も独立に検知）
        if (!HasPreventAllyDestroyByEnemyEffect(targetOwner))
        {
            return false;
        }

        Debug.Log(
            $"[PreventEnemyEffectDestroy] Blocked Destroy → {target.Data.cardName}(id:{target.Data.id}) "
            + $"targetOwner:{targetOwner} effectOwner:{effectSourceOwner} HP:{target.CurrentHp}");
        return true;
    }
}
