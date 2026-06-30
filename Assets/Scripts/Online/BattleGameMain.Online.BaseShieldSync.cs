using System.Collections.Generic;
using UnityEngine;

/// <summary>ベース配備・シールドゾーン配備のオンライン同期。</summary>
public partial class BattleGameMain
{
    private void NotifyLocalDeployBaseSynced(CardController baseCard, PlayerType ownerType)
    {
        if (_applyingRemoteBattleAction
            || !IsOnlineBattle()
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

    private void ApplyRemoteDeployedBaseHpUpdate(Gundam2024RuleScript.PlayerSide defenderSide, int defenderDeployedBaseHpAfter)
    {
        if (defenderDeployedBaseHpAfter < 0)
        {
            return;
        }

        CardGameRule rule = GetCardRuleForRuleSide(defenderSide);
        if (rule == null)
        {
            return;
        }

        PlayerType ownerType = defenderSide == Gundam2024RuleScript.PlayerSide.Player
            ? PlayerType.Player
            : PlayerType.Enemy;
        CardController baseCard = rule.DeployedBase;

        if (defenderDeployedBaseHpAfter <= 0)
        {
            if (baseCard != null)
            {
                SendDeployedBaseToTrash(baseCard, ownerType, rule);
                SyncResourceViewsFromRule(defenderSide);
            }

            SyncBaseZoneHeaderDisplay(defenderSide);
            return;
        }

        if (baseCard == null)
        {
            Debug.LogWarning(
                $"[OnlineBattle] Remote base HP sync ignored — no deployed base on side:{defenderSide} "
                + $"(hpAfter:{defenderDeployedBaseHpAfter}).");
            return;
        }

        baseCard.SetCurrentHpForSync(defenderDeployedBaseHpAfter);
        RefreshDeployedBaseHpOverlay(baseCard);
        SyncBaseZoneHeaderDisplay(defenderSide);
        Debug.Log(
            $"[OnlineBattle] Remote deployed base HP applied. side:{defenderSide} hp:{defenderDeployedBaseHpAfter}");
    }

    /// <summary>
    /// 攻撃側が受け取る ShieldBreakComplete 付属の防御側領域スナップショットを相手ミラーへ反映する。
    /// </summary>
    private void ApplyRemoteDefenderAreaSnapshotFromBurst(OnlineBattleActionPayload action)
    {
        if (action == null || !IsOnlineBattle() || DeckSettinObject.Instance == null)
        {
            return;
        }

        Gundam2024RuleScript.PlayerSide mirrorSide = Gundam2024RuleScript.PlayerSide.Enemy;
        CardGameRule rule = enemyCardGameRule;
        if (rule == null || gundamRule == null)
        {
            return;
        }

        _applyingRemoteBattleAction = true;
        try
        {
            Gundam2024RuleScript.PlayerState enemyState = gundamRule.Enemy;
            if (action.defenderExBaseAfter >= 0)
            {
                enemyState.exBase = Mathf.Max(0, action.defenderExBaseAfter);
            }

            if (action.defenderShieldAfter >= 0)
            {
                enemyState.shield = Mathf.Max(0, action.defenderShieldAfter);
            }

            if (action.shieldZoneCardIds != null)
            {
                rule.ApplyShieldZoneSnapshotFromCardIds(
                    CardImagePrefab,
                    OnCardClicked,
                    action.shieldZoneCardIds);
            }

            ApplyRemoteMirrorDeployedBaseFromSnapshot(action, rule, mirrorSide);

            if (action.defenderShieldAfter < 0 && action.shieldZoneCardIds != null)
            {
                gundamRule.SyncShieldCountFromZone(mirrorSide, rule.GetShieldZoneCardCount());
            }

            SyncResourceViewsFromRule(mirrorSide);
            ReconcileShieldStateWithZone(mirrorSide, force: true);
            SyncBaseZoneHeaderDisplay(mirrorSide);
            Debug.Log(
                $"[OnlineBattle] Remote burst aftermath applied. shield={enemyState.shield} exBase={enemyState.exBase} "
                + $"baseId={action.cardId} baseHp={action.defenderDeployedBaseHpAfter} zone={action.shieldZoneCardIds?.Length ?? 0}");
        }
        finally
        {
            _applyingRemoteBattleAction = false;
        }
    }

    private void ApplyRemoteMirrorDeployedBaseFromSnapshot(
        OnlineBattleActionPayload action,
        CardGameRule rule,
        Gundam2024RuleScript.PlayerSide mirrorSide)
    {
        if (action == null || rule == null)
        {
            return;
        }

        if (action.defenderDeployedBaseHpAfter < 0)
        {
            return;
        }

        if (action.defenderDeployedBaseHpAfter <= 0 || action.cardId <= 0)
        {
            ClearRemoteMirrorDeployedBaseVisual(rule);
            return;
        }

        CardData cardData = DeckSettinObject.Instance.GetCardDataById(action.cardId);
        if (cardData == null)
        {
            Debug.LogWarning($"[OnlineBattle] Unknown base card id in burst snapshot: {action.cardId}");
            return;
        }

        CardController existing = rule.DeployedBase;
        if (existing != null && existing.Data != null && existing.Data.id == action.cardId)
        {
            existing.SetCurrentHpForSync(action.defenderDeployedBaseHpAfter);
            RefreshDeployedBaseHpOverlay(existing);
            return;
        }

        ClearRemoteMirrorDeployedBaseVisual(rule);
        GameObject cardObject = Object.Instantiate(CardImagePrefab, rule.BaseSlotContent);
        CardController controller = cardObject.GetComponent<CardController>();
        controller.SetUp(cardData, OnCardClicked);
        controller.ResetRuntimeStatsFromData();
        controller.SetCurrentHpForSync(action.defenderDeployedBaseHpAfter);
        controller.RevealShieldFace();
        rule.AttachDeployedBaseCard(controller);
        RefreshDeployedBaseHpOverlay(controller);
    }
}
