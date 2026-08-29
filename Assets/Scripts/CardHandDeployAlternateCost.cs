using System;
using System.Collections.Generic;

/// <summary>
/// 手札からの配備時に、条件付きで Lv/Cost を 0 として扱う代替コスト（GD01-002 等）。
/// timedEffects の effectsName でプロファイルを参照する。
/// </summary>
public static class CardHandDeployAlternateCost
{
    public const string UnicornDestroyModeSacrificeEffectName =
        "HandDeploy_OptionalDestroyLinkedUnicornModeLv5_PlayAsZeroCostLevel";

    public sealed class Profile
    {
        public string sacrificeNameContains = "Unicorn Mode";
        public int sacrificePrintedLevel = 5;
        public bool sacrificeMustBeLinked = true;
        public int alternateLevel = 0;
        public int alternateCost = 0;
    }

    public static bool TryGetProfile(CardData data, out Profile profile)
    {
        profile = null;
        if (data?.timedEffects == null)
        {
            return false;
        }

        for (int i = 0; i < data.timedEffects.Count; i++)
        {
            TimedEffectData timed = data.timedEffects[i];
            if (timed == null || string.IsNullOrWhiteSpace(timed.effectsName))
            {
                continue;
            }

            if (!string.Equals(
                    timed.effectsName.Trim(),
                    UnicornDestroyModeSacrificeEffectName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            profile = new Profile();
            return true;
        }

        return false;
    }

    public static bool IsSacrificeCandidate(
        CardController unit,
        BattleGameMain.PlayerType ownerType,
        Profile profile,
        Func<CardController, BattleGameMain.PlayerType> resolveOwner,
        Func<CardController, bool> isOnBattleZone)
    {
        if (unit == null || unit.Data == null || profile == null || !unit.Data.IsUnitLike())
        {
            return false;
        }

        if (unit.CurrentHp <= 0)
        {
            return false;
        }

        if (resolveOwner == null || resolveOwner(unit) != ownerType)
        {
            return false;
        }

        if (isOnBattleZone != null && !isOnBattleZone(unit))
        {
            return false;
        }

        if (profile.sacrificeMustBeLinked
            && !UnitLinkExtensions.HasValidLinkPilot(unit.Data, unit.MountedPilot))
        {
            return false;
        }

        if (profile.sacrificePrintedLevel > 0 && unit.Data.level != profile.sacrificePrintedLevel)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(profile.sacrificeNameContains))
        {
            string name = unit.Data.cardName ?? string.Empty;
            if (name.IndexOf(profile.sacrificeNameContains.Trim(), StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }
        }

        return true;
    }

    public static List<CardController> CollectSacrificeCandidates(
        BattleGameMain.PlayerType ownerType,
        Profile profile,
        IReadOnlyList<CardController> battleZone,
        Func<CardController, BattleGameMain.PlayerType> resolveOwner,
        Func<CardController, bool> isOnBattleZone)
    {
        var result = new List<CardController>();
        if (profile == null || battleZone == null)
        {
            return result;
        }

        for (int i = 0; i < battleZone.Count; i++)
        {
            CardController unit = battleZone[i];
            if (IsSacrificeCandidate(unit, ownerType, profile, resolveOwner, isOnBattleZone))
            {
                result.Add(unit);
            }
        }

        return result;
    }
}
