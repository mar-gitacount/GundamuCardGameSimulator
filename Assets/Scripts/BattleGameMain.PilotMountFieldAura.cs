using System.Collections.Generic;
using UnityEngine;

public partial class BattleGameMain
{
    /// <summary>搭乗効果チェーン解決中のホストユニット（後配備オーラ登録用）。</summary>
    private CardController _pilotMountEffectHostUnit;

    /// <summary>
    /// パイロット搭乗時 AllyAllUnits 永続バフを、後から配備される味方ユニットにも適用するオーラとして登録する。
    /// </summary>
    private void TryRegisterPilotMountAllyFieldAura(
        CardController sourceCard,
        PlayerType ownerType,
        EffectData effect,
        int signedMagnitude)
    {
        if (_pilotMountEffectHostUnit == null
            || effect == null
            || effect.type != EffectType.Buff
            || effect.target != TargetType.AllyAllUnits
            || effect.duration != EffectDuration.Permanent
            || signedMagnitude == 0)
        {
            return;
        }

        CardController auraHost = _pilotMountEffectHostUnit;
        if (auraHost == null || auraHost.Data == null || !auraHost.Data.IsUnitLike() || auraHost.MountedPilot == null)
        {
            return;
        }

        auraHost.RegisterPilotMountAllyFieldAura(effect.statTarget, signedMagnitude, effect.duration);
        Debug.Log(
            $"[PilotMountAura] registered host:{auraHost.Data.cardName}(id:{auraHost.Data.id}) "
            + $"stat:{effect.statTarget} value:{signedMagnitude} sourceCard:{sourceCard?.Data?.cardName}(id:{sourceCard?.Data?.id}) "
            + $"side:{ownerType}");
    }

    /// <summary>配備直後の味方ユニットへ、搭乗中オーラの永続バフを適用する。</summary>
    private void ApplyPilotMountFieldAurasToDeployedUnit(CardController deployedUnit, PlayerType ownerType)
    {
        if (deployedUnit == null || deployedUnit.Data == null || !deployedUnit.Data.IsUnitLike())
        {
            return;
        }

        List<CardController> allies = ownerType == PlayerType.Player ? playerBattleZoneCards : enemyBattleZoneCards;
        BeginOnlineEffectSyncBatch(ownerType);
        for (int i = 0; i < allies.Count; i++)
        {
            CardController auraHost = allies[i];
            if (auraHost == null || auraHost == deployedUnit || !auraHost.HasActivePilotMountAllyFieldAuras)
            {
                continue;
            }

            string sourceKey = auraHost.MakePilotMountFieldAuraSourceKey();
            IReadOnlyList<CardController.PilotMountAllyFieldAuraEntry> auras = auraHost.GetPilotMountAllyFieldAuras();
            for (int j = 0; j < auras.Count; j++)
            {
                CardController.PilotMountAllyFieldAuraEntry aura = auras[j];
                if (deployedUnit.HasStatModifierFromSource(sourceKey, aura.StatTarget))
                {
                    continue;
                }

                ApplyStatEffect(deployedUnit, aura.SignedMagnitude, aura.StatTarget, aura.Duration, sourceKey);
                QueueOnlineUnitStat(
                    deployedUnit,
                    aura.SignedMagnitude,
                    aura.StatTarget,
                    aura.Duration,
                    sourceKey);
                Debug.Log(
                    $"[PilotMountAura] applied to {deployedUnit.Data.cardName}(id:{deployedUnit.Data.id}) "
                    + $"from host:{auraHost.Data.cardName}(id:{auraHost.Data.id}) "
                    + $"stat:{aura.StatTarget} value:{aura.SignedMagnitude}");
            }
        }

        FlushOnlineEffectSyncBatch();
    }

    /// <summary>バトルゾーン上のユニットが付与する Buff/Debuff に紐づける sourceKey。</summary>
    private string ResolveUnitStatModifierSourceKey(CardController sourceCard)
    {
        CardController grantingUnit = null;
        if (_pilotMountEffectHostUnit != null
            && _pilotMountEffectHostUnit.Data != null
            && _pilotMountEffectHostUnit.Data.IsUnitLike()
            && _pilotMountEffectHostUnit.BattleInstanceId > 0)
        {
            grantingUnit = _pilotMountEffectHostUnit;
        }
        else if (sourceCard != null
            && sourceCard.Data != null
            && sourceCard.Data.IsUnitLike()
            && sourceCard.BattleInstanceId > 0
            && IsCardOnBattleZone(sourceCard))
        {
            grantingUnit = sourceCard;
        }

        return grantingUnit != null ? grantingUnit.MakePilotMountFieldAuraSourceKey() : null;
    }
}
