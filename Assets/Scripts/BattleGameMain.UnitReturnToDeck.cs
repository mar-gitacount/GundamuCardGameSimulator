using System.Collections.Generic;
using UnityEngine;

/// <summary>バトルゾーンのユニットを山札の一番下へ戻す（ReturnUnitToDeckBottom）。</summary>
public partial class BattleGameMain
{
    private bool TryReturnBattleUnitToDeckBottom(CardController unit)
    {
        if (unit == null || unit.Data == null || !unit.Data.IsUnitLike() || !IsCardOnBattleZone(unit))
        {
            return false;
        }

        if (unit.Data.IsUnitToken())
        {
            return TryVanishBattleUnitTokenFromZone(unit);
        }

        PlayerType ownerType = ResolveCardOwner(unit.transform);
        CardGameRule rule = ownerType == PlayerType.Player ? cardGameRule : enemyCardGameRule;
        if (rule == null)
        {
            return false;
        }

        CardController pilot = unit.DetachMountedPilotWithoutDestroy();
        if (pilot != null)
        {
            TryReturnCardInstanceToHand(pilot, ownerType, rule);
        }

        int cardId = unit.Data.id;
        rule.AppendCardsToBottom(new[] { cardId });
        PruneObservedUnitWatchesOnCardRemoved(unit);
        FinalizeRemoveCardFromPlay(unit, ownerType, sendToTrashZone: false);
        Debug.Log($"[ReturnToDeckBottom] {unit.Data.cardName}(id:{cardId}) → {ownerType} deck bottom");
        return true;
    }

    private void ApplyReturnUnitToDeckBottomEffect(EffectData effect, List<CardController> targets)
    {
        if (effect == null || targets == null || targets.Count == 0)
        {
            return;
        }

        int limit = effect.value > 0 ? effect.value : targets.Count;
        int applied = 0;
        for (int i = 0; i < targets.Count && applied < limit; i++)
        {
            CardController target = targets[i];
            if (target == null)
            {
                continue;
            }

            QueueOnlineUnitReturnToDeckBottom(target);
            if (TryReturnBattleUnitToDeckBottom(target))
            {
                applied++;
            }
        }

        if (applied > 0)
        {
            Debug.Log($"[Effect] ReturnUnitToDeckBottom applied:{applied} target:{effect.target}");
        }
    }

    private static CardController PickLowestStatUnit(List<CardController> candidates, EffectData effect)
    {
        if (candidates == null || candidates.Count == 0 || effect == null)
        {
            return null;
        }

        EffectTargetUnitFilterStat stat = effect.GetTargetUnitFilterStat();
        if (stat == EffectTargetUnitFilterStat.Unset)
        {
            stat = EffectTargetUnitFilterStat.Level;
        }

        CardController best = null;
        int bestValue = int.MaxValue;
        for (int i = 0; i < candidates.Count; i++)
        {
            CardController candidate = candidates[i];
            if (candidate == null || candidate.Data == null)
            {
                continue;
            }

            int value = EffectDataExtensions.GetTargetUnitFilterStatValue(candidate, stat);
            if (value < bestValue)
            {
                bestValue = value;
                best = candidate;
            }
        }

        return best;
    }

    private static void CollapseToLowestStatUnitIfNeeded(List<CardController> targets, EffectData effect)
    {
        if (targets == null || effect == null || !effect.autoSelectLowestUnitStat || targets.Count <= 1)
        {
            return;
        }

        CardController lowest = PickLowestStatUnit(targets, effect);
        targets.Clear();
        if (lowest != null)
        {
            targets.Add(lowest);
        }
    }
}
