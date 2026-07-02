using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>ベースカードの配備（シールドゾーン EX 枠）とシールド攻撃時のベース HP 処理。</summary>
public partial class BattleGameMain
{
    /// <summary>オンライン同期用。直近の配備ベース HP 変化（-1=なし、0=破壊、1+=現在HP）。</summary>
    private int _pendingDefenderDeployedBaseHpForOnlineSync = -1;

    private void ClearPendingDefenderDeployedBaseHpForOnlineSync()
    {
        _pendingDefenderDeployedBaseHpForOnlineSync = -1;
    }

    private void MarkPendingDefenderDeployedBaseHpForOnlineSync(int hpAfter)
    {
        _pendingDefenderDeployedBaseHpForOnlineSync = Mathf.Max(0, hpAfter);
    }

    private int ConsumePendingDefenderDeployedBaseHpForOnlineSync()
    {
        int value = _pendingDefenderDeployedBaseHpForOnlineSync;
        _pendingDefenderDeployedBaseHpForOnlineSync = -1;
        return value;
    }

    private int ResolveOnlineSyncDeployedBaseHp(Gundam2024RuleScript.PlayerSide defenderSide)
    {
        CardController baseCard = GetDeployedBaseForRuleSide(defenderSide);
        if (baseCard != null && baseCard.Data != null)
        {
            return Mathf.Max(0, baseCard.CurrentHp);
        }

        return -1;
    }

    private void RegisterBaseProtectionCallbacks()
    {
        if (gundamRule == null)
        {
            return;
        }

        gundamRule.HasActiveDeployedBase = HasActiveDeployedBaseForRuleSide;
    }

    private CardGameRule GetCardRuleForRuleSide(Gundam2024RuleScript.PlayerSide side)
    {
        return side == Gundam2024RuleScript.PlayerSide.Player ? cardGameRule : enemyCardGameRule;
    }

    private CardController GetDeployedBaseForRuleSide(Gundam2024RuleScript.PlayerSide side)
    {
        CardGameRule rule = GetCardRuleForRuleSide(side);
        if (rule == null)
        {
            return null;
        }

        if (rule.DeployedBase != null)
        {
            return rule.DeployedBase;
        }

        if (rule.BaseSlotContent == null)
        {
            return null;
        }

        for (int i = 0; i < rule.BaseSlotContent.childCount; i++)
        {
            CardController occupant = rule.BaseSlotContent.GetChild(i).GetComponent<CardController>();
            if (occupant != null && occupant.Data != null && occupant.Data.type == Type.Base && occupant.CurrentHp > 0)
            {
                return occupant;
            }
        }

        return null;
    }

    private bool HasActiveDeployedBaseForRuleSide(Gundam2024RuleScript.PlayerSide side)
    {
        CardController baseCard = GetDeployedBaseForRuleSide(side);
        return baseCard != null && baseCard.Data != null && baseCard.CurrentHp > 0;
    }

    private bool IsCardInBaseSlot(CardController card)
    {
        if (card == null)
        {
            return false;
        }

        return (cardGameRule.BaseSlotContent != null && card.transform.IsChildOf(cardGameRule.BaseSlotContent))
            || (enemyCardGameRule.BaseSlotContent != null && card.transform.IsChildOf(enemyCardGameRule.BaseSlotContent));
    }

    private const string DeployedBaseHpOverlayName = "BaseHpOverlay";

    private void RefreshDeployedBaseHpOverlay(CardController baseCard)
    {
        if (baseCard == null || baseCard.Data == null)
        {
            return;
        }

        Transform existing = baseCard.transform.Find(DeployedBaseHpOverlayName);
        TextMeshProUGUI hpText;
        if (existing == null)
        {
            GameObject overlayRoot = new GameObject(DeployedBaseHpOverlayName, typeof(RectTransform), typeof(Image));
            overlayRoot.transform.SetParent(baseCard.transform, false);
            overlayRoot.transform.SetAsLastSibling();
            RectTransform overlayRt = overlayRoot.GetComponent<RectTransform>();
            overlayRt.anchorMin = new Vector2(0f, 1f);
            overlayRt.anchorMax = new Vector2(1f, 1f);
            overlayRt.pivot = new Vector2(0.5f, 1f);
            overlayRt.sizeDelta = new Vector2(0f, 20f);
            overlayRt.anchoredPosition = Vector2.zero;
            Image bg = overlayRoot.GetComponent<Image>();
            UIExtensions.ApplySolidUiImage(bg, new Color(0.1f, 0.1f, 0.15f, 0.85f));

            hpText = overlayRoot.CreateChildTextCustom("BaseHpText", UIAnchor.FullSize, 58, 20);
            hpText.fontSize = 14;
            hpText.fontStyle = FontStyles.Bold;
            hpText.color = Color.white;
            hpText.alignment = TextAlignmentOptions.Center;
            hpText.enableWordWrapping = false;
        }
        else
        {
            hpText = existing.GetComponentInChildren<TextMeshProUGUI>();
        }

        if (hpText != null)
        {
            hpText.text = $"HP {baseCard.CurrentHp}";
        }
    }

