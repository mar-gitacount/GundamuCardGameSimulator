using System.Collections.Generic;
using UnityEngine;

/// <summary>HP 回復（ターン終了 isRepair / 効果 RecoverHp）。</summary>
public partial class BattleGameMain
{
    /// <summary>
    /// 効果による HP 回復（<see cref="EffectType.RecoverHp"/>）。
    /// 戦闘ダメージ撃破・エフェクトバトル撃破など、敵ユニット破壊時の回復にも再利用する。
    /// </summary>
    private void ApplyRecoverHpEffect(IReadOnlyList<CardController> targets, int amount)
    {
        if (targets == null || amount <= 0)
        {
            return;
        }

        for (int i = 0; i < targets.Count; i++)
        {
            CardController unit = targets[i];
            if (unit == null || !unit.IsRepairEligibleUnit())
            {
                continue;
            }

            int before = unit.CurrentHp;
            int healed = unit.TryApplyRepair(amount);
            if (healed <= 0)
            {
                continue;
            }

            QueueOnlineUnitRepair(unit);
            Debug.Log(
                $"[RecoverHp] {unit.Data?.cardName}(id:{unit.Data?.id}) +{healed} HP "
                + $"({before}->{unit.CurrentHp}/{unit.GetRepairHpCap()})");
        }
    }

    /// <summary>
    /// 盤上の isRepair ユニットを回復する。ターン終了 OnAction の直後・OnTurnEnd 効果の前に呼ぶ。
    /// </summary>
    private void ApplyTurnEndRepairForAllInPlayUnits()
    {
        // オンラインでは「自分のターン終了」だけローカル適用＋同期する。
        // 相手ターン終了は EffectSync(Repair) のみで反映する（二重計算禁止）。
        if (IsOnlineBattle() && currentPlayerType != PlayerType.Player)
        {
            Debug.Log("[Repair] skip local turn-end repair (online, not local turn owner)");
            return;
        }

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

            int amount = GetResolvedTurnEndRepairAmount(unit);
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
        List<CardController> activeTurnZone =
            currentPlayerType == PlayerType.Player
                ? playerBattleZoneCards
                : enemyBattleZoneCards;
        CollectTurnEndRepairFromZone(activeTurnZone, result);
        TryAddTurnEndRepairTarget(GetDeployedBaseForRuleSide(Gundam2024RuleScript.PlayerSide.Player), result);
        TryAddTurnEndRepairTarget(GetDeployedBaseForRuleSide(Gundam2024RuleScript.PlayerSide.Enemy), result);
        return result;
    }

    private void CollectTurnEndRepairFromZone(List<CardController> zone, List<CardController> result)
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

    private void TryAddTurnEndRepairTarget(CardController unit, List<CardController> result)
    {
        if (unit == null || result == null || !unit.IsRepairEligibleUnit())
        {
            return;
        }

        if (GetResolvedTurnEndRepairAmount(unit) <= 0)
        {
            return;
        }

        if (!result.Contains(unit))
        {
            result.Add(unit);
        }
    }

    private int GetResolvedTurnEndRepairAmount(CardController unit)
    {
        if (unit == null || unit.Data == null || unit.CurrentHp <= 0)
        {
            return 0;
        }

        // Hashmal: ターン終了時、その時点の自軍 Pluma 数だけ回復する。
        if (unit.Data.id == 144)
        {
            PlayerType owner = ResolveCardOwner(unit.transform);
            int plumaCount = CountOwnedPlumaTokens(owner);
            Debug.Log(
                $"[HashmalRepair] owner:{owner} plumaCount:{plumaCount} currentHp:{unit.CurrentHp}");
            return plumaCount;
        }

        return unit.GetTurnEndRepairAmount();
    }

