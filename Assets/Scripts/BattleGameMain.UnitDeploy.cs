using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>効果によるユニット配備（トークン／手札／トラッシュ）と OnAttack 非戦闘効果チェーン。</summary>
public partial class BattleGameMain
{
    private CardController _pendingOnAttackPreCombatResolvedAttacker;
    /// <summary>OnAttack のプレイヤー領域ダメージ中はシールド攻撃溢れ防止を無視する。</summary>
    private bool _allowOnAttackEffectShieldAreaDamage;

    /// <summary>
    /// OnAttack の DiscardFromHand が Skip／枚数不足のとき、同攻撃内の ReturnUnitToDeckBottom を抑止する。
    /// </summary>
    private bool _suppressOnAttackReturnToDeckBottomAfterFailedDiscard;
    /// <summary>同一攻撃宣言内で OnAttack 非戦闘効果（GrantAttackFlag 等）を解決済みか。</summary>
    private CardController _onAttackPreCombatCompletedAttacker;

    /// <summary>1攻撃中のトラッシュ→山札返却（バンシー等）の進行フラグ。</summary>
    private struct OnAttackTrashReturnAttackState
    {
        public EntityId AttackerId;
        /// <summary>トラッシュ返却済み（返却成功で ON）。</summary>
        public bool TrashReturned;
        public int ReturnedCount;
        public int BatchSize;
        /// <summary>返却に伴う Activate を適用済み。</summary>
        public bool ActivateFollowUpApplied;
        /// <summary>返却に伴う FirstStrike を適用済み。</summary>
        public bool FirstStrikeFollowUpApplied;
    }

    private OnAttackTrashReturnAttackState _onAttackTrashReturnAttackState;

    /// <summary>同一攻撃セッションでトラッシュ返却 Activate を1回だけ（ClearAttackFlowContext 複数回対策）。</summary>
    private bool _onAttackTrashReturnActivateConsumed;

    private void BeginOnAttackTrashReturnAttackTracking(CardController attacker)
    {
        if (attacker == null)
        {
            return;
        }

        if (IsOnAttackTrashReturnAttackTracking(attacker))
        {
            return;
        }

        _onAttackTrashReturnActivateConsumed = false;
        _onAttackTrashReturnAttackState = new OnAttackTrashReturnAttackState
        {
            AttackerId = attacker.GetEntityId()
        };
    }

    private bool IsOnAttackTrashReturnEffectConsumedForAttack(CardController attacker)
    {
        return IsOnAttackTrashReturnAttackTracking(attacker)
            && _onAttackTrashReturnAttackState.TrashReturned;
    }

