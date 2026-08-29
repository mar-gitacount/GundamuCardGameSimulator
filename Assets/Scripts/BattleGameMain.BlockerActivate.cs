using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>ブロッカーのアクティブ化と直接攻撃制限（Archangel 等）。</summary>
public partial class BattleGameMain
{
    private List<CardController> _effectChainLastPickedTargets = new List<CardController>();

    private void ClearEffectChainLastPickedTargets()
    {
        _effectChainLastPickedTargets.Clear();
    }

    private void SetEffectChainLastPickedTargets(IReadOnlyList<CardController> targets)
    {
        _effectChainLastPickedTargets.Clear();
        if (targets == null)
        {
            return;
        }

        for (int i = 0; i < targets.Count; i++)
        {
            CardController unit = targets[i];
            if (unit != null && unit.Data != null && unit.Data.IsUnitLike() && unit.CurrentHp > 0)
            {
                _effectChainLastPickedTargets.Add(unit);
            }
        }
    }

    private List<CardController> GetAliveEffectChainLastPickedTargets()
    {
        List<CardController> alive = new List<CardController>(_effectChainLastPickedTargets.Count);
        for (int i = 0; i < _effectChainLastPickedTargets.Count; i++)
        {
            CardController unit = _effectChainLastPickedTargets[i];
            if (unit != null && unit.Data != null && unit.Data.IsUnitLike() && unit.CurrentHp > 0
                && IsUnitAliveOnAnyDeployField(unit))
            {
                alive.Add(unit);
            }
        }

        return alive;
    }

    private bool TryExecutePriorChainPickedTargetEffect(
        CardController sourceCard,
        PlayerType ownerType,
        EffectData effect,
        Action onChainContinue)
    {
        if (effect == null || !effect.selectionMode.IsUsePriorChainPickedTargetMode())
        {
            return false;
        }

        List<CardController> targets = GetAliveEffectChainLastPickedTargets();
        if (targets.Count == 0)
        {
            Debug.LogWarning(
                $"[Effect] 直前の選択対象がありません ({effect.type} cardId:{sourceCard?.Data?.id})。");
            onChainContinue?.Invoke();
            return true;
        }

        ApplyEffectToSpecificTargets(sourceCard, ownerType, effect, targets);
        onChainContinue?.Invoke();
        return true;
    }

    private static bool TryApplyActivateToUnit(CardController unit)
    {
        if (unit == null || unit.Data == null || !unit.Data.IsUnitLike() || unit.CurrentHp <= 0)
        {
            return false;
        }

        if (!unit.IsRestState)
        {
            return false;
        }

        unit.SetUnitRestVisual(false);
        unit.SetAttackFlg(AttackFlg.True);
        return true;
    }

    private void ApplyActivateEffect(EffectData effect, PlayerType ownerType, List<CardController> targets)
    {
        // 後続の UsePriorChainPickedTarget は「実際に ACTIVE になったユニット」だけを参照する。
        // 既に ACTIVE の場合、直前の別効果で選んだ対象を誤って引き継がない。
        ClearEffectChainLastPickedTargets();
        if (effect == null || targets == null || targets.Count == 0)
        {
            return;
        }

        int limit = effect.value > 0 ? effect.value : targets.Count;
        int applied = 0;
        List<CardController> activated = new List<CardController>();
        for (int i = 0; i < targets.Count && applied < limit; i++)
        {
            CardController target = targets[i];
            if (IsOnAttackTrashReturnChainSelfActivateBlocked(target, effect))
            {
                continue;
            }

            if (TryApplyActivateToUnit(target))
            {
                QueueOnlineUnitActivate(target);
                activated.Add(target);
                applied++;
            }
        }

        if (applied > 0)
        {
            SetEffectChainLastPickedTargets(activated);
            Debug.Log($"[Effect] Activate applied:{applied} target:{effect.target}");
        }
    }