    private void SyncBaseZoneHeaderDisplay(Gundam2024RuleScript.PlayerSide side)
    {
        CardGameRule rule = GetCardRuleForRuleSide(side);
        if (rule == null)
        {
            return;
        }

        CardController baseCard = rule.DeployedBase;
        if (baseCard != null && baseCard.Data != null)
        {
            RefreshDeployedBaseHpOverlay(baseCard);
            return;
        }

        Gundam2024RuleScript.PlayerState state = side == Gundam2024RuleScript.PlayerSide.Player
            ? gundamRule.Player
            : gundamRule.Enemy;
        rule.SetExBaseDisplay(state.exBase);
    }

    /// <summary>手札からの配備用。CanPlayCard とリソース消費をまとめて行う（バースト配備は使わない）。</summary>
    private bool TryPayHandDeployCost(Gundam2024RuleScript.PlayerSide side, CardController card, int exToUse = 0)
    {
        if (card == null || card.Data == null || gundamRule == null)
        {
            return false;
        }

        int requiredLevel = card.CurrentLevel;
        int cost = card.CurrentCost;
        if (!gundamRule.CanPlayCard(side, requiredLevel, cost, exToUse))
        {
            Gundam2024RuleScript.PlayerState state = GetRuleState(side);
            Debug.Log(
                $"[DeployPay] Cannot play from hand card:{card.Data.cardName}(id:{card.Data.id}) "
                + $"lvReq:{requiredLevel} cost:{cost} exUse:{exToUse} side:{side} "
                + $"totalLv:{state.TotalLevel} resource:{state.resource} exRes:{state.exResource}");
            return false;
        }

        if (!gundamRule.TryConsumeResource(side, cost, exToUse, card.Data.id, requiredLevel))
        {
            return false;
        }

        return true;
    }

    private void BeginDeployBaseFromHand(CardController cardController, PlayerType ownerType, CardGameRule ownerRule)
    {
        if (cardController == null || cardController.Data == null || cardController.Data.type != Type.Base)
        {
            return;
        }

        DeployBaseFromHand(cardController, ownerType, ownerRule);
    }

    private void DeployBaseFromHand(
        CardController cardController,
        PlayerType ownerType,
        CardGameRule ownerRule)
    {
        if (DeployCardToBaseZone(cardController, ownerType, ownerRule, triggerOnPlayed: true))
        {
            Debug.Log(
                $"[BaseDeploy] {cardController.Data.cardName}(id:{cardController.Data.id}) side:{ownerType} HP:{cardController.CurrentHp} (shields unchanged)");
        }
    }

    private bool HadActiveBaseLayerBeforeDeploy(PlayerType ownerType, CardGameRule ownerRule)
    {
        Gundam2024RuleScript.PlayerSide ruleSide = ToRuleSide(ownerType);
        Gundam2024RuleScript.PlayerState state = GetRuleState(ruleSide);
        if (state.exBase > 0)
        {
            return true;
        }

        CardController deployed = ownerRule != null ? ownerRule.DeployedBase : null;
        if (deployed != null && deployed.Data != null && deployed.CurrentHp > 0)
        {
            return true;
        }

        return ownerRule != null && ownerRule.HasOccupantInBaseSlot();
    }

