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

    /// <summary>
    /// 【リンク中】かつレストの味方ユニット（搭乗パイロット含む）が、
    /// このユニット以下の Lv の相手ユニットからの味方ベース（配備ベース／EXベース）ダメージを無効にする（GD06-098 デュオ等）。
    /// シールドカードは対象外。defenderBase が無くても EXベースに適用する。
    /// </summary>
    private bool ShouldIgnoreFriendlyBaseDamageFromLinkedRestedUnitLevelOrLess(
        CardController defenderBase,
        CardController sourceUnit,
        Gundam2024RuleScript.PlayerSide targetSide,
        PlayerType? sourceOwnerOverride = null)
    {
        CardController unitSource = ResolveUnitSourceForShieldAreaDamage(sourceUnit);
        if (unitSource == null || unitSource.Data == null || !unitSource.Data.IsUnitLike())
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

        int attackerLevel = unitSource.CurrentLevel;
        if (HasLinkedRestedFriendlyBaseImmunityOnZone(playerBattleZoneCards, targetOwner, attackerLevel)
            || HasLinkedRestedFriendlyBaseImmunityOnZone(enemyBattleZoneCards, targetOwner, attackerLevel)
            || HasLinkedRestedFriendlyBaseImmunityOnDeployPanel(targetOwner, attackerLevel))
        {
            string targetName = defenderBase?.Data != null ? defenderBase.Data.cardName : "EX/Shield";
            Debug.Log(
                $"[DuoMaxwell] {targetName} ignores dmg from "
                + $"{unitSource.Data.cardName} Lv:{attackerLevel} owner:{targetOwner}");
            return true;
        }

        return false;
    }

    private bool HasLinkedRestedFriendlyBaseImmunityOnZone(
        List<CardController> zone,
        PlayerType ownerType,
        int attackerLevel)
    {
        if (zone == null)
        {
            return false;
        }

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

            if (!UnitGrantsLinkedRestedFriendlyBaseImmunity(unit, ownerType, attackerLevel))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private bool HasLinkedRestedFriendlyBaseImmunityOnDeployPanel(PlayerType ownerType, int attackerLevel)
    {
        CardGameRule rule = ownerType == PlayerType.Player ? cardGameRule : enemyCardGameRule;
        if (rule == null || rule.PlayerDeployPanel == null)
        {
            return false;
        }

        CardController[] fieldCards = rule.PlayerDeployPanel.GetComponentsInChildren<CardController>(true);
        for (int i = 0; i < fieldCards.Length; i++)
        {
            if (UnitGrantsLinkedRestedFriendlyBaseImmunity(fieldCards[i], ownerType, attackerLevel))
            {
                return true;
            }
        }

        return false;
    }

    private bool UnitGrantsLinkedRestedFriendlyBaseImmunity(
        CardController unit,
        PlayerType ownerType,
        int attackerLevel)
    {
        if (unit == null
            || unit.Data == null
            || !unit.Data.IsUnitLike()
            || unit.CurrentHp <= 0)
        {
            return false;
        }

        if (ResolveBattleZoneUnitOwner(unit) != ownerType
            && !IsOnDeployPanel(unit, ownerType))
        {
            return false;
        }

        if (!IsOnDeployPanel(unit, ownerType) && !IsUnitInSideBattleZoneList(unit, ownerType))
        {
            return false;
        }

        if (!UnitLooksRested(unit) && !unit.IsRestState)
        {
            return false;
        }

        if (!UnitLinkExtensions.HasValidLinkPilot(unit.Data, unit.MountedPilot))
        {
            return false;
        }

        if (attackerLevel > unit.CurrentLevel)
        {
            return false;
        }

        return CardHasFriendlyBasesImmunityMarker(unit.Data)
            || CardHasFriendlyBasesImmunityMarker(unit.MountedPilot?.Data);
    }

    private static bool CardHasFriendlyBasesImmunityMarker(CardData data)
    {
        if (data?.timedEffects == null)
        {
            return false;
        }

        for (int ti = 0; ti < data.timedEffects.Count; ti++)
        {
            TimedEffectData timed = data.timedEffects[ti];
            if (timed == null || !timed.HasResolvedEffects())
            {
                continue;
            }

            IReadOnlyList<EffectData> effects = timed.GetResolvedEffects();
            for (int ei = 0; ei < effects.Count; ei++)
            {
                if (effects[ei] != null
                    && effects[ei].type == EffectType.FriendlyBasesImmunityFromEnemyUnitsLevelOrLessThanThisUnit)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
