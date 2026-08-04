using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>シールドゾーンと手札間の移動、およびシールド配備時の効果発動。</summary>
public partial class BattleGameMain
{
    /// <summary>OnBurst 中の DeployBase のみ、破壊元カード自身を対象にする。</summary>
    private bool burstDeployBasePreferSourceCard;

    /// <summary>バースト効果解決中は true（リソース消費・OnMain 扱いを抑止）。</summary>
    private int burstEffectResolutionDepth;

    private bool IsResolvingBurstEffect => burstEffectResolutionDepth > 0;

    /// <summary>コマンドの OnAction / OnMain 用。バースト解決中はリソースを消費しない。</summary>
    private bool TryConsumeResourceForCommandPlay(PlayerType side, CardController command, string context)
    {
        if (command == null || command.Data == null)
        {
            return false;
        }

        if (IsResolvingBurstEffect)
        {
            return true;
        }

        if (!gundamRule.TryConsumeResource(
                ToRuleSide(side),
                command.CurrentCost,
                0,
                command.Data.id,
                command.CurrentLevel))
        {
            Debug.Log($"[{context}] リソース不足で実行できません。");
            return false;
        }

        SyncResourceViewsFromRule(ToRuleSide(side));
        return true;
    }

    private struct BurstManualTargetStep
    {
        public ShieldBreakTaken Taken;
        public EffectData Effect;
    }

    /// <summary>手札からシールドゾーンへ再配備できるカードか（シールド→手札経由のみ）。</summary>
    private bool CanDeployShieldFromHand(CardController card)
    {
        if (card == null || card.Data == null || !card.IsEligibleForShieldZoneDeploy)
        {
            return false;
        }

        // ユニット・パイロット・ベースはバトル用。誤ってシールドに載せない。
        if (card.Data.IsUnitLike()
            || card.Data.IsPilot()
            || card.Data.type == Type.Base)
        {
            return false;
        }

        return true;
    }

    private Gundam2024RuleScript.PlayerState GetRuleState(Gundam2024RuleScript.PlayerSide side)
    {
        return side == Gundam2024RuleScript.PlayerSide.Player ? gundamRule.Player : gundamRule.Enemy;
    }

    private bool CanTakeShieldFromZone(Gundam2024RuleScript.PlayerSide ruleSide, CardGameRule rule)
    {
        if (rule == null || gundamRule == null)
        {
            return false;
        }

        // ゾーン実カードを正とする（カウント二重減算などで state.shield だけ 0 になる不整合を補正）
        int zoneCount = rule.GetShieldZoneCardCount();
        if (zoneCount <= 0)
        {
            return false;
        }

        Gundam2024RuleScript.PlayerState state = GetRuleState(ruleSide);
        if (state.shield <= 0 || state.shield != zoneCount)
        {
            gundamRule.SyncShieldCountFromZone(ruleSide, zoneCount);
            state = GetRuleState(ruleSide);
        }

        return state.shield > 0;
    }

    private void RegisterShieldCardInHandLists(CardController card, PlayerType ownerType)
    {
        if (card == null || card.Data == null)
        {
            return;
        }

        if (ownerType == PlayerType.Player)
        {
            playerHandCards.Add(card.Data);
        }
        else
        {
            enemyHandCards.Add(card.Data);
        }
    }

    private bool TryMoveShieldFromZoneToHand(
        CardGameRule targetRule,
        PlayerType targetType,
        Gundam2024RuleScript.PlayerSide ruleSide)
    {
        if (targetRule == null || !CanTakeShieldFromZone(ruleSide, targetRule))
        {
            return false;
        }

        if (!gundamRule.TryReduceShieldCountForHandMove(ruleSide, 1))
        {
            // ゾーンにカードがあるのに減算できない場合は実体に合わせて再試行
            int zoneCount = targetRule.GetShieldZoneCardCount();
            if (zoneCount <= 0)
            {
                return false;
            }

            gundamRule.SyncShieldCountFromZone(ruleSide, zoneCount);
            if (!gundamRule.TryReduceShieldCountForHandMove(ruleSide, 1))
            {
                return false;
            }
        }

        if (!targetRule.TryMoveTopShieldCardToHand(targetRule.HandScrollContent, out CardController shieldCard))
        {
            gundamRule.AddShieldCount(ruleSide, 1);
            return false;
        }

        shieldCard.gameObject.SetActive(true);
        shieldCard.SetEligibleForShieldZoneDeploy(true);
        targetRule.ApplyHandZoneLayoutToCard(shieldCard);
        RegisterShieldCardInHandLists(shieldCard, targetType);
        TriggerOnHandAutoEffects(shieldCard, targetType, skipHandZoneCheck: true);
        targetRule.RefreshHandCountDisplay();
        SyncResourceViewsFromRule(ruleSide);
        Debug.Log(
            $"[AddShieldToHand] {shieldCard.Data.cardName}(id:{shieldCard.Data.id}) shield zone → {targetType} hand (shield -1)");
        return true;
    }

