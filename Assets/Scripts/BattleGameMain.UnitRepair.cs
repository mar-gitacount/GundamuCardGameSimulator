using System.Collections.Generic;
using UnityEngine;

/// <summary>isRepair — ターン終了（双方 OnAction 完了後）のユニット HP 回復。</summary>
public partial class BattleGameMain
{
    /// <summary>
    /// 盤上の isRepair ユニットを回復する。ターン終了 OnAction の直後・OnTurnEnd 効果の前に呼ぶ。
    /// </summary>
    private void ApplyTurnEndRepairForAllInPlayUnits()
    {
        List<CardController> targets = CollectTurnEndRepairTargets();
        if (targets.Count == 0)
        {
            return;
        }

        Debug.Log($"[Repair] ターン終了リペア開始 targets:{targets.Count}");
        bool syncOnline = IsOnlineBattle() && !_applyingRemoteBattleAction;
        if (syncOnline)
        {
            BeginOnlineEffectSyncBatch(PlayerType.Player);
        }

        for (int i = 0; i < targets.Count; i++)
        {
            CardController unit = targets[i];
            if (unit == null)
            {
                continue;
            }

            int amount = unit.GetTurnEndRepairAmount();
            if (amount <= 0)
            {
                continue;
            }

            int healed = unit.TryApplyRepair(amount);
            if (healed <= 0)
            {
                continue;
            }

            PlayerType owner = ResolveCardOwner(unit.transform);
            if (syncOnline && owner == PlayerType.Player)
            {
                QueueOnlineUnitRepair(unit);
            }

            Debug.Log(
                $"[Repair] {unit.Data?.cardName}(id:{unit.Data?.id}) +{healed} HP → {unit.CurrentHp}/{unit.GetRepairHpCap()} owner:{owner}");
        }

        if (syncOnline)
        {
            FlushOnlineEffectSyncBatch();
        }
    }

    private List<CardController> CollectTurnEndRepairTargets()
    {
        List<CardController> result = new List<CardController>();
        CollectTurnEndRepairFromZone(playerBattleZoneCards, result);
        CollectTurnEndRepairFromZone(enemyBattleZoneCards, result);
        TryAddTurnEndRepairTarget(GetDeployedBaseForRuleSide(Gundam2024RuleScript.PlayerSide.Player), result);
        TryAddTurnEndRepairTarget(GetDeployedBaseForRuleSide(Gundam2024RuleScript.PlayerSide.Enemy), result);
        return result;
    }

    private static void CollectTurnEndRepairFromZone(List<CardController> zone, List<CardController> result)
    {
        if (zone == null)
        {
            return;
        }

        for (int i = 0; i < zone.Count; i++)
        {
            TryAddTurnEndRepairTarget(zone[i], result);
        }
    }

    private static void TryAddTurnEndRepairTarget(CardController unit, List<CardController> result)
    {
        if (unit == null || result == null || !unit.IsRepairEligibleUnit() || !unit.ShouldRepairAtTurnEnd())
        {
            return;
        }

        if (!result.Contains(unit))
        {
            result.Add(unit);
        }
    }
}
