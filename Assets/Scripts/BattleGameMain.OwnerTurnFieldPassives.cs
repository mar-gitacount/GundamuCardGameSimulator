using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 自ターン限定の盤面 Buff/Debuff（例: ガンダムの味方全体+1AP）をターン境界で解除・再付与する。
/// </summary>
public partial class BattleGameMain
{
    private static string MakeOwnerTurnFieldPassiveSourceKey(CardController unit, int blockIndex)
    {
        if (unit == null || unit.BattleInstanceId <= 0)
        {
            return null;
        }

        return CardController.MakeOwnerTurnFieldPassiveSourceKey(unit.BattleInstanceId, blockIndex);
    }

    /// <summary>全ユニットの自ターン限定盤面バフを一旦解除し、現在ターン側のみ再付与する。</summary>
    private void RefreshAllFieldOwnerTurnPassives()
    {
        bool nestedBatch = _onlineEffectSyncActive;
        if (!nestedBatch)
        {
            BeginOnlineEffectSyncBatch(currentPlayerType);
        }

        ClearAllOwnerTurnFieldPassiveModifiers();
        RefreshFieldOwnerTurnPassivesForSide(PlayerType.Player, syncOnlineBatch: false);
        RefreshFieldOwnerTurnPassivesForSide(PlayerType.Enemy, syncOnlineBatch: false);
        RefreshConditionalBlockerAbilities();

        // 個別 Stat は送らない。相手には「同じ再計算をして」とだけ伝える。
        // ネスト中でも外側バッチに載せる（Flush は外側に任せる）。
        // Begin がスキップされた受信側・Enemy ターンでは active のままなので Queue しない。
        if (_onlineEffectSyncActive)
        {
            QueueOnlineRefreshOwnerTurnFieldPassives();
        }
        if (!nestedBatch)
        {
            FlushOnlineEffectSyncBatch();
        }
    }

    /// <summary>
    /// 条件付き《ブロッカー》を盤面状態から再評価する。
    /// ユニット自身の isBlocker、および搭乗パイロットの isBlocker（Gyunei 等）を
    /// OnEnemyAttack の activationConditions でゲートし、ランタイム状態を ON/OFF する。
    /// </summary>
    private void RefreshConditionalBlockerAbilities()
    {
        RefreshConditionalBlockerAbilitiesOnZone(playerBattleZoneCards, PlayerType.Player);
        RefreshConditionalBlockerAbilitiesOnZone(enemyBattleZoneCards, PlayerType.Enemy);
    }

    private void RefreshConditionalBlockerAbilitiesOnZone(
        List<CardController> zone,
        PlayerType ownerType)
    {
        if (zone == null)
        {
            return;
        }

        for (int i = 0; i < zone.Count; i++)
        {
            CardController unit = zone[i];
            if (unit == null || unit.Data == null || !unit.Data.IsUnitLike())
            {
                continue;
            }

            EffectActivationContext context = BuildActivationContext(ownerType, unit);
            bool enabled = unit.IsBlockerEligible(context);
            bool changed = unit.HasBlockerAbility != enabled;
            unit.SetRuntimeBlockerAbility(enabled);
            if (changed)
            {
                Debug.Log(
                    $"[ConditionalBlocker] {unit.Data.cardName}(id:{unit.Data.id}) "
                    + $"owner:{ownerType} blocker:{enabled}");
            }
        }
    }

    private void ClearAllOwnerTurnFieldPassiveModifiers()
    {
        HashSet<string> keys = new HashSet<string>();
        CollectOwnerTurnFieldPassiveSourceKeys(playerBattleZoneCards, keys);
        CollectOwnerTurnFieldPassiveSourceKeys(enemyBattleZoneCards, keys);

        foreach (string key in keys)
        {
            RemoveModifiersBySourceKeyFromAllFieldUnits(key);
        }

        ClearLegacyPilotMountAurasForOwnerTurnPassiveHosts(playerBattleZoneCards);
        ClearLegacyPilotMountAurasForOwnerTurnPassiveHosts(enemyBattleZoneCards);
    }

    private void ClearLegacyPilotMountAurasForOwnerTurnPassiveHosts(List<CardController> zone)
    {
        if (zone == null)
        {
            return;
        }

        for (int i = 0; i < zone.Count; i++)
        {
            CardController unit = zone[i];
            if (unit == null || !UnitHasFieldOwnerTurnStatPassiveBlock(unit))
            {
                continue;
            }

            unit.ClearPilotMountAllyFieldAuras();
            if (unit.BattleInstanceId > 0)
            {
                RemoveModifiersBySourceKeyFromAllFieldUnits(unit.MakePilotMountFieldAuraSourceKey());
            }
        }
    }

    private static bool UnitHasFieldOwnerTurnStatPassiveBlock(CardController unit)
    {
        if (unit?.Data?.timedEffects == null)
        {
            return false;
        }

        for (int i = 0; i < unit.Data.timedEffects.Count; i++)
        {
            if (unit.Data.timedEffects[i] != null && unit.Data.timedEffects[i].IsFieldOwnerTurnStatPassiveBlock())
            {
                return true;
            }
        }

        return false;
    }

    private static void CollectOwnerTurnFieldPassiveSourceKeys(
        List<CardController> zone,
        HashSet<string> keys)
    {
        if (zone == null || keys == null)
        {
            return;
        }

        for (int ui = 0; ui < zone.Count; ui++)
        {
            CardController unit = zone[ui];
            if (unit?.Data?.timedEffects == null)
            {
                continue;
            }

            for (int bi = 0; bi < unit.Data.timedEffects.Count; bi++)
            {
                TimedEffectData timed = unit.Data.timedEffects[bi];
                if (timed == null || !timed.IsFieldOwnerTurnStatPassiveBlock())
                {
                    continue;
                }

                if (unit.BattleInstanceId <= 0)
                {
                    continue;
                }

                keys.Add(MakeOwnerTurnFieldPassiveSourceKey(unit, bi));
            }
        }
    }

