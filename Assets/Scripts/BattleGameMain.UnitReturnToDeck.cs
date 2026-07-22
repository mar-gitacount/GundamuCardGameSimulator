using System.Collections.Generic;
using UnityEngine;

/// <summary>バトルゾーンのユニットを山札の一番下へ戻す（ReturnUnitToDeckBottom）。</summary>
public partial class BattleGameMain
{
    private PlayerType ResolveBattleZoneUnitOwner(CardController unit)
    {
        if (unit == null)
        {
            return currentPlayerType;
        }

        if (playerBattleZoneCards.Contains(unit))
        {
            return PlayerType.Player;
        }

        if (enemyBattleZoneCards.Contains(unit))
        {
            return PlayerType.Enemy;
        }

        return ResolveCardOwner(unit.transform);
    }

    private bool TryReturnBattleUnitToDeckBottom(CardController unit)
    {
        if (unit == null || unit.Data == null || !unit.Data.IsUnitLike())
        {
            return false;
        }

        if (!IsCardOnBattleZone(unit))
        {
            Debug.LogWarning(
                $"[ReturnToDeckBottom] skip: not on battle zone ({unit.Data.cardName} id:{unit.Data.id})");
            return false;
        }

        if (unit.Data.IsUnitToken())
        {
            Debug.Log($"[ReturnToDeckBottom] {unit.Data.cardName}(id:{unit.Data.id}) is token, try to vanish");
            return TryVanishBattleUnitTokenFromZone(unit);
        }

        PlayerType ownerType = ResolveBattleZoneUnitOwner(unit);
        CardGameRule rule = ownerType == PlayerType.Player ? cardGameRule : enemyCardGameRule;
        if (rule == null)
        {
            return false;
        }

        CardController pilot = unit.DetachMountedPilotWithoutDestroy();
        int pilotId = 0;
        string pilotName = null;
        if (pilot != null && pilot.Data != null)
        {
            pilotId = pilot.Data.id;
            pilotName = pilot.Data.cardName;
            // 搭乗パイロットも山札下へ（手札には戻さない）
            Destroy(pilot.gameObject);
        }

        int cardId = unit.Data.id;
        if (pilotId > 0)
        {
            rule.AppendCardsToBottom(new[] { pilotId, cardId });
        }
        else
        {
            rule.AppendCardsToBottom(new[] { cardId });
        }

        PruneObservedUnitWatchesOnCardRemoved(unit);
        FinalizeRemoveCardFromPlay(unit, ownerType, sendToTrashZone: false);
        if (pilotId > 0)
        {
            Debug.Log(
                $"[ReturnToDeckBottom] {unit.Data.cardName}(id:{cardId}) + pilot:{pilotName}(id:{pilotId}) "
                + $"→ {ownerType} deck bottom");
        }
        else
        {
            Debug.Log($"[ReturnToDeckBottom] {unit.Data.cardName}(id:{cardId}) → {ownerType} deck bottom");
        }

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
            if (target == null || target.Data == null)
            {
                continue;
            }

            // ユニットトークンは山札に戻せず Destroy（消滅）で除去する
            if (target.Data.IsUnitToken())
            {
                if (!IsCardControllerInstanceValid(target) || target.CurrentHp <= 0)
                {
                    continue;
                }

                PlayerType targetOwner = ResolveBattleZoneUnitOwner(target);
                NotifyBlockRedirectUnitRemovedDuringAttackFlow(target);
                QueueOnlineUnitDestroy(target);
                SendCardToTrash(target, targetOwner, ResolveUnitKillSourceForTrash(null, target));
                applied++;
                Debug.Log(
                    $"[Effect] ReturnUnitToDeckBottom → Destroy token "
                    + $"{target.Data.cardName}(id:{target.Data.id})");
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

    /// <summary>
    /// autoSelectLowestUnitStat 時、最低値以外を除外する。
    /// 同値が複数いる場合は候補を残し、呼び出し側で選択 UI を出す。
    /// </summary>
    private static void FilterToLowestStatTiedUnitsIfNeeded(List<CardController> targets, EffectData effect)
    {
        if (targets == null || effect == null || !effect.autoSelectLowestUnitStat || targets.Count <= 1)
        {
            return;
        }

        CardController lowest = PickLowestStatUnit(targets, effect);
        if (lowest == null)
        {
            targets.Clear();
            return;
        }

        EffectTargetUnitFilterStat stat = effect.GetTargetUnitFilterStat();
        if (stat == EffectTargetUnitFilterStat.Unset)
        {
            stat = EffectTargetUnitFilterStat.Level;
        }

        int minValue = EffectDataExtensions.GetTargetUnitFilterStatValue(lowest, stat);
        for (int i = targets.Count - 1; i >= 0; i--)
        {
            CardController candidate = targets[i];
            if (candidate == null
                || EffectDataExtensions.GetTargetUnitFilterStatValue(candidate, stat) != minValue)
            {
                targets.RemoveAt(i);
            }
        }
    }

    private static bool NeedsLowestStatUnitManualPick(EffectData effect, List<CardController> targets)
    {
        return effect != null
            && effect.autoSelectLowestUnitStat
            && targets != null
            && targets.Count > 1;
    }

    /// <summary>
    /// OnAttack 時の ReturnUnitToDeckBottom（Lv最低自動選択）。
    /// UI 完了後は onStepResolved で残りの OnAttack 敵対象効果（パイロット等）へ進む。
    /// </summary>
    private bool TryResolveOnAttackLowestEnemyReturn(
        CardController sourceCard,
        CardController attacker,
        PlayerType attackerOwner,
        EffectData effect,
        System.Action onStepResolved)
    {
        if (effect == null
            || effect.type != EffectType.ReturnUnitToDeckBottom
            || !effect.autoSelectLowestUnitStat)
        {
            return false;
        }

        if (_suppressOnAttackReturnToDeckBottomAfterFailedDiscard)
        {
            Debug.Log(
                "[OnAttack] ReturnUnitToDeckBottom skipped — DiscardFromHand が Skip／枚数不足のため "
                + $"source:{sourceCard?.Data?.cardName}");
            return false;
        }

        List<CardController> autoTargets = ResolveEffectTargets(sourceCard, attackerOwner, effect);
        if (autoTargets.Count == 0)
        {
            Debug.Log("[OnAttack] ReturnUnitToDeckBottom: 対象となる敵ユニットがありません。");
            return false;
        }

        Debug.Log(
            $"[OnAttack] ReturnUnitToDeckBottom candidates:{autoTargets.Count} "
            + $"(tokens treated as Lv0) source:{sourceCard?.Data?.cardName}");

        if (NeedsLowestStatUnitManualPick(effect, autoTargets))
        {
            OpenEnemyUnitEffectSelectionUI(
                sourceCard,
                attacker,
                attackerOwner,
                effect,
                autoTargets,
                onStepResolved);
            return true;
        }

        ApplyEffectToSpecificTargets(sourceCard, attackerOwner, effect, autoTargets);
        return false;
    }
}
