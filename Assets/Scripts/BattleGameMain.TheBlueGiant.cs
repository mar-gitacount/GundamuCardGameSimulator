using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ST03-014 青い巨星専用。【アクション】で選んだ味方ユニットは、
/// このバトル中 AP2 以下の相手ユニットからバトルダメージを受けない。
/// 他カードの BattleDamageImmunity パッシブとは独立する。
/// </summary>
public partial class BattleGameMain
{
    private const int TheBlueGiantCardId = 1000576;
    private const string TheBlueGiantOfficialId = "ST03-014";
    private const int TheBlueGiantMaxEnemyAp = 2;

    /// <summary>
    /// CardController のランタイム初期化でフラグが消えても残す。
    /// </summary>
    private readonly HashSet<EntityId> _theBlueGiantProtectedUnityIds = new HashSet<EntityId>();

    private static bool IsTheBlueGiantCard(CardData data)
    {
        if (data == null)
        {
            return false;
        }

        if (data.id == TheBlueGiantCardId)
        {
            return true;
        }

        string officialId = data.gcgOfficialId;
        if (!string.IsNullOrEmpty(officialId)
            && string.Equals(officialId.Trim(), TheBlueGiantOfficialId, System.StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string name = data.cardName;
        return !string.IsNullOrEmpty(name)
            && (name.IndexOf("Blue Giant", System.StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("青い巨星", System.StringComparison.Ordinal) >= 0);
    }

    private static EffectData CreateTheBlueGiantPickEffect()
    {
        return new EffectData
        {
            type = EffectType.BattleDamageImmunityFromLowApEnemy,
            value = TheBlueGiantMaxEnemyAp,
            target = TargetType.AllyUnit,
            selectionMode = EffectSelectionMode.SelectSingle,
            statTarget = EffectStatTarget.AP,
            duration = EffectDuration.UntilEndOfBattle
        };
    }

    private void GrantTheBlueGiantImmunityToTargets(List<CardController> targets)
    {
        if (targets == null)
        {
            return;
        }

        for (int i = 0; i < targets.Count; i++)
        {
            CardController unit = targets[i];
            if (unit != null && unit.Data != null && unit.Data.IsPilot())
            {
                unit = ResolveEffectSourceBattleHost(unit) ?? unit;
            }

            if (unit == null || unit.Data == null || !unit.Data.IsUnitLike() || unit.CurrentHp <= 0)
            {
                continue;
            }

            AssignBattleInstanceIdIfNeeded(unit);
            unit.GrantTheBlueGiantBattleDamageImmunityThisBattle();
            _theBlueGiantProtectedUnityIds.Add(unit.GetEntityId());
            Debug.Log(
                $"[ST03-014] grant immunity on {unit.Data.cardName}(id:{unit.Data.id}) "
                + $"entity:{unit.GetEntityId()} protectedCount:{_theBlueGiantProtectedUnityIds.Count}");
        }
    }

    private bool IsTheBlueGiantProtectedUnit(CardController unit)
    {
        if (unit == null)
        {
            return false;
        }

        if (unit.HasTheBlueGiantBattleDamageImmunityThisBattle)
        {
            return true;
        }

        return _theBlueGiantProtectedUnityIds.Contains(unit.GetEntityId());
    }

    private bool AreOpposingBattleUnits(CardController a, CardController b)
    {
        if (a == null || b == null || ReferenceEquals(a, b))
        {
            return false;
        }

        Transform playerPanel = cardGameRule != null ? cardGameRule.PlayerDeployPanel : null;
        Transform enemyPanel = enemyCardGameRule != null ? enemyCardGameRule.PlayerDeployPanel : null;
        if (playerPanel != null && enemyPanel != null)
        {
            bool aPlayer = a.transform.IsChildOf(playerPanel);
            bool bPlayer = b.transform.IsChildOf(playerPanel);
            bool aEnemy = a.transform.IsChildOf(enemyPanel);
            bool bEnemy = b.transform.IsChildOf(enemyPanel);
            if ((aPlayer && bEnemy) || (aEnemy && bPlayer))
            {
                return true;
            }
        }

        bool aInPlayer = playerBattleZoneCards != null && playerBattleZoneCards.Contains(a);
        bool bInPlayer = playerBattleZoneCards != null && playerBattleZoneCards.Contains(b);
        bool aInEnemy = enemyBattleZoneCards != null && enemyBattleZoneCards.Contains(a);
        bool bInEnemy = enemyBattleZoneCards != null && enemyBattleZoneCards.Contains(b);
        return (aInPlayer && bInEnemy) || (aInEnemy && bInPlayer);
    }

    /// <summary>
    /// 青い巨星の付与中。相手ユニットの印刷AP／実効AP／この打撃のいずれかが 2 以下なら無効。
    /// 所有者 enum は使わない（ResolveCardOwner の誤判定で無効化が落ちるため）。
    /// </summary>
    private bool ShouldIgnoreTheBlueGiantBattleDamage(
        CardController damageTarget,
        CardController damageSource,
        int strikeDamage)
    {
        if (!IsTheBlueGiantProtectedUnit(damageTarget))
        {
            return false;
        }

        if (damageSource == null || damageSource.Data == null || !damageSource.Data.IsUnitLike())
        {
            return false;
        }

        if (!AreOpposingBattleUnits(damageTarget, damageSource))
        {
            Debug.Log(
                $"[ST03-014] skip ignore — not opposing units "
                + $"target:{damageTarget.Data?.cardName} source:{damageSource.Data.cardName}");
            return false;
        }

        int currentAp = damageSource.CurrentPower;
        int printedAp = damageSource.Data.power;
        bool ignore = currentAp <= TheBlueGiantMaxEnemyAp
            || printedAp <= TheBlueGiantMaxEnemyAp
            || strikeDamage <= TheBlueGiantMaxEnemyAp;
        Debug.Log(
            $"[ST03-014] combat check ignore:{ignore} target:{damageTarget.Data?.cardName} "
            + $"from:{damageSource.Data.cardName} currentAP:{currentAp} printedAP:{printedAp} strike:{strikeDamage}");
        return ignore;
    }

    private void ClearTheBlueGiantBattleDamageImmunityForAllInPlayUnits()
    {
        _theBlueGiantProtectedUnityIds.Clear();
        ClearTheBlueGiantBattleDamageImmunityOnZone(playerBattleZoneCards);
        ClearTheBlueGiantBattleDamageImmunityOnZone(enemyBattleZoneCards);
        ClearTheBlueGiantBattleDamageImmunityOnTransform(
            cardGameRule != null ? cardGameRule.PlayerDeployPanel : null);
        ClearTheBlueGiantBattleDamageImmunityOnTransform(
            enemyCardGameRule != null ? enemyCardGameRule.PlayerDeployPanel : null);
    }

    private static void ClearTheBlueGiantBattleDamageImmunityOnZone(List<CardController> zone)
    {
        if (zone == null)
        {
            return;
        }

        for (int i = 0; i < zone.Count; i++)
        {
            zone[i]?.ClearTheBlueGiantBattleDamageImmunityThisBattle();
        }
    }

    private static void ClearTheBlueGiantBattleDamageImmunityOnTransform(Transform root)
    {
        if (root == null)
        {
            return;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            CardController c = root.GetChild(i).GetComponent<CardController>();
            c?.ClearTheBlueGiantBattleDamageImmunityThisBattle();
        }
    }
}
