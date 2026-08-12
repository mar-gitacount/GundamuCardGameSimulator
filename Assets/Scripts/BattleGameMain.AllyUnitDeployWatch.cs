using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>味方ユニット配備監視（例: ムラサメの〔オーブ〕配備時《高機動》）。</summary>
public partial class BattleGameMain
{
    /// <summary>
    /// 自分のユニットがバトルゾーンへ配備された直後に呼ぶ。
    /// 配備カードは監視効果の ObservedCards として渡す。
    /// </summary>
    private void NotifyAllyUnitDeployed(
        PlayerType ownerType,
        CardController deployedUnit,
        Action onComplete = null)
    {
        if (deployedUnit == null || deployedUnit.Data == null || !deployedUnit.Data.IsUnitLike())
        {
            onComplete?.Invoke();
            return;
        }

        List<CardController> watchers = CollectOwnerBattleUnitsWithAllyDeployWatch(ownerType);
        if (watchers.Count == 0)
        {
            onComplete?.Invoke();
            return;
        }

        Debug.Log(
            $"[AllyDeployWatch] deployed:{deployedUnit.Data.cardName}(id:{deployedUnit.Data.id}) → "
            + $"watchers:{watchers.Count} owner:{ownerType}");

        RunAllyUnitDeployWatchUnits(ownerType, deployedUnit, watchers, 0, onComplete);
    }

    private List<CardController> CollectOwnerBattleUnitsWithAllyDeployWatch(PlayerType ownerType)
    {
        List<CardController> result = new List<CardController>();
        List<CardController> zone = ownerType == PlayerType.Player
            ? playerBattleZoneCards
            : enemyBattleZoneCards;
        if (zone == null)
        {
            return result;
        }

        for (int i = 0; i < zone.Count; i++)
        {
            CardController unit = zone[i];
            if (unit == null || unit.Data == null || !unit.Data.IsUnitLike() || unit.CurrentHp <= 0)
            {
                continue;
            }

            if (unit.Data.timedEffects == null)
            {
                continue;
            }

            for (int t = 0; t < unit.Data.timedEffects.Count; t++)
            {
                if (unit.Data.timedEffects[t].IsOnAllyUnitDeployedResolutionBlock())
                {
                    result.Add(unit);
                    break;
                }
            }
        }

        return result;
    }

    private EffectActivationContext BuildAllyUnitDeployActivationContext(
        PlayerType ownerType,
        CardController watcher,
        CardController deployedUnit)
    {
        CardData[] observed = deployedUnit?.Data != null
            ? new[] { deployedUnit.Data }
            : Array.Empty<CardData>();

        return new EffectActivationContext(
            ownerType,
            watcher,
            playerBattleZoneCards,
            enemyBattleZoneCards,
            CollectHandControllers(cardGameRule),
            CollectHandControllers(enemyCardGameRule),
            isOwnerTurn: ownerType == currentPlayerType,
            observedCards: observed,
            ownerTrashCardIds: cardGameRule.GetTrashCardIds(),
            opponentTrashCardIds: enemyCardGameRule.GetTrashCardIds(),
            priorChainDealtDamage: GetEffectChainDealtDamage(),
            ownerActivatedSpecialMoveCommandThisTurn: HasOwnerActivatedSpecialMoveCommandThisTurn(ownerType));
    }

    private void RunAllyUnitDeployWatchUnits(
        PlayerType ownerType,
        CardController deployedUnit,
        List<CardController> watchers,
        int index,
        Action onComplete)
    {
        if (watchers == null || index >= watchers.Count)
        {
            onComplete?.Invoke();
            return;
        }

        CardController unit = watchers[index];
        if (unit == null || unit.Data?.timedEffects == null)
        {
            RunAllyUnitDeployWatchUnits(ownerType, deployedUnit, watchers, index + 1, onComplete);
            return;
        }

        EffectActivationContext activationContext =
            BuildAllyUnitDeployActivationContext(ownerType, unit, deployedUnit);
        List<TimedEffectData> blocks = new List<TimedEffectData>();
        for (int i = 0; i < unit.Data.timedEffects.Count; i++)
        {
            TimedEffectData timed = unit.Data.timedEffects[i];
            if (!timed.IsOnAllyUnitDeployedResolutionBlock())
            {
                continue;
            }

            if (!CanRunTimedBlockAtChainTime(timed, activationContext, "OnAllyUnitDeployed"))
            {
                continue;
            }

            blocks.Add(timed);
        }

        if (blocks.Count == 0)
        {
            RunAllyUnitDeployWatchUnits(ownerType, deployedUnit, watchers, index + 1, onComplete);
            return;
        }

        BeginEffectChainObservationScope();
        if (deployedUnit?.Data != null)
        {
            ObserveCardInEffectChain(deployedUnit.Data);
        }

        RunOnPlayedTimedBlocks(
            unit,
            ownerType,
            blocks,
            0,
            () =>
            {
                EndEffectChainObservationScope();
                RunAllyUnitDeployWatchUnits(ownerType, deployedUnit, watchers, index + 1, onComplete);
            });
    }
}
