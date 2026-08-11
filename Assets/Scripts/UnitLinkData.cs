using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ユニットの Link（搭乗可能パイロット定義）1件。複数スロットは OR（いずれか一致で可）。
/// </summary>
[Serializable]
public class UnitLinkPilotSlot
{
    [Tooltip("特定パイロットカード ID。0 なら PilotId / Feature のみで判定。")]
    public int pilotCardId;

    [Tooltip("Link 対象の PilotId（いずれか一致で可）。複数アムロ等は同一 PilotId でまとめて指定。")]
    public List<CardPilotIdData> linkPilotIds = new List<CardPilotIdData>();

    [Tooltip("JSON 用 PilotId の整数 ID（linkPilotIds 未設定時に使用）。")]
    public int[] linkPilotIdIds;

    [Tooltip("パイロットが持つ必要がある Feature（すべて満たす）。Inspector 用。")]
    public List<CardFeatureData> pilotFeatures = new List<CardFeatureData>();

    [Tooltip("JSON 用 Feature ID（pilotFeatures 未設定時に使用）。")]
    public int[] pilotFeatureIds;
}

/// <summary>搭乗時 OnPilotMounted / OnLink の解決対象（ホストユニットの CardData で指定）。</summary>
public enum PilotMountOnPilotMountedSource
{
    /// <summary>ユニット・パイロット双方（双方に OnPilotMounted があれば両方）。</summary>
    Both = 0,
    /// <summary>搭乗先ユニットの OnPilotMounted のみ。</summary>
    UnitOnly = 1,
    /// <summary>搭乗パイロットの OnPilotMounted のみ。</summary>
    PilotOnly = 2,
}

/// <summary><see cref="PilotMountOnPilotMountedSource.Both"/> 時の解決順。</summary>
public enum PilotMountOnPilotMountedOrder
{
    UnitFirst = 0,
    PilotFirst = 1,
}

