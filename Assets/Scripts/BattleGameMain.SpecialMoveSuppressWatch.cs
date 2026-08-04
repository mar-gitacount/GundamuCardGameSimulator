using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 自分の〔必殺技〕コマンド【メイン】／【アクション】発動監視と、制圧のターン限定付与。
/// </summary>
public partial class BattleGameMain
{
    private const int SpecialMoveFeatureId = 11;

    private void ClearSuppressUntilEndOfTurnGrantsForAllInPlayUnits()
    {
        ClearSuppressUntilEndOfTurnGrantsOnZone(playerBattleZoneCards);
        ClearSuppressUntilEndOfTurnGrantsOnZone(enemyBattleZoneCards);
    }

    private static void ClearSuppressUntilEndOfTurnGrantsOnZone(List<CardController> zone)
    {
        if (zone == null)
        {
            return;
        }

        for (int i = 0; i < zone.Count; i++)
        {
            zone[i]?.ClearSuppressUntilEndOfTurnGrants();
        }
    }

    /// <summary>
    /// duration が UntilEndOfTurn の Suppress をユニットへ付与する。
    /// Permanent（カード印刷の OnShieldAttack マーカー）は従来どおりデータ参照のみ。
    /// </summary>
    private void ApplyTimedSuppressGrant(
        CardController sourceCard,
        PlayerType ownerType,
        EffectData effect,
        List<CardController> targets)
    {
        if (effect == null || effect.duration == EffectDuration.Permanent)
        {
            return;
        }

        if (effect.duration != EffectDuration.UntilEndOfTurn
            && effect.duration != EffectDuration.UntilEndOfBattle)
        {
            return;
        }

        int breaks = effect.value > 0 ? effect.value : DefaultSuppressShieldBreakCount;
        List<CardController> grantTargets = targets;
        if (grantTargets == null || grantTargets.Count == 0)
        {
            CardController selfHost = ResolveAttackActiveEnemyGrantHost(sourceCard);
            if (selfHost != null)
            {
                grantTargets = new List<CardController> { selfHost };
            }
        }

        if (grantTargets == null)
        {
            return;
        }

        for (int i = 0; i < grantTargets.Count; i++)
        {
            CardController unit = grantTargets[i];
            if (unit == null || unit.Data == null || !unit.Data.IsUnitLike() || unit.CurrentHp <= 0)
            {
                continue;
            }

            if (!IsCardOnBattleZone(unit))
            {
                continue;
            }

            // 現状はカードテキストどおりターン中のみ（UntilEndOfBattle も EOT 枠に載せる）
            unit.AddSuppressUntilEndOfTurnGrant(breaks);
            Debug.Log(
                $"[Suppress] UntilEndOfTurn 付与 breaks:{breaks} → {unit.Data.cardName} "
                + $"(source:{sourceCard?.Data?.cardName} owner:{ownerType})");
        }
    }

    /// <summary>
    /// 手札コマンドや搭乗【メイン】で〔必殺技〕コマンドを発動した直後に呼ぶ。
    /// </summary>
    private void NotifyOwnerSpecialMoveCommandActivated(
        PlayerType ownerType,
        CardData commandData,
        Action onComplete = null)
    {
        if (commandData == null || !commandData.IsCommand() || !commandData.HasFeatureId(SpecialMoveFeatureId))
        {
            onComplete?.Invoke();
            return;
        }

        bool hasMainOrAction = HasEffectTiming(commandData, EffectTiming.OnMain)
            || HasEffectTiming(commandData, EffectTiming.OnAction);
        if (!hasMainOrAction)
        {
            onComplete?.Invoke();
            return;
        }

        MarkOwnerSpecialMoveCommandActivatedThisTurn(ownerType);

        List<CardController> watchers = CollectOwnerBattleUnitsWithSpecialMoveWatch(ownerType);
        if (watchers.Count == 0)
        {
            onComplete?.Invoke();
            return;
        }

        Debug.Log(
            $"[SpecialMoveWatch] {commandData.cardName}(id:{commandData.id}) activated → "
            + $"watchers:{watchers.Count} owner:{ownerType}");

        RunOwnerSpecialMoveWatchUnits(ownerType, watchers, 0, onComplete);
    }

    private List<CardController> CollectOwnerBattleUnitsWithSpecialMoveWatch(PlayerType ownerType)
    {
        List<CardController> result = new List<CardController>();
        List<CardController> zone = ownerType == PlayerType.Player
            ? playerBattleZoneCards
            : enemyBattleZoneCards;
        if (zone == null)
        {
            return result;
        }

        for (int i = 0; i < zone.Count; i++)
        {
            CardController unit = zone[i];
            if (unit == null || unit.Data == null || !unit.Data.IsUnitLike() || unit.CurrentHp <= 0)
            {
                continue;
            }

            if (unit.Data.timedEffects == null)
            {
                continue;
            }

            for (int t = 0; t < unit.Data.timedEffects.Count; t++)
            {
                if (unit.Data.timedEffects[t].IsOnOwnerSpecialMoveCommandActivatedResolutionBlock())
                {
                    result.Add(unit);
                    break;
                }
            }
        }

        return result;
    }

    private void RunOwnerSpecialMoveWatchUnits(
        PlayerType ownerType,
        List<CardController> watchers,
        int index,
        Action onComplete)
    {
        if (watchers == null || index >= watchers.Count)
        {
            onComplete?.Invoke();
            return;
        }

        CardController unit = watchers[index];
        if (unit == null || unit.Data?.timedEffects == null)
        {
            RunOwnerSpecialMoveWatchUnits(ownerType, watchers, index + 1, onComplete);
            return;
        }

        EffectActivationContext activationContext = BuildActivationContext(ownerType, unit);
        List<TimedEffectData> blocks = new List<TimedEffectData>();
        for (int i = 0; i < unit.Data.timedEffects.Count; i++)
        {
            TimedEffectData timed = unit.Data.timedEffects[i];
            if (!timed.IsOnOwnerSpecialMoveCommandActivatedResolutionBlock())
            {
                continue;
            }

            if (!CanRunTimedBlockAtChainTime(timed, activationContext, "OnOwnerSpecialMoveCommandActivated"))
            {
                continue;
            }

            blocks.Add(timed);
        }

        if (blocks.Count == 0)
        {
            RunOwnerSpecialMoveWatchUnits(ownerType, watchers, index + 1, onComplete);
            return;
        }

        RunOnPlayedTimedBlocks(
            unit,
            ownerType,
            blocks,
            0,
            () => RunOwnerSpecialMoveWatchUnits(ownerType, watchers, index + 1, onComplete));
    }
}