    /// <summary>EX ベース（数値）とベース枠のカードを破壊してから新ベースを置く。</summary>
    private void DestroyExistingBaseLayerBeforeDeploy(
        PlayerType ownerType,
        CardGameRule ownerRule,
        CardController incomingCard)
    {
        if (ownerRule == null)
        {
            return;
        }

        Gundam2024RuleScript.PlayerSide ruleSide = ToRuleSide(ownerType);
        gundamRule.SetExBasePoints(ruleSide, 0);

        CardController registered = ownerRule.DeployedBase;
        if (registered != null && registered != incomingCard)
        {
            SendDeployedBaseToTrash(registered, ownerType, ownerRule);
        }

        if (ownerRule.BaseSlotContent == null)
        {
            return;
        }

        for (int i = ownerRule.BaseSlotContent.childCount - 1; i >= 0; i--)
        {
            CardController occupant = ownerRule.BaseSlotContent.GetChild(i).GetComponent<CardController>();
            if (occupant != null && occupant != incomingCard)
            {
                SendDeployedBaseToTrash(occupant, ownerType, ownerRule);
            }
        }
    }

    /// <summary>ベース配備前にシールドゾーン登録を外し、ゾーン／バースト昇格ならシールド枚数を1減らす。</summary>
    private void PrepareCardForBaseZoneDeploy(
        CardController cardController,
        PlayerType ownerType,
        CardGameRule ownerRule,
        Gundam2024RuleScript.PlayerSide ruleSide,
        ref bool wasInHand)
    {
        if (cardController == null || ownerRule == null)
        {
            return;
        }

        cardController.SetEligibleForShieldZoneDeploy(false);

        bool inShieldZone = ownerRule.ShieldCardsContent != null
            && cardController.transform.IsChildOf(ownerRule.ShieldCardsContent);
        bool wasTrackedInShieldZone = ownerRule.TryUnregisterShieldZoneCard(cardController);

        if (!wasInHand && (inShieldZone || wasTrackedInShieldZone || burstDeployBasePreferSourceCard))
        {
            if (!gundamRule.TryReduceShieldCountForHandMove(ruleSide, 1))
            {
                Debug.LogWarning(
                    $"[BaseDeploy] Shield count could not be reduced when promoting {cardController.Data?.cardName} to base.");
            }
        }
    }

    /// <summary>ベースカードを EX ベース枠へ配備する共通処理。</summary>
    private bool DeployCardToBaseZone(
        CardController cardController,
        PlayerType ownerType,
        CardGameRule ownerRule,
        bool triggerOnPlayed)
    {
        if (cardController == null || cardController.Data == null || cardController.Data.type != Type.Base || ownerRule == null)
        {
            return false;
        }

        if (IsCardInBaseSlot(cardController))
        {
            return true;
        }

        Gundam2024RuleScript.PlayerSide ruleSide = ToRuleSide(ownerType);
        bool replacingBaseLayer = HadActiveBaseLayerBeforeDeploy(ownerType, ownerRule);
        DestroyExistingBaseLayerBeforeDeploy(ownerType, ownerRule, cardController);

        bool wasInHand = ownerRule.HandScrollContent != null
            && cardController.transform.IsChildOf(ownerRule.HandScrollContent);
        PrepareCardForBaseZoneDeploy(cardController, ownerType, ownerRule, ruleSide, ref wasInHand);
        if (ownerType == PlayerType.Player && wasInHand)
        {
            RecordEnemyAiObservedPlayerCardPlay(cardController, "DeployBase");
        }

        RemoveCardFromHandLists(cardController, ownerType);
        cardController.RevealShieldFace();
        ownerRule.AttachDeployedBaseCard(cardController);
        cardController.ResetRuntimeStatsFromData();
        RefreshDeployedBaseHpOverlay(cardController);

        if (triggerOnPlayed)
        {
            TriggerOnPlayedEffects(cardController, ownerType, RefreshAllHandsConditionalOnHandAuto);
        }

        TriggerBaseDeployedEffects(cardController, ownerType, replacingBaseLayer);
        SyncResourceViewsFromRule(ruleSide);
        SyncBaseZoneHeaderDisplay(ruleSide);

        if (replacingBaseLayer)
        {
            Debug.Log(
                $"[BaseDeploy] Replaced base layer with {cardController.Data.cardName}(id:{cardController.Data.id}) side:{ownerType}");
        }

        NotifyLocalDeployBaseSynced(cardController, ownerType);
        return true;
    }

