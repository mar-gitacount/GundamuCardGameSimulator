using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Master Gundam（id:112）専用。
/// 【アタック時】ユニット（除外→シールド5）と搭乗パイロット（Master Asia 等）の解決順を選べる。
/// 汎用 OnAttack チェーンには乗せない。
/// </summary>
public partial class BattleGameMain
{
    private const int MasterGundamCardId = 112;
    private const int MasterGundamSpecialMoveFeatureId = 11;
    private const int MasterGundamExileCount = 2;
    private const int MasterGundamShieldAreaDamage = 5;

    private static bool IsMasterGundamUnit(CardController unit)
    {
        if (unit == null || unit.Data == null)
        {
            return false;
        }

        if (unit.Data.id == MasterGundamCardId)
        {
            return true;
        }

        string name = unit.Data.cardName;
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        return name.IndexOf("Master Gundam", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("マスターガンダム", StringComparison.Ordinal) >= 0;
    }

    /// <summary>
    /// Master Gundam 専用 OnAttack。開始したら true（完了後 onComplete）。
    /// </summary>
    private bool TryBeginMasterGundamOnAttackEffect(
        CardController attacker,
        PlayerType attackerOwner,
        Action onComplete)
    {
        if (!IsMasterGundamUnit(attacker) || HasOnAttackPreCombatEffectsBeenApplied(attacker))
        {
            return false;
        }

        Debug.Log(
            $"[MasterGundam] Dedicated OnAttack start "
            + $"attacker:{attacker.Data.cardName}(id:{attacker.Data.id}) owner:{attackerOwner} "
            + $"pilot:{attacker.MountedPilot?.Data?.cardName ?? "none"}");

        MarkOnAttackPreCombatEffectsApplied(attacker);
        _onAttackPreCombatCompletedAttacker = attacker;
        ClearOnMainPaidBlock();
        _pendingOnAttackPreCombatResolvedAttacker = attacker;

        StartCoroutine(CoMasterGundamOnAttackWithPilotOrder(attacker, attackerOwner, onComplete));
        return true;
    }

    private IEnumerator CoMasterGundamOnAttackWithPilotOrder(
        CardController attacker,
        PlayerType attackerOwner,
        Action onComplete)
    {
        CardController pilot = attacker != null ? attacker.MountedPilot : null;
        List<TimedEffectData> pilotBlocks = CollectOnAttackBlocksForPilotOnMasterGundam(
            pilot,
            attacker,
            attackerOwner);
        List<TimedEffectData> unitPlaceholder = new List<TimedEffectData>
        {
            CreateMasterGundamOrderPlaceholderBlock()
        };

        bool orderDone = false;
        List<UnitPilotEffectOrderEntry> ordered = null;
        ResolveUnitPilotEffectOrder(
            attackerOwner,
            attacker,
            pilot,
            unitPlaceholder,
            pilotBlocks,
            attacker != null ? attacker.Data : null,
            result =>
            {
                ordered = result;
                orderDone = true;
            },
            autoPilotFirst: false,
            titleJa: "アタック時効果の解決順を選択",
            titleEn: "Choose On Attack effect order",
            entrySelectable: (_, __) => true);

        yield return new WaitUntil(() => orderDone);

        if (ordered != null)
        {
            for (int i = 0; i < ordered.Count; i++)
            {
                UnitPilotEffectOrderEntry entry = ordered[i];
                if (entry == null || entry.Source == null)
                {
                    continue;
                }

                if (IsMasterGundamUnit(entry.Source))
                {
                    yield return CoRunMasterGundamExileAndShieldDamage(attacker, attackerOwner);
                }
                else
                {
                    yield return CoRunOnAttackPreCombatTimedBlocksWait(
                        attacker,
                        attackerOwner,
                        entry.Blocks);
                }
            }
        }
        else
        {
            yield return CoRunMasterGundamExileAndShieldDamage(attacker, attackerOwner);
        }

        Gundam2024RuleScript.PlayerSide targetSide = attackerOwner == PlayerType.Player
            ? Gundam2024RuleScript.PlayerSide.Enemy
            : Gundam2024RuleScript.PlayerSide.Player;
        RefreshMasterGundamShieldAttackLayerCacheAfterEffect(targetSide);

        // ブロック／アクション／本体攻撃は呼び出し元の通常フローに任せる（ここではスキップしない）
        ClearOnAttackPreCombatResolvedState();

        Debug.Log("[MasterGundam] OnAttack effects done — resume normal attack flow (block/action/strike).");
        onComplete?.Invoke();
    }

    private IEnumerator CoRunMasterGundamExileAndShieldDamage(
        CardController attacker,
        PlayerType attackerOwner)
    {
        bool exileFinished = false;
        bool exileSucceeded = false;

        ClearOnMainPaidBlock();
        EffectData exileEffect = BuildMasterGundamExileEffect();
        ApplyExileFromTrashEffect(
            attacker,
            attackerOwner,
            exileEffect,
            onComplete: () =>
            {
                exileSucceeded = true;
                exileFinished = true;
            },
            onSkipped: () =>
            {
                exileSucceeded = false;
                exileFinished = true;
            });

        yield return new WaitUntil(() => exileFinished);

        Gundam2024RuleScript.PlayerSide targetSide = attackerOwner == PlayerType.Player
            ? Gundam2024RuleScript.PlayerSide.Enemy
            : Gundam2024RuleScript.PlayerSide.Player;

        if (!exileSucceeded
            || !IsCardControllerInstanceValid(attacker)
            || attacker.Data == null
            || gundamRule == null)
        {
            Debug.Log("[MasterGundam] Exile skipped/cancelled — no shield-area damage.");
            yield break;
        }

        Gundam2024RuleScript.PlayerState before = targetSide == Gundam2024RuleScript.PlayerSide.Player
            ? gundamRule.Player
            : gundamRule.Enemy;
        Debug.Log(
            $"[MasterGundam] Exile OK — 5 damage to shield area "
            + $"(exBefore:{before?.exBase ?? -1} shieldBefore:{before?.shield ?? -1})");

        // 効果ダメージ中だけシールド溢れ防止を外し、終わったら必ず戻す（他フローを壊さない）
        bool prevBlockShieldFlow = blockShieldFlowDuringShieldAttack;
        blockShieldFlowDuringShieldAttack = false;
        _allowOnAttackEffectShieldAreaDamage = true;
        try
        {
            ApplyEffectDamageToPlayerArea(targetSide, MasterGundamShieldAreaDamage, attacker);
        }
        finally
        {
            _allowOnAttackEffectShieldAreaDamage = false;
            blockShieldFlowDuringShieldAttack = prevBlockShieldFlow;
        }

        yield return WaitForShieldBreakFlowCompleteCoroutine(20f);
        yield return WaitUntilBlockingChoiceOrTrashUiCleared(5f);
    }

    private IEnumerator CoRunOnAttackPreCombatTimedBlocksWait(
        CardController attacker,
        PlayerType attackerOwner,
        List<TimedEffectData> blocks)
    {
        if (blocks == null || blocks.Count == 0)
        {
            yield break;
        }

        bool done = false;
        RunOnAttackPreCombatTimedBlocks(
            attacker,
            attackerOwner,
            blocks,
            0,
            () => done = true);
        yield return new WaitUntil(() => done);
        yield return WaitUntilBlockingChoiceOrTrashUiCleared(5f);
    }

    /// <summary>
    /// Master Gundam 搭乗パイロットの【アタック時】（敵選択ダメージ含む。Master Asia 等）。
    /// </summary>
    private List<TimedEffectData> CollectOnAttackBlocksForPilotOnMasterGundam(
        CardController pilot,
        CardController attacker,
        PlayerType attackerOwner)
    {
        List<TimedEffectData> blocks = new List<TimedEffectData>();
        if (pilot?.Data?.timedEffects == null || attacker == null)
        {
            return blocks;
        }

        EffectActivationContext ctx = BuildOnAttackActivationContext(attackerOwner, attacker);

        for (int i = 0; i < pilot.Data.timedEffects.Count; i++)
        {
            TimedEffectData timed = pilot.Data.timedEffects[i];
            if (timed == null || timed.timing != EffectTiming.OnAttack || !timed.HasResolvedEffects())
            {
                continue;
            }

            if (timed.oncePerTurn && HasUsedPaidActivationThisTurn(attackerOwner, pilot, i))
            {
                continue;
            }

            if (!EffectActivationEvaluator.AreTimedConditionsMet(timed, ctx))
            {
                Debug.Log(
                    $"[MasterGundam] Pilot OnAttack skipped (conditions): {pilot.Data.cardName} block:{i} "
                    + $"linkedFlag:{ctx.OwnerActivatedSpecialMoveCommandThisTurn}");
                continue;
            }

            // プレコンバット扱い OR 敵ユニット選択ダメージ等（Master Asia）
            if (!TimedBlockNeedsOnAttackPreCombatResolution(timed)
                && !TimedBlockHasOnAttackEnemyUnitEffect(timed))
            {
                continue;
            }

            blocks.Add(timed);
        }

        Debug.Log(
            $"[MasterGundam] Pilot OnAttack blocks:{blocks.Count} "
            + $"pilot:{pilot.Data.cardName}(id:{pilot.Data.id})");
        return blocks;
    }

    private static bool TimedBlockHasOnAttackEnemyUnitEffect(TimedEffectData timed)
    {
        IReadOnlyList<EffectData> effects = timed.GetResolvedEffects();
        for (int i = 0; i < effects.Count; i++)
        {
            EffectData effect = effects[i];
            if (effect == null)
            {
                continue;
            }

            if (effect.type != EffectType.Damage
                && effect.type != EffectType.Destroy
                && effect.type != EffectType.Bounce
                && effect.type != EffectType.Rest
                && effect.type != EffectType.Debuff)
            {
                continue;
            }

            if (effect.target == TargetType.EnemyUnit
                || effect.target == TargetType.RestEnemyUnit
                || effect.target == TargetType.EnemyAllUnits
                || effect.target == TargetType.AnyUnit
                || effect.target == TargetType.EnemyTokenUnit)
            {
                return true;
            }
        }

        return false;
    }

    private static TimedEffectData CreateMasterGundamOrderPlaceholderBlock()
    {
        // 解決順 UI 用。実行は CoRunMasterGundamExileAndShieldDamage で行う。
        return new TimedEffectData
        {
            timing = EffectTiming.OnAttack,
            effects = new List<EffectData>
            {
                new EffectData
                {
                    type = EffectType.MillTopToTrash,
                    value = 1,
                    target = TargetType.SelfPlayer,
                    selectionMode = EffectSelectionMode.Unset,
                }
            }
        };
    }

    private void RefreshMasterGundamShieldAttackLayerCacheAfterEffect(
        Gundam2024RuleScript.PlayerSide targetSide)
    {
        if (gundamRule == null)
        {
            _shieldAttackHadExOrBaseAtDeclaration = false;
            _shieldAttackHadExOrBaseAtDeclarationValid = true;
            return;
        }

        Gundam2024RuleScript.PlayerState defender = targetSide == Gundam2024RuleScript.PlayerSide.Player
            ? gundamRule.Player
            : gundamRule.Enemy;
        bool layerNow = defender != null
            && (defender.exBase > 0 || HasActiveDeployedBaseForRuleSide(targetSide));
        _shieldAttackHadExOrBaseAtDeclaration = layerNow;
        _shieldAttackHadExOrBaseAtDeclarationValid = true;
        Debug.Log(
            $"[MasterGundam] Strike layer cache refreshed — hadExOrBaseNow:{layerNow} "
            + $"(ex:{defender?.exBase ?? -1})");
    }

    private static EffectData BuildMasterGundamExileEffect()
    {
        return new EffectData
        {
            type = EffectType.ExileFromTrash,
            value = MasterGundamExileCount,
            target = TargetType.SelfPlayer,
            selectionMode = EffectSelectionMode.Unset,
            targetFeatureId = MasterGundamSpecialMoveFeatureId,
            filterByTargetCardType = true,
            targetCardType = Type.Command,
            requireExactExileCount = true,
            abortRemainingChainOnSkip = true,
        };
    }

    /// <summary>汎用収集から Master Gundam 本体の timedEffects だけ除外（パイロットは収集可）。</summary>
    private static bool ShouldSkipMasterGundamInGenericOnAttack(CardController sourceOrAttacker)
    {
        return IsMasterGundamUnit(sourceOrAttacker);
    }

    /// <summary>
    /// Master Gundam＋搭乗パイロット解決中のみ、敵ユニット向け OnAttack（Master Asia の2ダメ等）を許可。
    /// </summary>
    private bool ShouldAllowMasterGundamPairEnemyUnitOnAttackEffect(EffectData effect)
    {
        if (effect == null
            || _pendingOnAttackPreCombatResolvedAttacker == null
            || !IsMasterGundamUnit(_pendingOnAttackPreCombatResolvedAttacker))
        {
            return false;
        }

        if (effect.type != EffectType.Damage
            && effect.type != EffectType.Destroy
            && effect.type != EffectType.Bounce
            && effect.type != EffectType.Rest
            && effect.type != EffectType.Debuff)
        {
            return false;
        }

        return effect.target == TargetType.EnemyUnit
            || effect.target == TargetType.RestEnemyUnit
            || effect.target == TargetType.EnemyAllUnits
            || effect.target == TargetType.AnyUnit
            || effect.target == TargetType.EnemyTokenUnit;
    }
}
