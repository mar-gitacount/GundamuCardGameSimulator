using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// GD06：配備ベース戦闘ダメージ監視（Argama）とブロッカー起動監視（Harry Ord）。
/// </summary>
public partial class BattleGameMain
{
    private const int BlockerActivatedPilotOncePerTurnBlockBase = 930000;
    private const int BaseBattleDamagePilotOncePerTurnBlockBase = 931000;
    private const int AeugFeatureId = 41;

    private int _playerPaidUnitEffectCostThisTurn;
    private int _enemyPaidUnitEffectCostThisTurn;

    private struct ArgamaAfterDamageStep
    {
        public CardController BaseCard;
        public PlayerType OwnerType;
        public int TimedBlockIndex;
        public bool DestroyAfterPick;
    }

    private struct PendingBlockerActivatedWatch
    {
        public CardController BlockerUnit;
        public PlayerType OwnerType;
        public bool SyncOnlineAfter;
    }

    /// <summary>アーガマが戦闘ダメージを受けたあと、相手ユニット選択 UI を出すための保留。</summary>
    private ArgamaAfterDamageStep? _argamaAfterDamageStep;
    private bool _argamaAfterDamageStepResolving;

    private readonly List<PendingBlockerActivatedWatch> _pendingBlockerActivatedWatches =
        new List<PendingBlockerActivatedWatch>();
    private bool _blockerActivatedWatchFlushRunning;

    private bool HasPendingOrRunningBaseBattleDamageWatch =>
        _argamaAfterDamageStep.HasValue || _argamaAfterDamageStepResolving;

    private int GetPaidUnitEffectCostThisTurn(PlayerType side)
    {
        return side == PlayerType.Player
            ? _playerPaidUnitEffectCostThisTurn
            : _enemyPaidUnitEffectCostThisTurn;
    }

    private void ClearPaidUnitEffectCostThisTurn(PlayerType side)
    {
        if (side == PlayerType.Player)
        {
            _playerPaidUnitEffectCostThisTurn = 0;
        }
        else
        {
            _enemyPaidUnitEffectCostThisTurn = 0;
        }
    }

    private void RecordPaidUnitEffectCostIfApplicable(PlayerType side, CardController source, int cost)
    {
        if (cost <= 0 || source?.Data == null)
        {
            return;
        }

        bool isFieldUnit = source.Data.IsUnitLike()
            && (IsCardOnBattleZone(source)
                || IsOnDeployPanel(source, PlayerType.Player)
                || IsOnDeployPanel(source, PlayerType.Enemy));
        bool isMountedPilot = source.Data.IsPilot()
            && (source.MountedUnit != null || FindHostUnitMountingPilot(source) != null);
        if (!isFieldUnit && !isMountedPilot)
        {
            return;
        }

        if (side == PlayerType.Player)
        {
            _playerPaidUnitEffectCostThisTurn += cost;
        }
        else
        {
            _enemyPaidUnitEffectCostThisTurn += cost;
        }

        Debug.Log(
            $"[PaidUnitEffectCost] +{cost} → {GetPaidUnitEffectCostThisTurn(side)} "
            + $"side:{side} source:{source.Data.cardName}(id:{source.Data.id})");
    }

    /// <summary>
    /// 配備ベースが戦闘ダメージで HP 減少した直後に呼ぶ。
    /// UI は出さず、打撃後コルーチンの <see cref="CoResolveArgamaAfterBaseDamageStep"/> で出す。
    /// </summary>
    private void NotifyThisBaseReceivedBattleDamageIfNeeded(CardController baseCard, int hpBefore)
    {
        if (baseCard == null || baseCard.Data == null || baseCard.Data.type != Type.Base)
        {
            return;
        }

        if (baseCard.CurrentHp >= hpBefore)
        {
            return;
        }

        PlayerType ownerType = ResolveDeployedBaseOwner(baseCard);
        if (IsOnlineBattle() && ownerType == PlayerType.Enemy && !_applyingRemoteBattleAction)
        {
            return;
        }

        int blockIndex = FindOnThisBaseReceivedBattleDamageBlockIndex(baseCard);
        if (blockIndex < 0)
        {
            Debug.LogWarning(
                $"[Argama] no OnThisBaseReceivedBattleDamage block on {baseCard.Data.cardName}");
            return;
        }

        _argamaAfterDamageStep = new ArgamaAfterDamageStep
        {
            BaseCard = baseCard,
            OwnerType = ownerType,
            TimedBlockIndex = blockIndex
        };
        Debug.Log(
            $"[Argama] queued after damage-step {baseCard.Data.cardName}(id:{baseCard.Data.id}) "
            + $"hp:{hpBefore}→{baseCard.CurrentHp} owner:{ownerType} block:{blockIndex}");
    }

