using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// バースト元を AP/HP 指定でバトルゾーンへユニット化配備する。
/// 印刷タイプはパイロットのまま残し、ランタイム複製のみ type=Unit。
/// </summary>
public partial class BattleGameMain
{
    private bool TryApplyDeploySelfAsBattleUnit(
        CardController sourceCard,
        PlayerType ownerType,
        EffectData effect)
    {
        if (sourceCard == null || sourceCard.Data == null || effect == null)
        {
            return false;
        }

        if (!IsResolvingBurstEffect)
        {
            Debug.LogWarning(
                $"[DeploySelfAsBattleUnit] バースト以外では未対応 (cardId:{sourceCard.Data.id})。");
            return false;
        }

        PlayerType recipient = ResolveEffectOwnerPlayerType(ownerType, effect.target);
        if (recipient != ownerType)
        {
            Debug.LogWarning("[DeploySelfAsBattleUnit] 自分以外への配備は未対応。");
            return false;
        }

        CardGameRule rule = recipient == PlayerType.Player ? cardGameRule : enemyCardGameRule;
        if (rule?.PlayerDeployPanel == null)
        {
            return false;
        }

        // Shared ScriptableObject を壊さないようランタイム複製してユニット化する
        CardData printed = sourceCard.Data;
        Type printedType = printed.type;
        int printedPower = printed.power;
        int printedHp = printed.hp;

        CardData unitData = Instantiate(printed);
        unitData.name = printed.cardName + " (BattleUnit)";
        unitData.type = Type.Unit;
        if (effect.deployUnitOverrideAp > 0)
        {
            unitData.power = effect.deployUnitOverrideAp;
        }

        if (effect.deployUnitOverrideHp > 0)
        {
            unitData.hp = effect.deployUnitOverrideHp;
        }

        // ユニット形態ではリンク・搭乗扱いを使わない
        unitData.link = new List<UnitLinkPilotSlot>();

        rule.TryUnregisterShieldZoneCard(sourceCard);
        sourceCard.gameObject.SetActive(true);
        sourceCard.RevealShieldFace();
        sourceCard.CleanupUnitBattleMountVisuals();
        sourceCard.SetUp(unitData, OnCardClicked);
        sourceCard.RebindClickHandler(OnCardClicked);
        // バトルゾーン在場中のみユニット扱い。退場時に印刷タイプへ戻す
        sourceCard.MarkTemporaryBurstBattleUnit(printedType, printedPower, printedHp);

        if (!DeployUnitToBattleZone(
                sourceCard,
                recipient,
                rule,
                effect.deployUnitTriggerOnPlayed,
                fromHand: false))
        {
            Debug.LogWarning(
                $"[DeploySelfAsBattleUnit] DeployUnitToBattleZone failed cardId:{sourceCard.Data?.id}");
            return false;
        }

        RecordRemoteShieldBreakBurstDeployedUnitIfNeeded(sourceCard);
        MarkBurstCardRetained(sourceCard);

        Debug.Log(
            $"[DeploySelfAsBattleUnit] {unitData.cardName}(id:{unitData.id}) "
            + $"AP:{unitData.power} HP:{unitData.hp} → {recipient} battle zone");
        return true;
    }
}
