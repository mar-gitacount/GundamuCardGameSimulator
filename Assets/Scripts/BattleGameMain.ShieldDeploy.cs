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
            || card.Data.type == Type.Pilot
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

        Gundam2024RuleScript.PlayerState state = GetRuleState(ruleSide);
        return state.shield > 0 && rule.HasShieldCardInZone;
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
            return false;
        }

        if (!targetRule.TryMoveTopShieldCardToHand(targetRule.HandScrollContent, out CardController shieldCard))
        {
            gundamRule.AddShieldCount(ruleSide, 1);
            return false;
        }

        shieldCard.SetEligibleForShieldZoneDeploy(true);
        RegisterShieldCardInHandLists(shieldCard, targetType);
        TriggerOnHandAutoEffects(shieldCard, targetType, skipHandZoneCheck: true);
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
        PlayerType recipient = ResolveEffectOwnerPlayerType(sourceOwner, effect.target);
        Gundam2024RuleScript.PlayerSide ruleSide = ToRuleSide(recipient);
        CardGameRule rule = recipient == PlayerType.Player ? cardGameRule : enemyCardGameRule;

        int applied = 0;
        for (int i = 0; i < magnitude; i++)
        {
            if (!TryMoveShieldFromZoneToHand(rule, recipient, ruleSide))
            {
                if (applied == 0)
                {
                    Debug.LogWarning(
                        $"[AddShieldToHand] No shield available side:{recipient} (shield count or zone card missing).");
                }

                break;
            }

            applied++;
        }

        Debug.Log(
            $"[Effect] AddShieldToHand x{applied}/{magnitude} target:{effect.target} "
            + $"by cardId:{sourceCard?.Data?.id ?? -1}");
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

        List<EffectData> pendingEffects = new List<EffectData>();
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

                pendingEffects.Add(effect);
            }
        }

        if (pendingEffects.Count > 0)
        {
            StartCoroutine(ResolveTimedEffectsForCardCoroutine(sourceCard, ownerType, timing, pendingEffects));
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
                        for (int e = 0; e < resolved.Count; e++)
                        {
                            EffectData effect = resolved[e];
                            if (effect == null || EffectRequiresManualUnitSelection(effect))
                            {
                                continue;
                            }

                            ApplyEffect(source, shieldOwner, effect);
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

    private static void CommitShieldBreakTakenAfterBurst(ShieldBreakTaken taken, CardGameRule rule)
    {
        if (rule == null)
        {
            return;
        }

        bool keepCard = IsBurstCardRetained(taken.Controller, rule);
        if (!keepCard)
        {
            rule.CommitShieldCardToTrash(taken);
        }
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
    }
}
