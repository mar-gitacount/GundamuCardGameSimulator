using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ユニット本体／搭乗パイロットの「低AP／低Lv敵からの戦闘ダメージ無効」パッシブ判定。
/// </summary>
public static class CardPilotBattleDamageImmunityExtensions
{
    /// <summary>
    /// BattleDamageImmunityFromLowApEnemy により、
    /// 敵ユニット（AP または Lv が value 以下）からの戦闘ダメージを無効化するか。
    /// statTarget=Level のときは CardData.level 基準の CurrentLevel を比較する。
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

        EffectActivationContext ctx = BuildCombatImmunityContext(
            damageTarget,
            damageTarget.MountedPilot,
            damageTargetOwner,
            isDamageTargetOwnerTurn);

        if (damageTarget.Data?.timedEffects != null
            && TryIgnoreFromTimedEffects(
                damageTarget.Data.timedEffects,
                damageTarget,
                damageSource,
                ctx,
                damageTarget.Data.cardName))
        {
            return true;
        }

        CardController pilot = damageTarget.MountedPilot;
        if (pilot?.Data?.timedEffects != null
            && TryIgnoreFromTimedEffects(
                pilot.Data.timedEffects,
                pilot,
                damageSource,
                ctx,
                pilot.Data.cardName))
        {
            return true;
        }

        return false;
    }

    private static bool TryIgnoreFromTimedEffects(
        IReadOnlyList<TimedEffectData> timedEffects,
        CardController effectOwner,
        CardController damageSource,
        EffectActivationContext ctx,
        string ownerLabel)
    {
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
                if (effect == null || effect.type != EffectType.BattleDamageImmunityFromLowApEnemy)
                {
                    continue;
                }

                int threshold = effect.value > 0 ? effect.value : 3;
                int attackerStat = ResolveAttackerStatForImmunity(damageSource, effect);
                if (attackerStat <= threshold)
                {
                    string statLabel = effect.statTarget == EffectStatTarget.Level ? "Lv" : "AP";
                    Debug.Log(
                        $"[BattleDamageImmunity] {ctx.SourceCard?.Data?.cardName} ignores {attackerStat} dmg "
                        + $"from {damageSource.Data.cardName} (owner:{ownerLabel}, max{statLabel}:{threshold})");
                    return true;
                }
            }
        }

        return false;
    }

    private static int ResolveAttackerStatForImmunity(CardController attacker, EffectData effect)
    {
        if (effect != null && effect.statTarget == EffectStatTarget.Level)
        {
            if (attacker?.Data != null && attacker.Data.IsUnitToken())
            {
                return 0;
            }

            return attacker != null ? attacker.CurrentLevel : 0;
        }

        return attacker != null ? attacker.CurrentPower : 0;
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