    private static bool TimedBlockContainsReturnFromTrash(TimedEffectData timed)
    {
        if (timed == null || !timed.HasResolvedEffects())
        {
            return false;
        }

        IReadOnlyList<EffectData> effects = timed.GetResolvedEffects();
        for (int i = 0; i < effects.Count; i++)
        {
            if (effects[i]?.type == EffectType.ReturnFromTrashToDeckAndShuffle)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsOnAttackPreCombatSourceSelectable(
        CardController attacker,
        PlayerType attackerOwner,
        CardController source,
        List<TimedEffectData> blocks)
    {
        if (attacker == null || source == null || blocks == null || blocks.Count == 0)
        {
            return false;
        }

        if (IsOnAttackTrashReturnEffectConsumedForAttack(attacker))
        {
            for (int i = 0; i < blocks.Count; i++)
            {
                if (TimedBlockContainsReturnFromTrash(blocks[i]))
                {
                    return false;
                }
            }
        }

        for (int i = 0; i < blocks.Count; i++)
        {
            TimedEffectData timed = blocks[i];
            if (timed == null)
            {
                continue;
            }

            if (TimedBlockContainsReturnFromTrash(timed))
            {
                if (ShouldOfferOnAttackReturnFromTrashBlock(attacker, attackerOwner, timed))
                {
                    return true;
                }

                continue;
            }

            return true;
        }

        return false;
    }

    private void ClearOnAttackTrashReturnAttackTracking()
    {
        _onAttackTrashReturnAttackState = default;
    }

    /// <summary>トラッシュ返却フォローアップ（Activate 等）とチェーン観測の返却枚数をリセット。</summary>
    private void ClearOnAttackTrashReturnFollowUpState()
    {
        ClearOnAttackTrashReturnAttackTracking();
        SetEffectChainLastReturnFromTrashCount(0);
        SetEffectChainReturnFromTrashBatchSize(0);
    }

    /// <summary>攻撃セッション終了／キャンセル時。次の攻撃でトラッシュ返却フェーズから再開。</summary>
    private void ResetOnAttackTrashReturnSession()
    {
        _onAttackTrashReturnActivateConsumed = false;
        ClearOnAttackTrashReturnFollowUpState();
        ResetOnAttackPreCombatEffectsAppliedGuard();
        ClearOnAttackPreCombatCompletedForNewAttack();
        ClearOnAttackPreCombatResolvedState();
    }

    private bool IsOnAttackTrashReturnChainSelfActivateBlocked(CardController unit, EffectData effect)
    {
        if (effect == null
            || effect.type != EffectType.Activate
            || effect.target != TargetType.Self
            || unit == null)
        {
            return false;
        }

        if (_onAttackTrashReturnActivateConsumed)
        {
            return true;
        }

        return IsOnAttackTrashReturnAttackTracking(unit);
    }

    private bool IsOnAttackTrashReturnAttackTracking(CardController attacker)
    {
        return attacker != null
            && _onAttackTrashReturnAttackState.AttackerId != default
            && _onAttackTrashReturnAttackState.AttackerId == attacker.GetEntityId();
    }

    private void MarkOnAttackTrashReturnedForCurrentAttack(int returnedCount, int batchSize)
    {
        if (returnedCount <= 0 || batchSize <= 0 || _onAttackTrashReturnAttackState.AttackerId == default)
        {
            return;
        }

        _onAttackTrashReturnAttackState.TrashReturned = true;
        _onAttackTrashReturnAttackState.ReturnedCount = returnedCount;
        _onAttackTrashReturnAttackState.BatchSize = batchSize;
        SetEffectChainLastReturnFromTrashCount(returnedCount);
        SetEffectChainReturnFromTrashBatchSize(batchSize);
    }

    /// <summary>アタック時効果選択で ReturnFromTrash 付きブロックを出してよいか（未返却かつ候補十分）。</summary>
    private bool ShouldOfferOnAttackReturnFromTrashBlock(
        CardController attacker,
        PlayerType attackerOwner,
        TimedEffectData timed)
    {
        if (attacker == null || timed == null || !timed.HasResolvedEffects())
        {
            return false;
        }

        if (IsOnAttackTrashReturnAttackTracking(attacker)
            && _onAttackTrashReturnAttackState.TrashReturned)
        {
            return false;
        }

        IReadOnlyList<EffectData> effects = timed.GetResolvedEffects();
        for (int i = 0; i < effects.Count; i++)
        {
            EffectData effect = effects[i];
            if (effect == null || effect.type != EffectType.ReturnFromTrashToDeckAndShuffle)
            {
                continue;
            }

            int batchSize = effect.value > 0 ? effect.value : 1;
            CardController source = ResolveOnAttackBlockSource(attacker, attackerOwner, timed) ?? attacker;
            CardGameRule trashRule = ResolveTrashRuleForEffect(attackerOwner, effect);
            if (trashRule == null)
            {
                return false;
            }

            List<TrashExileCandidate> candidates = CollectTrashExileCandidates(trashRule, effect);
            return candidates.Count >= batchSize;
        }

        return true;
    }

    private bool TryApplyOnAttackTrashReturnFollowUpEffect(
        CardController sourceCard,
        PlayerType ownerType,
        EffectData effect)
    {
        if (effect == null
            || (effect.type != EffectType.Activate && effect.type != EffectType.FirstStrike)
            || effect.target != TargetType.Self)
        {
            return false;
        }

        CardController attackHost = _pendingOnAttackPreCombatResolvedAttacker ?? sourceCard;
        if (!IsOnAttackTrashReturnAttackTracking(attackHost))
        {
            return false;
        }

        if (!_onAttackTrashReturnAttackState.TrashReturned)
        {
            return true;
        }

        int batchSize = _onAttackTrashReturnAttackState.BatchSize;
        int returned = _onAttackTrashReturnAttackState.ReturnedCount;
        if (batchSize <= 0 || returned < batchSize)
        {
            return true;
        }

        if (effect.type == EffectType.Activate)
        {
            // アクティブ化はトラッシュ返却チェーンでは行わず、攻撃フロー完了時に適用する。
            return true;
        }

        if (_onAttackTrashReturnAttackState.FirstStrikeFollowUpApplied)
        {
            return true;
        }

        ApplyEffect(sourceCard, ownerType, effect);
        _onAttackTrashReturnAttackState.FirstStrikeFollowUpApplied = true;
        return true;
    }

    /// <summary>トラッシュ返却済みなら攻撃フロー完了時に REST→ACTIVE を1回だけ適用する。</summary>
    private void ApplyDeferredOnAttackTrashReturnActivate()
    {
        if (_onAttackTrashReturnActivateConsumed)
        {
            return;
        }

        OnAttackTrashReturnAttackState state = _onAttackTrashReturnAttackState;
        if (state.AttackerId == default
            || !state.TrashReturned
            || state.ActivateFollowUpApplied)
        {
            return;
        }

        int batchSize = state.BatchSize;
        int returned = state.ReturnedCount;
        if (batchSize <= 0 || returned < batchSize)
        {
            return;
        }

        _onAttackTrashReturnActivateConsumed = true;
        _onAttackTrashReturnAttackState.ActivateFollowUpApplied = true;

        CardController attacker = ResolveOnAttackTrashReturnAttackerUnit(state.AttackerId);
        if (attacker != null && attacker.CurrentHp > 0)
        {
            if (TryApplyActivateToUnit(attacker))
            {
                QueueOnlineUnitActivate(attacker);
                SyncAllResourceViewsFromRule();
                Debug.Log(
                    "[OnAttackTrashReturn] Deferred Activate at attack end (once) "
                    + $"attacker:{attacker.Data?.cardName}(id:{attacker.Data?.id}) returned:{returned} batch:{batchSize}");
            }
        }

        ClearOnAttackTrashReturnFollowUpState();
    }

    private CardController ResolveOnAttackTrashReturnAttackerUnit(EntityId attackerEntityId)
    {
        if (attackerEntityId == default)
        {
            return null;
        }

        if (IsCardControllerInstanceValid(attackFlowAttackerUnit)
            && attackFlowAttackerUnit.GetEntityId() == attackerEntityId)
        {
            return attackFlowAttackerUnit;
        }

        CardController fromPlayer = FindBattleUnitByEntityId(playerBattleZoneCards, attackerEntityId);
        if (fromPlayer != null)
        {
            return fromPlayer;
        }

        return FindBattleUnitByEntityId(enemyBattleZoneCards, attackerEntityId);
    }

    private static CardController FindBattleUnitByEntityId(List<CardController> units, EntityId entityId)
    {
        if (units == null || entityId == default)
        {
            return null;
        }

        for (int i = 0; i < units.Count; i++)
        {
            CardController unit = units[i];
            if (unit != null && unit.CurrentHp > 0 && unit.GetEntityId() == entityId)
            {
                return unit;
            }
        }

        return null;
    }

    private bool IsPendingOnAttackEffectResolvedForAttacker(CardController attacker)
    {
        if (attacker == null || pendingOnAttackEffectResolvedAttacker == null)
        {
            return false;
        }

        return IsSameBattleUnit(attacker, pendingOnAttackEffectResolvedAttacker);
    }

    private PlayerType ResolveDeployRecipientPlayerType(PlayerType sourceOwner, EffectData effect)
    {
        if (effect != null && effect.target == TargetType.EnemyPlayer)
        {
            return sourceOwner == PlayerType.Player ? PlayerType.Enemy : PlayerType.Player;
        }

        return sourceOwner;
    }

    private CardGameRule ResolveDeployRecipientRule(PlayerType recipient)
    {
        return recipient == PlayerType.Player ? cardGameRule : enemyCardGameRule;
    }

    private List<CardController> CollectHandDeployCandidates(PlayerType owner, EffectData effect)
    {
        List<CardController> result = new List<CardController>();
        CardGameRule rule = owner == PlayerType.Player ? cardGameRule : enemyCardGameRule;
        if (rule?.HandScrollContent == null || effect == null)
        {
            return result;
        }

        for (int i = 0; i < rule.HandScrollContent.childCount; i++)
        {
            CardController cc = rule.HandScrollContent.GetChild(i).GetComponent<CardController>();
            if (cc == null || cc.Data == null || !effect.MatchesDeployCandidateFilter(cc.Data))
            {
                continue;
            }

            // LV 等の実効ステータス条件（手札コントローラ基準）
            if (effect.HasTargetUnitFilter() && !effect.MatchesTargetUnitFilter(cc, null))
            {
                continue;
            }

            result.Add(cc);
        }

        return result;
    }

    private static string FormatDeployUnitFromHandSelectionTitle(EffectData effect)
    {
        if (effect == null)
        {
            return GameLocale.T("手札から配備するユニットを選択", "Choose a Unit from hand to deploy");
        }

        string detail = effect.FormatTargetUnitFilterDescription();
        if (!string.IsNullOrEmpty(detail))
        {
            return GameLocale.T($"手札から配備（{detail}）", $"Deploy from hand ({detail})");
        }

        return GameLocale.T("手札から配備するユニットを選択", "Choose a Unit from hand to deploy");
    }

    private List<TrashExileCandidate> CollectTrashDeployCandidates(CardGameRule trashRule, EffectData effect)
    {
        List<TrashExileCandidate> result = new List<TrashExileCandidate>();
        if (trashRule == null || effect == null)
        {
            return result;
        }

        IReadOnlyList<int> trashIds = trashRule.GetTrashCardIds();
        for (int i = 0; i < trashIds.Count; i++)
        {
            int cardId = trashIds[i];
            CardData data = DeckSettinObject.Instance.GetCardDataById(cardId);
            if (!effect.MatchesDeployCandidateFilter(data))
            {
                continue;
            }

            result.Add(new TrashExileCandidate(i, cardId, data));
        }

        return result;
    }

    private CardController InstantiateBattleUnit(CardData data, Transform parent)
    {
        if (data == null || CardImagePrefab == null || parent == null)
        {
            return null;
        }

        GameObject go = Instantiate(CardImagePrefab, parent);
        CardController cc = go.GetComponent<CardController>();
        if (cc != null)
        {
            cc.SetUp(data, OnCardClicked);
        }

        return cc;
    }

    /// <summary>バトルゾーンへ配備（手札／トラッシュ／トークン共通のフィールド反映）。</summary>
    /// <param name="bypassBattleZoneCap">true のとき 6 体上限チェックをスキップ（枠確保済み／リモート適用向け）。</param>
    private bool DeployUnitToBattleZone(
        CardController unit,
        PlayerType recipient,
        CardGameRule rule,
        bool triggerOnPlayed,
        bool fromHand,
        bool deployAsRested = false,
        bool fromTrash = false,
        bool bypassBattleZoneCap = false)
    {
        if (unit == null || unit.Data == null || !unit.Data.IsUnitLike() || rule == null)
        {
            return false;
        }

        if (!bypassBattleZoneCap
            && !_applyingRemoteBattleAction
            && IsBattleZoneAtCapacity(recipient))
        {
            Debug.LogWarning(
                $"[DeployUnit] バトルゾーン満杯のため配備拒否: {unit.Data.cardName} → {recipient}");
            return false;
        }

        unit.transform.SetParent(rule.PlayerDeployPanel, false);

        if (!rule.TryPlaceUnitInBattleZone(unit))
        {
            Debug.LogWarning(
                $"[DeployUnit] バトル枠への配置に失敗: {unit.Data.cardName} → {recipient}");
            return false;
        }

        if (recipient == PlayerType.Player)
        {
            if (fromHand)
            {
                playerHandCards.Remove(unit.Data);
                NotifyLocalPlayerHandDeckSnapshotAfterHandListChange();
            }

            if (!playerBattleZoneCards.Contains(unit))
            {
                playerBattleZoneCards.Add(unit);
            }
        }
        else
        {
            if (fromHand)
            {
                enemyHandCards.Remove(unit.Data);
            }

            if (!enemyBattleZoneCards.Contains(unit))
            {
                enemyBattleZoneCards.Add(unit);
            }
        }

        unit.SetEligibleForShieldZoneDeploy(false);
        unit.ResetRuntimeStatsFromData();
        unit.SetDeployedFromTrash(fromTrash);
        ApplyUnitDeployFieldAttackState(unit);
        AssignBattleInstanceIdIfNeeded(unit);
        ApplyPilotMountFieldAurasToDeployedUnit(unit, recipient);
        // トークン等の効果配置直後にも、ガンダム等の自ターン盤面バフをかけ直す
        RefreshAllFieldOwnerTurnPassives();

        if (triggerOnPlayed)
        {
            TriggerOnPlayedEffects(
                unit,
                recipient,
                () =>
                {
                    NotifyAllyUnitDeployed(recipient, unit, RefreshAllHandsConditionalOnHandAuto);
                });
        }
        else
        {
            NotifyAllyUnitDeployed(recipient, unit, null);
        }

        if (IsOnlineBattle() && !_applyingRemoteBattleAction)
        {
            // 破壊時トークン配備などは相手ターン中でも PlayCard 同期する
            bool allowOffTurn = IsResolvingBurstEffect
                || unit.IsTemporaryBurstBattleUnit
                || IsResolvingLocalOwnerOnDestroyedEffects();
            if (allowOffTurn || currentPlayerType == PlayerType.Player)
            {
                // REST は PlayCard.extras.deployAsRested で同期（EffectSync Rest の到着順レースを避ける）
                NotifyLocalPlayCardDeployed(
                    unit,
                    recipient,
                    allowOffTurnDeploy: allowOffTurn,
                    deployAsRested: deployAsRested);
            }
        }

        if (deployAsRested)
        {
            ApplyDeployedUnitRestedState(unit);
        }

        Debug.Log(
            $"[DeployUnit] {unit.Data.cardName}(id:{unit.Data.id}) → {recipient} battle zone "
            + $"(triggerOnPlayed:{triggerOnPlayed} rested:{deployAsRested})");
        return true;
    }

    /// <summary>配備直後に REST（アタック不可）にする。</summary>
    private static void ApplyDeployedUnitRestedState(CardController unit)
    {
        if (unit == null || unit.Data == null || !unit.Data.IsUnitLike())
        {
            return;
        }

        unit.SetUnitRestVisual(true);
        unit.SetAttackFlg(AttackFlg.False);
    }

    private bool TryDeployTokenUnit(
        EffectData effect,
        int resolvedMagnitude,
        PlayerType sourceOwner,
        CardController sourceCard)
    {
        // 同期フォールバック（枠が空いているときのみ）。満杯時は CoDeployTokenUnitsWithCap を使う。
        if (effect == null || effect.deployUnitSource != DeployUnitSource.Token)
        {
            return false;
        }

        PlayerType recipient = ResolveDeployRecipientPlayerType(sourceOwner, effect);
        if (IsBattleZoneAtCapacity(recipient))
        {
            return false;
        }

        return TryDeployTokenUnitImmediate(effect, resolvedMagnitude, sourceOwner, sourceCard, recipient);
    }

    private bool TryDeployTokenUnitImmediate(
        EffectData effect,
        int resolvedMagnitude,
        PlayerType sourceOwner,
        CardController sourceCard,
        PlayerType recipient)
    {
        if (effect == null || effect.deployUnitSource != DeployUnitSource.Token)
        {
            return false;
        }

        int cardId = effect.deployCardId;
        if (cardId <= 0 && sourceCard?.Data != null && sourceCard.Data.type == Type.UnitToken)
        {
            cardId = sourceCard.Data.id;
        }

        if (cardId <= 0)
        {
            Debug.LogWarning($"[DeployUnit] Token deploy skipped: deployCardId unset (source:{sourceCard?.Data?.id})");
            return false;
        }

        CardData tokenData = DeckSettinObject.Instance.GetCardDataById(cardId);
        if (tokenData == null || !tokenData.IsUnitLike())
        {
            Debug.LogWarning($"[DeployUnit] Unknown or non-unit token id:{cardId}");
            return false;
        }

        CardGameRule rule = ResolveDeployRecipientRule(recipient);
        if (rule?.PlayerDeployPanel == null)
        {
            return false;
        }

        int deployCount = effect.GetDeployUnitCount(resolvedMagnitude);
        int applied = 0;
        BeginOnlineEffectSyncBatch(sourceOwner);
        for (int i = 0; i < deployCount; i++)
        {
            if (IsBattleZoneAtCapacity(recipient))
            {
                break;
            }

            CardController spawned = InstantiateBattleUnit(tokenData, rule.PlayerDeployPanel);
            if (spawned == null)
            {
                break;
            }

            if (DeployUnitToBattleZone(
                    spawned,
                    recipient,
                    rule,
                    effect.deployUnitTriggerOnPlayed,
                    fromHand: false,
                    deployAsRested: effect.deployUnitAsRested,
                    bypassBattleZoneCap: true))
            {
                applied++;
            }
            else
            {
                Destroy(spawned.gameObject);
            }
        }

        FlushOnlineEffectSyncBatch();
        if (applied > 0)
        {
            RefreshSyncTurnEndRepairBonusesForSide(recipient);
        }

        Debug.Log(
            $"[Effect] DeployUnit Token x{applied}/{deployCount} id:{cardId} target:{effect.target} "
            + $"by cardId:{sourceCard?.Data?.id}");
        return applied > 0;
    }

    /// <summary>満杯時は置換 UI を挟み、Cancel ならそのトークン以降を出さない。</summary>
    private IEnumerator CoDeployTokenUnitsWithCap(
        EffectData effect,
        int resolvedMagnitude,
        PlayerType sourceOwner,
        CardController sourceCard,
        Action onComplete)
    {
        if (effect == null || effect.deployUnitSource != DeployUnitSource.Token)
        {
            onComplete?.Invoke();
            yield break;
        }

        int cardId = effect.deployCardId;
        if (cardId <= 0 && sourceCard?.Data != null && sourceCard.Data.type == Type.UnitToken)
        {
            cardId = sourceCard.Data.id;
        }

        if (cardId <= 0)
        {
            Debug.LogWarning($"[DeployUnit] Token deploy skipped: deployCardId unset (source:{sourceCard?.Data?.id})");
            onComplete?.Invoke();
            yield break;
        }

        CardData tokenData = DeckSettinObject.Instance.GetCardDataById(cardId);
        if (tokenData == null || !tokenData.IsUnitLike())
        {
            Debug.LogWarning($"[DeployUnit] Unknown or non-unit token id:{cardId}");
            onComplete?.Invoke();
            yield break;
        }

        PlayerType recipient = ResolveDeployRecipientPlayerType(sourceOwner, effect);
        CardGameRule rule = ResolveDeployRecipientRule(recipient);
        if (rule?.PlayerDeployPanel == null)
        {
            onComplete?.Invoke();
            yield break;
        }

        int deployCount = effect.GetDeployUnitCount(resolvedMagnitude);
        int applied = 0;
        BeginOnlineEffectSyncBatch(sourceOwner);
        for (int i = 0; i < deployCount; i++)
        {
            bool slotReady = false;
            bool cancelled = false;
            yield return CoEnsureBattleZoneDeploySlot(
                recipient,
                sourceCard != null ? sourceCard : null,
                () => slotReady = true,
                () => cancelled = true);

            if (cancelled || !slotReady)
            {
                Debug.Log(
                    $"[DeployUnit] Token deploy cancelled at {i + 1}/{deployCount} "
                    + $"(battle zone cap) by cardId:{sourceCard?.Data?.id}");
                break;
            }

            // プレビュー用に一時スポーンせず、枠確保後に生成
            CardController spawned = InstantiateBattleUnit(tokenData, rule.PlayerDeployPanel);
            if (spawned == null)
            {
                break;
            }

            if (DeployUnitToBattleZone(
                    spawned,
                    recipient,
                    rule,
                    effect.deployUnitTriggerOnPlayed,
                    fromHand: false,
                    deployAsRested: effect.deployUnitAsRested,
                    bypassBattleZoneCap: true))
            {
                applied++;
            }
            else
            {
                Destroy(spawned.gameObject);
            }
        }

        FlushOnlineEffectSyncBatch();
        if (applied > 0)
        {
            RefreshSyncTurnEndRepairBonusesForSide(recipient);
        }

        SyncAllResourceViewsFromRule();
        Debug.Log(
            $"[Effect] DeployUnit Token x{applied}/{deployCount} id:{cardId} target:{effect.target} "
            + $"by cardId:{sourceCard?.Data?.id}");
        onComplete?.Invoke();
    }

    private bool TryDeployUnitFromTrashIndex(
        CardGameRule trashRule,
        PlayerType trashOwner,
        PlayerType recipient,
        int trashIndex,
        CardData data,
        bool triggerOnPlayed,
        bool payCost = false,
        PlayerType payCostOwner = PlayerType.Player)
    {
        if (trashRule == null)
        {
            return false;
        }

        IReadOnlyList<int> trashIds = trashRule.GetTrashCardIds();
        if (trashIndex < 0 || trashIndex >= trashIds.Count)
        {
            return false;
        }

        CardData resolved = data ?? DeckSettinObject.Instance.GetCardDataById(trashIds[trashIndex]);
        if (resolved == null || !resolved.IsUnitLike())
        {
            return false;
        }

        if (payCost && !TryPayCardDataDeployCost(payCostOwner, resolved))
        {
            return false;
        }

        int removedId = -1;
        WithZoneSyncSuppressed(() =>
        {
            if (!trashRule.TryRemoveCardFromTrashAt(trashIndex, out removedId))
            {
                removedId = -1;
            }
        });

        if (removedId < 0)
        {
            return false;
        }

        CardGameRule deployRule = ResolveDeployRecipientRule(recipient);
        CardController spawned = InstantiateBattleUnit(resolved, deployRule.PlayerDeployPanel);
        if (spawned == null)
        {
            trashRule.AddCardToTrash(removedId);
            return false;
        }

        return DeployUnitToBattleZone(
            spawned,
            recipient,
            deployRule,
            triggerOnPlayed,
            fromHand: false,
            fromTrash: true,
            bypassBattleZoneCap: true);
    }

    private IEnumerator CoTryDeployUnitFromTrashIndexWithCap(
        CardController sourcePreview,
        CardGameRule trashRule,
        PlayerType trashOwner,
        PlayerType recipient,
        int trashIndex,
        CardData data,
        bool triggerOnPlayed,
        bool payCost,
        PlayerType payCostOwner,
        Action<bool> onDone)
    {
        bool slotReady = false;
        bool cancelled = false;
        yield return CoEnsureBattleZoneDeploySlot(
            recipient,
            sourcePreview,
            () => slotReady = true,
            () => cancelled = true);
        if (cancelled || !slotReady)
        {
            onDone?.Invoke(false);
            yield break;
        }

        bool ok = TryDeployUnitFromTrashIndex(
            trashRule,
            trashOwner,
            recipient,
            trashIndex,
            data,
            triggerOnPlayed,
            payCost,
            payCostOwner);
        onDone?.Invoke(ok);
    }

    private void ApplyDeployUnitFromTrashAuto(
        CardGameRule trashRule,
        PlayerType trashOwner,
        PlayerType recipient,
        List<TrashExileCandidate> candidates,
        int pickCount,
        bool triggerOnPlayed,
        bool payCost,
        PlayerType payCostOwner)
    {
        List<TrashExileCandidate> ordered = new List<TrashExileCandidate>(candidates);
        ordered.Sort((a, b) => b.TrashIndex.CompareTo(a.TrashIndex));
        int deployed = 0;
        for (int i = 0; i < ordered.Count && deployed < pickCount; i++)
        {
            if (IsBattleZoneAtCapacity(recipient))
            {
                break;
            }

            TrashExileCandidate candidate = ordered[i];
            if (TryDeployUnitFromTrashIndex(
                trashRule,
                trashOwner,
                recipient,
                candidate.TrashIndex,
                candidate.Data,
                triggerOnPlayed,
                payCost,
                payCostOwner))
            {
                deployed++;
            }
        }
    }

    private IEnumerator CoApplyDeployUnitFromTrashAutoWithCap(
        CardController sourcePreview,
        CardGameRule trashRule,
        PlayerType trashOwner,
        PlayerType recipient,
        List<TrashExileCandidate> candidates,
        int pickCount,
        bool triggerOnPlayed,
        bool payCost,
        PlayerType payCostOwner,
        Action onComplete)
    {
        List<TrashExileCandidate> ordered = new List<TrashExileCandidate>(candidates);
        ordered.Sort((a, b) => b.TrashIndex.CompareTo(a.TrashIndex));
        int deployed = 0;
        for (int i = 0; i < ordered.Count && deployed < pickCount; i++)
        {
            TrashExileCandidate candidate = ordered[i];
            bool ok = false;
            yield return CoTryDeployUnitFromTrashIndexWithCap(
                sourcePreview,
                trashRule,
                trashOwner,
                recipient,
                candidate.TrashIndex,
                candidate.Data,
                triggerOnPlayed,
                payCost,
                payCostOwner,
                result => ok = result);
            if (!ok)
            {
                break;
            }

            deployed++;
        }

        onComplete?.Invoke();
    }

    private void ApplyDeployUnitEffect(
        CardController sourceCard,
        PlayerType ownerType,
        EffectData effect,
        Action onComplete = null)
    {
        if (effect == null || effect.type != EffectType.DeployUnit)
        {
            onComplete?.Invoke();
            return;
        }

        int magnitude = ResolveEffectMagnitude(effect, ownerType, sourceCard);
        int deployCount = effect.GetDeployUnitCount(magnitude);
        if (deployCount <= 0)
        {
            onComplete?.Invoke();
            return;
        }

        switch (effect.deployUnitSource)
        {
            case DeployUnitSource.Token:
                StartCoroutine(CoDeployTokenUnitsWithCap(
                    effect,
                    magnitude,
                    ownerType,
                    sourceCard,
                    () =>
                    {
                        SyncAllResourceViewsFromRule();
                        onComplete?.Invoke();
                    }));
                return;

            case DeployUnitSource.Hand:
                ApplyDeployUnitFromHandEffect(sourceCard, ownerType, effect, deployCount, onComplete);
                return;

            case DeployUnitSource.Trash:
                ApplyDeployUnitFromTrashEffect(sourceCard, ownerType, effect, deployCount, onComplete);
                return;

            default:
                onComplete?.Invoke();
                return;
        }
    }

    private void ApplyDeployUnitFromHandEffect(
        CardController sourceCard,
        PlayerType ownerType,
        EffectData effect,
        int deployCount,
        Action onComplete)
    {
        PlayerType recipient = ResolveDeployRecipientPlayerType(ownerType, effect);
        List<CardController> candidates = CollectHandDeployCandidates(recipient, effect);
        if (candidates.Count == 0)
        {
            onComplete?.Invoke();
            return;
        }

        if (ownerType == PlayerType.Enemy)
        {
            StartCoroutine(CoDeployUnitsFromHandWithCap(
                sourceCard,
                ownerType,
                recipient,
                effect,
                candidates,
                deployCount,
                onComplete));
            return;
        }

        if (!effect.RequiresDeployUnitZoneSelection() && candidates.Count == 1)
        {
            StartCoroutine(CoDeployUnitsFromHandWithCap(
                sourceCard,
                ownerType,
                recipient,
                effect,
                new List<CardController> { candidates[0] },
                1,
                onComplete));
            return;
        }

        StartCoroutine(ShowDeployUnitFromHandSelectionCoroutine(
            sourceCard,
            ownerType,
            recipient,
            effect,
            candidates,
            deployCount,
            onComplete));
    }

    private IEnumerator CoDeployUnitsFromHandWithCap(
        CardController sourceCard,
        PlayerType ownerType,
        PlayerType recipient,
        EffectData effect,
        List<CardController> orderedCandidates,
        int deployCount,
        Action onComplete)
    {
        if (orderedCandidates == null || orderedCandidates.Count == 0 || effect == null)
        {
            onComplete?.Invoke();
            yield break;
        }

        CardGameRule rule = ResolveDeployRecipientRule(recipient);
        int deployed = 0;
        BeginOnlineEffectSyncBatch(ownerType);
        for (int i = 0; i < orderedCandidates.Count && deployed < deployCount; i++)
        {
            CardController pick = orderedCandidates[i];
            if (pick == null)
            {
                continue;
            }

            bool slotReady = false;
            bool cancelled = false;
            yield return CoEnsureBattleZoneDeploySlot(
                recipient,
                pick,
                () => slotReady = true,
                () => cancelled = true);
            if (cancelled || !slotReady)
            {
                break;
            }

            if (DeployUnitToBattleZone(
                    pick,
                    recipient,
                    rule,
                    effect.deployUnitTriggerOnPlayed,
                    fromHand: true,
                    bypassBattleZoneCap: true))
            {
                deployed++;
            }
        }

        FlushOnlineEffectSyncBatch();
        SyncAllResourceViewsFromRule();
        onComplete?.Invoke();
    }

    private void ApplyDeployUnitFromTrashEffect(
        CardController sourceCard,
        PlayerType ownerType,
        EffectData effect,
        int deployCount,
        Action onComplete)
    {
        CardGameRule trashRule = ResolveTrashRuleForEffect(ownerType, effect);
        if (trashRule == null)
        {
            onComplete?.Invoke();
            return;
        }

        PlayerType trashOwner = effect.target == TargetType.EnemyPlayer
            ? (ownerType == PlayerType.Player ? PlayerType.Enemy : PlayerType.Player)
            : ownerType;
        PlayerType recipient = ResolveDeployRecipientPlayerType(ownerType, effect);
        List<TrashExileCandidate> candidates = CollectTrashDeployCandidates(trashRule, effect);
        if (effect.deployUnitPayCost)
        {
            candidates = FilterAffordableTrashDeployCandidates(ownerType, candidates);
        }

        if (candidates.Count == 0)
        {
            onComplete?.Invoke();
            return;
        }

        int pickCount = Mathf.Min(deployCount, candidates.Count);
        bool payCost = effect.deployUnitPayCost;
        if (ownerType == PlayerType.Enemy)
        {
            BeginOnlineEffectSyncBatch(ownerType);
            StartCoroutine(CoApplyDeployUnitFromTrashAutoWithCap(
                sourceCard,
                trashRule,
                trashOwner,
                recipient,
                candidates,
                pickCount,
                effect.deployUnitTriggerOnPlayed,
                payCost,
                ownerType,
                () =>
                {
                    FlushOnlineEffectSyncBatch();
                    SyncAllResourceViewsFromRule();
                    InvokeAfterOnlineDeployConfirmIfNeeded(onComplete);
                }));
            return;
        }

        void BeginTrashSelection()
        {
            // 「してもよい」は候補1枚でも選択／スキップ UI を出す（自動配備しない）
            if (!effect.optionalPlayerConfirm && candidates.Count == 1 && pickCount == 1)
            {
                BeginOnlineEffectSyncBatch(ownerType);
                StartCoroutine(CoTryDeployUnitFromTrashIndexWithCap(
                    sourceCard,
                    trashRule,
                    trashOwner,
                    recipient,
                    candidates[0].TrashIndex,
                    candidates[0].Data,
                    effect.deployUnitTriggerOnPlayed,
                    payCost,
                    ownerType,
                    _ =>
                    {
                        FlushOnlineEffectSyncBatch();
                        SyncAllResourceViewsFromRule();
                        InvokeAfterOnlineDeployConfirmIfNeeded(onComplete);
                    }));
                return;
            }

            StartCoroutine(ShowDeployUnitFromTrashSelectionCoroutine(
                sourceCard,
                trashRule,
                ownerType,
                trashOwner,
                recipient,
                effect,
                candidates,
                pickCount,
                () => InvokeAfterOnlineDeployConfirmIfNeeded(onComplete)));
        }

        if (effect.optionalPlayerConfirm)
        {
            TryBeginOptionalConfirmedEffect(
                sourceCard,
                ownerType,
                effect,
                onAccepted: BeginTrashSelection,
                onDeclined: () => onComplete?.Invoke());
            return;
        }

        BeginTrashSelection();
    }

    /// <summary>
    /// オンライン配備の「カード確認待ち」が終わるまで onComplete を遅延する。
    /// OnAttack → アクションステップへ進む前に確認を完了させ、フリーズを防ぐ。
    /// </summary>
    private void InvokeAfterOnlineDeployConfirmIfNeeded(Action onComplete)
    {
        if (onComplete == null)
        {
            return;
        }

        if (!IsOnlineBattle()
            || _applyingRemoteBattleAction
            || (!isOnlineOpponentCardConfirmWaitOpen
                && _pendingCommandPlayRevealRequestId <= 0))
        {
            onComplete.Invoke();
            return;
        }

        StartCoroutine(CoInvokeAfterOnlineDeployConfirm(onComplete));
    }

    private IEnumerator CoInvokeAfterOnlineDeployConfirm(Action onComplete)
    {
        int requestId = _pendingCommandPlayRevealRequestId;
        const float timeoutSeconds = 45f;
        float deadline = Time.realtimeSinceStartup + timeoutSeconds;
        while (Time.realtimeSinceStartup < deadline)
        {
            bool stillWaiting = isOnlineOpponentCardConfirmWaitOpen
                || (requestId > 0
                    && _pendingCommandPlayRevealRequestId == requestId
                    && !_commandPlayRevealRemoteCompleteReceived);
            if (!stillWaiting)
            {
                break;
            }

            yield return null;
        }

        if (isOnlineOpponentCardConfirmWaitOpen
            || (requestId > 0
                && _pendingCommandPlayRevealRequestId == requestId
                && !_commandPlayRevealRemoteCompleteReceived))
        {
            Debug.LogWarning(
                $"[DeployUnit] Opponent card confirm timeout ({timeoutSeconds}s) — continue attack flow. "
                + $"requestId:{requestId}");
            CloseOnlineOpponentCardConfirmWaitOverlay();
            ClearPendingOpponentCardConfirmRequest();
        }

        onComplete?.Invoke();
    }

    private List<TrashExileCandidate> FilterAffordableTrashDeployCandidates(
        PlayerType ownerType,
        List<TrashExileCandidate> candidates)
    {
        List<TrashExileCandidate> result = new List<TrashExileCandidate>();
        if (candidates == null)
        {
            return result;
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            TrashExileCandidate c = candidates[i];
            if (c.Data != null && CanAffordCardDataDeployCost(ownerType, c.Data))
            {
                result.Add(c);
            }
        }

        return result;
    }

    private IEnumerator ShowDeployUnitFromHandSelectionCoroutine(
        CardController sourceCard,
        PlayerType ownerType,
        PlayerType recipient,
        EffectData effect,
        List<CardController> candidates,
        int deployCount,
        Action onComplete)
    {
        if (candidates == null || candidates.Count == 0 || CardImagePrefab == null)
        {
            onComplete?.Invoke();
            yield break;
        }

        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            onComplete?.Invoke();
            yield break;
        }

        bool resolved = false;
        DestroyActiveOnActionPopupIfAny();
        GameObject root = new GameObject(
            "DeployUnitFromHandSelect",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        activeOnActionPopupRoot = root;
        isOnActionPopupOpen = true;
        root.transform.SetParent(canvas.transform, false);
        root.transform.SetAsLastSibling();
        root.SetFullSize();
        Image dim = root.GetComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.62f);
        dim.raycastTarget = true;

        TextMeshProUGUI title = root.CreateChildTextCustom("DeployHandTitle", UIAnchor.TopCenter, 760, 48);
        title.text = FormatDeployUnitFromHandSelectionTitle(effect);
        title.fontSize = 26;
        title.fontStyle = FontStyles.Bold;
        title.color = Color.white;
        title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -18f);

        GameObject scrollGo = root.CreateGridScrollView(560, 360, UIAnchor.TopCenter);
        RectTransform scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.anchoredPosition = new Vector2(0f, -72f);
        scrollGo.ConfigureGridCellFromViewportHeight(0.75f, 56f);
        ScrollRect sr = scrollGo.GetComponent<ScrollRect>();
        RectTransform content = sr != null ? sr.content : null;

        if (content != null)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                CardController candidate = candidates[i];
                if (candidate?.Data == null)
                {
                    continue;
                }

                GameObject go = Instantiate(CardImagePrefab, content);
                CardController display = go.GetComponent<CardController>();
                if (display != null)
                {
                    display.SetUp(candidate.Data, _ => { });
                    go.transform.localScale = new Vector3(0.4f, 0.4f, 1f);
                }

                Button pickBtn = go.GetComponent<Button>();
                if (pickBtn == null)
                {
                    pickBtn = go.AddComponent<Button>();
                }

                CardController pickedRef = candidate;
                pickBtn.onClick.AddListener(() =>
                {
                    if (resolved)
                    {
                        return;
                    }

                    resolved = true;
                    Destroy(root);
                    activeOnActionPopupRoot = null;
                    isOnActionPopupOpen = false;

                    EnsureBattleZoneDeploySlotThen(
                        recipient,
                        pickedRef,
                        () =>
                        {
                            BeginOnlineEffectSyncBatch(ownerType);
                            DeployUnitToBattleZone(
                                pickedRef,
                                recipient,
                                ResolveDeployRecipientRule(recipient),
                                effect.deployUnitTriggerOnPlayed,
                                fromHand: true,
                                bypassBattleZoneCap: true);
                            FlushOnlineEffectSyncBatch();
                            SyncAllResourceViewsFromRule();
                            onComplete?.Invoke();
                        },
                        () => onComplete?.Invoke());
                });
            }
        }

        Button cancel = root.CreateChildButton(GameLocale.T("キャンセル", "Cancel"));
        RectTransform cancelRt = cancel.GetComponent<RectTransform>();
        cancelRt.sizeDelta = new Vector2(160f, 44f);
        cancelRt.anchorMin = new Vector2(0.5f, 0f);
        cancelRt.anchorMax = new Vector2(0.5f, 0f);
        cancelRt.pivot = new Vector2(0.5f, 0f);
        cancelRt.anchoredPosition = new Vector2(0f, 36f);
        cancel.onClick.AddListener(() =>
        {
            if (resolved)
            {
                return;
            }

            resolved = true;
            Destroy(root);
            activeOnActionPopupRoot = null;
            isOnActionPopupOpen = false;
            onComplete?.Invoke();
        });

        yield return new WaitUntil(() => resolved || root == null);
        if (!resolved)
        {
            onComplete?.Invoke();
        }
    }

    private IEnumerator ShowDeployUnitFromTrashSelectionCoroutine(
        CardController sourceCard,
        CardGameRule trashRule,
        PlayerType ownerType,
        PlayerType trashOwner,
        PlayerType recipient,
        EffectData effect,
        List<TrashExileCandidate> candidates,
        int pickCount,
        Action onComplete)
    {
        if (trashRule == null || candidates == null || candidates.Count == 0 || CardImagePrefab == null)
        {
            onComplete?.Invoke();
            yield break;
        }

        Canvas canvas = ResolveBattleCanvas();
        if (canvas == null)
        {
            onComplete?.Invoke();
            yield break;
        }

        int remaining = pickCount;
        HashSet<int> usedTrashIndices = new HashSet<int>();

        while (remaining > 0)
        {
            List<TrashExileCandidate> available = new List<TrashExileCandidate>();
            for (int i = 0; i < candidates.Count; i++)
            {
                TrashExileCandidate c = candidates[i];
                if (!usedTrashIndices.Contains(c.TrashIndex))
                {
                    available.Add(c);
                }
            }

            if (available.Count == 0)
            {
                break;
            }

            // optionalPlayerConfirm 時は1枚でも UI（スキップ可）を出す
            if (available.Count == 1 && (effect == null || !effect.optionalPlayerConfirm))
            {
                TrashExileCandidate only = available[0];
                bool autoDone = false;
                bool autoOk = false;
                yield return CoTryDeployUnitFromTrashIndexWithCap(
                    sourceCard,
                    trashRule,
                    trashOwner,
                    recipient,
                    only.TrashIndex,
                    only.Data,
                    effect.deployUnitTriggerOnPlayed,
                    effect.deployUnitPayCost,
                    ownerType,
                    ok =>
                    {
                        autoOk = ok;
                        autoDone = true;
                    });
                yield return new WaitUntil(() => autoDone);
                if (autoOk)
                {
                    usedTrashIndices.Add(only.TrashIndex);
                    remaining--;
                }
                else
                {
                    remaining = 0;
                }

                SyncAllResourceViewsFromRule();
                continue;
            }

            bool pickedThisRound = false;
            bool trashCapResolved = false;
            DestroyActiveOnActionPopupIfAny();
            GameObject root = new GameObject(
                "DeployUnitFromTrashSelect",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            activeOnActionPopupRoot = root;
            isOnActionPopupOpen = true;
            root.transform.SetParent(canvas.transform, false);
            root.transform.SetAsLastSibling();
            root.SetFullSize();
            Image dim = root.GetComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.62f);
            dim.raycastTarget = true;

            TextMeshProUGUI title = root.CreateChildTextCustom("DeployTrashTitle", UIAnchor.TopCenter, 760, 48);
            if (effect != null && effect.optionalPlayerConfirm)
            {
                title.SetLocalizedText(
                    $"トラッシュから配備（してもよい・残り{remaining}枚）",
                    $"Deploy from Trash (optional, {remaining} left)");
            }
            else
            {
                title.SetLocalizedText(
                    $"トラッシュから配備 ({remaining}枚)",
                    $"Deploy from Trash ({remaining} left)");
            }
            title.fontSize = 26;
            title.fontStyle = FontStyles.Bold;
            title.color = Color.white;
            title.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -18f);

            GameObject scrollGo = root.CreateGridScrollView(560, 360, UIAnchor.TopCenter);
            RectTransform scrollRt = scrollGo.GetComponent<RectTransform>();
            scrollRt.anchoredPosition = new Vector2(0f, -72f);
            scrollGo.ConfigureGridCellFromViewportHeight(0.75f, 56f);
            ScrollRect sr = scrollGo.GetComponent<ScrollRect>();
            RectTransform content = sr != null ? sr.content : null;

            if (content != null)
            {
                for (int i = 0; i < available.Count; i++)
                {
                    TrashExileCandidate candidate = available[i];
                    CardData data = candidate.Data ?? DeckSettinObject.Instance.GetCardDataById(candidate.CardId);
                    if (data == null)
                    {
                        continue;
                    }

                    GameObject go = Instantiate(CardImagePrefab, content);
                    CardController display = go.GetComponent<CardController>();
                    if (display != null)
                    {
                        display.SetUp(data, _ => { });
                        go.transform.localScale = new Vector3(0.4f, 0.4f, 1f);
                    }

                    Button pickBtn = go.GetComponent<Button>();
                    if (pickBtn == null)
                    {
                        pickBtn = go.AddComponent<Button>();
                    }

                    int trashIndex = candidate.TrashIndex;
                    CardData dataRef = data;
                    pickBtn.onClick.AddListener(() =>
                    {
                        if (pickedThisRound)
                        {
                            return;
                        }

                        pickedThisRound = true;
                        Destroy(root);
                        activeOnActionPopupRoot = null;
                        isOnActionPopupOpen = false;

                        StartCoroutine(CoTryDeployUnitFromTrashIndexWithCap(
                            sourceCard,
                            trashRule,
                            trashOwner,
                            recipient,
                            trashIndex,
                            dataRef,
                            effect.deployUnitTriggerOnPlayed,
                            effect.deployUnitPayCost,
                            ownerType,
                            ok =>
                            {
                                if (ok)
                                {
                                    usedTrashIndices.Add(trashIndex);
                                    remaining--;
                                }
                                else
                                {
                                    // 満杯置換 Cancel → この配備は行わない
                                    remaining = 0;
                                }

                                SyncAllResourceViewsFromRule();
                                trashCapResolved = true;
                            }));
                    });
                }
            }

            Button skip = root.CreateChildButton(GameLocale.T("スキップ", "Skip"));
            RectTransform skipRt = skip.GetComponent<RectTransform>();
            skipRt.sizeDelta = new Vector2(160f, 44f);
            skipRt.anchorMin = new Vector2(0.5f, 0f);
            skipRt.anchorMax = new Vector2(0.5f, 0f);
            skipRt.pivot = new Vector2(0.5f, 0f);
            skipRt.anchoredPosition = new Vector2(0f, 36f);
            skip.onClick.AddListener(() =>
            {
                if (pickedThisRound)
                {
                    return;
                }

                pickedThisRound = true;
                remaining = 0;
                trashCapResolved = true;
                Destroy(root);
                activeOnActionPopupRoot = null;
                isOnActionPopupOpen = false;
            });

            yield return new WaitUntil(() => pickedThisRound || root == null);
            yield return new WaitUntil(() => trashCapResolved || root == null);
        }

        onComplete?.Invoke();
    }

    private static bool IsOnAttackNonCombatEffect(EffectData effect)
    {
        if (effect == null)
        {
            return false;
        }

        // 戦闘中の AP/HP 修飾は「このバトル中」扱いでユニット戦に載せる（プレコンバットでは付与しない）。
        // Self AP の UntilEndOfBattle は ComputeOnAttackSelfApBonus（ストライク加算）で反映する。
        if (effect.type == EffectType.Buff || effect.type == EffectType.Debuff)
        {
            return false;
        }

        if (effect.type == EffectType.DeployUnit
            || effect.type == EffectType.ExileFromTrash
            || effect.type == EffectType.ReturnFromTrashToDeckAndShuffle
            || effect.type == EffectType.ReturnFromTrashToDeckAndShuffle
            || effect.type == EffectType.Draw
            || effect.type == EffectType.MillTopToTrash
            || effect.type == EffectType.ExileFromDeck
            || effect.type == EffectType.Look
            || effect.type == EffectType.ActivateMountedCardOnMain
            || effect.type == EffectType.ActivateResource
            || ((effect.type == EffectType.Damage)
                && (effect.target == TargetType.EnemyPlayer || effect.target == TargetType.SelfPlayer)))
        {
            return true;
        }

        // 条件付き《先制攻撃》は戦闘交換時に再評価（ZnO 等）。OnAttack 付与経路に載せない。
        if (effect.type == EffectType.FirstStrike)
        {
            return false;
        }

        // GrantAttackFlag は TryOpenOnAttackAllyGrantAttackFlagSelection で攻撃前に解決。
        if (effect.type == EffectType.GrantAttackFlag)
        {
            return false;
        }

        return !effect.target.IsOpponentUnitTarget() && !effect.type.UsesTargetCountValue();
    }

    private static bool TimedBlockNeedsOnAttackPreCombatResolution(TimedEffectData timed)
    {
        if (timed == null || !timed.HasResolvedEffects())
        {
            return false;
        }

        IReadOnlyList<EffectData> effects = timed.GetResolvedEffects();
        for (int i = 0; i < effects.Count; i++)
        {
            if (IsOnAttackNonCombatEffect(effects[i]))
            {
                return true;
            }
        }

        return false;
    }

    private readonly struct OnAttackEffectSource
    {
        public OnAttackEffectSource(CardController source, PlayerType owner)
        {
            Source = source;
            Owner = owner;
        }

        public CardController Source { get; }
        public PlayerType Owner { get; }
    }

    private List<OnAttackEffectSource> BuildOnAttackEffectSources(CardController attacker, PlayerType attackerOwner)
    {
        List<OnAttackEffectSource> list = new List<OnAttackEffectSource>();
        if (attacker != null && attacker.Data != null)
        {
            list.Add(new OnAttackEffectSource(attacker, attackerOwner));
        }

        if (attacker?.MountedPilot != null && attacker.MountedPilot.Data != null)
        {
            list.Add(new OnAttackEffectSource(attacker.MountedPilot, attackerOwner));
        }

        return list;
    }

    private List<TimedEffectData> CollectOnAttackPreCombatBlocksForSource(
        CardController source,
        CardController attacker,
        PlayerType attackerOwner)
    {
        List<TimedEffectData> blocks = new List<TimedEffectData>();
        if (source?.Data?.timedEffects == null || attacker == null)
        {
            return blocks;
        }

        // Master Gundam 本体の timedEffects だけ専用パスへ（搭乗パイロットは収集する）
        if (ShouldSkipMasterGundamInGenericOnAttack(source))
        {
            return blocks;
        }

        EffectActivationContext ctx = BuildOnAttackActivationContext(attackerOwner, attacker);
        for (int i = 0; i < source.Data.timedEffects.Count; i++)
        {
            TimedEffectData timed = source.Data.timedEffects[i];
            if (timed == null || timed.timing != EffectTiming.OnAttack || !timed.HasResolvedEffects())
            {
                continue;
            }

            if (!TimedBlockNeedsOnAttackPreCombatResolution(timed))
            {
                continue;
            }

            if (timed.oncePerTurn && HasUsedPaidActivationThisTurn(attackerOwner, source, i))
            {
                continue;
            }

            if (!EffectActivationEvaluator.AreTimedConditionsMet(timed, ctx))
            {
                continue;
            }

            if (!ShouldOfferOnAttackReturnFromTrashBlock(attacker, attackerOwner, timed))
            {
                continue;
            }

            blocks.Add(timed);
        }

        return blocks;
    }

    private List<TimedEffectData> CollectOnAttackPreCombatBlocks(
        CardController attacker,
        PlayerType attackerOwner)
    {
        List<TimedEffectData> blocks = new List<TimedEffectData>();
        List<OnAttackEffectSource> sources = BuildOnAttackEffectSources(attacker, attackerOwner);
        for (int si = 0; si < sources.Count; si++)
        {
            blocks.AddRange(CollectOnAttackPreCombatBlocksForSource(
                sources[si].Source,
                attacker,
                attackerOwner));
        }

        return blocks;
    }

    /// <summary>攻撃宣言時に DeployUnit 等の非戦闘 OnAttack 効果を解決する。解決が必要なら true。</summary>
    private bool TryBeginOnAttackPreCombatEffectChain(
        CardController attacker,
        PlayerType attackerOwner,
        Action onResolved)
    {
        if (attacker == null)
        {
            return false;
        }

        if (HasOnAttackPreCombatEffectsBeenApplied(attacker))
        {
            return false;
        }

        if (IsOnAttackTrashReturnEffectConsumedForAttack(attacker))
        {
            Debug.Log(
                "[OnAttackPreCombat] Trash-return effect already consumed this attack — skip chain "
                + $"(cardId:{attacker.Data?.id})");
            return false;
        }

        if (IsOnAttackTrashReturnAttackTracking(attacker)
            && !HasOnAttackPreCombatEffectsBeenApplied(attacker))
        {
            Debug.Log(
                "[OnAttackPreCombat] OnAttack chain already in progress — skip duplicate "
                + $"(cardId:{attacker.Data?.id})");
            return false;
        }

        _suppressOnAttackReturnToDeckBottomAfterFailedDiscard = false;
        List<TimedEffectData> unitBlocks = CollectOnAttackPreCombatBlocksForSource(
            attacker,
            attacker,
            attackerOwner);
        CardController pilot = attacker.MountedPilot;
        List<TimedEffectData> pilotBlocks = CollectOnAttackPreCombatBlocksForSource(
            pilot,
            attacker,
            attackerOwner);
        if (unitBlocks.Count == 0 && pilotBlocks.Count == 0)
        {
            return false;
        }

        Debug.Log(
            $"[OnAttackPreCombat] Start unitBlocks:{unitBlocks.Count} pilotBlocks:{pilotBlocks.Count} "
            + $"attacker:{attacker.Data?.cardName}(id:{attacker.Data?.id}) "
            + $"pilot:{pilot?.Data?.cardName ?? "none"}");

        BeginOnAttackTrashReturnAttackTracking(attacker);
        _pendingOnAttackPreCombatResolvedAttacker = attacker;
        // 前攻撃の観測リークで「除外しなくてもダメージ」が他ユニット／翌ターンへ残らないようルートを毎回新規にする。
        BeginEffectChainObservationScope(forceNewRoot: true);

        void FinishPreCombat()
        {
            EndEffectChainObservationScope();
            MarkOnAttackPreCombatEffectsApplied(attacker);
            _onAttackPreCombatCompletedAttacker = attacker;
            // 打撃／OnAction 中の溢れ防止判定に影響しないよう、プレコンバット終了時にクリア
            ClearOnAttackPreCombatResolvedState();
            onResolved?.Invoke();
        }

        ResolveUnitPilotEffectOrder(
            attackerOwner,
            attacker,
            pilot,
            unitBlocks,
            pilotBlocks,
            attacker.Data,
            ordered =>
            {
                if (ordered == null || ordered.Count == 0)
                {
                    FinishPreCombat();
                    return;
                }

                RunOrderedOnAttackPreCombatEntries(
                    attacker,
                    attackerOwner,
                    ordered,
                    0,
                    FinishPreCombat);
            },
            autoPilotFirst: false,
            titleJa: "アタック時効果の解決順を選択",
            titleEn: "Choose On Attack effect order",
            entrySelectable: (source, blocks) =>
                IsOnAttackPreCombatSourceSelectable(attacker, attackerOwner, source, blocks));
        return true;
    }

    private void RunOrderedOnAttackPreCombatEntries(
        CardController attacker,
        PlayerType attackerOwner,
        List<UnitPilotEffectOrderEntry> ordered,
        int index,
        Action onComplete)
    {
        if (ordered == null || index >= ordered.Count)
        {
            onComplete?.Invoke();
            return;
        }

        UnitPilotEffectOrderEntry entry = ordered[index];
        if (entry == null || entry.Source == null || entry.Blocks == null || entry.Blocks.Count == 0)
        {
            RunOrderedOnAttackPreCombatEntries(attacker, attackerOwner, ordered, index + 1, onComplete);
            return;
        }

        RunOnAttackPreCombatTimedBlocks(
            attacker,
            attackerOwner,
            entry.Blocks,
            0,
            () => RunOrderedOnAttackPreCombatEntries(
                attacker,
                attackerOwner,
                ordered,
                index + 1,
                onComplete));
    }

    private void RunOnAttackPreCombatTimedBlocks(
        CardController attacker,
        PlayerType attackerOwner,
        List<TimedEffectData> blocks,
        int blockIndex,
        Action onComplete)
    {
        if (blocks == null || blockIndex >= blocks.Count)
        {
            onComplete?.Invoke();
            return;
        }

        TimedEffectData block = blocks[blockIndex];
        CardController source = ResolveOnAttackBlockSource(attacker, attackerOwner, block);
        if (source == null)
        {
            RunOnAttackPreCombatTimedBlocks(attacker, attackerOwner, blocks, blockIndex + 1, onComplete);
            return;
        }

        EffectActivationContext ctx = BuildOnAttackActivationContext(attackerOwner, attacker);
        if (!CanRunTimedBlockAtChainTime(block, ctx, "OnAttack"))
        {
            RunOnAttackPreCombatTimedBlocks(attacker, attackerOwner, blocks, blockIndex + 1, onComplete);
            return;
        }

        if (block.oncePerTurn)
        {
            int timedIndex = IndexOfTimedEffectOnCard(source, block);
            if (timedIndex >= 0)
            {
                MarkPaidActivationUsedThisTurn(attackerOwner, source, timedIndex);
            }
        }

        TryExecuteOnAttackPreCombatEffectChain(
            source,
            attackerOwner,
            block.GetResolvedEffects(),
            0,
            () => RunOnAttackPreCombatTimedBlocks(attacker, attackerOwner, blocks, blockIndex + 1, onComplete));
    }

    private static int IndexOfTimedEffectOnCard(CardController source, TimedEffectData block)
    {
        if (source?.Data?.timedEffects == null || block == null)
        {
            return -1;
        }

        for (int i = 0; i < source.Data.timedEffects.Count; i++)
        {
            if (ReferenceEquals(source.Data.timedEffects[i], block))
            {
                return i;
            }
        }

        return -1;
    }

    private CardController ResolveOnAttackBlockSource(
        CardController attacker,
        PlayerType attackerOwner,
        TimedEffectData block)
    {
        if (attacker?.Data?.timedEffects != null && attacker.Data.timedEffects.Contains(block))
        {
            return attacker;
        }

        if (attacker?.MountedPilot?.Data?.timedEffects != null
            && attacker.MountedPilot.Data.timedEffects.Contains(block))
        {
            return attacker.MountedPilot;
        }

        return attacker;
    }

    private void TryExecuteOnAttackPreCombatEffectChain(
        CardController sourceCard,
        PlayerType ownerType,
        IReadOnlyList<EffectData> effects,
        int index,
        Action onDone)
    {
        if (effects == null || index >= effects.Count)
        {
            onDone?.Invoke();
            return;
        }

        EffectData effect = effects[index];
        if (effect == null)
        {
            TryExecuteOnAttackPreCombatEffectChain(sourceCard, ownerType, effects, index + 1, onDone);
            return;
        }

        // CombatPair 経路では Activate（UsesTargetCountValue）がスキップされるため、自身対象はここで解決
        if ((effect.type == EffectType.Activate || effect.type == EffectType.FirstStrike)
            && effect.target == TargetType.Self)
        {
            CardController attackHost = _pendingOnAttackPreCombatResolvedAttacker ?? sourceCard;
            EffectActivationContext selfActivationContext = BuildOnAttackActivationContext(
                ownerType,
                attackHost);
            if (!ShouldApplyChainedEffect(effect, selfActivationContext, "OnAttackPreCombat"))
            {
                TryExecuteOnAttackPreCombatEffectChain(sourceCard, ownerType, effects, index + 1, onDone);
                return;
            }

            if (TryApplyOnAttackTrashReturnFollowUpEffect(sourceCard, ownerType, effect))
            {
                TryExecuteOnAttackPreCombatEffectChain(sourceCard, ownerType, effects, index + 1, onDone);
                return;
            }

            // バンシー等：トラッシュ返却チェーン内の Self Activate/FirstStrike は専用経路のみ（二重適用防止）
            if (IsOnAttackTrashReturnAttackTracking(attackHost))
            {
                TryExecuteOnAttackPreCombatEffectChain(sourceCard, ownerType, effects, index + 1, onDone);
                return;
            }

            ApplyEffect(sourceCard, ownerType, effect);
            TryExecuteOnAttackPreCombatEffectChain(sourceCard, ownerType, effects, index + 1, onDone);
            return;
        }

        if (!IsOnAttackNonCombatEffect(effect)
            && !ShouldAllowMasterGundamPairEnemyUnitOnAttackEffect(effect))
        {
            TryExecuteOnAttackPreCombatEffectChain(sourceCard, ownerType, effects, index + 1, onDone);
            return;
        }

        EffectActivationContext activationContext = BuildOnAttackActivationContext(
            ownerType,
            _pendingOnAttackPreCombatResolvedAttacker ?? sourceCard);
        if (!ShouldApplyChainedEffect(effect, activationContext, "OnAttackPreCombat"))
        {
            TryExecuteOnAttackPreCombatEffectChain(sourceCard, ownerType, effects, index + 1, onDone);
            return;
        }

        if (effect.type == EffectType.DeployUnit && effect.RequiresDeployUnitZoneSelection())
        {
            ApplyDeployUnitEffect(
                sourceCard,
                ownerType,
                effect,
                () => TryExecuteOnAttackPreCombatEffectChain(sourceCard, ownerType, effects, index + 1, onDone));
            return;
        }

        if (effect.type == EffectType.ActivateMountedCardOnMain)
        {
            ApplyActivateMountedCardOnMain(
                sourceCard,
                ownerType,
                () => TryExecuteOnAttackPreCombatEffectChain(sourceCard, ownerType, effects, index + 1, onDone));
            return;
        }

        // 「してもよい。そうしたなら…」除外：Cancel／候補不足は後続ダメージを打ち切る
        if (effect.type == EffectType.ReturnFromTrashToDeckAndShuffle)
        {
            CardController attackHost = _pendingOnAttackPreCombatResolvedAttacker ?? sourceCard;
            if (IsOnAttackTrashReturnAttackTracking(attackHost)
                && _onAttackTrashReturnAttackState.TrashReturned)
            {
                TryExecuteOnAttackPreCombatEffectChain(sourceCard, ownerType, effects, index + 1, onDone);
                return;
            }

            bool abortRemainingOnSkip = effect.abortRemainingChainOnSkip;
            ApplyReturnFromTrashToDeckAndShuffleEffect(
                sourceCard,
                ownerType,
                effect,
                onComplete: () => TryExecuteOnAttackPreCombatEffectChain(
                    sourceCard,
                    ownerType,
                    effects,
                    index + 1,
                    onDone),
                onSkipped: () =>
                {
                    if (abortRemainingOnSkip)
                    {
                        Debug.Log(
                            "[OnAttackPreCombat] ReturnFromTrashToDeck skipped — abort remaining chain "
                            + $"(cardId:{sourceCard?.Data?.id})");
                        onDone?.Invoke();
                    }
                    else
                    {
                        TryExecuteOnAttackPreCombatEffectChain(
                            sourceCard,
                            ownerType,
                            effects,
                            index + 1,
                            onDone);
                    }
                });
            return;
        }

        if (effect.type == EffectType.ExileFromTrash)
        {
            bool abortRemainingOnSkip = effect.abortRemainingChainOnSkip;
            ApplyExileFromTrashEffect(
                sourceCard,
                ownerType,
                effect,
                onComplete: () => TryExecuteOnAttackPreCombatEffectChain(
                    sourceCard,
                    ownerType,
                    effects,
                    index + 1,
                    onDone),
                onSkipped: () =>
                {
                    if (abortRemainingOnSkip)
                    {
                        Debug.Log(
                            "[OnAttackPreCombat] ExileFromTrash skipped — abort remaining chain "
                            + $"(cardId:{sourceCard?.Data?.id})");
                        onDone?.Invoke();
                    }
                    else
                    {
                        TryExecuteOnAttackPreCombatEffectChain(
                            sourceCard,
                            ownerType,
                            effects,
                            index + 1,
                            onDone);
                    }
                });
            return;
        }

        // プレイヤー領域ダメージ（Master Gundam 等）はシールド破壊 UI 完了後にチェーン続行する。
        if (effect.type == EffectType.Damage
            && (effect.target == TargetType.EnemyPlayer || effect.target == TargetType.SelfPlayer))
        {
            _allowOnAttackEffectShieldAreaDamage = true;
            try
            {
                ApplyEffect(sourceCard, ownerType, effect);
            }
            finally
            {
                _allowOnAttackEffectShieldAreaDamage = false;
            }

            StartCoroutine(CoContinueOnAttackPreCombatAfterPlayerAreaDamage(
                () => TryExecuteOnAttackPreCombatEffectChain(
                    sourceCard,
                    ownerType,
                    effects,
                    index + 1,
                    onDone)));
            return;
        }

        if (EffectRequiresManualHandSelection(effect))
        {
            PlayerType handOwner = ResolveHandDiscardOwner(ownerType, effect);
            List<CardController> handCandidates = CollectSelectableHandCards(
                handOwner,
                excludeSource: sourceCard);
            if (handCandidates.Count == 0)
            {
                Debug.Log("[OnAttackPreCombat] 捨てる手札がありません (DiscardFromHand)。山札下送りを抑止。");
                _suppressOnAttackReturnToDeckBottomAfterFailedDiscard = true;
                TryExecuteOnAttackPreCombatEffectChain(sourceCard, ownerType, effects, index + 1, onDone);
                return;
            }

            TryExecuteManualHandSelectionEffect(
                sourceCard,
                ownerType,
                effect,
                success =>
                {
                    if (!success)
                    {
                        _suppressOnAttackReturnToDeckBottomAfterFailedDiscard = true;
                        Debug.Log(
                            "[OnAttackPreCombat] DiscardFromHand 未完了（Skip または枚数不足）。"
                            + " ReturnUnitToDeckBottom を抑止。");
                    }

                    TryExecuteOnAttackPreCombatEffectChain(
                        sourceCard,
                        ownerType,
                        effects,
                        index + 1,
                        onDone);
                });
            return;
        }

        if (EffectRequiresManualUnitSelection(effect))
        {
            CardController attackHost = _pendingOnAttackPreCombatResolvedAttacker ?? sourceCard;
            TryExecuteManualUnitSelectionEffect(
                sourceCard,
                ownerType,
                effect,
                attackHost,
                () => TryExecuteOnAttackPreCombatEffectChain(sourceCard, ownerType, effects, index + 1, onDone));
            return;
        }

        ApplyEffectRespectingLookAsync(
            sourceCard,
            ownerType,
            effect,
            () => TryExecuteOnAttackPreCombatEffectChain(sourceCard, ownerType, effects, index + 1, onDone));
    }

    /// <summary>
    /// OnAttack のプレイヤー領域ダメージ後、シールド破壊／バースト UI が終わってからチェーンを進める。
    /// </summary>
    private IEnumerator CoContinueOnAttackPreCombatAfterPlayerAreaDamage(Action onContinue)
    {
        yield return null;
        yield return WaitForShieldBreakFlowCompleteCoroutine(45f);
        yield return WaitUntilBlockingChoiceOrTrashUiCleared(8f);
        onContinue?.Invoke();
    }

    private void ClearOnAttackPreCombatResolvedState()
    {
        _pendingOnAttackPreCombatResolvedAttacker = null;
    }

    private void ClearOnAttackPreCombatCompletedForNewAttack()
    {
        _onAttackPreCombatCompletedAttacker = null;
    }

    private EntityId _onAttackPreCombatEffectsAppliedAttackerId;

    private void ResetOnAttackPreCombatEffectsAppliedGuard()
    {
        _onAttackPreCombatEffectsAppliedAttackerId = default;
    }

    private void MarkOnAttackPreCombatEffectsApplied(CardController attacker)
    {
        if (attacker != null)
        {
            _onAttackPreCombatEffectsAppliedAttackerId = attacker.GetEntityId();
        }
    }

    private bool HasOnAttackPreCombatEffectsBeenApplied(CardController attacker)
    {
        return attacker != null
            && _onAttackPreCombatEffectsAppliedAttackerId == attacker.GetEntityId();
    }

    /// <summary>
    /// Draw / DeployUnit 等の非戦闘 OnAttack 効果を同期的に適用（敵 AI や pre-combat チェーン未経由時のフォールバック）。
    /// </summary>
    private void ApplyOnAttackPreCombatEffectsImmediately(CardController attacker, PlayerType attackerOwner)
    {
        if (attacker == null || HasOnAttackPreCombatEffectsBeenApplied(attacker))
        {
            return;
        }

        List<TimedEffectData> blocks = CollectOnAttackPreCombatBlocks(attacker, attackerOwner);
        if (blocks.Count == 0)
        {
            return;
        }

        EffectActivationContext ctx = BuildOnAttackActivationContext(attackerOwner, attacker);
        BeginEffectChainObservationScope(forceNewRoot: true);
        try
        {
            for (int bi = 0; bi < blocks.Count; bi++)
            {
                TimedEffectData block = blocks[bi];
                CardController source = ResolveOnAttackBlockSource(attacker, attackerOwner, block);
                if (source == null || !CanRunTimedBlockAtChainTime(block, ctx, "OnAttackPreCombatSync"))
                {
                    continue;
                }

                IReadOnlyList<EffectData> effects = block.GetResolvedEffects();
                for (int ei = 0; ei < effects.Count; ei++)
                {
                    EffectData effect = effects[ei];
                    if (effect == null || !IsOnAttackNonCombatEffect(effect))
                    {
                        continue;
                    }

                    if (!ShouldApplyChainedEffect(effect, ctx, "OnAttackPreCombatSync"))
                    {
                        continue;
                    }

                    if (EffectRequiresManualUnitSelection(effect))
                    {
                        continue;
                    }

                    // プレイヤー向け同期経路でも「除外スキップ後のダメージ」を落とす
                    if (effect.type == EffectType.ExileFromTrash && effect.abortRemainingChainOnSkip)
                    {
                        bool exileCompleted = false;
                        ApplyExileFromTrashEffect(
                            source,
                            attackerOwner,
                            effect,
                            onComplete: () => exileCompleted = true,
                            onSkipped: () => exileCompleted = false);
                        // 同期 UI 無し（Enemy）なら onComplete 即時。プレイヤー UI は非同期のためここでは完了想定外。
                        if (!exileCompleted)
                        {
                            break;
                        }

                        continue;
                    }

                    ApplyEffect(source, attackerOwner, effect);
                }
            }
        }
        finally
        {
            EndEffectChainObservationScope();
        }

        MarkOnAttackPreCombatEffectsApplied(attacker);
        Debug.Log(
            $"[OnAttackPreCombat] Sync applied blocks:{blocks.Count} attacker:{attacker.Data?.cardName}(id:{attacker.Data?.id})");
    }

    /// <summary>
    /// キラデバフと同様、TryUnitVsUnitAttack の前に OnAttack 効果 UI を解決する。
    /// 1) Draw 等の非戦闘 OnAttack → 2) GrantAttackFlag → 3) 敵ユニット向け OnAttack 効果。
    /// </summary>
    /// <returns>非同期 UI 表示中なら true（onResolved は UI 完了後に呼ばれる）。</returns>
    private bool TryOpenOnAttackEffectSelectionBeforeCombat(
        CardController attacker,
        PlayerType attackerOwner,
        CardController attackedTarget,
        Action onResolved)
    {
        if (attacker == null)
        {
            onResolved?.Invoke();
            return false;
        }

        // Master Gundam は専用パス（除外→シールド5→本体攻撃）。汎用チェーンは使わない。
        if (TryBeginMasterGundamOnAttackEffect(attacker, attackerOwner, onResolved))
        {
            return true;
        }

        void AfterAllyGrantAttackFlag()
        {
            _onAttackPreCombatCompletedAttacker = attacker;
            if (TryOpenOnAttackEnemySelectionPanel(attacker, attackerOwner, attackedTarget, onResolved))
            {
                return;
            }

            onResolved?.Invoke();
        }

        void AfterPreCombatOnAttackChain()
        {
            NotifyAllyUnitAttack(attackerOwner, attacker, () =>
            {
                if (TryOpenOnAttackAllyGrantAttackFlagSelection(attacker, attackerOwner, AfterAllyGrantAttackFlag))
                {
                    return;
                }

                AfterAllyGrantAttackFlag();
            });
        }

        if (_onAttackPreCombatCompletedAttacker == attacker)
        {
            AfterAllyGrantAttackFlag();
            // 同期/onResolved 済み、または UI 表示中。呼び出し元は 8116 以降に進まない。
            return true;
        }

        if (HasOnAttackPreCombatEffectsBeenApplied(attacker))
        {
            AfterPreCombatOnAttackChain();
            return true;
        }

        if (TryBeginOnAttackPreCombatEffectChain(attacker, attackerOwner, AfterPreCombatOnAttackChain))
        {
            return true;
        }

        AfterPreCombatOnAttackChain();
        return true;
    }

    /// <summary>攻撃対象確定後：OnAttack 効果 UI → ユニット戦へ。</summary>
    private void BeginUnitAttackAfterTargetDeclared(
        CardController attacker,
        CardController defender,
        PlayerType attackerOwner,
        PlayerType defenderOwner)
    {
        // OnAttack 条件（SourceAttackingEnemyUnit / AP 等）評価のため、ユニット攻撃コンテキストを先に登録する。
        RegisterAttackFlowContextForOnAction(
            attacker,
            attackerOwner,
            AttackFlowStrikeKind.UnitVsUnit,
            defender,
            null);

        // OnAttack は TryUnitVsUnitAttack 先頭で1回だけ解決（二重起動防止）
        TryUnitVsUnitAttack(attacker, defender, attackerOwner, defenderOwner);
    }

    private void ResumeUnitVsUnitAttackAfterOnAttackPreCombat(
        CardController attacker,
        PlayerType attackerOwner,
        CardController defender,
        bool skipOnActionPause,
        bool skipAttackedSidePanelPause)
    {
        if (attacker == null)
        {
            return;
        }

        if (!IsPendingOnAttackEffectResolvedForAttacker(attacker))
        {
            if (TryOpenOnAttackEnemySelectionPanel(
                attacker,
                attackerOwner,
                defender,
                () => ContinueUnitAttackAfterOnAttackEffects(attacker, attackerOwner, defender, skipOnActionPause)))
            {
                return;
            }

            pendingOnAttackEffectResolvedAttacker = attacker;
        }

        ContinueUnitAttackAfterOnAttackEffects(attacker, attackerOwner, defender, skipOnActionPause);
    }

    /// <summary>
    /// OnAttack（Sazabi 等）完了後の続行。
    /// ブロッカーがいればブロック → アクションの順（アクションを先に始めない）。
    /// </summary>
    private void ContinueUnitAttackAfterOnAttackEffects(
        CardController attacker,
        PlayerType attackerOwner,
        CardController defender,
        bool skipOnActionPause)
    {
        if (attacker == null)
        {
            return;
        }

        pendingOnAttackEffectResolvedAttacker = attacker;
        _onAttackPreCombatCompletedAttacker = attacker;

        if (attackFlowBlockSelectionResolved)
        {
            if (attackFlowPostBlockPassOnActionDone)
            {
                CardController resumeAttacker = attackFlowAttackerUnit != null ? attackFlowAttackerUnit : attacker;
                CardController resumeDefender = attackFlowDeclaredDefenderUnit != null
                    ? attackFlowDeclaredDefenderUnit
                    : defender;
                if (!IsUnitAliveOnAnyDeployField(resumeDefender))
                {
                    CancelPendingUnitAttackFlow();
                    return;
                }

                ExecuteUnitVsUnitDeclaredCombat(
                    resumeAttacker,
                    resumeDefender,
                    attackFlowAttackerOwner,
                    ResolveCardOwner(resumeDefender.transform));
                return;
            }

            RunOnActionStepsImmediatelyAfterBlockPass(
                attackFlowAttackerUnit != null ? attackFlowAttackerUnit : attacker,
                attackFlowDeclaredDefenderUnit != null ? attackFlowDeclaredDefenderUnit : defender,
                attackFlowAttackerOwner,
                ResolveCardOwner((attackFlowDeclaredDefenderUnit != null
                    ? attackFlowDeclaredDefenderUnit
                    : defender).transform),
                AttackFlowStrikeKind.UnitVsUnit);
            return;
        }

        PlayerType defenderOwner = defender != null
            ? ResolveCardOwner(defender.transform)
            : (attackerOwner == PlayerType.Player ? PlayerType.Enemy : PlayerType.Player);

        Debug.Log(
            $"[AttackFlow] OnAttack 完了 → ブロック→アクションへ "
            + $"attacker:{attacker.Data?.cardName}");
        // 戦前 OnAttack は解決済み。宣言→ブロック→OnAction→戦闘のみ再開（先頭からの再入で二重処理しない）。
        TryUnitVsUnitAttack(
            attacker,
            defender,
            attackerOwner,
            defenderOwner,
            skipOnActionPause,
            skipAttackedSidePanelPause: false,
            resumeAfterPreCombatOnAttack: true);
    }
}