    private void ApplyDeployBaseEffect(
        CardController sourceCard,
        PlayerType sourceOwner,
        EffectData effect,
        int magnitude,
        bool allowBurstSource = false)
    {
        PlayerType recipient = ResolveEffectOwnerPlayerType(sourceOwner, effect.target);
        CardGameRule rule = recipient == PlayerType.Player ? cardGameRule : enemyCardGameRule;

        int applied = 0;
        for (int i = 0; i < magnitude; i++)
        {
            if (allowBurstSource
                && sourceCard != null
                && recipient == sourceOwner
                && applied == 0
                && DeployCardToBaseZone(sourceCard, recipient, rule, triggerOnPlayed: false))
            {
                Debug.Log(
                    $"[DeployBase] burst source {sourceCard.Data.cardName}(id:{sourceCard.Data.id}) → {recipient} base zone");
                applied++;
                continue;
            }

            if (applied == 0)
            {
                Debug.LogWarning($"[DeployBase] No deployable base for burst side:{recipient}");
            }

            break;
        }

        Debug.Log(
            $"[Effect] DeployBase x{applied}/{magnitude} target:{effect.target} "
            + $"by cardId:{sourceCard?.Data?.id ?? -1}");
    }

    private void RemoveCardFromHandLists(CardController cardController, PlayerType ownerType)
    {
        if (cardController == null || cardController.Data == null)
        {
            return;
        }

        if (ownerType == PlayerType.Player)
        {
            playerHandCards.Remove(cardController.Data);
        }
        else
        {
            enemyHandCards.Remove(cardController.Data);
        }
    }

    private void SendDeployedBaseToTrash(CardController baseCard, PlayerType ownerType, CardGameRule ownerRule)
    {
        if (baseCard == null)
        {
            return;
        }

        ownerRule.ClearDeployedBaseCard();
        SendCardToTrash(baseCard, ownerType);
    }

    private bool TryApplyEffectDamageToDeployedBase(Gundam2024RuleScript.PlayerSide targetSide, int baseMagnitude, out string logMessage)
    {
        logMessage = null;
        if (baseMagnitude <= 0)
        {
            return false;
        }

        CardController defenderBase = GetDeployedBaseForRuleSide(targetSide);
        if (defenderBase == null || defenderBase.Data == null || defenderBase.CurrentHp <= 0)
        {
            return false;
        }

        if (defenderBase.HasEffectDamageImmunity)
        {
            Debug.Log(
                $"[EffectDamage] Blocked base damage — {defenderBase.Data.cardName} has EffectDamageImmunity (fall through to EX/shield).");
            return false;
        }

        int amount = ResolveEffectDamageAmount(baseMagnitude, defenderBase);
        if (amount <= 0)
        {
            return false;
        }

        int hpBefore = defenderBase.CurrentHp;
        defenderBase.ApplyDamage(amount);
        MarkPendingDefenderDeployedBaseHpForOnlineSync(defenderBase.CurrentHp);
        PlayerType defenderOwner = targetSide == Gundam2024RuleScript.PlayerSide.Player
            ? PlayerType.Player
            : PlayerType.Enemy;
        CardGameRule defenderRule = GetCardRuleForRuleSide(targetSide);

        logMessage =
            $"[EffectDamage] Dealt {amount} to Base {defenderBase.Data.cardName} HP:{hpBefore}→{defenderBase.CurrentHp}";

        SyncBaseZoneHeaderDisplay(targetSide);

        if (defenderBase.CurrentHp <= 0)
        {
            MarkPendingDefenderDeployedBaseHpForOnlineSync(0);
            SendDeployedBaseToTrash(defenderBase, defenderOwner, defenderRule);
            SyncResourceViewsFromRule(targetSide);
            logMessage += " (destroyed)";
        }

        return true;
    }