    private bool TryApplyNotDirectAttackMarker(EffectData effect, List<CardController> targets)
    {
        if (effect == null || effect.type != EffectType.NotDirectAttack || targets == null || targets.Count == 0)
        {
            return false;
        }

        if (effect.duration != EffectDuration.UntilEndOfTurn
            && effect.duration != EffectDuration.UntilEndOfBattle
            && effect.duration != EffectDuration.Permanent)
        {
            return false;
        }

        int limit = effect.value > 0 ? effect.value : targets.Count;
        int applied = 0;
        for (int i = 0; i < targets.Count && applied < limit; i++)
        {
            CardController unit = targets[i];
            if (unit == null || unit.Data == null || !unit.Data.IsUnitLike() || unit.CurrentHp <= 0)
            {
                continue;
            }

            if (effect.duration == EffectDuration.UntilEndOfTurn)
            {
                unit.AddNotDirectAttackUntilEndOfTurnGrant();
            }

            applied++;
            Debug.Log(
                $"[NotDirectAttack] {effect.duration} 付与: {unit.Data.cardName}(id:{unit.Data.id})");
        }

        return applied > 0;
    }

    private bool TryApplyFirstStrikeMarker(EffectData effect, List<CardController> targets)
    {
        if (effect == null || effect.type != EffectType.FirstStrike || targets == null || targets.Count == 0)
        {
            return false;
        }

        if (effect.duration != EffectDuration.UntilEndOfTurn
            && effect.duration != EffectDuration.UntilEndOfBattle
            && effect.duration != EffectDuration.Permanent)
        {
            return false;
        }

        int limit = effect.value > 0 ? effect.value : targets.Count;
        int applied = 0;
        for (int i = 0; i < targets.Count && applied < limit; i++)
        {
            CardController unit = targets[i];
            if (unit == null || unit.Data == null || !unit.Data.IsUnitLike() || unit.CurrentHp <= 0)
            {
                continue;
            }

            if (effect.duration == EffectDuration.UntilEndOfTurn)
            {
                unit.AddFirstStrikeUntilEndOfTurnGrant();
            }

            applied++;
            Debug.Log(
                $"[FirstStrike] {effect.duration} 付与: {unit.Data.cardName}(id:{unit.Data.id})");
        }

        return applied > 0;
    }

    private bool TryApplyHighMobilityMarker(EffectData effect, List<CardController> targets)
    {
        if (effect == null || effect.type != EffectType.HighMobility || targets == null || targets.Count == 0)
        {
            return false;
        }

        // Permanent はカード印刷のマーカー判定（HasHighMobilityAbility）に任せる
        if (effect.duration != EffectDuration.UntilEndOfTurn
            && effect.duration != EffectDuration.UntilEndOfBattle)
        {
            return false;
        }

        int limit = effect.value > 0 ? effect.value : targets.Count;
        int applied = 0;
        for (int i = 0; i < targets.Count && applied < limit; i++)
        {
            CardController unit = targets[i];
            if (unit == null || unit.Data == null || !unit.Data.IsUnitLike() || unit.CurrentHp <= 0)
            {
                continue;
            }

            unit.AddHighMobilityUntilEndOfTurnGrant();
            applied++;
            Debug.Log(
                $"[HighMobility] UntilEndOfTurn 付与: {unit.Data.cardName}(id:{unit.Data.id})");
        }

        return applied > 0;
    }

    private void ClearHighMobilityUntilEndOfTurnGrantsForAllInPlayUnits()
    {
        ClearHighMobilityUntilEndOfTurnGrantsOnZone(playerBattleZoneCards);
        ClearHighMobilityUntilEndOfTurnGrantsOnZone(enemyBattleZoneCards);
    }

    private static void ClearHighMobilityUntilEndOfTurnGrantsOnZone(List<CardController> zone)
    {
        if (zone == null)
        {
            return;
        }

        for (int i = 0; i < zone.Count; i++)
        {
            zone[i]?.ClearHighMobilityUntilEndOfTurnGrants();
        }
    }

