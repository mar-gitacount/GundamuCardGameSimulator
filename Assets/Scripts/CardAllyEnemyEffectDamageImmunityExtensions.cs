using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 場ユニットによる「味方への敵効果ダメージ無効」パッシブ判定（ST11-002 アッガイ等）。
/// </summary>
public static class CardAllyEnemyEffectDamageImmunityExtensions
{
    /// <summary>
    /// 味方場の AllyEnemyEffectDamageImmunity により、敵効果ダメージを無効化するか。
    /// </summary>
    public static bool ShouldIgnoreEnemyEffectDamageByAllyProtector(
        CardController damageTarget,
        IReadOnlyList<CardController> allyBattleZone,
        BattleGameMain.PlayerType damageTargetOwner,
        BattleGameMain.PlayerType effectSourceOwner,
        bool isDamageTargetOwnerTurn)
    {
        if (damageTarget == null
            || damageTarget.Data == null
            || !damageTarget.Data.IsUnitLike()
            || allyBattleZone == null
            || damageTargetOwner == effectSourceOwner)
        {
            return false;
        }

        for (int ui = 0; ui < allyBattleZone.Count; ui++)
        {
            CardController protector = allyBattleZone[ui];
            if (protector == null
                || protector.Data == null
                || protector.Data.timedEffects == null
                || protector.CurrentHp <= 0)
            {
                continue;
            }

            EffectActivationContext ctx = new EffectActivationContext(
                damageTargetOwner,
                protector,
                playerBattleZone: null,
                enemyBattleZone: null,
                playerHand: null,
                enemyHand: null,
                isOwnerTurn: isDamageTargetOwnerTurn,
                mountHostUnit: protector,
                mountedPilot: protector.MountedPilot);

            IReadOnlyList<TimedEffectData> timedEffects = protector.Data.timedEffects;
            for (int ti = 0; ti < timedEffects.Count; ti++)
            {
                TimedEffectData timed = timedEffects[ti];
                if (timed == null || !EffectActivationEvaluator.AreTimedConditionsMet(timed, ctx))
                {
                    continue;
                }

                IReadOnlyList<EffectData> effects = timed.GetResolvedEffects();
                for (int ei = 0; ei < effects.Count; ei++)
                {
                    EffectData effect = effects[ei];
                    if (effect == null || effect.type != EffectType.AllyEnemyEffectDamageImmunity)
                    {
                        continue;
                    }

                    if (!effect.MatchesTargetUnitFilter(damageTarget, protector))
                    {
                        continue;
                    }

                    Debug.Log(
                        $"[AllyEffectDamageImmunity] {protector.Data.cardName} blocks enemy effect dmg "
                        + $"on {damageTarget.Data.cardName} "
                        + $"(rest:{protector.IsRestState} ownerTurn:{isDamageTargetOwnerTurn} "
                        + $"hp:{damageTarget.CurrentHp})");
                    return true;
                }
            }
        }

        return false;
    }
}