    /// <summary>
    /// 効果ダメージによるプレイヤー領域へのダメージ。
    /// 配備ベース → EXベース（いずれも value 分）→ シールド1枚のみの順。戦闘交換ダメージとは別経路。
    /// baseMagnitude は生の効果量。配備ベースは自身の修飾のみ適用、EX/シールドは修飾なし。
    /// </summary>
    private void ApplyEffectDamageToPlayerArea(Gundam2024RuleScript.PlayerSide targetSide, int baseMagnitude)
    {
        if (baseMagnitude <= 0 || gundamRule == null)
        {
            return;
        }

        Gundam2024RuleScript.PlayerState target = targetSide == Gundam2024RuleScript.PlayerSide.Player
            ? gundamRule.Player
            : gundamRule.Enemy;
        int shieldBefore = target != null ? target.shield : 0;
        int exBaseBefore = target != null ? target.exBase : 0;

        if (TryApplyEffectDamageToDeployedBase(targetSide, baseMagnitude, out string baseLog))
        {
            Debug.Log(baseLog);
            SyncResourceViewsFromRule(targetSide);
            TryNotifyOnlineDefenderAreaStateAfterEffectDamage(targetSide, shieldBefore, exBaseBefore);
            return;
        }

        int exDamage = ResolveEffectDamageAmount(baseMagnitude);
        if (target != null && target.exBase > 0 && exDamage > 0)
        {
            gundamRule.DamageExBaseOnly(targetSide, exDamage);
            Debug.Log($"[EffectDamage] Dealt {exDamage} to EX Base (now {target.exBase}).");
            SyncResourceViewsFromRule(targetSide);
            TryNotifyOnlineDefenderAreaStateAfterEffectDamage(targetSide, shieldBefore, exBaseBefore);
            return;
        }

        // シールド攻撃フロー中はベース層（配備ベース/EX）消化後も実シールドを割らない。
        if (blockShieldFlowDuringShieldAttack && targetSide == blockedShieldFlowSide)
        {
            Debug.Log(
                $"[EffectDamage] Shield-attack flow — skipped shield break (side:{targetSide}, amount:{baseMagnitude}).");
            return;
        }

        if (target != null && target.shield > 0)
        {
            gundamRule.DamageShield(targetSide, 1, simultaneousReveal: false);
            Debug.Log($"[EffectDamage] Broke 1 shield (effect value:{baseMagnitude} does not multiply shield breaks).");
            SyncResourceViewsFromRule(targetSide);
            TryNotifyOnlineDefenderAreaStateAfterEffectDamage(targetSide, shieldBefore, exBaseBefore);
        }
    }

    private void TryNotifyOnlineDefenderAreaStateAfterEffectDamage(
        Gundam2024RuleScript.PlayerSide targetSide,
        int shieldBefore,
        int exBaseBefore)
    {
        if (!IsOnlineBattle()
            || currentPlayerType != PlayerType.Player
            || _applyingRemoteBattleAction
            || targetSide != Gundam2024RuleScript.PlayerSide.Enemy)
        {
            return;
        }

        Gundam2024RuleScript.PlayerState defender = gundamRule.Enemy;
        if (defender.shield == shieldBefore
            && defender.exBase == exBaseBefore
            && _pendingDefenderDeployedBaseHpForOnlineSync < 0)
        {
            return;
        }

        NotifyLocalDefenderAreaStateSync();
    }

    private bool TryApplyShieldAttackDamageToDeployedBase(
        CardController attacker,
        Gundam2024RuleScript.PlayerSide targetSide,
        out string logMessage)
    {
        logMessage = null;
        CardController defenderBase = GetDeployedBaseForRuleSide(targetSide);
        if (defenderBase == null || defenderBase.Data == null || defenderBase.CurrentHp <= 0)
        {
            return false;
        }

        int power = attacker != null ? attacker.CurrentPower : 0;
        if (power <= 0)
        {
            return false;
        }

        int hpBefore = defenderBase.CurrentHp;
        defenderBase.ApplyDamage(power);
        MarkPendingDefenderDeployedBaseHpForOnlineSync(defenderBase.CurrentHp);
        PlayerType defenderOwner = targetSide == Gundam2024RuleScript.PlayerSide.Player
            ? PlayerType.Player
            : PlayerType.Enemy;
        CardGameRule defenderRule = GetCardRuleForRuleSide(targetSide);

        logMessage =
            $"[Attack] Shield attack dealt {power} to Base {defenderBase.Data.cardName} HP:{hpBefore}→{defenderBase.CurrentHp}";

        SyncBaseZoneHeaderDisplay(targetSide);

        if (defenderBase.CurrentHp <= 0)
        {
            MarkPendingDefenderDeployedBaseHpForOnlineSync(0);
            SendDeployedBaseToTrash(defenderBase, defenderOwner, defenderRule);
            SyncResourceViewsFromRule(targetSide);
            logMessage += " (destroyed)";
        }

        return true;
    }
}