    private bool TryApplyGrantBreachMarker(EffectData effect, List<CardController> targets)
    {
        if (effect == null || effect.type != EffectType.GrantBreach || targets == null || targets.Count == 0)
        {
            return false;
        }

        if (effect.duration != EffectDuration.UntilEndOfTurn
            && effect.duration != EffectDuration.UntilEndOfBattle
            && effect.duration != EffectDuration.Permanent)
        {
            return false;
        }

        int amount = effect.value > 0 ? effect.value : 0;
        if (amount <= 0)
        {
            return false;
        }

        int applied = 0;
        for (int i = 0; i < targets.Count; i++)
        {
            CardController unit = targets[i];
            if (unit == null || unit.Data == null || !unit.Data.IsUnitLike() || unit.CurrentHp <= 0)
            {
                continue;
            }

            if (effect.requireTargetLacksBreach && unit.GetBreachAmount() > 0)
            {
                continue;
            }

            if (effect.duration == EffectDuration.UntilEndOfTurn)
            {
                unit.AddBreachUntilEndOfTurnGrant(amount);
            }
            else if (effect.duration == EffectDuration.UntilEndOfBattle)
            {
                unit.AddBreachUntilEndOfBattleGrant(amount);
            }

            applied++;
            Debug.Log(
                $"[GrantBreach] {effect.duration} Breach{amount} 付与: {unit.Data.cardName}(id:{unit.Data.id})");
        }

        return applied > 0;
    }

    private void ClearBreachUntilEndOfTurnGrantsForAllInPlayUnits()
    {
        ClearBreachUntilEndOfTurnGrantsOnZone(playerBattleZoneCards);
        ClearBreachUntilEndOfTurnGrantsOnZone(enemyBattleZoneCards);
    }

    private void ClearBreachUntilEndOfBattleGrantsForAllInPlayUnits()
    {
        ClearBreachUntilEndOfBattleGrantsOnZone(playerBattleZoneCards);
        ClearBreachUntilEndOfBattleGrantsOnZone(enemyBattleZoneCards);
    }

    private static void ClearBreachUntilEndOfTurnGrantsOnZone(List<CardController> zone)
    {
        if (zone == null)
        {
            return;
        }

        for (int i = 0; i < zone.Count; i++)
        {
            zone[i]?.ClearBreachUntilEndOfTurnGrants();
        }
    }

    private static void ClearBreachUntilEndOfBattleGrantsOnZone(List<CardController> zone)
    {
        if (zone == null)
        {
            return;
        }

        for (int i = 0; i < zone.Count; i++)
        {
            zone[i]?.ClearBreachUntilEndOfBattleGrants();
        }
    }

    private void ClearNotDirectAttackGrants(EffectDuration duration)
    {
        if (duration != EffectDuration.UntilEndOfTurn && duration != EffectDuration.UntilEndOfBattle)
        {
            return;
        }

        ClearNotDirectAttackGrantsOnZone(playerBattleZoneCards, duration);
        ClearNotDirectAttackGrantsOnZone(enemyBattleZoneCards, duration);
    }

    private static void ClearNotDirectAttackGrantsOnZone(List<CardController> zone, EffectDuration duration)
    {
        if (zone == null)
        {
            return;
        }

        for (int i = 0; i < zone.Count; i++)
        {
            CardController unit = zone[i];
            if (unit == null)
            {
                continue;
            }

            if (duration == EffectDuration.UntilEndOfTurn)
            {
                unit.ClearNotDirectAttackUntilEndOfTurnGrants();
            }
        }
    }

    private static void FilterOutNonRestedUnits(List<CardController> targets)
    {
        if (targets == null)
        {
            return;
        }

        for (int i = targets.Count - 1; i >= 0; i--)
        {
            CardController unit = targets[i];
            if (unit == null || !unit.IsRestState)
            {
                targets.RemoveAt(i);
            }
        }
    }
}
