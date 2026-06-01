using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>ベースカードの配備（シールドゾーン EX 枠）とシールド攻撃時のベース HP 処理。</summary>
public partial class BattleGameMain
{
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
        return rule != null ? rule.DeployedBase : null;
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
            bg.color = new Color(0.1f, 0.1f, 0.15f, 0.85f);
            bg.raycastTarget = false;

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
        PlayerType defenderOwner = targetSide == Gundam2024RuleScript.PlayerSide.Player
            ? PlayerType.Player
            : PlayerType.Enemy;
        CardGameRule defenderRule = GetCardRuleForRuleSide(targetSide);

        logMessage =
            $"[Attack] Shield attack dealt {power} to Base {defenderBase.Data.cardName} HP:{hpBefore}→{defenderBase.CurrentHp}";

        SyncBaseZoneHeaderDisplay(targetSide);

        if (defenderBase.CurrentHp <= 0)
        {
            SendDeployedBaseToTrash(defenderBase, defenderOwner, defenderRule);
            SyncResourceViewsFromRule(targetSide);
            logMessage += " (destroyed)";
        }

        return true;
    }
}
