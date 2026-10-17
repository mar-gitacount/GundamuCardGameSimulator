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
            && (timed.IsFieldOwnerTurnStatPassiveBlock()
                || timed.IsFieldNotOwnerTurnStatPassiveBlock()
                || timed.MatchesOwnerTurnMountFieldPassivePattern());
    }

    /// <summary>自ターン限定はターンプレイヤー側、相手ターン限定は非ターンプレイヤー側だけ再付与する。</summary>
    private bool ShouldApplyFieldTurnStatPassiveForSide(TimedEffectData timed, PlayerType side)
    {
        if (timed == null)
        {
            return false;
        }

        if (timed.IsFieldNotOwnerTurnStatPassiveBlock())
        {
            return side != currentPlayerType;
        }

        if (timed.IsFieldOwnerTurnStatPassiveBlock() || timed.MatchesOwnerTurnMountFieldPassivePattern())
        {
            return side == currentPlayerType;
        }

        return true;
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
    private const int GymGhingnhamCardId = 1000855;
    private const int GymGhingnhamPilotId = 144;
    private const int GhingnhamFleetFeatureId = 83;

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
        RefreshDuringLinkAllyFieldStatPassives();
        RefreshGymGhingnhamOpponentTurnApBuff();

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

    /// <summary>ユニット自身の自ターン条件付き Self stat（キュリオス等）。</summary>
    private static bool IsFieldUnitOwnerTurnSelfStatPassive(TimedEffectData timed, bool isPilotSource)
    {
        return !isPilotSource && timed != null && timed.IsFieldOwnerTurnSelfStatPassiveBlock();
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

    /// <summary>セット中パイロット条件の Self AP（GQuuuuuuX 等）。自ターン限定ではない。</summary>
    private static bool IsFieldUnitMountedPilotSelfStatPassive(TimedEffectData timed, bool isPilotSource)
    {
        return !isPilotSource
            && timed != null
            && timed.HasResolvedEffects()
            && timed.HasActivationConditions()
            && timed.ContainsOnlySelfStatBuffDebuffEffects()
            && timed.HasMountedPilotActivationRequirement()
            && !timed.HasOwnerTurnActivationRequirement();
    }

    /// <summary>トラッシュ特徴枚数条件の Self AP（ザクⅡF2型等）。自ターン限定ではない。</summary>
    private static bool IsFieldUnitTrashFeatureSelfStatPassive(TimedEffectData timed, bool isPilotSource)
    {
        return !isPilotSource
            && timed != null
            && timed.HasResolvedEffects()
            && timed.HasActivationConditions()
            && timed.ContainsOnlySelfStatBuffDebuffEffects()
            && timed.HasTrashHasFeatureActivationRequirement();
    }

    /// <summary>手札枚数連動 Self AP（GGumega 等）。自ターン限定ではない。</summary>
    private static bool IsFieldUnitHandCountSelfStatPassive(TimedEffectData timed, bool isPilotSource)
    {
        return !isPilotSource
            && timed != null
            && timed.HasResolvedEffects()
            && timed.HasOwnerHandCountSelfStatValueScale();
    }

    /// <summary>プレイヤー Lv 条件の Self AP（AGE-1 タイタス C 等）。</summary>
    private static bool IsFieldUnitOwnerLevelSelfStatPassive(TimedEffectData timed, bool isPilotSource)
    {
        return !isPilotSource
            && timed != null
            && timed.HasResolvedEffects()
            && timed.HasActivationConditions()
            && timed.ContainsOnlySelfStatBuffDebuffEffects()
            && timed.HasOwnerTotalLevelActivationRequirement();
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
                && !IsFieldUnitOnEnemyAttackSelfStatPassive(timed, isPilotSource)
                && !IsFieldUnitOwnerTurnSelfStatPassive(timed, isPilotSource)
                && !IsFieldUnitMountedPilotSelfStatPassive(timed, isPilotSource)
                && !IsFieldUnitOwnerLevelSelfStatPassive(timed, isPilotSource)
                && !IsFieldUnitTrashFeatureSelfStatPassive(timed, isPilotSource)
                && !IsFieldUnitHandCountSelfStatPassive(timed, isPilotSource))
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

    /// <summary>
    /// GD06-085 ギンガム：相手ターン中、味方〔ギンガム艦隊〕全体（リンクしている自身を含む）へ AP+1。
    /// 名前付き効果の分類漏れでも確実に再付与する。
    /// </summary>
    private void RefreshGymGhingnhamOpponentTurnApBuff()
    {
        ClearGymGhingnhamOpponentTurnApBuffOnZone(playerBattleZoneCards);
        ClearGymGhingnhamOpponentTurnApBuffOnZone(enemyBattleZoneCards);
        ApplyGymGhingnhamOpponentTurnApBuffOnZone(playerBattleZoneCards, PlayerType.Player);
        ApplyGymGhingnhamOpponentTurnApBuffOnZone(enemyBattleZoneCards, PlayerType.Enemy);
    }

    private static string MakeGymGhingnhamOpponentTurnApSourceKey(CardController host)
    {
        if (host == null)
        {
            return null;
        }

        if (host.BattleInstanceId > 0)
        {
            return $"GymGhingnhamOppTurnAp:{host.BattleInstanceId}";
        }

        return $"GymGhingnhamOppTurnAp:{host.GetEntityId()}";
    }

    private static bool IsGymGhingnhamPilotCard(CardData data)
    {
        if (data == null || !data.IsPilot())
        {
            return false;
        }

        if (data.id == GymGhingnhamCardId)
        {
            return true;
        }

        if (!string.IsNullOrEmpty(data.gcgOfficialId)
            && string.Equals(data.gcgOfficialId, "GD06-085", System.StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return data.HasPilotIdValue(GymGhingnhamPilotId);
    }

    private void ClearGymGhingnhamOpponentTurnApBuffOnZone(List<CardController> zone)
    {
        if (zone == null)
        {
            return;
        }

        for (int i = 0; i < zone.Count; i++)
        {
            string key = MakeGymGhingnhamOpponentTurnApSourceKey(zone[i]);
            if (!string.IsNullOrEmpty(key))
            {
                RemoveModifiersBySourceKeyFromAllFieldUnits(key);
            }
        }
    }

    private void ApplyGymGhingnhamOpponentTurnApBuffOnZone(List<CardController> zone, PlayerType ownerType)
    {
        if (zone == null || ownerType == currentPlayerType)
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

            CardController gym = host.MountedPilot;
            if (gym?.Data == null || !IsGymGhingnhamPilotCard(gym.Data))
            {
                continue;
            }

            AssignBattleInstanceIdIfNeeded(host);
            string sourceKey = MakeGymGhingnhamOpponentTurnApSourceKey(host);
            if (string.IsNullOrEmpty(sourceKey))
            {
                continue;
            }

            int applied = 0;
            for (int ai = 0; ai < zone.Count; ai++)
            {
                CardController ally = zone[ai];
                if (ally == null || ally.Data == null || !ally.Data.IsUnitLike() || ally.CurrentHp <= 0)
                {
                    continue;
                }

                if (ResolveBattleZoneUnitOwner(ally) != ownerType)
                {
                    continue;
                }

                bool isHost = ally == host;
                ally.Data.EnsureFeaturesResolved();
                bool isGhingnhamFleet = ally.HasFeatureId(GhingnhamFleetFeatureId);
                if (!isHost && !isGhingnhamFleet)
                {
                    continue;
                }

                ApplyStatEffect(ally, 1, EffectStatTarget.AP, EffectDuration.Permanent, sourceKey);
                applied++;
            }

            Debug.Log(
                $"[GymGhingnham] 相手ターン AP+1 host:{host.Data.cardName} "
                + $"targets:{applied} (自身含む) current:{currentPlayerType}");
        }
    }

    /// <summary>
    /// 【リンク中】味方全体 AP 等（ユニット定義の Julieta、搭乗パイロットの GD06-085 ギンガム）。
    /// 自ターン／相手ターン条件は activationConditions の turnCheck で評価する。
    /// </summary>
    private void RefreshDuringLinkAllyFieldStatPassives()
    {
        ClearDuringLinkAllyFieldStatPassivesOnZone(playerBattleZoneCards);
        ClearDuringLinkAllyFieldStatPassivesOnZone(enemyBattleZoneCards);
        ApplyDuringLinkAllyFieldStatPassivesOnZone(playerBattleZoneCards, PlayerType.Player);
        ApplyDuringLinkAllyFieldStatPassivesOnZone(enemyBattleZoneCards, PlayerType.Enemy);
    }

    private static string MakeDuringLinkAllyFieldSourceKey(CardController host, int blockIndex, bool isPilotSource)
    {
        if (host == null)
        {
            return string.Empty;
        }

        string prefix = isPilotSource ? "DuringLinkPilotAllyField" : "DuringLinkAllyField";
        if (host.BattleInstanceId > 0)
        {
            return $"{prefix}:{host.BattleInstanceId}:{blockIndex}";
        }

        return $"{prefix}:{host.GetEntityId()}:{blockIndex}";
    }

    private void ClearDuringLinkAllyFieldStatPassivesOnZone(List<CardController> zone)
    {
        if (zone == null)
        {
            return;
        }

        HashSet<string> keys = new HashSet<string>();
        for (int i = 0; i < zone.Count; i++)
        {
            CardController host = zone[i];
            if (host?.Data == null || !host.Data.IsUnitLike())
            {
                continue;
            }

            CollectDuringLinkAllyFieldSourceKeys(host, host.Data.timedEffects, isPilotSource: false, keys);
            CollectDuringLinkAllyFieldSourceKeys(
                host,
                host.MountedPilot?.Data?.timedEffects,
                isPilotSource: true,
                keys);
        }

        foreach (string key in keys)
        {
            RemoveModifiersBySourceKeyFromAllFieldUnits(key);
        }
    }

    private static void CollectDuringLinkAllyFieldSourceKeys(
        CardController host,
        IList<TimedEffectData> blocks,
        bool isPilotSource,
        HashSet<string> keys)
    {
        if (host == null || blocks == null || keys == null)
        {
            return;
        }

        for (int bi = 0; bi < blocks.Count; bi++)
        {
            TimedEffectData timed = blocks[bi];
            if (timed == null || !timed.IsDuringLinkAllyFieldStatPassiveBlock())
            {
                continue;
            }

            string key = MakeDuringLinkAllyFieldSourceKey(host, bi, isPilotSource);
            if (!string.IsNullOrEmpty(key))
            {
                keys.Add(key);
            }
        }
    }

    private void ApplyDuringLinkAllyFieldStatPassivesOnZone(List<CardController> zone, PlayerType ownerType)
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

            AssignBattleInstanceIdIfNeeded(host);
            ApplyDuringLinkAllyFieldStatFromBlocks(host, ownerType, host.Data.timedEffects, isPilotSource: false);

            CardController pilot = host.MountedPilot;
            if (pilot?.Data?.timedEffects != null)
            {
                ApplyDuringLinkAllyFieldStatFromBlocks(host, ownerType, pilot.Data.timedEffects, isPilotSource: true);
            }
        }
    }

    private void ApplyDuringLinkAllyFieldStatFromBlocks(
        CardController host,
        PlayerType ownerType,
        IList<TimedEffectData> blocks,
        bool isPilotSource)
    {
        if (host == null || blocks == null)
        {
            return;
        }

        CardController pilot = host.MountedPilot;
        for (int bi = 0; bi < blocks.Count; bi++)
        {
            TimedEffectData timed = blocks[bi];
            if (timed == null || !timed.IsDuringLinkAllyFieldStatPassiveBlock())
            {
                continue;
            }

            if (pilot == null)
            {
                continue;
            }

            // GD06-085 は RefreshGymGhingnhamOpponentTurnApBuff が自身込みで付与する（二重加算しない）
            if (isPilotSource && IsGymGhingnhamPilotCard(pilot.Data))
            {
                continue;
            }

            EffectActivationContext ctx = isPilotSource
                ? BuildPilotMountActivationContext(ownerType, pilot, host, pilot)
                : BuildOwnerTurnFieldPassiveActivationContext(ownerType, host, timed);
            if (!EffectActivationEvaluator.AreTimedConditionsMet(timed, ctx))
            {
                continue;
            }

            string sourceKey = MakeDuringLinkAllyFieldSourceKey(host, bi, isPilotSource);
            if (string.IsNullOrEmpty(sourceKey))
            {
                continue;
            }

            IReadOnlyList<EffectData> effects = timed.GetResolvedEffects();
            for (int ei = 0; ei < effects.Count; ei++)
            {
                EffectData effect = effects[ei];
                if (effect == null
                    || (effect.type != EffectType.Buff && effect.type != EffectType.Debuff)
                    || (effect.target != TargetType.AllyAllUnits && effect.target != TargetType.AllyOtherUnit))
                {
                    continue;
                }

                int magnitude = ResolveEffectMagnitude(effect, ownerType, host);
                if (magnitude == 0)
                {
                    continue;
                }

                int signedValue = (effect.type == EffectType.Buff ? 1 : -1) * magnitude;
                List<CardController> targets = ResolveEffectTargets(host, ownerType, effect);
                Debug.Log(
                    $"[LinkAllyField] {host.Data?.cardName} pilot:{pilot.Data?.cardName} "
                    + $"{effect.type} {signedValue} targets:{targets.Count} ownerTurn:{ownerType == currentPlayerType}");
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
                }
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
        if (TimedListHasFieldTurnStatPassive(unit?.Data?.timedEffects))
        {
            return true;
        }

        return TimedListHasFieldTurnStatPassive(unit?.MountedPilot?.Data?.timedEffects);
    }

    private static bool TimedListHasFieldTurnStatPassive(IList<TimedEffectData> timedEffects)
    {
        if (timedEffects == null)
        {
            return false;
        }

        for (int i = 0; i < timedEffects.Count; i++)
        {
            if (timedEffects[i] != null
                && (IsOwnerTurnFieldPassiveTimedBlock(timedEffects[i])
                    || IsOnPilotMountedAllyFieldStatBlock(timedEffects[i])))
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
            if (unit == null)
            {
                continue;
            }

            CollectOwnerTurnFieldPassiveSourceKeysFromTimedList(unit, unit.Data?.timedEffects, 0, keys);
            CollectOwnerTurnFieldPassiveSourceKeysFromTimedList(
                unit,
                unit.MountedPilot?.Data?.timedEffects,
                1000,
                keys);
        }
    }

    private static void CollectOwnerTurnFieldPassiveSourceKeysFromTimedList(
        CardController unit,
        IList<TimedEffectData> timedEffects,
        int blockIndexOffset,
        HashSet<string> keys)
    {
        if (unit == null || timedEffects == null || keys == null)
        {
            return;
        }

        for (int bi = 0; bi < timedEffects.Count; bi++)
        {
            TimedEffectData timed = timedEffects[bi];
            if (timed == null
                || (!IsOwnerTurnFieldPassiveTimedBlock(timed)
                    && !IsOnPilotMountedAllyFieldStatBlock(timed)
                    && !(IsProvidenceZaftHost(unit) && IsZaftDuringPairAllyApBuffBlock(timed))))
            {
                continue;
            }

            string key = MakeOwnerTurnFieldPassiveSourceKey(unit, blockIndexOffset + bi);
            if (!string.IsNullOrEmpty(key))
            {
                keys.Add(key);
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
        // 自ターン限定はターンプレイヤー側、相手ターン限定（GD06-085 等）は非ターン側を再付与する。

        List<CardController> zone = side == PlayerType.Player ? playerBattleZoneCards : enemyBattleZoneCards;
        if (zone == null)
        {
            return;
        }

        for (int ui = 0; ui < zone.Count; ui++)
        {
            CardController unit = zone[ui];
            if (unit == null || unit.Data == null)
            {
                continue;
            }

            if (ResolveBattleZoneUnitOwner(unit) != side)
            {
                continue;
            }

            AssignBattleInstanceIdIfNeeded(unit);

            // プロヴィデンス専用 ZAFT 経路（他カードの味方全体 AP バフには触れない）
            if (IsProvidenceZaftHost(unit)
                && unit.MountedPilot?.Data != null
                && unit.MountedPilot.Data.HasFeatureId(ZaftFeatureId))
            {
                unit.MountedPilot.Data.EnsureFeaturesResolved();
                ApplyZaftDuringPairAllyApBuff(unit, unit.MountedPilot, side);
            }

            IList<TimedEffectData> unitTimed = unit.Data.timedEffects;
            for (int bi = 0; unitTimed != null && bi < unitTimed.Count; bi++)
            {
                TimedEffectData timed = unitTimed[bi];
                if (timed == null)
                {
                    continue;
                }

                // プロヴィデンスの ZAFT バフは上の専用経路のみ。通常パッシブ再付与では二重適用しない。
                if (IsProvidenceZaftHost(unit) && IsZaftDuringPairAllyApBuffBlock(timed))
                {
                    continue;
                }

                if (timed.IsDuringLinkAllyFieldStatPassiveBlock())
                {
                    continue;
                }

                if (!IsOwnerTurnFieldPassiveTimedBlock(timed)
                    && !IsOnPilotMountedAllyFieldStatBlock(timed))
                {
                    continue;
                }

                if (!ShouldApplyFieldTurnStatPassiveForSide(timed, side))
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

            IList<TimedEffectData> pilotTimed = unit.MountedPilot?.Data?.timedEffects;
            bool skipGymPilotFieldPassive = IsGymGhingnhamPilotCard(unit.MountedPilot?.Data);
            for (int bi = 0; !skipGymPilotFieldPassive && pilotTimed != null && bi < pilotTimed.Count; bi++)
            {
                TimedEffectData timed = pilotTimed[bi];
                if (timed == null)
                {
                    continue;
                }

                if (timed.IsDuringLinkAllyFieldStatPassiveBlock())
                {
                    continue;
                }

                if (!IsOwnerTurnFieldPassiveTimedBlock(timed)
                    && !IsOnPilotMountedAllyFieldStatBlock(timed))
                {
                    continue;
                }

                if (!ShouldApplyFieldTurnStatPassiveForSide(timed, side))
                {
                    continue;
                }

                EffectActivationContext ctx = BuildOwnerTurnFieldPassiveActivationContext(side, unit, timed);
                if (!EffectActivationEvaluator.AreTimedConditionsMet(timed, ctx))
                {
                    continue;
                }

                ApplyOwnerTurnFieldStatPassiveBlock(unit, side, timed, 1000 + bi, syncOnlineBatch);
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

    /// <summary>
    /// OnPilotMounted の味方／敵全体など、場に張り続ける Buff/Debuff ブロックか。
    /// ST01-006 のような選択必須の一発 Debuff は含めない（ClearLegacy で UnitGranted が消えるため）。
    /// </summary>
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

            if (effect.type != EffectType.Buff && effect.type != EffectType.Debuff)
            {
                continue;
            }

            if (effect.target == TargetType.Self)
            {
                continue;
            }

            // SelectSingle 等の手動選択は搭乗時一発効果。場パッシブ／レガシーオーラ掃除の対象外。
            if (effect.selectionMode.RequiresManualUnitPick())
            {
                continue;
            }

            return true;
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
    /// 自ターン中、プロヴィデンスに (ZAFT) パイロットが載っているとき味方 (ZAFT) 全体に AP バフ。
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

        // ST01-001 等へは絶対に入らない
        if (!IsProvidenceZaftHost(hostUnit))
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

    /// <summary>プロヴィデンス（GD03-033）専用ホストか。</summary>
    private static bool IsProvidenceZaftHost(CardController hostUnit)
    {
        if (hostUnit?.Data == null)
        {
            return false;
        }

        return hostUnit.Data.id == ProvidenceCardId
            || string.Equals(
                hostUnit.Data.gcgOfficialId,
                "GD03-033",
                System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>プロヴィデンス専用: OnPilotMounted 味方 ZAFT AP バフの量を得る。</summary>
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

        // プロヴィデンス以外は ZAFT 専用経路に入れない（ST01-001 等へ影響させない）
        if (!IsProvidenceZaftHost(hostUnit))
        {
            return false;
        }

        if (hostUnit.Data.timedEffects != null)
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
                        || effect.statTarget != EffectStatTarget.AP
                        || effect.targetFeatureId != ZaftFeatureId)
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
                    targetFeatureId = effect.targetFeatureId;
                    return true;
                }
            }
        }

        blockIndex = 0;
        apBonus = 2;
        targetFeatureId = ZaftFeatureId;
        return true;
    }

    private static bool IsOnPilotMountedTiming(TimedEffectData timed)
    {
        return timed != null
            && (timed.timing == EffectTiming.OnPilotMounted || (int)timed.timing == 15);
    }

    /// <summary>
    /// ZAFT 限定の During Pair 味方 AP バフ（Providence 等）。
    /// Feature 制限のない ST01-001 ガンダム等は通常の自ターン盤面パッシブへ回す。
    /// </summary>
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
                && effect.statTarget == EffectStatTarget.AP
                && effect.targetFeatureId == ZaftFeatureId)
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

            // AllyAllUnits 等の自動全体対象は通す。
            // effect.type.RequiresManualUnitSelection() は Buff/Debuff が常に true のため使わない
            // （使うと ST01-001 ガンダムの味方全体 AP+1 が永久にスキップされる）。
            if (EffectRequiresManualUnitSelection(effect))
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
