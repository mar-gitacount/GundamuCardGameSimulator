using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 【アタック時】搭乗カードの【メイン】をコストなし・セット維持で発動。
/// </summary>
public partial class BattleGameMain
{
    private void ApplyActivateMountedCardOnMain(
        CardController sourceCard,
        PlayerType ownerType,
        Action onComplete)
    {
        CardController host = _pendingOnAttackPreCombatResolvedAttacker ?? sourceCard;
        if (host == null || host.Data == null || !host.Data.IsUnitLike())
        {
            host = sourceCard != null && sourceCard.MountedUnit != null
                ? sourceCard.MountedUnit
                : sourceCard;
        }

        CardController pilot = host != null ? host.MountedPilot : null;
        if (pilot == null || pilot.Data == null || !pilot.Data.IsPilot())
        {
            Debug.Log(
                $"[ActivateMountedOnMain] 搭乗カードがありません "
                + $"(host:{host?.Data?.cardName ?? "?"})。スキップ。");
            onComplete?.Invoke();
            return;
        }

        List<EffectData> effects = CollectMountedCardOnMainEffectsForFreeActivation(pilot.Data);
        if (effects.Count == 0)
        {
            Debug.Log(
                $"[ActivateMountedOnMain] OnMain 効果がありません "
                + $"(pilot:{pilot.Data.cardName} id:{pilot.Data.id})。スキップ。");
            onComplete?.Invoke();
            return;
        }

        Debug.Log(
            $"[ActivateMountedOnMain] {pilot.Data.cardName}(id:{pilot.Data.id}) の【メイン】を "
            + $"コストなしで発動 (effects:{effects.Count}) host:{host.Data.cardName}");

        EffectActivationContext chainContext = BuildOnAttackActivationContext(ownerType, host);
        CardData mountedData = pilot.Data;
        TryExecuteOnMainEffectChain(
            ownerType,
            pilot,
            effects,
            0,
            activationCostAlreadyPaid: true,
            chainContext,
            () => NotifyOwnerSpecialMoveCommandActivated(ownerType, mountedData, onComplete));
    }

    private static List<EffectData> CollectMountedCardOnMainEffectsForFreeActivation(CardData pilotData)
    {
        List<EffectData> result = new List<EffectData>();
        if (pilotData?.timedEffects == null)
        {
            return result;
        }

        for (int i = 0; i < pilotData.timedEffects.Count; i++)
        {
            TimedEffectData timed = pilotData.timedEffects[i];
            if (timed == null || timed.timing != EffectTiming.OnMain || !timed.HasResolvedEffects())
            {
                continue;
            }

            IReadOnlyList<EffectData> resolved = timed.GetResolvedEffects();
            for (int e = 0; e < resolved.Count; e++)
            {
                EffectData effect = resolved[e];
                if (effect == null)
                {
                    continue;
                }

                // セット維持のため、トラッシュからの再セットは行わない。
                if (effect.type == EffectType.MountSelfFromTrashAsPilot)
                {
                    continue;
                }

                result.Add(effect);
            }
        }

        return result;
    }

    /// <summary>
    /// 【アタック時】搭乗カードの【アクション】をコストなし・セット維持で発動。
    /// コマンドパイロットがアクションステップから起動できない場合でも、この経路は使う（GD06-066 シュピーゲル等）。
    /// </summary>
    private void ApplyActivateMountedCardOnAction(
        CardController sourceCard,
        PlayerType ownerType,
        Action onComplete)
    {
        CardController host = _pendingOnAttackPreCombatResolvedAttacker ?? sourceCard;
        if (host == null || host.Data == null || !host.Data.IsUnitLike())
        {
            host = sourceCard != null && sourceCard.MountedUnit != null
                ? sourceCard.MountedUnit
                : sourceCard;
        }

        CardController pilot = host != null ? host.MountedPilot : null;
        if (pilot == null || pilot.Data == null || !pilot.Data.IsPilot())
        {
            Debug.Log(
                $"[ActivateMountedOnAction] 搭乗カードがありません "
                + $"(host:{host?.Data?.cardName ?? "?"})。スキップ。");
            onComplete?.Invoke();
            return;
        }

        List<EffectData> effects = CollectMountedCardOnActionEffectsForFreeActivation(pilot.Data);
        if (effects.Count == 0)
        {
            Debug.Log(
                $"[ActivateMountedOnAction] OnAction 効果がありません "
                + $"(pilot:{pilot.Data.cardName} id:{pilot.Data.id})。スキップ。");
            onComplete?.Invoke();
            return;
        }

        Debug.Log(
            $"[ActivateMountedOnAction] {pilot.Data.cardName}(id:{pilot.Data.id}) の【アクション】を "
            + $"コストなしで発動 (effects:{effects.Count}) host:{host.Data.cardName}");

        EffectActivationContext chainContext = BuildOnAttackActivationContext(ownerType, host);
        CardData mountedData = pilot.Data;
        TryExecuteOnMainEffectChain(
            ownerType,
            pilot,
            effects,
            0,
            activationCostAlreadyPaid: true,
            chainContext,
            () => NotifyOwnerSpecialMoveCommandActivated(ownerType, mountedData, onComplete));
    }

    private static List<EffectData> CollectMountedCardOnActionEffectsForFreeActivation(CardData pilotData)
    {
        List<EffectData> result = new List<EffectData>();
        if (pilotData?.timedEffects == null)
        {
            return result;
        }

        for (int i = 0; i < pilotData.timedEffects.Count; i++)
        {
            TimedEffectData timed = pilotData.timedEffects[i];
            if (timed == null || timed.timing != EffectTiming.OnAction || !timed.HasResolvedEffects())
            {
                continue;
            }

            IReadOnlyList<EffectData> resolved = timed.GetResolvedEffects();
            for (int e = 0; e < resolved.Count; e++)
            {
                EffectData effect = resolved[e];
                if (effect == null)
                {
                    continue;
                }

                if (effect.type == EffectType.MountSelfFromTrashAsPilot)
                {
                    continue;
                }

                result.Add(effect);
            }
        }

        return result;
    }
}
