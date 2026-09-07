using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 配備ベースによる「味方ユニットへの低量敵効果ダメージ無効」パッシブ判定（Archangel 等）。
/// </summary>
public static class CardBaseAllyEffectDamageImmunityExtensions
{
    /// <summary>
    /// 配備ベースの EffectDamageImmunityFromAmountOrLess により、
    /// 敵効果ダメージ（resolvedAmount 以下）を無効化するか。
    /// </summary>
    public static bool ShouldIgnoreEnemyEffectDamageFromAmount(
        CardController damageTarget,
        CardController protectingBase,
        BattleGameMain.PlayerType damageTargetOwner,
        BattleGameMain.PlayerType effectSourceOwner,
        bool isDamageTargetOwnerTurn,
        int resolvedAmount)
    {
        if (damageTarget == null
            || damageTarget.Data == null
            || !damageTarget.Data.IsUnitLike()
            || protectingBase?.Data?.timedEffects == null
            || damageTargetOwner == effectSourceOwner
            || resolvedAmount <= 0)
        {
            return false;
        }

        EffectActivationContext ctx = new EffectActivationContext(
            damageTargetOwner,
            protectingBase,
            playerBattleZone: null,
            enemyBattleZone: null,
            playerHand: null,
            enemyHand: null,
            isOwnerTurn: isDamageTargetOwnerTurn,
            mountHostUnit: null,
            mountedPilot: null);

        IReadOnlyList<TimedEffectData> timedEffects = protectingBase.Data.timedEffects;
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
                if (effect == null || effect.type != EffectType.EffectDamageImmunityFromAmountOrLess)
                {
                    continue;
                }

                int threshold = effect.value > 0 ? effect.value : 2;
                if (resolvedAmount > threshold)
                {
                    continue;
                }

                if (effect.HasTargetFeatureFilter()
                    && !damageTarget.HasAnyFeature(effect.GetTargetFeatures()))
                {
                    continue;
                }

                Debug.Log(
                    $"[EffectDamageImmunity] {protectingBase.Data.cardName} blocks {resolvedAmount} enemy effect dmg "
                    + $"on {damageTarget.Data.cardName} (max:{threshold}, ownerTurn:{isDamageTargetOwnerTurn})");
                return true;
            }
        }

        return false;
    }
}
