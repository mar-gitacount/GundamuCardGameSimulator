using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ユニットの Link（搭乗可能パイロット定義）1件。複数スロットは OR（いずれか一致で可）。
/// </summary>
[Serializable]
public class UnitLinkPilotSlot
{
    [Tooltip("特定パイロットカード ID。0 なら Feature のみで判定。")]
    public int pilotCardId;

    [Tooltip("パイロットが持つ必要がある Feature（すべて満たす）。Inspector 用。")]
    public List<CardFeatureData> pilotFeatures = new List<CardFeatureData>();

    [Tooltip("JSON 用 Feature ID（pilotFeatures 未設定時に使用）。")]
    public int[] pilotFeatureIds;
}

/// <summary>
/// <see cref="CardData.link"/> の照合・AttackFlg 判定。
/// </summary>
public static class UnitLinkExtensions
{
    public static bool HasLinkRequirements(CardData unitData)
    {
        if (unitData == null || unitData.type != Type.Unit || unitData.link == null)
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

        if (slot.pilotFeatures != null && slot.pilotFeatures.Count > 0)
        {
            return true;
        }

        return slot.pilotFeatureIds != null && slot.pilotFeatureIds.Length > 0;
    }

    /// <summary>搭乗パイロットが Link 条件（いずれかスロット）に一致するか。搭乗可否には使わない。</summary>
    public static bool MatchesLinkPilot(CardData unitData, CardData pilotData)
    {
        if (!HasLinkRequirements(unitData) || pilotData == null || pilotData.type != Type.Pilot)
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

        bool idOk = slot.pilotCardId <= 0 || slot.pilotCardId == pilotData.id;
        if (!idOk)
        {
            return false;
        }

        IReadOnlyList<CardFeatureData> required = ResolveSlotFeatures(slot);
        if (required.Count == 0)
        {
            return slot.pilotCardId > 0;
        }

        for (int i = 0; i < required.Count; i++)
        {
            if (required[i] == null || !pilotData.HasFeature(required[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyList<CardFeatureData> ResolveSlotFeatures(UnitLinkPilotSlot slot)
    {
        if (slot.pilotFeatures != null && slot.pilotFeatures.Count > 0)
        {
            return slot.pilotFeatures;
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
}
