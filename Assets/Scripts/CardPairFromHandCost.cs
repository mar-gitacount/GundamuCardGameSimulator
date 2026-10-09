using System.Collections.Generic;

/// <summary>
/// 手札からパイロットをセットするときの条件付きコスト減（GD06-100 Corin Nander 等）。
/// </summary>
public static class CardPairFromHandCost
{
    public static bool HasDiscountProfile(CardData pilot)
    {
        return pilot != null
            && pilot.IsPilot()
            && pilot.pairFromHandCostReduction > 0;
    }

    public static bool IsEligibleHost(CardData pilot, CardController host)
    {
        if (!HasDiscountProfile(pilot) || host == null || host.Data == null || !host.Data.IsUnitLike())
        {
            return false;
        }

        if (host.CurrentHp <= 0)
        {
            return false;
        }

        if (pilot.pairFromHandHostMinLevel > 0 && host.CurrentLevel < pilot.pairFromHandHostMinLevel)
        {
            return false;
        }

        if (pilot.pairFromHandHostFeature != null && !HostHasPairFeature(host, pilot.pairFromHandHostFeature))
        {
            return false;
        }

        return true;
    }

    private static bool HostHasPairFeature(CardController host, CardFeatureData required)
    {
        if (host?.Data == null || required == null)
        {
            return false;
        }

        host.Data.EnsureFeaturesResolved();
        if (required.id > 0 && host.HasFeatureId(required.id))
        {
            return true;
        }

        if (host.Data.HasFeature(required))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(required.featureKey)
            && host.Data.HasFeatureKey(required.featureKey);
    }

    public static bool HasEligibleHost(CardData pilot, IList<CardController> mountTargets)
    {
        if (!HasDiscountProfile(pilot) || mountTargets == null)
        {
            return false;
        }

        for (int i = 0; i < mountTargets.Count; i++)
        {
            if (IsEligibleHost(pilot, mountTargets[i]))
            {
                return true;
            }
        }

        return false;
    }

    public static int GetPlayCost(CardController pilot, IList<CardController> mountTargets)
    {
        int printed = pilot != null ? pilot.CurrentCost : 0;
        if (pilot?.Data == null || !HasEligibleHost(pilot.Data, mountTargets))
        {
            return printed;
        }

        return UnityEngine.Mathf.Max(0, printed - pilot.Data.pairFromHandCostReduction);
    }

    /// <summary>指定ユニットへセットするときの実コスト（条件未達なら印刷コスト）。</summary>
    public static int GetPlayCost(CardController pilot, CardController host)
    {
        int printed = pilot != null ? pilot.CurrentCost : 0;
        if (pilot?.Data == null || !IsEligibleHost(pilot.Data, host))
        {
            return printed;
        }

        return UnityEngine.Mathf.Max(0, printed - pilot.Data.pairFromHandCostReduction);
    }

    public static List<CardController> FilterMountTargets(
        CardController pilot,
        IList<CardController> mountTargets,
        int paidCost)
    {
        var result = new List<CardController>();
        if (mountTargets == null)
        {
            return result;
        }

        CardData data = pilot != null ? pilot.Data : null;
        int printed = pilot != null ? pilot.CurrentCost : 0;
        bool restrict = HasDiscountProfile(data)
            && paidCost < printed
            && HasEligibleHost(data, mountTargets);
        for (int i = 0; i < mountTargets.Count; i++)
        {
            CardController host = mountTargets[i];
            if (host == null)
            {
                continue;
            }

            if (restrict && !IsEligibleHost(data, host))
            {
                continue;
            }

            result.Add(host);
        }

        return result;
    }
}