    private void RemoveModifiersBySourceKeyFromAllFieldUnits(string sourceKey)
    {
        if (string.IsNullOrEmpty(sourceKey))
        {
            return;
        }

        RemoveAndSyncStatModifiersBySourceFromCardList(
            playerBattleZoneCards,
            sourceKey,
            exclude: null,
            queueOnlineStatDeltas: false);
        RemoveAndSyncStatModifiersBySourceFromCardList(
            enemyBattleZoneCards,
            sourceKey,
            exclude: null,
            queueOnlineStatDeltas: false);
    }

    private void RefreshFieldOwnerTurnPassivesForSide(PlayerType side, bool syncOnlineBatch = true)
    {
        if (side != currentPlayerType)
        {
            return;
        }

        // オンラインでも currentPlayerType 側のローカル再計算は行う。
        // 以前の「Player のみ」ガードだと、受信側（相手ターン=Enemy）でガンダム等の再付与が走らず
        // 相手盤面にバフが乗らない。送信可否は Begin/ShouldSyncOnlineEffects 側で制限する。

        List<CardController> zone = side == PlayerType.Player ? playerBattleZoneCards : enemyBattleZoneCards;
        if (zone == null)
        {
            return;
        }

        for (int ui = 0; ui < zone.Count; ui++)
        {
            CardController unit = zone[ui];
            if (unit == null || unit.Data == null || unit.Data.timedEffects == null)
            {
                continue;
            }

            if (ResolveBattleZoneUnitOwner(unit) != side)
            {
                continue;
            }

            for (int bi = 0; bi < unit.Data.timedEffects.Count; bi++)
            {
                TimedEffectData timed = unit.Data.timedEffects[bi];
                if (timed == null || !timed.IsFieldOwnerTurnStatPassiveBlock())
                {
                    continue;
                }

                if (timed.timing == EffectTiming.OnPilotMounted
                    && unit.Data.IsUnitLike()
                    && unit.MountedPilot == null)
                {
                    continue;
                }

                if (timed.timing == EffectTiming.OnPilotMounted
                    && unit.Data.IsPilot()
                    && unit.MountedUnit == null)
                {
                    continue;
                }

                EffectActivationContext ctx = unit.Data.IsPilot() && unit.MountedUnit != null
                    ? BuildPilotMountActivationContext(side, unit, unit.MountedUnit, unit)
                    : BuildActivationContext(side, unit);
                if (!EffectActivationEvaluator.AreTimedConditionsMet(timed, ctx))
                {
                    continue;
                }

                ApplyOwnerTurnFieldStatPassiveBlock(unit, side, timed, bi, syncOnlineBatch);
            }
        }
    }

    private void ApplyOwnerTurnFieldStatPassiveBlock(
        CardController sourceUnit,
        PlayerType ownerType,
        TimedEffectData timed,
        int blockIndex,
        bool syncOnlineBatch = true)
    {
        if (sourceUnit == null || timed == null)
        {
            return;
        }

        string sourceKey = MakeOwnerTurnFieldPassiveSourceKey(sourceUnit, blockIndex);
        if (string.IsNullOrEmpty(sourceKey))
        {
            return;
        }

        IReadOnlyList<EffectData> effects = timed.GetResolvedEffects();
        if (syncOnlineBatch)
        {
            BeginOnlineEffectSyncBatch(ownerType);
        }
        for (int i = 0; i < effects.Count; i++)
        {
            EffectData effect = effects[i];
            if (effect == null
                || (effect.type != EffectType.Buff && effect.type != EffectType.Debuff)
                || effect.target == TargetType.Self)
            {
                continue;
            }

            if (effect.type.RequiresManualUnitSelection() || EffectRequiresManualUnitSelection(effect))
            {
                continue;
            }

            int magnitude = ResolveEffectMagnitude(effect, ownerType, sourceUnit);
            if (magnitude == 0)
            {
                continue;
            }

            int signedValue = (effect.type == EffectType.Buff ? 1 : -1) * magnitude;
            List<CardController> targets = ResolveEffectTargets(sourceUnit, ownerType, effect);
            for (int ti = 0; ti < targets.Count; ti++)
            {
                CardController target = targets[ti];
                if (target == null)
                {
                    continue;
                }

                ApplyStatEffect(
                    target,
                    signedValue,
                    effect.statTarget,
                    EffectDuration.Permanent,
                    sourceKey);
                if (syncOnlineBatch)
                {
                    Debug.Log($"[OwnerTurnField][OnlineQueueStat] {effect.type} {magnitude} target:{effect.target} stat:{effect.statTarget} "
                        + $"source:{sourceUnit.Data?.cardName}(id:{sourceUnit.Data?.id}) side:{ownerType}");
                    QueueOnlineUnitStat(target, signedValue, effect.statTarget, EffectDuration.Permanent, sourceKey);
                }
            }

            Debug.Log(
                $"[OwnerTurnField] {effect.type} {magnitude} target:{effect.target} stat:{effect.statTarget} "
                + $"online:{syncOnlineBatch} "
                + $"source:{sourceUnit.Data?.cardName}(id:{sourceUnit.Data?.id}) side:{ownerType}");
        }

        if (syncOnlineBatch)
        {
            FlushOnlineEffectSyncBatch();
        }
    }
}