    /// <summary>配備ベースは BaseSlot にいるため、手番フォールバックの ResolveCardOwner だけでは所有者を誤る。</summary>
    private PlayerType ResolveDeployedBaseOwner(CardController baseCard)
    {
        if (baseCard != null)
        {
            if (GetDeployedBaseForRuleSide(Gundam2024RuleScript.PlayerSide.Player) == baseCard)
            {
                return PlayerType.Player;
            }

            if (GetDeployedBaseForRuleSide(Gundam2024RuleScript.PlayerSide.Enemy) == baseCard)
            {
                return PlayerType.Enemy;
            }
        }

        return ResolveCardOwner(baseCard != null ? baseCard.transform : null);
    }

    private static int FindOnThisBaseReceivedBattleDamageBlockIndex(CardController baseCard)
    {
        if (baseCard?.Data?.timedEffects == null)
        {
            return -1;
        }

        for (int i = 0; i < baseCard.Data.timedEffects.Count; i++)
        {
            TimedEffectData timed = baseCard.Data.timedEffects[i];
            if (timed != null && timed.timing == EffectTiming.OnThisBaseReceivedBattleDamage)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// 判定（シールド破壊・撃破 UI）が終わったあと、アーガマの相手ユニット選択を出す。
    /// </summary>
    private IEnumerator CoResolveArgamaAfterCombatJudgment()
    {
        // 判定 UI が残っていても無限待ちしない（タイムアウト後に選択 UI を出す）。
        yield return WaitUntilBlockingChoiceOrTrashUiCleared(8f);
        yield return CoResolveArgamaAfterBaseDamageStep();
    }

    /// <summary>
    /// リモートシールド攻撃：破壊判定のあとでアーガマ UI。
    /// </summary>
    private IEnumerator CoRemoteShieldAttackJudgmentThenArgama(
        int brokenCount,
        bool simultaneousReveal,
        int[] brokenShieldCardIds,
        int requestId)
    {
        if (brokenCount > 0)
        {
            yield return ApplyRemoteDefenderShieldBreakCoroutine(
                Gundam2024RuleScript.PlayerSide.Player,
                brokenCount,
                simultaneousReveal,
                brokenShieldCardIds,
                requestId);
        }
        else if (requestId > 0)
        {
            SendOnlineShieldBreakComplete(requestId, Gundam2024RuleScript.PlayerSide.Player);
        }

        yield return CoResolveArgamaAfterCombatJudgment();
    }

    /// <summary>
    /// アーガマ：戦闘ダメージ→判定のあと、レスト〔エゥーゴ〕がいれば相手ユニット選択 UI を出し選ぶまで待つ。
    /// </summary>
    private IEnumerator CoResolveArgamaAfterBaseDamageStep()
    {
        if (!_argamaAfterDamageStep.HasValue)
        {
            yield break;
        }

        ArgamaAfterDamageStep pending = _argamaAfterDamageStep.Value;
        _argamaAfterDamageStep = null;
        _argamaAfterDamageStepResolving = true;
        try
        {
            yield return CoShowArgamaEnemyUnitPick(pending);
        }
        finally
        {
            _argamaAfterDamageStepResolving = false;
        }
    }

    private IEnumerator CoShowArgamaEnemyUnitPick(ArgamaAfterDamageStep pending)
    {
        CardController baseCard = pending.BaseCard;
        PlayerType ownerType = pending.OwnerType;
        try
        {
            yield return CoShowArgamaEnemyUnitPickBody(pending);
        }
        finally
        {
            if (pending.DestroyAfterPick && baseCard != null)
            {
                Gundam2024RuleScript.PlayerSide side = ToRuleSide(ownerType);
                SendDeployedBaseToTrash(baseCard, ownerType, GetCardRuleForRuleSide(side));
                SyncResourceViewsFromRule(side);
                SyncBaseZoneHeaderDisplay(side);
            }
        }
    }

    private IEnumerator CoShowArgamaEnemyUnitPickBody(ArgamaAfterDamageStep pending)
    {
        CardController baseCard = pending.BaseCard;
        PlayerType ownerType = pending.OwnerType;
        if (baseCard == null || baseCard.Data == null)
        {
            yield break;
        }

        TimedEffectData timed = null;
        if (baseCard.Data.timedEffects != null
            && pending.TimedBlockIndex >= 0
            && pending.TimedBlockIndex < baseCard.Data.timedEffects.Count)
        {
            timed = baseCard.Data.timedEffects[pending.TimedBlockIndex];
        }

        if (timed != null
            && timed.oncePerTurn
            && HasUsedPaidActivationThisTurn(ownerType, baseCard, pending.TimedBlockIndex))
        {
            Debug.Log("[Argama] skip — already used this turn");
            yield break;
        }

        int restedAeug = CountOwnerRestedAeugUnitsForArgama(ownerType);
        if (restedAeug < 1)
        {
            LogArgamaRestedAeugSkip(ownerType);
            yield break;
        }

        EffectData pickEffect = ResolveArgamaDamagePickEffect(timed);
        List<CardController> candidates = CollectAliveEnemyUnitsForArgamaPick(ownerType);
        bool showUi = ShouldShowArgamaEnemyPickUi(ownerType);
        Debug.Log(
            $"[Argama] after judgment → enemy pick UI. owner:{ownerType} "
            + $"restedAeug:{restedAeug} enemies:{candidates.Count} showUi:{showUi}");
        if (candidates.Count == 0)
        {
            Debug.LogWarning("[Argama] skip — no alive enemy units to pick");
            yield break;
        }

        if (timed != null && timed.oncePerTurn)
        {
            MarkPaidActivationUsedThisTurn(ownerType, baseCard, pending.TimedBlockIndex);
        }

        CloseAttackFlowPanelsForArgamaPick();
        yield return null;
        yield return null;

        if (!showUi)
        {
            EnemyAiEffectPickContext pickCtx = BuildEnemyAiEffectPickContext(ownerType, baseCard, null, null);
            CardController aiPick = PickEnemyAiEffectTarget(pickEffect, pickCtx, candidates);
            if (aiPick != null)
            {
                ApplyEffectToSpecificTargets(
                    baseCard,
                    ownerType,
                    pickEffect,
                    new List<CardController> { aiPick });
            }

            yield break;
        }

        bool resolved = false;
        OpenManualUnitTargetSelectionUI(
            baseCard,
            ownerType,
            pickEffect,
            candidates,
            attackingUnitInAttackFlow: null,
            picked =>
            {
                resolved = true;
                if (picked != null)
                {
                    ApplyEffectToSpecificTargets(
                        baseCard,
                        ownerType,
                        pickEffect,
                        new List<CardController> { picked });
                }
            });

        if (!isOnActionPopupOpen)
        {
            CloseAttackFlowPanelsForArgamaPick();
            yield return null;
            OpenManualUnitTargetSelectionUI(
                baseCard,
                ownerType,
                pickEffect,
                candidates,
                attackingUnitInAttackFlow: null,
                picked =>
                {
                    resolved = true;
                    if (picked != null)
                    {
                        ApplyEffectToSpecificTargets(
                            baseCard,
                            ownerType,
                            pickEffect,
                            new List<CardController> { picked });
                    }
                });
        }

        if (!isOnActionPopupOpen)
        {
            Debug.LogWarning("[Argama] enemy pick UI failed to open");
            yield break;
        }

        yield return new WaitUntil(() => resolved);
    }

    /// <summary>Player のアーガマは相手ターン・リモート適用中でも選択 UI を出す。TestPlay は両サイド。</summary>
    private bool ShouldShowArgamaEnemyPickUi(PlayerType ownerType)
    {
        if (ownerType == PlayerType.Player)
        {
            return true;
        }

        return IsTestPlayBattle();
    }

    private void CloseAttackFlowPanelsForArgamaPick()
    {
        if (activeAttackFlowDebugPanelRoot != null)
        {
            Destroy(activeAttackFlowDebugPanelRoot);
            activeAttackFlowDebugPanelRoot = null;
        }

        isAttackedSidePanelOpen = false;
        DestroyActiveOnActionPopupIfAny();
    }

    private int CountOwnerRestedAeugUnitsForArgama(PlayerType ownerType)
    {
        int n = CountRestedAllyUnitsWithFeatureId(ownerType, AeugFeatureId);
        if (n > 0)
        {
            return n;
        }

        return CountOwnerRestedUnitsWithAeugKey(ownerType);
    }

    private int CountOwnerRestedUnitsWithAeugKey(PlayerType ownerType)
    {
        var seen = new HashSet<EntityId>();
        int n = 0;
        n += CountRestedAeugKeyOnList(playerBattleZoneCards, ownerType, seen);
        n += CountRestedAeugKeyOnList(enemyBattleZoneCards, ownerType, seen);
        CardGameRule rule = ownerType == PlayerType.Player ? cardGameRule : enemyCardGameRule;
        Transform panel = rule != null ? rule.PlayerDeployPanel : null;
        if (panel != null)
        {
            n += CountRestedAeugKeyOnList(panel.GetComponentsInChildren<CardController>(true), ownerType, seen);
        }

        return n;
    }

    private int CountRestedAeugKeyOnList(
        IReadOnlyList<CardController> units,
        PlayerType ownerType,
        HashSet<EntityId> seen)
    {
        if (units == null)
        {
            return 0;
        }

        int n = 0;
        for (int i = 0; i < units.Count; i++)
        {
            CardController unit = units[i];
            if (unit == null || unit.Data == null || !unit.Data.IsUnitLike() || unit.CurrentHp <= 0)
            {
                continue;
            }

            if (!IsOnDeployPanel(unit, ownerType) && !IsUnitInSideBattleZoneList(unit, ownerType))
            {
                continue;
            }

            if (!UnitLooksRested(unit))
            {
                continue;
            }

            if (seen != null && !seen.Add(unit.GetEntityId()))
            {
                continue;
            }

            if (!unit.HasFeatureId(AeugFeatureId) && !unit.Data.HasFeatureKey("AEUG"))
            {
                continue;
            }

            n++;
        }

        return n;
    }

    private List<CardController> CollectAliveEnemyUnitsForArgamaPick(PlayerType ownerType)
    {
        PlayerType enemyType = ownerType == PlayerType.Player ? PlayerType.Enemy : PlayerType.Player;
        var seen = new HashSet<EntityId>();
        var result = new List<CardController>();
        AddAliveOwnedUnitsToList(result, playerBattleZoneCards, enemyType, seen);
        AddAliveOwnedUnitsToList(result, enemyBattleZoneCards, enemyType, seen);
        CardGameRule rule = enemyType == PlayerType.Player ? cardGameRule : enemyCardGameRule;
        Transform panel = rule != null ? rule.PlayerDeployPanel : null;
        if (panel != null)
        {
            AddAliveOwnedUnitsToList(
                result,
                panel.GetComponentsInChildren<CardController>(true),
                enemyType,
                seen);
        }

        return result;
    }

    private void AddAliveOwnedUnitsToList(
        List<CardController> result,
        IReadOnlyList<CardController> units,
        PlayerType ownerType,
        HashSet<EntityId> seen)
    {
        if (result == null || units == null)
        {
            return;
        }

        for (int i = 0; i < units.Count; i++)
        {
            CardController unit = units[i];
            if (unit == null || unit.Data == null || !unit.Data.IsUnitLike() || unit.CurrentHp <= 0)
            {
                continue;
            }

            if (!IsOnDeployPanel(unit, ownerType) && !IsUnitInSideBattleZoneList(unit, ownerType))
            {
                continue;
            }

            if (seen != null && !seen.Add(unit.GetEntityId()))
            {
                continue;
            }

            result.Add(unit);
        }
    }

    private void LogArgamaRestedAeugSkip(PlayerType ownerType)
    {
        var sb = new System.Text.StringBuilder(256);
        sb.Append("[Argama] skip — no rested AEUG owner:").Append(ownerType);
        List<CardController> zone = ownerType == PlayerType.Player ? playerBattleZoneCards : enemyBattleZoneCards;
        if (zone != null)
        {
            for (int i = 0; i < zone.Count; i++)
            {
                CardController unit = zone[i];
                if (unit == null || unit.Data == null || !unit.Data.IsUnitLike())
                {
                    continue;
                }

                sb.Append(" | ").Append(unit.Data.cardName)
                    .Append(" rest:").Append(unit.IsRestState)
                    .Append(" lookRest:").Append(UnitLooksRested(unit))
                    .Append(" aeug:").Append(unit.HasFeatureId(AeugFeatureId))
                    .Append(" hp:").Append(unit.CurrentHp);
            }
        }

        Debug.Log(sb.ToString());
    }

    private static EffectData ResolveArgamaDamagePickEffect(TimedEffectData timed)
    {
        IReadOnlyList<EffectData> resolved = timed != null ? timed.GetResolvedEffects() : null;
        if (resolved != null)
        {
            for (int i = 0; i < resolved.Count; i++)
            {
                EffectData effect = resolved[i];
                if (effect != null && effect.type == EffectType.Damage)
                {
                    return effect;
                }
            }
        }

        return new EffectData
        {
            type = EffectType.Damage,
            value = 1,
            target = TargetType.EnemyUnit,
            selectionMode = EffectSelectionMode.SelectSingle
        };
    }

    private IEnumerator WaitUntilBaseBattleDamageWatchesSettled(float timeoutSeconds = 60f)
    {
        yield return CoResolveArgamaAfterBaseDamageStep();
    }

    /// <summary>
    /// ブロック確定後にブロッカーをレストし、既に REST でも起動監視を走らせる。
    /// Harry Ord 等：このユニットが《ブロッカー》を起動したとき、ターンに1度アクティブにする。
    /// </summary>
    private void RestBlockerThenNotifyActivated(CardController blocker, bool syncOnlineRest)
    {
        if (blocker == null || blocker.Data == null || !blocker.Data.IsUnitLike() || blocker.CurrentHp <= 0)
        {
            return;
        }

        bool alreadyRest = blocker.IsRestState;
        bool restedNow = TryApplyRestToUnit(blocker);
        if (restedNow || alreadyRest)
        {
            // REST 同期は監視後に最終状態（REST or ACTIVE）を送る。先に REST だけ送ると防御側が起きない。
            NotifyBlockerActivatedAfterRest(blocker, syncOnlineAfter: syncOnlineRest);
        }
    }

    private void NotifyBlockerActivatedAfterRest(CardController blocker, bool syncOnlineAfter = false)
    {
        if (blocker == null || blocker.Data == null || !blocker.Data.IsUnitLike())
        {
            return;
        }

        PlayerType ownerType = ResolveCardOwner(blocker.transform);
        _pendingBlockerActivatedWatches.Add(new PendingBlockerActivatedWatch
        {
            BlockerUnit = blocker,
            OwnerType = ownerType,
            SyncOnlineAfter = syncOnlineAfter
        });
        StartCoroutine(FlushPendingBlockerActivatedWatchesCoroutine());
    }

    private IEnumerator FlushPendingBlockerActivatedWatchesCoroutine()
    {
        if (_blockerActivatedWatchFlushRunning)
        {
            yield return new WaitUntil(() => !_blockerActivatedWatchFlushRunning);
            if (_blockerActivatedWatchFlushRunning || _pendingBlockerActivatedWatches.Count == 0)
            {
                yield break;
            }
        }

        _blockerActivatedWatchFlushRunning = true;
        try
        {
            yield return null;
            // トラッシュ／選択 UI 待ちでハングしないよう上限を付ける
            yield return WaitUntilBlockingChoiceOrTrashUiCleared(8f);
            while (_pendingBlockerActivatedWatches.Count > 0)
            {
                PendingBlockerActivatedWatch pending = _pendingBlockerActivatedWatches[0];
                _pendingBlockerActivatedWatches.RemoveAt(0);
                bool finished = false;
                RunBlockerActivatedWatch(pending.BlockerUnit, pending.OwnerType, () => finished = true);
                yield return new WaitUntil(() => finished);
                if (pending.SyncOnlineAfter)
                {
                    SyncOnlineBlockerStandOrRestAfterWatch(pending.BlockerUnit);
                }
            }
        }
        finally
        {
            _blockerActivatedWatchFlushRunning = false;
        }

        if (_pendingBlockerActivatedWatches.Count > 0)
        {
            StartCoroutine(FlushPendingBlockerActivatedWatchesCoroutine());
        }
    }

    private int CountOwnerRestedUnitsWithFeatureId(PlayerType ownerType, int featureId)
    {
        var seen = new HashSet<EntityId>();
        int n = 0;
        List<CardController> zone = ownerType == PlayerType.Player ? playerBattleZoneCards : enemyBattleZoneCards;
        n += CountRestedFeatureUnitsOnList(zone, featureId, seen);
        CardGameRule rule = ownerType == PlayerType.Player ? cardGameRule : enemyCardGameRule;
        Transform panel = rule != null ? rule.PlayerDeployPanel : null;
        if (panel != null)
        {
            CardController[] nested = panel.GetComponentsInChildren<CardController>(true);
            n += CountRestedFeatureUnitsOnList(nested, featureId, seen);
        }

        return n;
    }

    private static int CountRestedFeatureUnitsOnList(
        IReadOnlyList<CardController> units,
        int featureId,
        HashSet<EntityId> seen)
    {
        if (units == null)
        {
            return 0;
        }

        int n = 0;
        for (int i = 0; i < units.Count; i++)
        {
            CardController unit = units[i];
            if (unit == null || unit.Data == null || !unit.Data.IsUnitLike() || unit.CurrentHp <= 0)
            {
                continue;
            }

            if (!unit.IsRestState)
            {
                continue;
            }

            if (seen != null && !seen.Add(unit.GetEntityId()))
            {
                continue;
            }

            if (featureId > 0 && !unit.HasFeatureId(featureId))
            {
                continue;
            }

            n++;
        }

        return n;
    }


    private void RunBlockerActivatedWatch(CardController blocker, PlayerType ownerType, Action onComplete)
    {
        if (blocker?.Data == null)
        {
            onComplete?.Invoke();
            return;
        }

        var sources = new List<CardController> { blocker };
        AddMountedPilotEffectSources(blocker, sources);
        RunBlockerActivatedWatchSources(blocker, ownerType, sources, 0, onComplete);
    }

    /// <summary>搭乗パイロット（MountedPilot 欠落時は子オブジェクトからも拾う）。</summary>
    private static void AddMountedPilotEffectSources(CardController host, List<CardController> sources)
    {
        if (host == null || sources == null)
        {
            return;
        }

        if (host.MountedPilot != null && host.MountedPilot.Data != null && host.MountedPilot.Data.IsPilot())
        {
            sources.Add(host.MountedPilot);
        }

        CardController[] nested = host.GetComponentsInChildren<CardController>(true);
        if (nested == null)
        {
            return;
        }

        for (int i = 0; i < nested.Length; i++)
        {
            CardController child = nested[i];
            if (child == null || child == host || child.Data == null || !child.Data.IsPilot())
            {
                continue;
            }

            if (sources.Contains(child))
            {
                continue;
            }

            sources.Add(child);
        }
    }

    private void RunBlockerActivatedWatchSources(
        CardController blocker,
        PlayerType ownerType,
        List<CardController> sources,
        int sourceIndex,
        Action onComplete)
    {
        if (sources == null || sourceIndex >= sources.Count)
        {
            onComplete?.Invoke();
            return;
        }

        CardController effectSource = sources[sourceIndex];
        if (effectSource?.Data?.timedEffects == null)
        {
            RunBlockerActivatedWatchSources(blocker, ownerType, sources, sourceIndex + 1, onComplete);
            return;
        }

        bool isPilot = effectSource.Data.IsPilot();
        var blocks = new List<TimedEffectData>();
        var onceKeys = new List<int>();
        EffectActivationContext ctx = BuildOnAttackActivationContext(ownerType, blocker);
        for (int i = 0; i < effectSource.Data.timedEffects.Count; i++)
        {
            TimedEffectData timed = effectSource.Data.timedEffects[i];
            if (!timed.IsOnBlockerActivatedResolutionBlock())
            {
                continue;
            }

            int onceKey = isPilot ? BlockerActivatedPilotOncePerTurnBlockBase + i : i;
            if (timed.oncePerTurn && HasUsedPaidActivationThisTurn(ownerType, blocker, onceKey))
            {
                continue;
            }

            if (!CanRunTimedBlockAtChainTime(timed, ctx, "OnBlockerActivated"))
            {
                continue;
            }

            blocks.Add(timed);
            onceKeys.Add(onceKey);
        }

        RunNamedWatchTimedBlocks(
            effectSource,
            ownerType,
            blocks,
            onceKeys,
            0,
            () => RunBlockerActivatedWatchSources(blocker, ownerType, sources, sourceIndex + 1, onComplete),
            oncePerTurnKeyCard: blocker);
    }

    private void RunNamedWatchTimedBlocks(
        CardController sourceCard,
        PlayerType ownerType,
        List<TimedEffectData> blocks,
        List<int> onceKeys,
        int index,
        Action onComplete,
        CardController oncePerTurnKeyCard = null)
    {
        if (blocks == null || index >= blocks.Count)
        {
            onComplete?.Invoke();
            return;
        }

        TimedEffectData timed = blocks[index];
        CardController keyCard = oncePerTurnKeyCard != null ? oncePerTurnKeyCard : sourceCard;
        if (timed != null && timed.oncePerTurn && onceKeys != null && index < onceKeys.Count)
        {
            MarkPaidActivationUsedThisTurn(ownerType, keyCard, onceKeys[index]);
        }

        // パイロットの Activate Self は搭乗ユニット（ブロッカー本体）をアクティブにする
        if (keyCard != null && TimedBlockIsActivateSelfOnly(timed))
        {
            TryActivateUnitAfterBlockerActivated(keyCard);
            RunNamedWatchTimedBlocks(
                sourceCard,
                ownerType,
                blocks,
                onceKeys,
                index + 1,
                onComplete,
                oncePerTurnKeyCard);
            return;
        }

        TryExecuteOnPlayedEffectChain(
            sourceCard,
            ownerType,
            timed != null ? timed.GetResolvedEffects() : null,
            0,
            () => RunNamedWatchTimedBlocks(
                sourceCard,
                ownerType,
                blocks,
                onceKeys,
                index + 1,
                onComplete,
                oncePerTurnKeyCard));
    }

    private static bool TimedBlockIsActivateSelfOnly(TimedEffectData timed)
    {
        IReadOnlyList<EffectData> resolved = timed != null ? timed.GetResolvedEffects() : null;
        if (resolved == null || resolved.Count == 0)
        {
            return false;
        }

        bool any = false;
        for (int i = 0; i < resolved.Count; i++)
        {
            EffectData effect = resolved[i];
            if (effect == null)
            {
                continue;
            }

            if (effect.type != EffectType.Activate || effect.target != TargetType.Self)
            {
                return false;
            }

            any = true;
        }

        return any;
    }

    private void TryActivateUnitAfterBlockerActivated(CardController blocker)
    {
        if (blocker == null || blocker.Data == null || !blocker.Data.IsUnitLike() || blocker.CurrentHp <= 0)
        {
            return;
        }

        if (!IsUnitAliveOnAnyDeployField(blocker) && !IsCardOnBattleZone(blocker))
        {
            return;
        }

        if (!TryApplyActivateToUnit(blocker))
        {
            return;
        }

        Debug.Log(
            $"[OnBlockerActivated] Activate {blocker.Data.cardName}(id:{blocker.Data.id}) "
            + $"instance:{blocker.BattleInstanceId}");
    }

    /// <summary>
    /// ブロック監視後の最終姿勢を攻撃権限側から同期する。
    /// Harry Ord で ACTIVE になった場合は Activate を送り、防御側の戦闘 REST を上書きする。
    /// </summary>
    private void SyncOnlineBlockerStandOrRestAfterWatch(CardController blocker)
    {
        if (!IsOnlineBattle()
            || _applyingRemoteBattleAction
            || currentPlayerType != PlayerType.Player
            || blocker == null
            || blocker.BattleInstanceId <= 0
            || blocker.CurrentHp <= 0)
        {
            return;
        }

        BeginOnlineEffectSyncBatch(PlayerType.Player);
        if (blocker.IsRestState)
        {
            QueueOnlineUnitRest(blocker);
        }
        else
        {
            QueueOnlineUnitActivate(blocker);
        }

        FlushOnlineEffectSyncBatch();
    }
}
