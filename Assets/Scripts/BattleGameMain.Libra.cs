using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ST12-016 リーブラの待機型メイン効果。
/// リーブラを先にRESTし、その後のパイロット搭乗ユニットによる戦闘撃破を待つ。
/// </summary>
public partial class BattleGameMain
{
    private CardController _playerArmedLibra;
    private CardController _enemyArmedLibra;

    private void ArmLibraAfterRest(CardController libra, PlayerType ownerType)
    {
        if (libra?.Data?.id != 1000669 || !libra.IsRestState)
        {
            return;
        }

        if (ownerType == PlayerType.Player)
        {
            _playerArmedLibra = libra;
        }
        else
        {
            _enemyArmedLibra = libra;
        }

        Debug.Log($"[ST12-016] リーブラをRESTし、戦闘撃破待機を開始 side:{ownerType}");
    }

    private bool TryResolveArmedLibraAfterPilotBattleKill(
        CardController killer,
        PlayerType killerOwner,
        bool destroyedByBattleDamage,
        System.Action onComplete)
    {
        if (!destroyedByBattleDamage
            || killer?.Data == null
            || !killer.Data.IsUnitLike()
            || killer.MountedPilot?.Data == null
            || !killer.MountedPilot.Data.IsPilot())
        {
            return false;
        }

        CardController libra = killerOwner == PlayerType.Player
            ? _playerArmedLibra
            : _enemyArmedLibra;
        if (libra?.Data?.id != 1000669 || !libra.IsRestState || libra.CurrentHp <= 0)
        {
            return false;
        }

        // このRESTで待機した効果は最初の条件達成時に1回だけ解決する。
        if (killerOwner == PlayerType.Player)
        {
            _playerArmedLibra = null;
        }
        else
        {
            _enemyArmedLibra = null;
        }

        EffectData damageEffect = new EffectData
        {
            type = EffectType.Damage,
            value = 1,
            target = TargetType.EnemyUnit,
            selectionMode = EffectSelectionMode.SelectSingle,
            targetUnitFilterStat = EffectTargetUnitFilterStat.Level,
            targetUnitStatCompareOp = EffectCompareOperator.LessOrEqual,
            targetUnitStatCompareValue = 4,
            abortRemainingChainOnSkip = true
        };
        List<CardController> candidates =
            ResolveSelectableEffectTargets(libra, killerOwner, damageEffect);
        FilterOutUnitsPendingSendToTrash(candidates);

        Debug.Log(
            $"[ST12-016] 待機条件達成 side:{killerOwner} killer:{killer.Data.cardName} "
            + $"Lv4以下候補:{candidates.Count}");
        if (candidates.Count == 0)
        {
            onComplete?.Invoke();
            return true;
        }

        if (killerOwner == PlayerType.Enemy)
        {
            EnemyAiEffectPickContext pickContext =
                BuildEnemyAiEffectPickContext(killerOwner, libra, null, null);
            CardController picked = PickEnemyAiEffectTarget(damageEffect, pickContext, candidates);
            if (picked != null)
            {
                ApplyEffectToSpecificTargets(
                    libra,
                    killerOwner,
                    damageEffect,
                    new List<CardController> { picked });
            }
            onComplete?.Invoke();
            return true;
        }

        OpenManualUnitTargetSelectionUI(
            libra,
            killerOwner,
            damageEffect,
            candidates,
            null,
            picked =>
            {
                if (picked != null)
                {
                    ApplyEffectToSpecificTargets(
                        libra,
                        killerOwner,
                        damageEffect,
                        new List<CardController> { picked });
                }
                onComplete?.Invoke();
            });
        return true;
    }

    private void ClearPilotSetBattleDamageKillThisTurn()
    {
        _playerArmedLibra = null;
        _enemyArmedLibra = null;
    }
}
