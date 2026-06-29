using System.Collections.Generic;
using UnityEngine;

/// <summary>ベース配備・シールドゾーン配備のオンライン同期。</summary>
public partial class BattleGameMain
{
    private void NotifyLocalDeployBaseSynced(CardController baseCard, PlayerType ownerType)
    {
        if (_applyingRemoteBattleAction
            || !IsOnlineBattle()
            || currentPlayerType != PlayerType.Player
            || ownerType != PlayerType.Player
            || baseCard == null
            || baseCard.Data == null)
        {
            return;
        }

        Gundam2024RuleScript.PlayerSide ruleSide = ToRuleSide(ownerType);
        Gundam2024RuleScript.PlayerState state = GetRuleState(ruleSide);
        CardGameRule rule = cardGameRule;
        int[] shieldIds = CollectShieldZoneCardIds(rule);

        SendOnlineBattleMessage(EosOnlineBattleMessage.CreatePlayCard(
            OnlineBattleActionPayload.CreateDeployBase(
                baseCard.Data.id,
                baseCard.CurrentHp,
                state.exBase,
                state.shield,
                shieldIds)));

        Debug.Log(
            $"[OnlineBattle] DeployBase sync sent. card={baseCard.Data.cardName}(id:{baseCard.Data.id}) "
            + $"hp:{baseCard.CurrentHp} exBase:{state.exBase} shield:{state.shield} zone:{shieldIds.Length}");
    }

    private void NotifyLocalDeployShieldSynced(CardController shieldCard, PlayerType ownerType)
    {
        if (_applyingRemoteBattleAction
            || !IsOnlineBattle()
            || currentPlayerType != PlayerType.Player
            || ownerType != PlayerType.Player
            || shieldCard == null
            || shieldCard.Data == null)
        {
            return;
        }

        Gundam2024RuleScript.PlayerSide ruleSide = ToRuleSide(ownerType);
        Gundam2024RuleScript.PlayerState state = GetRuleState(ruleSide);
        CardGameRule rule = cardGameRule;
        int[] shieldIds = CollectShieldZoneCardIds(rule);

        SendOnlineBattleMessage(EosOnlineBattleMessage.CreatePlayCard(
            OnlineBattleActionPayload.CreateDeployShield(
                shieldCard.Data.id,
                state.shield,
                shieldIds)));

        Debug.Log(
            $"[OnlineBattle] DeployShield sync sent. card={shieldCard.Data.cardName}(id:{shieldCard.Data.id}) "
            + $"shield:{state.shield} zone:{shieldIds.Length}");
    }

    private static int[] CollectShieldZoneCardIds(CardGameRule rule)
    {
        if (rule == null)
        {
            return System.Array.Empty<int>();
        }

        IReadOnlyList<int> ids = rule.GetShieldCardIds();
        int[] result = new int[ids.Count];
        for (int i = 0; i < ids.Count; i++)
        {
            result[i] = ids[i];
        }

        return result;
    }

    private void ApplyRemoteDeployBase(OnlineBattleActionPayload action)
    {
        if (DeckSettinObject.Instance == null || action == null || action.cardId <= 0)
        {
            return;
        }

        CardData cardData = DeckSettinObject.Instance.GetCardDataById(action.cardId);
        if (cardData == null)
        {
            Debug.LogWarning($"[OnlineBattle] Unknown base card id for remote deploy: {action.cardId}");
            return;
        }

        Gundam2024RuleScript.PlayerSide ruleSide = Gundam2024RuleScript.PlayerSide.Enemy;
        CardGameRule rule = enemyCardGameRule;
        _applyingRemoteBattleAction = true;
        try
        {
            ClearRemoteMirrorDeployedBaseVisual(rule);

            gundamRule.SetExBasePoints(ruleSide, Mathf.Max(0, action.defenderExBaseAfter));

            rule.ApplyShieldZoneSnapshotFromCardIds(
                CardImagePrefab,
                OnCardClicked,
                action.shieldZoneCardIds);

            GameObject cardObject = Instantiate(CardImagePrefab, rule.BaseSlotContent);
            CardController controller = cardObject.GetComponent<CardController>();
            controller.SetUp(cardData, OnCardClicked);
            controller.ResetRuntimeStatsFromData();
            if (action.baseHpAfter > 0)
            {
                controller.SetCurrentHpForSync(action.baseHpAfter);
            }

            controller.RevealShieldFace();
            rule.AttachDeployedBaseCard(controller);
            RefreshDeployedBaseHpOverlay(controller);

            Gundam2024RuleScript.PlayerState enemyState = gundamRule.Enemy;
            if (action.defenderShieldAfter >= 0)
            {
                enemyState.shield = action.defenderShieldAfter;
            }
            else
            {
                gundamRule.SyncShieldCountFromZone(ruleSide, rule.GetShieldZoneCardCount());
            }

            SyncResourceViewsFromRule(ruleSide);
            Debug.Log(
                $"[OnlineBattle] Remote base deployed on opponent zone: {cardData.cardName}({action.cardId}) "
                + $"hp:{controller.CurrentHp} exBase:{enemyState.exBase} shield:{enemyState.shield}");
        }
        finally
        {
            _applyingRemoteBattleAction = false;
        }
    }

    private void ApplyRemoteDeployShield(OnlineBattleActionPayload action)
    {
        if (DeckSettinObject.Instance == null || action == null || action.cardId <= 0)
        {
            return;
        }

        Gundam2024RuleScript.PlayerSide ruleSide = Gundam2024RuleScript.PlayerSide.Enemy;
        CardGameRule rule = enemyCardGameRule;
        _applyingRemoteBattleAction = true;
        try
        {
            rule.ApplyShieldZoneSnapshotFromCardIds(
                CardImagePrefab,
                OnCardClicked,
                action.shieldZoneCardIds);

            Gundam2024RuleScript.PlayerState enemyState = gundamRule.Enemy;
            if (action.defenderShieldAfter >= 0)
            {
                enemyState.shield = action.defenderShieldAfter;
            }
            else
            {
                gundamRule.SyncShieldCountFromZone(ruleSide, rule.GetShieldZoneCardCount());
            }

            SyncResourceViewsFromRule(ruleSide);
            Debug.Log(
                $"[OnlineBattle] Remote shield zone synced. addedCardId={action.cardId} "
                + $"shield:{enemyState.shield} zone:{rule.GetShieldZoneCardCount()}");
        }
        finally
        {
            _applyingRemoteBattleAction = false;
        }
    }

    private static void ClearRemoteMirrorDeployedBaseVisual(CardGameRule rule)
    {
        if (rule == null)
        {
            return;
        }

        CardController registered = rule.DeployedBase;
        if (registered != null)
        {
            Object.Destroy(registered.gameObject);
            rule.ClearDeployedBaseCard();
        }

        if (rule.BaseSlotContent == null)
        {
            return;
        }

        for (int i = rule.BaseSlotContent.childCount - 1; i >= 0; i--)
        {
            CardController occupant = rule.BaseSlotContent.GetChild(i).GetComponent<CardController>();
            if (occupant != null)
            {
                Object.Destroy(occupant.gameObject);
            }
        }

        rule.ClearDeployedBaseCard();
    }
}
