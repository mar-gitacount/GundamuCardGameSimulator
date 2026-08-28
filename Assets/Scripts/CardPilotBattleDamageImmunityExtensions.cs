using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 搭乗パイロットの「低AP敵からの戦闘ダメージ無効」パッシブ判定。
/// </summary>
public static class CardPilotBattleDamageImmunityExtensions
{
    /// <summary>
    /// 搭乗パイロットの BattleDamageImmunityFromLowApEnemy により、
    /// 敵ユニット（AP≤value）からの戦闘ダメージを無効化するか。
    /// </summary>
    public static bool ShouldIgnoreBattleDamageFromAttacker(
        CardController damageTarget,
        CardController damageSource,
        BattleGameMain.PlayerType damageTargetOwner,
        BattleGameMain.PlayerType damageSourceOwner,
        bool isDamageTargetOwnerTurn)
    {
        if (damageTarget == null
            || damageSource == null
            || damageSource.Data == null
            || !damageSource.Data.IsUnitLike()
            || damageTargetOwner == damageSourceOwner)
        {
            return false;
        }

        CardController pilot = damageTarget.MountedPilot;
        if (pilot?.Data == null || pilot.Data.timedEffects == null)
        {
            return false;
        }

        int attackerAp = damageSource.CurrentPower;
        EffectActivationContext ctx = BuildCombatImmunityContext(
            damageTarget,
            pilot,
            damageTargetOwner,
            isDamageTargetOwnerTurn);

        for (int ti = 0; ti < pilot.Data.timedEffects.Count; ti++)
        {
            TimedEffectData timed = pilot.Data.timedEffects[ti];
            if (timed == null || !EffectActivationEvaluator.AreTimedConditionsMet(timed, ctx))
            {
                continue;
            }

            IReadOnlyList<EffectData> effects = timed.GetResolvedEffects();
            for (int ei = 0; ei < effects.Count; ei++)
            {
                EffectData effect = effects[ei];
                if (effect == null || effect.type != EffectType.BattleDamageImmunityFromLowApEnemy)
                {
                    continue;
                }

                int maxAp = effect.value > 0 ? effect.value : 3;
                if (attackerAp <= maxAp)
                {
                    Debug.Log(
                        $"[BattleDamageImmunity] {damageTarget.Data?.cardName} ignores {attackerAp} dmg "
                        + $"from {damageSource.Data.cardName} (pilot:{pilot.Data.cardName}, maxAp:{maxAp})");
                    return true;
                }
            }
        }

        return false;
    }

    private static EffectActivationContext BuildCombatImmunityContext(
        CardController hostUnit,
        CardController pilot,
        BattleGameMain.PlayerType ownerType,
        bool isOwnerTurn)
    {
        return new EffectActivationContext(
            ownerType,
            hostUnit,
            playerBattleZone: null,
            enemyBattleZone: null,
            playerHand: null,
            enemyHand: null,
            isOwnerTurn: isOwnerTurn,
            mountHostUnit: hostUnit,
            mountedPilot: pilot);
    }
}
