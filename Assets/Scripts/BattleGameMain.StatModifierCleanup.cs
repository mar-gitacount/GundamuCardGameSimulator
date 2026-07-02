using System.Collections.Generic;
using UnityEngine;

/// <summary>ユニットが付与した Buff/Debuff の除去とオンライン同期。</summary>
public partial class BattleGameMain
{
    /// <summary>破壊・除外されたユニットが付与した Buff/Debuff を盤上の全ユニットから除去する。</summary>
    private void ClearStatModifiersGrantedByDestroyedUnit(CardController destroyedUnit)
    {
        if (destroyedUnit == null || destroyedUnit.BattleInstanceId <= 0)
        {
            return;
        }

        int grantId = destroyedUnit.BattleInstanceId;
        destroyedUnit.ClearPilotMountAllyFieldAuras();

        BeginOnlineEffectSyncBatch(currentPlayerType);
        ClearStatGrantsFromBattleInstanceOnAllFieldUnits(grantId, destroyedUnit, queueOnlineStatDeltas: false);
        QueueOnlineClearStatGrantsFromSource(grantId);
        FlushOnlineEffectSyncBatch();

        Debug.Log(
            $"[UnitBuff] cleared grants from destroyed unit instance:{grantId} "
            + $"{destroyedUnit.Data?.cardName}(id:{destroyedUnit.Data?.id})");
    }

    private void ClearStatGrantsFromBattleInstanceOnAllFieldUnits(
        int grantingBattleInstanceId,
        CardController exclude,
        bool queueOnlineStatDeltas = true)
    {
        if (grantingBattleInstanceId <= 0)
        {
            return;
        }

        ClearStatGrantsFromCardList(playerBattleZoneCards, grantingBattleInstanceId, exclude, queueOnlineStatDeltas);
        ClearStatGrantsFromCardList(enemyBattleZoneCards, grantingBattleInstanceId, exclude, queueOnlineStatDeltas);
    }

    private void ClearStatGrantsFromCardList(
        List<CardController> cards,
        int grantingBattleInstanceId,
        CardController exclude,
        bool queueOnlineStatDeltas)
    {
        if (cards == null || grantingBattleInstanceId <= 0)
        {
            return;
        }

        for (int i = 0; i < cards.Count; i++)
        {
            CardController card = cards[i];
            if (card == null || card == exclude)
            {
                continue;
            }

            List<CardController.StatModifierRemoval> removed =
                card.RemoveStatModifiersGrantedByBattleInstance(grantingBattleInstanceId);
            if (queueOnlineStatDeltas)
            {
                QueueOnlineStatRemovals(card, removed);
            }
        }
    }

    private void RemoveAndSyncStatModifiersBySourceFromCardList(
        List<CardController> cards,
        string sourceKey,
        CardController exclude,
        bool queueOnlineStatDeltas = true)
    {
        if (cards == null || string.IsNullOrEmpty(sourceKey))
        {
            return;
        }

        for (int i = 0; i < cards.Count; i++)
        {
            CardController card = cards[i];
            if (card == null || card == exclude)
            {
                continue;
            }

            List<CardController.StatModifierRemoval> removed = card.RemoveStatModifiersBySourceDetailed(sourceKey);
            if (queueOnlineStatDeltas)
            {
                QueueOnlineStatRemovals(card, removed);
            }
        }
    }

    private void QueueOnlineStatRemovals(
        CardController target,
        List<CardController.StatModifierRemoval> removed)
    {
        if (removed == null || removed.Count == 0)
        {
            return;
        }

        for (int i = 0; i < removed.Count; i++)
        {
            CardController.StatModifierRemoval removal = removed[i];
            if (removal.SignedTotal == 0)
            {
                continue;
            }

            QueueOnlineUnitStat(
                target,
                -removal.SignedTotal,
                removal.StatTarget,
                removal.Duration,
                statModifierSourceKey: null);
        }
    }
}
