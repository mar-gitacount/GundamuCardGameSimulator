using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 自ターン限定の盤面 Buff/Debuff（例: ガンダムの味方全体+1AP）をターン境界で解除・再付与する。
/// </summary>
public partial class BattleGameMain
{
    private static bool IsOwnerTurnFieldPassiveTimedBlock(TimedEffectData timed)
    {
        return timed != null
            && (timed.IsFieldOwnerTurnStatPassiveBlock() || timed.MatchesOwnerTurnMountFieldPassivePattern());
    }

    private static string MakeOwnerTurnFieldPassiveSourceKey(CardController unit, int blockIndex)
    {
        if (unit == null)
        {
            return null;
        }

        if (unit.BattleInstanceId > 0)
        {
            return CardController.MakeOwnerTurnFieldPassiveSourceKey(unit.BattleInstanceId, blockIndex);
        }

        return $"OwnerTurnField:{unit.GetEntityId()}:{blockIndex}";
    }

    private const int ZaftFeatureId = 3;
    private const int ProvidenceCardId = 1000297;

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
        RefreshDuringLinkFeatureGrants();
        RefreshDuringLinkSelfStatPassives();

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
        RefreshConditionalFieldSelfStatPassives();
    }

    private static string MakeFieldConditionalSelfStatSourceKey(CardController unit, int blockIndex)
    {
        return unit != null ? $"FieldConditionalSelfStat:{unit.GetEntityId()}:{blockIndex}" : string.Empty;
    }

    /// <summary>
    /// 盤面条件付き Self Buff/Debuff。
    /// ユニット自身（OnEnemyAttack 条件付き）および搭乗パイロットの
    /// 《リペア》持ちホスト向け常時 AP（リディ等）を再評価する。
    /// </summary>
    private void RefreshConditionalFieldSelfStatPassives()
    {
        RefreshConditionalFieldSelfStatPassivesOnZone(playerBattleZoneCards, PlayerType.Player);
        RefreshConditionalFieldSelfStatPassivesOnZone(enemyBattleZoneCards, PlayerType.Enemy);
    }

    private void RefreshConditionalFieldSelfStatPassivesOnZone(
        List<CardController> zone,
        PlayerType ownerType)
    {
        if (zone == null)
        {
            return;
        }

        for (int i = 0; i < zone.Count; i++)
        {
            CardController host = zone[i];
            if (host == null || host.Data == null || !host.Data.IsUnitLike() || host.CurrentHp <= 0)
            {
                continue;
            }

            if (ResolveBattleZoneUnitOwner(host) != ownerType)
            {
                continue;
            }

            ApplyConditionalFieldSelfStatFromTimedBlocks(host, host, ownerType, host.Data.timedEffects, isPilotSource: false);

            CardController pilot = host.MountedPilot;
            if (pilot?.Data?.timedEffects != null)
            {
                ApplyConditionalFieldSelfStatFromTimedBlocks(
                    pilot,
                    host,
                    ownerType,
                    pilot.Data.timedEffects,
                    isPilotSource: true);
            }
        }
    }

    private static bool TimedHasSourceMountHostHasRepairCondition(TimedEffectData timed)
    {
        if (timed?.activationConditions == null)
        {
            return false;
        }

        for (int i = 0; i < timed.activationConditions.Count; i++)
        {
            EffectActivationCondition c = timed.activationConditions[i];
            if (c != null && c.checkKind == EffectActivationCheckKind.SourceMountHostHasRepair)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>搭乗中・ホストが《リペア》なら Self AP 等（リディ GD01-089）。常時表示。</summary>
    private static bool IsMountedPilotWhileHostHasRepairSelfStatPassive(TimedEffectData timed, bool isPilotSource)
    {
        return isPilotSource
            && timed != null
            && timed.HasResolvedEffects()
            && timed.HasActivationConditions()
            && timed.ContainsOnlySelfStatBuffDebuffEffects()
            && TimedHasSourceMountHostHasRepairCondition(timed);
    }

    /// <summary>ユニット自身の OnEnemyAttack 条件付き Self stat（Michaelis 等）。</summary>
    private static bool IsFieldUnitOnEnemyAttackSelfStatPassive(TimedEffectData timed, bool isPilotSource)
    {
        return !isPilotSource
            && timed != null
            && timed.timing == EffectTiming.OnEnemyAttack
            && timed.HasResolvedEffects()
            && timed.HasActivationConditions()
            && timed.ContainsOnlySelfStatBuffDebuffEffects();
    }

    private void ApplyConditionalFieldSelfStatFromTimedBlocks(
        CardController effectSource,
        CardController statTarget,
        PlayerType ownerType,
        IReadOnlyList<TimedEffectData> blocks,
        bool isPilotSource)
    {
        if (effectSource == null || statTarget == null || blocks == null)
        {
            return;
        }

        for (int bi = 0; bi < blocks.Count; bi++)
        {
            TimedEffectData timed = blocks[bi];
            if (!IsMountedPilotWhileHostHasRepairSelfStatPassive(timed, isPilotSource)
                && !IsFieldUnitOnEnemyAttackSelfStatPassive(timed, isPilotSource))
            {
                continue;
            }

            string sourceKey = isPilotSource
                ? MakeMountedPilotHostApBonusSourceKey(effectSource)
                : MakeFieldConditionalSelfStatSourceKey(statTarget, bi);
            if (string.IsNullOrEmpty(sourceKey))
            {
                continue;
            }

            statTarget.RemoveStatModifiersBySource(sourceKey);

            CardController mountPilot = isPilotSource ? effectSource : statTarget.MountedPilot;
            EffectActivationContext ctx = isPilotSource
                ? BuildPilotMountActivationContext(ownerType, effectSource, statTarget, mountPilot)
                : BuildActivationContext(ownerType, statTarget);
            if (!EffectActivationEvaluator.AreTimedConditionsMet(timed, ctx))
            {
                continue;
            }

            IReadOnlyList<EffectData> effects = timed.GetResolvedEffects();
            for (int ei = 0; ei < effects.Count; ei++)
            {
                EffectData effect = effects[ei];
                if (effect == null
                    || (effect.type != EffectType.Buff && effect.type != EffectType.Debuff)
                    || effect.target != TargetType.Self)
                {
                    continue;
                }

                int magnitude = ResolveEffectMagnitude(effect, ownerType, effectSource);
                if (magnitude == 0)
                {
                    continue;
                }

                int signedValue = (effect.type == EffectType.Buff ? 1 : -1) * magnitude;
                ApplyStatEffect(
                    statTarget,
                    signedValue,
                    effect.statTarget,
                    effect.duration,
                    sourceKey);
            }
        }
    }

    private static string MakeMountedPilotHostApBonusSourceKey(CardController pilot)
    {
        return pilot != null ? $"MountedPilotHostAp:{pilot.GetEntityId()}" : string.Empty;
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

    /// <summary>【リンク中】味方全体 Feature 付与（Neo Zeong 等）を盤面状態から再評価する。</summary>
    private void RefreshDuringLinkFeatureGrants()
    {
        ClearRuntimeFeatureGrantsOnZone(playerBattleZoneCards);
        ClearRuntimeFeatureGrantsOnZone(enemyBattleZoneCards);
        ApplyDuringLinkFeatureGrantsOnZone(playerBattleZoneCards, PlayerType.Player);
        ApplyDuringLinkFeatureGrantsOnZone(enemyBattleZoneCards, PlayerType.Enemy);
    }

    private static void ClearRuntimeFeatureGrantsOnZone(List<CardController> zone)
    {
        if (zone == null)
        {
            return;
        }

        for (int i = 0; i < zone.Count; i++)
        {
            zone[i]?.ClearRuntimeFeatureGrants();
        }
    }

    private void ApplyDuringLinkFeatureGrantsOnZone(List<CardController> zone, PlayerType ownerType)
    {
        if (zone == null)
        {
            return;
        }

        for (int wi = 0; wi < zone.Count; wi++)
        {
            CardController watcher = zone[wi];
            if (watcher?.Data?.timedEffects == null || !watcher.Data.IsUnitLike() || watcher.CurrentHp <= 0)
            {
                continue;
            }

            if (ResolveBattleZoneUnitOwner(watcher) != ownerType)
            {
                continue;
            }

            for (int bi = 0; bi < watcher.Data.timedEffects.Count; bi++)
            {
                TimedEffectData timed = watcher.Data.timedEffects[bi];
                if (timed == null || !timed.IsDuringLinkFeatureGrantBlock())
                {
                    continue;
                }

                EffectActivationContext ctx = BuildActivationContext(ownerType, watcher);
                if (!EffectActivationEvaluator.AreTimedConditionsMet(timed, ctx))
                {
                    continue;
                }

                IReadOnlyList<EffectData> resolved = timed.GetResolvedEffects();
                for (int ei = 0; ei < resolved.Count; ei++)
                {
                    EffectData effect = resolved[ei];
                    if (effect == null
                        || effect.type != EffectType.GrantFeature
                        || effect.value <= 0
                        || (effect.target != TargetType.AllyAllUnits && effect.target != TargetType.AllyUnit))
                    {
                        continue;
                    }

                    int featureId = effect.value;
                    for (int ai = 0; ai < zone.Count; ai++)
                    {
                        CardController ally = zone[ai];
                        if (ally?.Data == null || !ally.Data.IsUnitLike() || ally.CurrentHp <= 0)
                        {
                            continue;
                        }

                        if (ResolveBattleZoneUnitOwner(ally) != ownerType)
                        {
                            continue;
                        }

                        ally.SetRuntimeFeatureGrant(featureId, true);
                    }
                }
            }
        }
    }

    /// <summary>【リンク中】Self への Buff/Debuff（リディ GD04-098 等）を盤面状態から再評価する。</summary>
    private void RefreshDuringLinkSelfStatPassives()
    {
        ClearDuringLinkSelfStatPassivesOnZone(playerBattleZoneCards);
        ClearDuringLinkSelfStatPassivesOnZone(enemyBattleZoneCards);
        ApplyDuringLinkSelfStatPassivesOnZone(playerBattleZoneCards, PlayerType.Player);
        ApplyDuringLinkSelfStatPassivesOnZone(enemyBattleZoneCards, PlayerType.Enemy);
    }

    private static string MakeDuringLinkSelfStatSourceKey(CardController source, int blockIndex, bool isPilotSource)
    {
        if (source == null)
        {
            return string.Empty;
        }

        return isPilotSource
            ? $"DuringLinkPilotSelfStat:{source.GetEntityId()}:{blockIndex}"
            : $"DuringLinkSelfStat:{source.GetEntityId()}:{blockIndex}";
    }

    private static void ClearDuringLinkSelfStatKeysFromCard(
        CardController statTarget,
        CardController effectSource,
        IReadOnlyList<TimedEffectData> blocks,
        bool isPilotSource)
    {
        if (statTarget == null || effectSource == null || blocks == null)
        {
            return;
        }

        for (int bi = 0; bi < blocks.Count; bi++)
        {
            TimedEffectData timed = blocks[bi];
            if (timed == null || !timed.IsDuringLinkSelfStatPassiveBlock())
            {
                continue;
            }

            string sourceKey = MakeDuringLinkSelfStatSourceKey(effectSource, bi, isPilotSource);
            if (!string.IsNullOrEmpty(sourceKey))
            {
                statTarget.RemoveStatModifiersBySource(sourceKey);
            }
        }
    }

    private void ClearDuringLinkSelfStatPassivesOnZone(List<CardController> zone)
    {
        if (zone == null)
        {
            return;
        }

        for (int i = 0; i < zone.Count; i++)
        {
            CardController host = zone[i];
            if (host?.Data == null || !host.Data.IsUnitLike())
            {
                continue;
            }

            ClearDuringLinkSelfStatKeysFromCard(host, host, host.Data.timedEffects, isPilotSource: false);

            CardController pilot = host.MountedPilot;
            if (pilot?.Data?.timedEffects != null)
            {
                ClearDuringLinkSelfStatKeysFromCard(host, pilot, pilot.Data.timedEffects, isPilotSource: true);
            }
        }
    }

    private void ApplyDuringLinkSelfStatPassivesOnZone(List<CardController> zone, PlayerType ownerType)
    {
        if (zone == null)
        {
            return;
        }

        for (int i = 0; i < zone.Count; i++)
        {
            CardController host = zone[i];
            if (host?.Data == null || !host.Data.IsUnitLike() || host.CurrentHp <= 0)
            {
                continue;
            }

            if (ResolveBattleZoneUnitOwner(host) != ownerType)
            {
                continue;
            }

            ApplyDuringLinkSelfStatFromBlocks(host, host, ownerType, host.Data.timedEffects, isPilotSource: false);

            CardController pilot = host.MountedPilot;
            if (pilot?.Data?.timedEffects != null)
            {
                ApplyDuringLinkSelfStatFromBlocks(pilot, host, ownerType, pilot.Data.timedEffects, isPilotSource: true);
            }
        }
    }

    private void ApplyDuringLinkSelfStatFromBlocks(
        CardController effectSource,
        CardController statTarget,
        PlayerType ownerType,
        IReadOnlyList<TimedEffectData> blocks,
        bool isPilotSource)
    {
        if (effectSource == null || statTarget == null || blocks == null)
        {
            return;
        }

        for (int bi = 0; bi < blocks.Count; bi++)
        {
            TimedEffectData timed = blocks[bi];
            if (timed == null || !timed.IsDuringLinkSelfStatPassiveBlock())
            {
                continue;
            }

            string sourceKey = MakeDuringLinkSelfStatSourceKey(effectSource, bi, isPilotSource);
            if (string.IsNullOrEmpty(sourceKey))
            {
                continue;
            }

            CardController mountPilot = isPilotSource ? effectSource : statTarget.MountedPilot;
            EffectActivationContext ctx = isPilotSource
                ? BuildPilotMountActivationContext(ownerType, effectSource, statTarget, mountPilot)
                : BuildActivationContext(ownerType, statTarget);
            if (!EffectActivationEvaluator.AreTimedConditionsMet(timed, ctx))
            {
                continue;
            }

            IReadOnlyList<EffectData> effects = timed.GetResolvedEffects();
            for (int ei = 0; ei < effects.Count; ei++)
            {
                EffectData effect = effects[ei];
                if (effect == null
                    || (effect.type != EffectType.Buff && effect.type != EffectType.Debuff)
                    || effect.target != TargetType.Self)
                {
                    continue;
                }

                int magnitude = ResolveEffectMagnitude(effect, ownerType, effectSource);
                if (magnitude == 0)
                {
                    continue;
                }

                int signedValue = (effect.type == EffectType.Buff ? 1 : -1) * magnitude;
                ApplyStatEffect(
                    statTarget,
                    signedValue,
                    effect.statTarget,
                    effect.duration,
                    sourceKey);
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
            if (unit.Data.timedEffects[i] != null
                && (IsOwnerTurnFieldPassiveTimedBlock(unit.Data.timedEffects[i])
                    || IsOnPilotMountedAllyFieldStatBlock(unit.Data.timedEffects[i])))
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
                if (timed == null
                    || (!IsOwnerTurnFieldPassiveTimedBlock(timed)
                        && !IsOnPilotMountedAllyFieldStatBlock(timed)
                        && !IsZaftDuringPairAllyApBuffBlock(timed)))
                {
                    continue;
                }

                string key = MakeOwnerTurnFieldPassiveSourceKey(unit, bi);
                if (!string.IsNullOrEmpty(key))
                {
                    keys.Add(key);
                }
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

            AssignBattleInstanceIdIfNeeded(unit);

            if (unit.Data.IsUnitLike()
                && unit.MountedPilot?.Data != null
                && unit.MountedPilot.Data.HasFeatureId(ZaftFeatureId)
                && TryResolveZaftDuringPairAllyApBuffFromHost(unit, side, out _, out _, out _))
            {
                unit.MountedPilot.Data.EnsureFeaturesResolved();
                ApplyZaftDuringPairAllyApBuff(unit, unit.MountedPilot, side);
            }

            for (int bi = 0; bi < unit.Data.timedEffects.Count; bi++)
            {
                TimedEffectData timed = unit.Data.timedEffects[bi];
                if (timed == null)
                {
                    continue;
                }

                if (IsZaftDuringPairAllyApBuffBlock(timed))
                {
                    continue;
                }

                if (!IsOwnerTurnFieldPassiveTimedBlock(timed)
                    && !IsOnPilotMountedAllyFieldStatBlock(timed))
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

                bool conditionsMet;
                if (timed.timing == EffectTiming.OnPilotMounted
                    && unit.Data.IsUnitLike()
                    && unit.MountedPilot != null)
                {
                    conditionsMet = PilotMeetsOnPilotMountedDuringPairRequirement(unit.MountedPilot, timed);
                }
                else
                {
                    EffectActivationContext ctx = BuildOwnerTurnFieldPassiveActivationContext(side, unit, timed);
                    conditionsMet = EffectActivationEvaluator.AreTimedConditionsMet(timed, ctx);
                }

                if (!conditionsMet)
                {
                    continue;
                }

                ApplyOwnerTurnFieldStatPassiveBlock(unit, side, timed, bi, syncOnlineBatch);
            }
        }
    }

    /// <summary>
    /// 自ターン盤面パッシブ評価用コンテキスト。
    /// During Pair（MountedPilot 条件）を正しく見るため、ユニットにパイロットが載っている場合は搭乗情報を明示する。
    /// </summary>
    private EffectActivationContext BuildOwnerTurnFieldPassiveActivationContext(
        PlayerType side,
        CardController unit,
        TimedEffectData timed)
    {
        if (unit == null)
        {
            return BuildActivationContext(side, unit);
        }

        if (unit.Data != null && unit.Data.IsPilot() && unit.MountedUnit != null)
        {
            return BuildPilotMountActivationContext(side, unit, unit.MountedUnit, unit);
        }

        if (unit.Data != null && unit.Data.IsUnitLike() && unit.MountedPilot != null)
        {
            unit.MountedPilot.Data?.EnsureFeaturesResolved();
            return BuildPilotMountActivationContext(side, unit, unit, unit.MountedPilot);
        }

        return BuildActivationContext(side, unit);
    }

    private static bool IsOnPilotMountedAllyFieldStatBlock(TimedEffectData timed)
    {
        if (timed == null || timed.timing != EffectTiming.OnPilotMounted || !timed.HasResolvedEffects())
        {
            return false;
        }

        IReadOnlyList<EffectData> resolved = timed.GetResolvedEffects();
        for (int i = 0; i < resolved.Count; i++)
        {
            EffectData effect = resolved[i];
            if (effect == null)
            {
                continue;
            }

            if ((effect.type == EffectType.Buff || effect.type == EffectType.Debuff)
                && effect.target != TargetType.Self)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 搭乗直後: (ZAFT) パイロットが載ったとき、味方 (ZAFT) ユニット全体（自身含む）へ AP バフ。
    /// プロヴィデンスガンダム等の OnPilotMounted 味方全体 Buff を直接適用する。
    /// </summary>
    private void ApplyOnPilotMountedAllyBuffsDirect(
        CardController hostUnit,
        CardController pilot,
        PlayerType ownerType)
    {
        ApplyZaftDuringPairAllyApBuff(hostUnit, pilot, ownerType);
    }

    /// <summary>
    /// 自ターン中、ホストに (ZAFT) パイロットが載っているとき味方 (ZAFT) 全体に AP バフ。
    /// </summary>
    private void ApplyZaftDuringPairAllyApBuff(
        CardController hostUnit,
        CardController pilot,
        PlayerType ownerType)
    {
        if (hostUnit?.Data == null || pilot?.Data == null || !hostUnit.Data.IsUnitLike())
        {
            return;
        }

        if (ownerType != currentPlayerType)
        {
            return;
        }

        pilot.Data.EnsureFeaturesResolved();
        if (!pilot.Data.HasFeatureId(ZaftFeatureId))
        {
            return;
        }

        int blockIndex = 0;
        int apBonus = 0;
        int targetFeatureId = ZaftFeatureId;
        if (!TryResolveZaftDuringPairAllyApBuffFromHost(hostUnit, ownerType, out blockIndex, out apBonus, out targetFeatureId))
        {
            return;
        }

        AssignBattleInstanceIdIfNeeded(hostUnit);
        string sourceKey = MakeOwnerTurnFieldPassiveSourceKey(hostUnit, blockIndex);
        if (string.IsNullOrEmpty(sourceKey))
        {
            return;
        }

        RemoveModifiersBySourceKeyFromAllFieldUnits(sourceKey);

        List<CardController> allies = ownerType == PlayerType.Player ? playerBattleZoneCards : enemyBattleZoneCards;
        if (allies == null)
        {
            return;
        }

        bool nestedBatch = _onlineEffectSyncActive;
        if (!nestedBatch)
        {
            BeginOnlineEffectSyncBatch(ownerType);
        }

        int applied = 0;
        for (int i = 0; i < allies.Count; i++)
        {
            CardController ally = allies[i];
            if (ally?.Data == null || !ally.Data.IsUnitLike() || ally.CurrentHp <= 0)
            {
                continue;
            }

            ally.Data.EnsureFeaturesResolved();
            if (!ally.Data.HasFeatureId(targetFeatureId))
            {
                continue;
            }

            ApplyStatEffect(
                ally,
                apBonus,
                EffectStatTarget.AP,
                EffectDuration.Permanent,
                sourceKey);
            if (_onlineEffectSyncActive)
            {
                QueueOnlineUnitStat(
                    ally,
                    apBonus,
                    EffectStatTarget.AP,
                    EffectDuration.Permanent,
                    sourceKey);
            }

            applied++;
        }

        if (!nestedBatch)
        {
            FlushOnlineEffectSyncBatch();
        }

        Debug.Log(
            $"[MountBuff] {hostUnit.Data.cardName} +{apBonus}AP to {applied} ZAFT unit(s) "
            + $"(pilot:{pilot.Data.cardName})");
    }

    /// <summary>ホストの OnPilotMounted 味方 Buff ブロックから AP 量・対象 Feature を得る。</summary>
    private bool TryResolveZaftDuringPairAllyApBuffFromHost(
        CardController hostUnit,
        PlayerType ownerType,
        out int blockIndex,
        out int apBonus,
        out int targetFeatureId)
    {
        blockIndex = 0;
        apBonus = 0;
        targetFeatureId = ZaftFeatureId;

        if (hostUnit?.Data?.timedEffects != null)
        {
            for (int bi = 0; bi < hostUnit.Data.timedEffects.Count; bi++)
            {
                TimedEffectData timed = hostUnit.Data.timedEffects[bi];
                if (timed == null || !IsOnPilotMountedTiming(timed))
                {
                    continue;
                }

                IReadOnlyList<EffectData> effects = timed.GetResolvedEffects();
                for (int ei = 0; ei < effects.Count; ei++)
                {
                    EffectData effect = effects[ei];
                    if (effect == null
                        || effect.type != EffectType.Buff
                        || effect.target != TargetType.AllyAllUnits
                        || effect.statTarget != EffectStatTarget.AP)
                    {
                        continue;
                    }

                    int magnitude = ResolveEffectMagnitude(effect, ownerType, hostUnit);
                    if (magnitude <= 0)
                    {
                        magnitude = effect.value;
                    }

                    if (magnitude <= 0)
                    {
                        continue;
                    }

                    blockIndex = bi;
                    apBonus = magnitude;
                    if (effect.targetFeatureId > 0)
                    {
                        targetFeatureId = effect.targetFeatureId;
                    }

                    return true;
                }
            }
        }

        if (hostUnit.Data.id == ProvidenceCardId
            || string.Equals(hostUnit.Data.gcgOfficialId, "GD03-033", System.StringComparison.OrdinalIgnoreCase))
        {
            blockIndex = 0;
            apBonus = 2;
            targetFeatureId = ZaftFeatureId;
            return true;
        }

        return false;
    }

    private static bool IsOnPilotMountedTiming(TimedEffectData timed)
    {
        return timed != null
            && (timed.timing == EffectTiming.OnPilotMounted || (int)timed.timing == 15);
    }

    private static bool IsZaftDuringPairAllyApBuffBlock(TimedEffectData timed)
    {
        if (!IsOnPilotMountedTiming(timed) || !timed.HasResolvedEffects())
        {
            return false;
        }

        IReadOnlyList<EffectData> effects = timed.GetResolvedEffects();
        for (int i = 0; i < effects.Count; i++)
        {
            EffectData effect = effects[i];
            if (effect != null
                && effect.type == EffectType.Buff
                && effect.target == TargetType.AllyAllUnits
                && effect.statTarget == EffectStatTarget.AP)
            {
                return true;
            }
        }

        return false;
    }

    private static bool PilotMeetsOnPilotMountedDuringPairRequirement(
        CardController pilot,
        TimedEffectData timed)
    {
        if (pilot?.Data == null)
        {
            return false;
        }

        pilot.Data.EnsureFeaturesResolved();
        if (timed?.activationConditions == null || timed.activationConditions.Count == 0)
        {
            return true;
        }

        for (int i = 0; i < timed.activationConditions.Count; i++)
        {
            EffectActivationCondition c = timed.activationConditions[i];
            if (c == null || c.checkKind != EffectActivationCheckKind.MountedPilot)
            {
                continue;
            }

            if (c.featureId > 0 && !pilot.Data.HasFeatureId(c.featureId))
            {
                return false;
            }

            IReadOnlyList<CardFeatureData> required = c.GetActivationFeatures();
            if (required.Count > 0 && !pilot.Data.HasAnyFeature(required))
            {
                return false;
            }
        }

        return true;
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

        AssignBattleInstanceIdIfNeeded(sourceUnit);

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
            Debug.Log(
                $"[MountBuff] {sourceUnit.Data?.cardName} → {effect.type} +{magnitude} "
                + $"targets:{targets.Count} pilot:{sourceUnit.MountedPilot?.Data?.cardName}");
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
                    QueueOnlineUnitStat(target, signedValue, effect.statTarget, EffectDuration.Permanent, sourceKey);
                }
            }
        }

        if (syncOnlineBatch)
        {
            FlushOnlineEffectSyncBatch();
        }
    }
}
