using UnityEngine;

/// <summary>ユニットトークンは破壊・バウンス・除外時にゾーンへ送らず場から消える。</summary>
public partial class BattleGameMain
{
    /// <summary>
    /// カードを場から取り除く。ユニットトークンはトラッシュに積まず消滅。
    /// それ以外は <paramref name="sendToTrashZone"/> が true のときトラッシュへ送る。
    /// </summary>
    private void FinalizeRemoveCardFromPlay(
        CardController cardController,
        PlayerType ownerType,
        bool sendToTrashZone)
    {
        if (cardController == null || cardController.Data == null)
        {
            return;
        }

        bool vanish = cardController.Data.LeavesPlayWithoutZone();
        CardGameRule ownerRule = ownerType == PlayerType.Player ? cardGameRule : enemyCardGameRule;
        Gundam2024RuleScript.PlayerSide ruleSide = ToRuleSide(ownerType);
        if (ownerRule != null)
        {
            if (ownerRule.DeployedBase == cardController)
            {
                ownerRule.ClearDeployedBaseCard();
            }

            ownerRule.TryUnregisterShieldZoneCard(cardController);
        }

        if (sendToTrashZone && !vanish)
        {
            ownerRule?.AddCardToTrash(cardController.Data.id);
        }

        if (cardController.Data.IsUnitLike() && cardController.BattleInstanceId > 0)
        {
            ClearStatModifiersGrantedByDestroyedUnit(cardController, ownerType);
        }

        playerBattleZoneCards.Remove(cardController);
        enemyBattleZoneCards.Remove(cardController);
        playerHandCards.Remove(cardController.Data);
        enemyHandCards.Remove(cardController.Data);
        unitsPendingSendToTrash.Remove(cardController);

        if (cardController.Data.IsUnitLike() && cardController.BattleInstanceId > 0)
        {
            RefreshAllFieldOwnerTurnPassives();
        }

        Destroy(cardController.gameObject);
        ReconcileShieldStateWithZone(ruleSide);
        RefreshAllHandsConditionalOnHandAuto();
        ownerRule?.RefreshHandCountDisplay();

        if (vanish)
        {
            Debug.Log(
                $"[Vanish] {cardController.Data.cardName}(id:{cardController.Data.id}) "
                + $"removed from play (unit token, no zone)");
        }
    }

    /// <summary>
    /// オンラインで相手操作を反映するとき、場からユニットを取り除く。
    /// トラッシュ枚数は送信側の ZoneSync AddTrash に任せ、ここでは二重に積まない。
    /// </summary>
    private void ApplyRemoteUnitRemovedFromField(CardController unit)
    {
        if (unit == null || unit.Data == null)
        {
            Debug.LogWarning("[EffectSync][RemoveFromField][Skip] unit=null");
            return;
        }

        PlayerType owner = ResolveCardOwner(unit.transform);
        Debug.Log(
            $"[EffectSync][RemoveFromField][Start] owner={owner} "
            + $"unit={FormatOnlineEffectSyncUnit(unit)}");

        if (unit.Data.IsUnitLike() && unit.MountedPilot != null)
        {
            CardController pilot = unit.DetachMountedPilotWithoutDestroy();
            if (pilot != null)
            {
                Debug.Log(
                    $"[EffectSync][RemoveFromField][Pilot] host={FormatOnlineEffectSyncUnit(unit)} "
                    + $"pilot={FormatOnlineEffectSyncUnit(pilot)}");
                ApplyRemoteUnitRemovedFromField(pilot);
            }
        }

        FinalizeRemoveCardFromPlay(unit, owner, sendToTrashZone: false);
        Debug.Log(
            $"[EffectSync][RemoveFromField][Done] owner={owner} "
            + $"unit={unit.Data.cardName}(cardId:{unit.Data.id})");
    }

    /// <summary>ユニットトークンをバウンス等で手札に戻さず消滅させる。搭乗パイロットは手札へ。</summary>
    private bool TryVanishBattleUnitTokenFromZone(CardController unit)
    {
        if (unit == null || unit.Data == null || !unit.Data.IsUnitToken() || !IsCardOnBattleZone(unit))
        {
            return false;
        }

        PlayerType ownerType = ResolveCardOwner(unit.transform);
        CardGameRule rule = ownerType == PlayerType.Player ? cardGameRule : enemyCardGameRule;

        CardController pilot = unit.DetachMountedPilotWithoutDestroy();
        if (pilot != null && rule != null)
        {
            TryReturnCardInstanceToHand(pilot, ownerType, rule);
        }

        FinalizeRemoveCardFromPlay(unit, ownerType, sendToTrashZone: false);
        Debug.Log(
            $"[Bounce][Vanish] {unit.Data.cardName}(id:{unit.Data.id}) removed from play (unit token)");
        return true;
    }
}
