using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 配備ベース常時：レストの指定特徴味方ユニットがいる間、
/// このベースはユニットトークン以外の Lv 以下の相手ユニットからダメージを受けない
/// （ST07-015 プトレマイオス等）。
/// </summary>
public partial class BattleGameMain
{
    /// <summary>
    /// この配備ベースだけが、相手の非トークン・低 Lv ユニットからのダメージを無視するか。
    /// シールドエリア全体無効（穏やかな音色）とは別。ヒットはここで消費し後ろへ抜けない。
    /// </summary>
    private bool ShouldIgnoreThisDeployedBaseDamageFromEnemyNonTokenUnitLevelOrLess(
        CardController defenderBase,
        CardController sourceUnit,
        Gundam2024RuleScript.PlayerSide targetSide,
        PlayerType? sourceOwnerOverride = null)
    {
        if (defenderBase == null
            || defenderBase.Data == null
            || defenderBase.Data.timedEffects == null
            || defenderBase.CurrentHp <= 0)
        {
            return false;
        }

        CardController unitSource = ResolveUnitSourceForShieldAreaDamage(sourceUnit);
        if (unitSource == null
            || unitSource.Data == null
            || !unitSource.Data.IsUnitLike())
        {
            return false;
        }

        // 「ユニットトークン以外の」相手ユニット → トークンからは受ける
        if (unitSource.Data.IsUnitToken())
        {
            return false;
        }

        PlayerType targetOwner = targetSide == Gundam2024RuleScript.PlayerSide.Player
            ? PlayerType.Player
            : PlayerType.Enemy;
        PlayerType sourceOwner = sourceOwnerOverride
            ?? ResolveUnitOwnerForShieldAreaDamageSource(unitSource);
        if (sourceOwner == targetOwner)
        {
            return false;
        }

        IReadOnlyList<TimedEffectData> timedEffects = defenderBase.Data.timedEffects;
        for (int ti = 0; ti < timedEffects.Count; ti++)
        {
            TimedEffectData timed = timedEffects[ti];
            if (timed == null || !timed.HasResolvedEffects())
            {
                continue;
            }

            IReadOnlyList<EffectData> effects = timed.GetResolvedEffects();
            for (int ei = 0; ei < effects.Count; ei++)
            {
                EffectData effect = effects[ei];
                if (effect == null
                    || effect.type != EffectType.ThisDeployedBaseImmunityFromEnemyNonTokenUnitLevelOrLess)
                {
                    continue;
                }

                int maxLevel = effect.value > 0 ? effect.value : 3;
                if (unitSource.CurrentLevel > maxLevel)
                {
                    continue;
                }

                if (!HasRestedAllyUnitMatchingBaseImmunityFeature(targetOwner, effect))
                {
                    continue;
                }

                Debug.Log(
                    $"[Ptolemaios] {defenderBase.Data.cardName} ignores dmg from "
                    + $"{unitSource.Data.cardName}(id:{unitSource.Data.id}) "
                    + $"Lv:{unitSource.CurrentLevel}≤{maxLevel} owner:{targetOwner}");
                return true;
            }
        }

        return false;
    }

    /// <summary>レストかつ効果の対象特徴（未指定時は〔CB〕）を持つ味方ユニットが1体以上いるか。</summary>
    private bool HasRestedAllyUnitMatchingBaseImmunityFeature(PlayerType ownerType, EffectData effect)
    {
        int featureId = effect != null ? effect.targetFeatureId : 0;
        if (featureId <= 0 && effect != null && effect.HasTargetFeatureFilter())
        {
            IReadOnlyList<CardFeatureData> features = effect.GetTargetFeatures();
            if (features != null && features.Count > 0 && features[0] != null)
            {
                featureId = features[0].id;
            }
        }

        if (featureId <= 0)
        {
            featureId = 25;
        }

        if (featureId == 25)
        {
            return CollectRestedCbAllyUnitsForAttackRedirect(ownerType).Count > 0;
        }

        return CountRestedAllyUnitsWithFeatureId(ownerType, featureId) > 0;
    }

    private int CountRestedAllyUnitsWithFeatureId(PlayerType ownerType, int featureId)
    {
        int n = 0;
        n += CountRestedAllyUnitsWithFeatureIdInZone(playerBattleZoneCards, ownerType, featureId);
        n += CountRestedAllyUnitsWithFeatureIdInZone(enemyBattleZoneCards, ownerType, featureId);
        return n;
    }

    private int CountRestedAllyUnitsWithFeatureIdInZone(
        List<CardController> zone,
        PlayerType ownerType,
        int featureId)
    {
        if (zone == null)
        {
            return 0;
        }

        int n = 0;
        for (int i = 0; i < zone.Count; i++)
        {
            CardController unit = zone[i];
            if (unit == null
                || unit.Data == null
                || !unit.Data.IsUnitLike()
                || unit.CurrentHp <= 0)
            {
                continue;
            }

            if (!IsOnDeployPanel(unit, ownerType) && !IsUnitInSideBattleZoneList(unit, ownerType))
            {
                continue;
            }

            if (!UnitLooksRested(unit) || !unit.HasFeatureId(featureId))
            {
                continue;
            }

            n++;
        }

        return n;
    }
}