    private CardController FindEligibleShieldDeployInHand(CardGameRule rule, CardController preferred)
    {
        if (rule?.HandScrollContent == null)
        {
            return null;
        }

        if (preferred != null
            && preferred.Data != null
            && preferred.Data.type != Type.Base
            && CanDeployShieldFromHand(preferred)
            && preferred.transform.IsChildOf(rule.HandScrollContent))
        {
            return preferred;
        }

        List<CardController> hand = CollectHandControllers(rule);
        for (int i = 0; i < hand.Count; i++)
        {
            CardController candidate = hand[i];
            if (candidate?.Data != null && candidate.Data.type == Type.Base)
            {
                continue;
            }

            if (CanDeployShieldFromHand(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private bool TryDeployCardToShieldZone(
        CardController card,
        PlayerType ownerType,
        CardGameRule rule,
        bool requireEligibleFromHand)
    {
        if (card == null || card.Data == null || rule == null)
        {
            return false;
        }

        if (card.Data.type == Type.Base)
        {
            return false;
        }

        if (rule.ShieldCardsContent != null && card.transform.IsChildOf(rule.ShieldCardsContent))
        {
            return false;
        }

        if (requireEligibleFromHand && !CanDeployShieldFromHand(card))
        {
            return false;
        }

        Gundam2024RuleScript.PlayerSide ruleSide = ToRuleSide(ownerType);
        bool wasInHand = rule.HandScrollContent != null
            && card.transform.IsChildOf(rule.HandScrollContent);
        if (wasInHand)
        {
            RemoveCardFromHandLists(card, ownerType);
        }

        if (!rule.TryAttachShieldCardFromHand(card))
        {
            if (wasInHand)
            {
                RegisterShieldCardInHandLists(card, ownerType);
            }

            return false;
        }

        if (ownerType == PlayerType.Player && wasInHand)
        {
            RecordEnemyAiObservedPlayerCardPlay(card, "DeployShield");
        }

        card.SetEligibleForShieldZoneDeploy(false);
        gundamRule.AddShieldCount(ruleSide, 1);
        TriggerShieldDeployedEffects(card, ownerType);
        SyncResourceViewsFromRule(ruleSide);
        NotifyLocalDeployShieldSynced(card, ownerType);
        Debug.Log(
            $"[DeployShieldFromHand] {card.Data.cardName}(id:{card.Data.id}) side:{ownerType} → shield zone");
        return true;
    }

    private void ApplyDeployShieldFromHandEffect(
        CardController sourceCard,
        PlayerType sourceOwner,
        EffectData effect,
        int magnitude)
    {
        PlayerType recipient = ResolveEffectOwnerPlayerType(sourceOwner, effect.target);
        CardGameRule rule = recipient == PlayerType.Player ? cardGameRule : enemyCardGameRule;

        int applied = 0;
        for (int i = 0; i < magnitude; i++)
        {
            CardController pick = FindEligibleShieldDeployInHand(rule, null);
            if (pick == null)
            {
                if (applied == 0)
                {
                    Debug.LogWarning(
                        $"[DeployShieldFromHand] No deployable shield in hand side:{recipient}");
                }

                break;
            }

            if (!TryDeployCardToShieldZone(pick, recipient, rule, requireEligibleFromHand: true))
            {
                break;
            }

            applied++;
        }

        Debug.Log(
            $"[Effect] DeployShieldFromHand x{applied}/{magnitude} target:{effect.target} "
            + $"by cardId:{sourceCard?.Data?.id ?? -1}");
    }

    private void ApplyAddShieldToHandEffect(
        CardController sourceCard,
        PlayerType sourceOwner,
        EffectData effect,
        int magnitude)
    {
        // AddShieldToHand は常に効果オーナー側のシールドゾーンを参照（旧 JSON の target=EnemyAllUnits 互換）
        PlayerType recipient = sourceOwner;
        if (effect != null && effect.target == TargetType.EnemyPlayer)
        {
            recipient = ResolveEffectOwnerPlayerType(sourceOwner, effect.target);
        }

        Gundam2024RuleScript.PlayerSide ruleSide = ToRuleSide(recipient);
        CardGameRule rule = recipient == PlayerType.Player ? cardGameRule : enemyCardGameRule;
        int want = Mathf.Max(1, magnitude);

        int applied = 0;
        for (int i = 0; i < want; i++)
        {
            if (!TryMoveShieldFromZoneToHand(rule, recipient, ruleSide))
            {
                if (applied == 0)
                {
                    Debug.LogWarning(
                        $"[AddShieldToHand] No shield available side:{recipient} "
                        + $"(stateShield:{(gundamRule != null ? GetRuleState(ruleSide).shield : -1)} "
                        + $"zone:{rule?.GetShieldZoneCardCount() ?? -1})");
                }

                break;
            }

            applied++;
        }

        Debug.Log(
            $"[Effect] AddShieldToHand x{applied}/{want} target:{(effect != null ? effect.target.ToString() : "?")} "
            + $"by cardId:{sourceCard?.Data?.id ?? -1}");
    }

    private void ApplyAddSelfToHandEffect(
        CardController sourceCard,
        PlayerType sourceOwner,
        EffectData effect,
        int magnitude)
    {
        PlayerType recipient = ResolveEffectOwnerPlayerType(sourceOwner, effect.target);
        CardGameRule rule = recipient == PlayerType.Player ? cardGameRule : enemyCardGameRule;

        if (IsResolvingBurstEffect)
        {
            if (sourceCard == null || recipient != sourceOwner)
            {
                Debug.LogWarning(
                    $"[AddSelfToHand] バースト元カードを手札へ移せません (cardId:{sourceCard?.Data?.id ?? -1})。");
                return;
            }

            int applied = 0;
            int count = magnitude > 0 ? magnitude : 1;
            for (int i = 0; i < count; i++)
            {
                if (!TryMoveBurstSourceCardToHand(sourceCard, recipient, rule))
                {
                    if (applied == 0)
                    {
                        Debug.LogWarning(
                            $"[AddSelfToHand] burst source move failed side:{recipient} cardId:{sourceCard.Data?.id}");
                    }

                    break;
                }

                applied++;
                break;
            }

            Debug.Log(
                $"[Effect] AddSelfToHand x{applied}/{count} target:{effect.target} "
                + $"by cardId:{sourceCard?.Data?.id ?? -1}");
            return;
        }

        // OnDestroyed: トラッシュへ置いた扱いの後、自身を手札へ戻す
        if (unitsPendingSendToTrash.Contains(sourceCard)
            || (sourceCard != null && IsCardOnBattleZone(sourceCard)))
        {
            if (sourceCard == null || recipient != sourceOwner || rule == null)
            {
                Debug.LogWarning(
                    $"[AddSelfToHand] OnDestroyed 手札戻し不可 (cardId:{sourceCard?.Data?.id ?? -1})。");
                return;
            }

            if (TryReturnDestroyedUnitToHandViaTrash(sourceCard, sourceOwner, rule))
            {
                Debug.Log(
                    $"[Effect] AddSelfToHand(OnDestroyed via trash) "
                    + $"{sourceCard.Data.cardName}(id:{sourceCard.Data.id}) → {sourceOwner} hand");
            }

            return;
        }

        Debug.LogWarning(
            $"[AddSelfToHand] OnBurst / OnDestroyed 以外では未対応です (cardId:{sourceCard?.Data?.id ?? -1})。");
    }

    private void ApplyDeploySelfToShieldEffect(
        CardController sourceCard,
        PlayerType sourceOwner,
        EffectData effect,
        int magnitude)
    {
        PlayerType recipient = ResolveEffectOwnerPlayerType(sourceOwner, effect != null ? effect.target : TargetType.SelfPlayer);
        CardGameRule rule = recipient == PlayerType.Player ? cardGameRule : enemyCardGameRule;

        if (!IsResolvingBurstEffect)
        {
            Debug.LogWarning(
                $"[DeploySelfToShield] OnBurst 以外では未対応です (cardId:{sourceCard?.Data?.id ?? -1})。");
            return;
        }

        if (sourceCard == null || recipient != sourceOwner || rule == null)
        {
            Debug.LogWarning(
                $"[DeploySelfToShield] バースト元カードをシールドへ配備できません (cardId:{sourceCard?.Data?.id ?? -1})。");
            return;
        }

        int count = magnitude > 0 ? magnitude : 1;
        int applied = 0;
        for (int i = 0; i < count; i++)
        {
            if (!TryMoveBurstSourceCardToShieldZone(sourceCard, recipient, rule))
            {
                if (applied == 0)
                {
                    Debug.LogWarning(
                        $"[DeploySelfToShield] burst source shield deploy failed side:{recipient} cardId:{sourceCard.Data?.id}");
                }

                break;
            }

            applied++;
            break;
        }

        Debug.Log(
            $"[Effect] DeploySelfToShield x{applied}/{count} target:{(effect != null ? effect.target.ToString() : "?")} "
            + $"by cardId:{sourceCard?.Data?.id ?? -1}");
    }

    /// <summary>
    /// 破壊時効果用。いったんトラッシュへ積み、直後に取り出して手札へ戻す。
    /// GameObject は Destroy せずバウンス同様に手札へ移す（パイプラインの再トラッシュは pending 解除で抑止）。
    /// </summary>
    private bool TryReturnDestroyedUnitToHandViaTrash(
        CardController card,
        PlayerType ownerType,
        CardGameRule rule)
    {
        if (card == null || card.Data == null || rule == null)
        {
            return false;
        }

        if (card.Data.IsUnitToken())
        {
            return false;
        }

        int cardId = card.Data.id;
        // 公式どおり「トラッシュに置いた後」手札へ（ZoneSync の一瞬の AddTrash は抑止）
        WithZoneSyncSuppressed(() =>
        {
            rule.AddCardToTrash(cardId);
            rule.TryRemoveCardFromTrash(cardId, out _);
        });

        // 搭乗パイロットが残っていれば先に手札へ（通常は破壊時に既に切り離し済み）
        if (card.Data.IsUnitLike() && card.MountedPilot != null)
        {
            CardController pilot = card.DetachMountedPilotWithoutDestroy();
            if (pilot != null)
            {
                TryReturnCardInstanceToHand(pilot, ownerType, rule);
            }
        }

        playerBattleZoneCards.Remove(card);
        enemyBattleZoneCards.Remove(card);
        unitsPendingSendToTrash.Remove(card);

        if (card.Data.IsUnitLike() && card.BattleInstanceId > 0)
        {
            ClearStatModifiersGrantedByDestroyedUnit(card, ownerType);
            RefreshAllFieldOwnerTurnPassives();
        }

        card.ResetRuntimeStatsFromData();
        card.CleanupUnitBattleMountVisuals();
        card.SetAttackFlg(AttackFlg.False);
        card.SetUnitRestVisual(false);
        card.RevealShieldFace();
        card.RebindClickHandler(OnCardClicked);
        card.transform.SetParent(rule.HandScrollContent, false);
        rule.ApplyHandZoneLayoutToCard(card);

        RegisterCardInHandLists(card, ownerType);
        TriggerOnHandAutoEffects(card, ownerType, skipHandZoneCheck: true);
        rule.RefreshHandCountDisplay();
        SyncResourceViewsFromRule(ToRuleSide(ownerType));
        _pendingOnDestroyedReturnedToHandCardId = cardId;
        return true;
    }

    private bool TryMoveBurstSourceCardToHand(CardController card, PlayerType ownerType, CardGameRule rule)
    {
        if (card == null || card.Data == null || rule?.HandScrollContent == null)
        {
            return false;
        }

        if (card.transform.IsChildOf(rule.HandScrollContent))
        {
            RegisterCardInHandLists(card, ownerType);
            rule.RefreshHandCountDisplay();
            return true;
        }

        rule.TryUnregisterShieldZoneCard(card);
        card.gameObject.SetActive(true);
        card.RevealShieldFace();
        card.ResetRuntimeStatsFromData();
        card.CleanupUnitBattleMountVisuals();
        card.SetAttackFlg(AttackFlg.False);
        card.SetUnitRestVisual(false);
        card.SetEligibleForShieldZoneDeploy(false);
        card.RebindClickHandler(OnCardClicked);
        card.transform.SetParent(rule.HandScrollContent, false);
        rule.ApplyHandZoneLayoutToCard(card);

        RegisterCardInHandLists(card, ownerType);
        TriggerOnHandAutoEffects(card, ownerType, skipHandZoneCheck: true);
        rule.RefreshHandCountDisplay();
        SyncResourceViewsFromRule(ToRuleSide(ownerType));
        Debug.Log(
            $"[AddSelfToHand] {card.Data.cardName}(id:{card.Data.id}) shield break → {ownerType} hand");
        return true;
    }

    private bool TryMoveBurstSourceCardToShieldZone(CardController card, PlayerType ownerType, CardGameRule rule)
    {
        if (card == null || card.Data == null || rule == null)
        {
            return false;
        }

        if (card.Data.type == Type.Base)
        {
            return false;
        }

        if (rule.IsRegisteredInShieldZone(card))
        {
            return true;
        }

        // 破壊切り離し後は親だけシールドゾーンに残るため、再登録してから成功扱い。
        if (rule.TryReregisterDetachedShieldCard(card))
        {
            Gundam2024RuleScript.PlayerSide ruleSide = ToRuleSide(ownerType);
            gundamRule.AddShieldCount(ruleSide, 1);
            TriggerShieldDeployedEffects(card, ownerType);
            SyncResourceViewsFromRule(ruleSide);
            return true;
        }

        // 破壊公開中はシールド解除済みのため、手札経由の可否チェックなしで再配備する。
        card.gameObject.SetActive(true);
        if (!TryDeployCardToShieldZone(card, ownerType, rule, requireEligibleFromHand: false))
        {
            return false;
        }

        Debug.Log(
            $"[DeploySelfToShield] {card.Data.cardName}(id:{card.Data.id}) shield break → {ownerType} shield zone");
        return true;
    }

    private PlayerType ResolveEffectOwnerPlayerType(PlayerType sourceOwner, TargetType target)
    {
        if (target == TargetType.EnemyPlayer)
        {
            return sourceOwner == PlayerType.Player ? PlayerType.Enemy : PlayerType.Player;
        }

        return sourceOwner;
    }

    private void TriggerTimedEffectsForCard(
        CardController sourceCard,
        PlayerType ownerType,
        EffectTiming timing,
        bool skipDeployShieldFromHandOnBaseReplace = false)
    {
        if (sourceCard == null || sourceCard.Data == null || sourceCard.Data.timedEffects == null)
        {
            return;
        }

        List<EffectData> pendingManualEffects = new List<EffectData>();
        EffectActivationContext activationContext = BuildActivationContext(ownerType, sourceCard);
        for (int i = 0; i < sourceCard.Data.timedEffects.Count; i++)
        {
            TimedEffectData timed = sourceCard.Data.timedEffects[i];
            if (timed == null || timed.timing != timing || !timed.HasResolvedEffects())
            {
                continue;
            }

            if (!EffectActivationEvaluator.AreTimedConditionsMet(timed, activationContext))
            {
                continue;
            }

            IReadOnlyList<EffectData> resolved = timed.GetResolvedEffects();
            for (int j = 0; j < resolved.Count; j++)
            {
                EffectData effect = resolved[j];
                if (effect == null)
                {
                    continue;
                }

                if (skipDeployShieldFromHandOnBaseReplace
                    && effect.type == EffectType.DeployShieldFromHand)
                {
                    continue;
                }

                if (effect.HasEffectActivationConditions()
                    && !EffectActivationEvaluator.AreAllConditionsMet(
                        effect.effectActivationConditions,
                        activationContext))
                {
                    continue;
                }

                // 手動選択以外は即時解決（バースト配備→AddShieldToHand がコミット前に終わるようにする）
                if (!EffectRequiresManualUnitSelection(effect))
                {
                    ApplyEffect(sourceCard, ownerType, effect);
                }
                else
                {
                    pendingManualEffects.Add(effect);
                }
            }
        }

        if (pendingManualEffects.Count > 0)
        {
            StartCoroutine(ResolveTimedEffectsForCardCoroutine(sourceCard, ownerType, timing, pendingManualEffects));
        }
        else if (timing == EffectTiming.OnBaseDeployed
            || timing == EffectTiming.OnShieldDeployed
            || timing == EffectTiming.OnRest)
        {
            SyncAllResourceViewsFromRule();
        }
    }

    private IEnumerator ResolveTimedEffectsForCardCoroutine(
        CardController sourceCard,
        PlayerType ownerType,
        EffectTiming timing,
        List<EffectData> effects)
    {
        if (sourceCard == null || effects == null || effects.Count == 0)
        {
            yield break;
        }

        int manualTotal = 0;
        for (int i = 0; i < effects.Count; i++)
        {
            if (EffectRequiresManualUnitSelection(effects[i]))
            {
                manualTotal++;
            }
        }

        int manualIndex = 0;
        for (int i = 0; i < effects.Count; i++)
        {
            EffectData effect = effects[i];
            if (effect == null)
            {
                continue;
            }

            if (EffectRequiresManualUnitSelection(effect))
            {
                manualIndex++;
                yield return ApplyTimedEffectResolvedCoroutine(
                    sourceCard,
                    ownerType,
                    effect,
                    timing,
                    manualIndex,
                    manualTotal);
                continue;
            }

            ApplyEffect(sourceCard, ownerType, effect);
        }

        if (timing == EffectTiming.OnBaseDeployed
            || timing == EffectTiming.OnShieldDeployed
            || timing == EffectTiming.OnRest)
        {
            SyncAllResourceViewsFromRule();
        }
    }

    /// <summary>
    /// OnBurst / OnBaseDeployed 等の自動解決。敵ユニット手動選択は UI または敵 AI で適用し、先頭ユニットへの誤適用を防ぐ。
    /// </summary>
    private void TryApplyTimedEffectResolved(CardController sourceCard, PlayerType ownerType, EffectData effect)
    {
        if (!EffectRequiresManualUnitSelection(effect))
        {
            ApplyEffect(sourceCard, ownerType, effect);
            return;
        }

        List<CardController> candidates = ResolveSelectableEffectTargets(sourceCard, ownerType, effect);
        if (candidates.Count == 0)
        {
            Debug.Log(
                $"[TimedEffect] Manual selection skipped — no candidates (timing effect target:{effect.target} cardId:{sourceCard?.Data?.id}).");
            return;
        }

        if (ownerType == PlayerType.Enemy)
        {
            EnemyAiEffectPickContext pickCtx = BuildEnemyAiEffectPickContext(ownerType, sourceCard, null, null);
            CardController picked = PickEnemyAiEffectTarget(effect, pickCtx, candidates);
            if (picked != null)
            {
                ApplyEffectToSpecificTargets(sourceCard, ownerType, effect, new List<CardController> { picked });
            }

            return;
        }

        Debug.LogWarning(
            $"[TimedEffect] Player manual selection requires coroutine resolve (cardId:{sourceCard?.Data?.id}).");
    }

    private IEnumerator ApplyTimedEffectResolvedCoroutine(
        CardController sourceCard,
        PlayerType ownerType,
        EffectData effect,
        EffectTiming timing,
        int manualPickIndex = 0,
        int manualPickTotal = 0)
    {
        if (!EffectRequiresManualUnitSelection(effect))
        {
            ApplyEffect(sourceCard, ownerType, effect);
            yield break;
        }

        List<CardController> candidates = ResolveSelectableEffectTargets(sourceCard, ownerType, effect);
        if (candidates.Count == 0)
        {
            Debug.Log(
                $"[Burst] Manual selection skipped — no candidates (target:{effect.target} cardId:{sourceCard?.Data?.id}).");
            yield break;
        }

        if (ownerType == PlayerType.Enemy)
        {
            EnemyAiEffectPickContext pickCtx = BuildEnemyAiEffectPickContext(ownerType, sourceCard, null, null);
            CardController picked = PickEnemyAiEffectTarget(effect, pickCtx, candidates);
            if (picked != null)
            {
                ApplyEffectToSpecificTargets(sourceCard, ownerType, effect, new List<CardController> { picked });
            }

            yield break;
        }

        string cardLabel = sourceCard != null && sourceCard.Data != null
            ? sourceCard.Data.cardName
            : "?";
        string timingLabel = timing switch
        {
            EffectTiming.OnRest => "OnRest",
            EffectTiming.OnBaseDeployed => "配備効果",
            EffectTiming.OnShieldDeployed => "シールド配備",
            EffectTiming.OnBurst => "バースト",
            _ => "効果",
        };
        string title = manualPickTotal > 1
            ? $"{timingLabel} {manualPickIndex}/{manualPickTotal} — 対象を選択"
            : $"{timingLabel} — 対象を選択";
        string summary = effect != null
            ? $"{cardLabel}：{effect.FormatEffectSelectionSummary()}"
            : cardLabel;
        string titleForEffect = effect != null && effect.type == EffectType.Bounce
            ? (manualPickTotal > 1
                ? $"バウンス {manualPickIndex}/{manualPickTotal} — 手札に戻すユニット"
                : "バウンス — 手札に戻すユニットを選択")
            : effect != null && effect.type == EffectType.Rest
                ? (manualPickTotal > 1
                    ? $"REST {manualPickIndex}/{manualPickTotal} — 対象ユニット"
                    : "REST — 対象ユニットを選択")
                : title;

        yield return WaitForPlayerBurstTargetSelectionCoroutine(
            sourceCard,
            ownerType,
            effect,
            candidates,
            titleForEffect,
            summary);
    }

    private IEnumerator WaitForPlayerBurstTargetSelectionCoroutine(
        CardController sourceCard,
        PlayerType ownerType,
        EffectData effect,
        List<CardController> candidates,
        string title,
        string effectSummary)
    {
        bool selectionFinished = false;
        GameObject uiRoot = OpenCommandWithTargetsSelectionUI(
            title,
            effectSummary,
            sourceCard,
            candidates,
            null,
            picked =>
            {
                selectionFinished = true;
                ApplyEffectToSpecificTargets(sourceCard, ownerType, effect, new List<CardController> { picked });
            },
            () => { selectionFinished = true; });

        if (uiRoot == null)
        {
            Debug.LogWarning($"[Burst] Target selection UI could not open (cardId:{sourceCard?.Data?.id}).");
            yield break;
        }

        yield return new WaitUntil(() => selectionFinished);
        yield return new WaitUntil(() => !isOnActionPopupOpen);
        yield return null;
    }

    private static void CollectBurstManualTargetSteps(
        ShieldBreakTaken taken,
        PlayerType ownerType,
        BattleGameMain host,
        List<BurstManualTargetStep> manualSteps)
    {
        if (taken.Data == null || taken.Data.timedEffects == null || host == null)
        {
            return;
        }

        EffectActivationContext activationContext = host.BuildActivationContext(ownerType, taken.Controller);
        for (int i = 0; i < taken.Data.timedEffects.Count; i++)
        {
            TimedEffectData timed = taken.Data.timedEffects[i];
            if (timed == null || timed.timing != EffectTiming.OnBurst || !timed.HasResolvedEffects())
            {
                continue;
            }

            if (!EffectActivationEvaluator.AreTimedConditionsMet(timed, activationContext))
            {
                continue;
            }

            IReadOnlyList<EffectData> resolved = timed.GetResolvedEffects();
            for (int j = 0; j < resolved.Count; j++)
            {
                EffectData effect = resolved[j];
                if (effect != null && EffectRequiresManualUnitSelection(effect))
                {
                    manualSteps.Add(new BurstManualTargetStep { Taken = taken, Effect = effect });
                }
            }
        }
    }

    /// <summary>複数枚のバーストを順に解決（手動対象は1枚ごとにUI）。</summary>
    private IEnumerator ResolveBurstEffectsForTakenCardsCoroutine(
        IReadOnlyList<ShieldBreakTaken> takenCards,
        PlayerType shieldOwner)
    {
        if (takenCards == null || takenCards.Count == 0)
        {
            yield break;
        }

        burstEffectResolutionDepth++;
        try
        {
            List<BurstManualTargetStep> manualSteps = new List<BurstManualTargetStep>();
            for (int i = 0; i < takenCards.Count; i++)
            {
                CollectBurstManualTargetSteps(takenCards[i], shieldOwner, this, manualSteps);
            }

            for (int i = 0; i < takenCards.Count; i++)
            {
                ShieldBreakTaken taken = takenCards[i];
                if (taken.Data == null)
                {
                    continue;
                }

                CardController source = taken.Controller;
                if (source == null || source.Data == null)
                {
                    Debug.LogWarning($"[Burst] No visual for shield card id:{taken.CardId} — auto effects skipped.");
                    continue;
                }

                bool preferDeployBase = IsBaseCardWithDeployBaseBurst(taken.Data);
                if (preferDeployBase)
                {
                    burstDeployBasePreferSourceCard = true;
                }

                try
                {
                    Debug.Log($"[Burst] {taken.Data.cardName}(id:{taken.Data.id}) owner:{shieldOwner}");
                    EffectActivationContext activationContext = BuildActivationContext(shieldOwner, source);
                    for (int t = 0; t < taken.Data.timedEffects.Count; t++)
                    {
                        TimedEffectData timed = taken.Data.timedEffects[t];
                        if (timed == null || timed.timing != EffectTiming.OnBurst || !timed.HasResolvedEffects())
                        {
                            continue;
                        }

                        if (!EffectActivationEvaluator.AreTimedConditionsMet(timed, activationContext))
                        {
                            continue;
                        }

                        IReadOnlyList<EffectData> resolved = timed.GetResolvedEffects();
                        bool waitBurstAsync = false;
                        for (int e = 0; e < resolved.Count; e++)
                        {
                            EffectData effect = resolved[e];
                            if (effect == null || EffectRequiresManualUnitSelection(effect))
                            {
                                continue;
                            }

                            if (effect.type == EffectType.ChooseOne
                                || effect.type == EffectType.DeploySelfAsBattleUnit
                                || effect.type == EffectType.ActivateSelfOnMain)
                            {
                                waitBurstAsync = true;
                                bool done = false;
                                ApplyEffectRespectingLookAsync(
                                    source,
                                    shieldOwner,
                                    effect,
                                    () => done = true);
                                yield return new WaitUntil(() => done);
                                continue;
                            }

                            ApplyEffect(source, shieldOwner, effect);
                        }

                        // ChooseOne 解決後は同一 OnBurst ブロックの手動ステップへ
                        if (waitBurstAsync)
                        {
                            // no-op: フラグはログ用
                        }
                    }
                }
                finally
                {
                    if (preferDeployBase)
                    {
                        burstDeployBasePreferSourceCard = false;
                    }
                }
            }

            int manualTotal = manualSteps.Count;
            for (int i = 0; i < manualSteps.Count; i++)
            {
                BurstManualTargetStep step = manualSteps[i];
                CardController source = step.Taken.Controller;
                if (source == null || source.Data == null)
                {
                    Debug.LogWarning(
                        $"[Burst] Manual step skipped — no controller (cardId:{step.Taken.CardId}).");
                    continue;
                }

                yield return ApplyTimedEffectResolvedCoroutine(
                    source,
                    shieldOwner,
                    step.Effect,
                    EffectTiming.OnBurst,
                    i + 1,
                    manualTotal);
            }

            SyncAllResourceViewsFromRule();
        }
        finally
        {
            burstEffectResolutionDepth--;
        }
    }

    private void CommitShieldBreakTakenAfterBurst(
        ShieldBreakTaken taken,
        CardGameRule rule,
        PlayerType? shieldOwner = null)
    {
        if (rule == null)
        {
            return;
        }

        PlayerType ownerType = shieldOwner ?? (rule == cardGameRule ? PlayerType.Player : PlayerType.Enemy);
        bool keepCard = IsBurstCardRetainedForCommit(taken.Controller, rule, ownerType);
        if (!keepCard
            && taken.Data != null
            && taken.Data.IsPilot()
            && HasAddSelfToHandOnBurst(taken.Data)
            && TryMoveBurstSourceCardToHand(taken.Controller, ownerType, rule))
        {
            keepCard = IsBurstCardRetainedForCommit(taken.Controller, rule, ownerType);
        }

        if (!keepCard
            && taken.Data != null
            && HasDeploySelfToShieldOnBurst(taken.Data)
            && TryMoveBurstSourceCardToShieldZone(taken.Controller, ownerType, rule))
        {
            keepCard = IsBurstCardRetainedForCommit(taken.Controller, rule, ownerType);
        }

        if (!keepCard)
        {
            rule.CommitShieldCardToTrash(taken);
            RecordRemoteShieldBreakTrashedCardIdIfNeeded(taken);
        }
    }

    private bool IsBurstCardRetainedForCommit(CardController card, CardGameRule rule, PlayerType ownerType)
    {
        return IsBurstCardRetained(card, rule);
    }

    private void TriggerBaseDeployedEffects(CardController baseCard, PlayerType ownerType, bool replacingExistingBaseLayer = false)
    {
        TriggerTimedEffectsForCard(
            baseCard,
            ownerType,
            EffectTiming.OnBaseDeployed,
            skipDeployShieldFromHandOnBaseReplace: replacingExistingBaseLayer);
    }

    private void TriggerShieldDeployedEffects(CardController shieldCard, PlayerType ownerType)
    {
        TriggerTimedEffectsForCard(shieldCard, ownerType, EffectTiming.OnShieldDeployed);
    }

    private void DeployShieldCardFromHand(
        CardController shieldCard,
        PlayerType ownerType,
        CardGameRule ownerRule)
    {
        if (shieldCard == null || shieldCard.Data == null || !CanDeployShieldFromHand(shieldCard))
        {
            return;
        }

        Gundam2024RuleScript.PlayerSide ruleSide = ToRuleSide(ownerType);
        RemoveCardFromHandLists(shieldCard, ownerType);
        if (!ownerRule.TryAttachShieldCardFromHand(shieldCard))
        {
            RegisterShieldCardInHandLists(shieldCard, ownerType);
            Debug.LogWarning("[ShieldDeploy] TryAttachShieldCardFromHand failed.");
            return;
        }

        gundamRule.AddShieldCount(ruleSide, 1);
        Debug.Log(
            $"[ShieldDeploy] {shieldCard.Data.cardName}(id:{shieldCard.Data.id}) side:{ownerType} hand → shield zone");

        TriggerShieldDeployedEffects(shieldCard, ownerType);
        SyncResourceViewsFromRule(ruleSide);
        NotifyLocalDeployShieldSynced(shieldCard, ownerType);
    }
}