/// <summary>
/// <see cref="CardData.link"/> の照合・AttackFlg 判定。
/// </summary>
public static class UnitLinkExtensions
{
    public static bool HasLinkRequirements(CardData unitData)
    {
        if (unitData == null || !unitData.IsUnitLike() || unitData.link == null)
        {
            return false;
        }

        for (int i = 0; i < unitData.link.Count; i++)
        {
            if (IsSlotDefined(unitData.link[i]))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsSlotDefined(UnitLinkPilotSlot slot)
    {
        if (slot == null)
        {
            return false;
        }

        if (slot.pilotCardId > 0)
        {
            return true;
        }

        if (slot.linkPilotIds != null && slot.linkPilotIds.Count > 0)
        {
            return true;
        }

        if (slot.linkPilotIdIds != null && slot.linkPilotIdIds.Length > 0)
        {
            return true;
        }

        if (slot.pilotFeatures != null && slot.pilotFeatures.Count > 0)
        {
            return true;
        }

        return slot.pilotFeatureIds != null && slot.pilotFeatureIds.Length > 0;
    }

    /// <summary>搭乗パイロットが Link 条件（いずれかスロット）に一致するか。搭乗可否には使わない。</summary>
    public static bool MatchesLinkPilot(CardData unitData, CardData pilotData)
    {
        if (!HasLinkRequirements(unitData) || pilotData == null || !pilotData.IsPilot())
        {
            return false;
        }

        for (int i = 0; i < unitData.link.Count; i++)
        {
            if (SlotMatchesPilot(unitData.link[i], pilotData))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>搭乗済みパイロットが Link 条件に一致するか。</summary>
    public static bool HasValidLinkPilot(CardData unitData, CardController mountedPilot)
    {
        return mountedPilot != null
            && mountedPilot.Data != null
            && MatchesLinkPilot(unitData, mountedPilot.Data);
    }

    /// <summary>出したターンに Link 搭乗で即攻撃可能にするか（Link 定義あり＋条件パイロットのみ）。</summary>
    public static bool GrantsSameTurnAttackOnLink(CardData unitData, CardController mountedPilot)
    {
        return HasLinkRequirements(unitData) && HasValidLinkPilot(unitData, mountedPilot);
    }

    private static bool SlotMatchesPilot(UnitLinkPilotSlot slot, CardData pilotData)
    {
        if (!IsSlotDefined(slot) || pilotData == null)
        {
            return false;
        }

        if (slot.pilotCardId > 0 && slot.pilotCardId != pilotData.id)
        {
            return false;
        }

        IReadOnlyList<CardPilotIdData> requiredPilotIds = ResolveSlotPilotIds(slot);
        IReadOnlyList<CardFeatureData> requiredFeatures = ResolveSlotFeatures(slot);

        // 指定されている条件はすべて満たす必要がある（AND）。未指定の条件は無視。
        if (requiredPilotIds.Count == 0 && requiredFeatures.Count == 0)
        {
            return slot.pilotCardId > 0;
        }

        if (requiredPilotIds.Count > 0 && !pilotData.HasAnyPilotId(requiredPilotIds))
        {
            return false;
        }

        for (int i = 0; i < requiredFeatures.Count; i++)
        {
            if (requiredFeatures[i] == null || !pilotData.HasFeature(requiredFeatures[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyList<CardPilotIdData> ResolveSlotPilotIds(UnitLinkPilotSlot slot)
    {
        if (slot.linkPilotIds != null && slot.linkPilotIds.Count > 0)
        {
            List<CardPilotIdData> filtered = new List<CardPilotIdData>(slot.linkPilotIds.Count);
            for (int i = 0; i < slot.linkPilotIds.Count; i++)
            {
                CardPilotIdData pilotId = slot.linkPilotIds[i];
                if (pilotId != null)
                {
                    filtered.Add(pilotId);
                }
            }

            if (filtered.Count > 0)
            {
                return filtered;
            }
        }

        if (slot.linkPilotIdIds == null || slot.linkPilotIdIds.Length == 0)
        {
            return Array.Empty<CardPilotIdData>();
        }

        CardPilotIdRegistry.EnsureLoaded();
        List<CardPilotIdData> list = new List<CardPilotIdData>();
        for (int i = 0; i < slot.linkPilotIdIds.Length; i++)
        {
            int id = slot.linkPilotIdIds[i];
            if (id <= 0)
            {
                continue;
            }

            CardPilotIdData pilotId = CardPilotIdRegistry.GetById(id);
            if (pilotId != null)
            {
                list.Add(pilotId);
            }
        }

        return list;
    }

    private static IReadOnlyList<CardFeatureData> ResolveSlotFeatures(UnitLinkPilotSlot slot)
    {
        if (slot.pilotFeatures != null && slot.pilotFeatures.Count > 0)
        {
            List<CardFeatureData> filtered = new List<CardFeatureData>(slot.pilotFeatures.Count);
            for (int i = 0; i < slot.pilotFeatures.Count; i++)
            {
                CardFeatureData feature = slot.pilotFeatures[i];
                if (feature != null)
                {
                    filtered.Add(feature);
                }
            }

            if (filtered.Count > 0)
            {
                return filtered;
            }
        }

        if (slot.pilotFeatureIds == null || slot.pilotFeatureIds.Length == 0)
        {
            return Array.Empty<CardFeatureData>();
        }

        CardFeatureRegistry.EnsureLoaded();
        List<CardFeatureData> list = new List<CardFeatureData>();
        for (int i = 0; i < slot.pilotFeatureIds.Length; i++)
        {
            int id = slot.pilotFeatureIds[i];
            if (id <= 0)
            {
                continue;
            }

            CardFeatureData f = CardFeatureRegistry.GetById(id);
            if (f != null)
            {
                list.Add(f);
            }
        }

        return list;
    }

    /// <summary>搭乗時 OnPilotMounted の実行計画（ホストユニットの CardData 設定を参照）。</summary>
    public static void ResolveOnPilotMountedExecutionPlan(
        CardData hostUnitData,
        out bool resolveUnitEffects,
        out bool resolvePilotEffects,
        out bool unitRunsBeforePilot)
    {
        resolveUnitEffects = true;
        resolvePilotEffects = true;
        unitRunsBeforePilot = true;
        if (hostUnitData == null || !hostUnitData.IsUnitLike())
        {
            return;
        }

        switch (hostUnitData.pilotMountOnPilotMountedSource)
        {
            case PilotMountOnPilotMountedSource.UnitOnly:
                resolvePilotEffects = false;
                break;
            case PilotMountOnPilotMountedSource.PilotOnly:
                resolveUnitEffects = false;
                break;
        }

        unitRunsBeforePilot = hostUnitData.pilotMountOnPilotMountedOrder != PilotMountOnPilotMountedOrder.PilotFirst;
    }
}