    private int CountOwnedPlumaTokens(PlayerType owner)
    {
        List<CardController> zone = owner == PlayerType.Player ? playerBattleZoneCards : enemyBattleZoneCards;
        if (zone == null)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < zone.Count; i++)
        {
            CardController unit = zone[i];
            if (unit == null || unit.Data == null || unit.CurrentHp <= 0)
            {
                continue;
            }

            if (unit.Data.id == 143)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// 盤上の isRepair ユニットへ、SyncTurnEndRepairBonus 定義に基づきボーナスを再計算する。
    /// プルーマ配備後など、ターン中の枚数変動に追従する。
    /// </summary>
    private void RefreshSyncTurnEndRepairBonusesForSide(PlayerType side)
    {
        List<CardController> zone = side == PlayerType.Player ? playerBattleZoneCards : enemyBattleZoneCards;
        if (zone == null)
        {
            return;
        }

        for (int i = 0; i < zone.Count; i++)
        {
            CardController unit = zone[i];
            if (unit?.Data == null || !unit.Data.isRepair)
            {
                continue;
            }

            EffectData syncEffect = FindSyncTurnEndRepairBonusEffect(unit.Data);
            if (syncEffect == null)
            {
                continue;
            }

            ApplySyncTurnEndRepairBonusEffect(unit, side, syncEffect);
        }
    }

    private static EffectData FindSyncTurnEndRepairBonusEffect(CardData data)
    {
        if (data?.timedEffects == null)
        {
            return null;
        }

        for (int i = 0; i < data.timedEffects.Count; i++)
        {
            TimedEffectData timed = data.timedEffects[i];
            if (timed == null)
            {
                continue;
            }

            IReadOnlyList<EffectData> resolved = timed.GetResolvedEffects();
            for (int j = 0; j < resolved.Count; j++)
            {
                EffectData effect = resolved[j];
                if (effect != null && effect.type == EffectType.SyncTurnEndRepairBonus)
                {
                    return effect;
                }
            }
        }

        return null;
    }

    private void ApplySyncTurnEndRepairBonusEffect(
        CardController sourceCard,
        PlayerType ownerType,
        EffectData effect)
    {
        if (sourceCard == null || effect == null || effect.type != EffectType.SyncTurnEndRepairBonus)
        {
            return;
        }

        sourceCard.ClearTurnEndRepairBonus();
        int perToken = Mathf.Max(1, effect.value);
        int tokenCount = CountOwnerUnitTokensMatchingEffectFilter(ownerType, effect, sourceCard);
        int bonus = perToken * tokenCount;
        if (bonus > 0)
        {
            sourceCard.AddTurnEndRepairBonus(bonus);
        }

        Debug.Log(
            $"[SyncTurnEndRepairBonus] {sourceCard.Data?.cardName}(id:{sourceCard.Data?.id}) "
            + $"tokens:{tokenCount} bonus:+{bonus} total:{sourceCard.GetTurnEndRepairAmount()}");
    }

    private int CountOwnerUnitTokensMatchingEffectFilter(
        PlayerType ownerType,
        EffectData effect,
        CardController sourceCard)
    {
        List<CardController> zone = ownerType == PlayerType.Player ? playerBattleZoneCards : enemyBattleZoneCards;
        if (zone == null || effect == null)
        {
            return 0;
        }

        CardFeatureData feature = null;
        if (effect.valueCountFeature != null)
        {
            feature = effect.valueCountFeature;
        }
        else if (effect.valueCountFeatureId > 0)
        {
            CardFeatureRegistry.EnsureLoaded();
            feature = CardFeatureRegistry.GetById(effect.valueCountFeatureId);
        }

        int count = 0;
        for (int i = 0; i < zone.Count; i++)
        {
            CardController unit = zone[i];
            if (unit == null || unit.Data == null || unit.CurrentHp <= 0 || !unit.Data.IsUnitToken())
            {
                continue;
            }

            if (!effect.MatchesTargetCardTypeFilter(unit.Data))
            {
                continue;
            }

            if (feature != null && !unit.Data.HasFeature(feature) && !unit.Data.HasFeatureId(feature.id))
            {
                continue;
            }

            if (effect.valueCountExcludeSource && sourceCard != null && unit == sourceCard)
            {
                continue;
            }

            count++;
        }

        return count;
    }
}
