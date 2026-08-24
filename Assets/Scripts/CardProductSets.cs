using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>収録ライン（ブースター / スターター / Eternal Booster）。</summary>
public enum CardProductLine
{
    [InspectorName("未設定")]
    Unknown = 0,
    [InspectorName("ブースター")]
    Booster = 1,
    [InspectorName("スターター")]
    Starter = 2,
    [InspectorName("Eternal Booster")]
    EternalBooster = 3,
}

/// <summary>
/// 公式サイト detailSearch 用のセット種別（ST / GD / EB / T）。
/// 収録セットの productLine とは別に、公式カード番号用に明示選択する。
/// </summary>
public enum GcgOfficialSetKind
{
    [InspectorName("未設定")]
    Unset = 0,
    [InspectorName("スターター (ST)")]
    Starter = 1,
    [InspectorName("ブースター (GD)")]
    Booster = 2,
    [InspectorName("Eternal Booster (EB)")]
    EternalBooster = 3,
    [InspectorName("トークン (T)")]
    Token = 4,
}

/// <summary>ブースター作品（プルダウン用）。</summary>
public enum BoosterProductSet
{
    [InspectorName("未設定")]
    None = 0,
    [InspectorName("1. NewType Rising")]
    NewTypeRising = 1,
    [InspectorName("2. Dual Impact")]
    DualImpact = 2,
    [InspectorName("3. Steal Requiem")]
    StealRequiem = 3,
    [InspectorName("4. Phantom Aria")]
    PhantomAria = 4,
    [InspectorName("5. Freedom Ascension")]
    FreedomAscension = 5,
}

/// <summary>スターター作品（プルダウン用）。</summary>
public enum StarterProductSet
{
    [InspectorName("未設定")]
    None = 0,
    [InspectorName("1. Heroic Beginnings")]
    HeroicBeginnings = 1,
    [InspectorName("2. Wings of Advance")]
    WingsOfAdvance = 2,
    [InspectorName("3. Zeon's Rush")]
    ZeonsRush = 3,
    [InspectorName("4. SEED Strike")]
    SeedStrike = 4,
    [InspectorName("5. Iron Bloom")]
    IronBloom = 5,
    [InspectorName("6. Clan Unity")]
    ClanUnity = 6,
    [InspectorName("7. Celestial Drive")]
    CelestialDrive = 7,
    [InspectorName("8. Flash of Radiance")]
    FlashOfRadiance = 8,
    [InspectorName("9. Destiny Ignition")]
    DestinyIgnition = 9,
    [InspectorName("10. Generation Pulse")]
    GenerationPulse = 10,
    [InspectorName("11. Aquatic Assault")]
    AquaticAssault = 11,
    [InspectorName("12. Raging Onslaught")]
    RagingOnslaught = 12,
    [InspectorName("13. Silent Barrage")]
    SilentBarrage = 13,
    [InspectorName("14. Heavy Dominion")]
    HeavyDominion = 14,
}

/// <summary>Eternal Booster 作品（プルダウン用）。</summary>
public enum EternalBoosterProductSet
{
    [InspectorName("未設定")]
    None = 0,
    [InspectorName("1. Eternal Nexus")]
    EternalNexus = 1,
}

/// <summary>収録セット名の表示・検索用ヘルパー。</summary>
public static class CardProductSetNames
{
    public static string GetBoosterDisplay(BoosterProductSet set, bool japanese)
    {
        switch (set)
        {
            case BoosterProductSet.NewTypeRising:
                return japanese ? "NewType Rising" : "NewType Rising";
            case BoosterProductSet.DualImpact:
                return japanese ? "Dual Impact" : "Dual Impact";
            case BoosterProductSet.StealRequiem:
                return japanese ? "Steal Requiem" : "Steal Requiem";
            case BoosterProductSet.PhantomAria:
                return japanese ? "Phantom Aria" : "Phantom Aria";
            case BoosterProductSet.FreedomAscension:
                return japanese ? "Freedom Ascension" : "Freedom Ascension";
            default:
                return "-";
        }
    }

    public static string GetStarterDisplay(StarterProductSet set, bool japanese)
    {
        switch (set)
        {
            case StarterProductSet.HeroicBeginnings:
                return "Heroic Beginnings";
            case StarterProductSet.WingsOfAdvance:
                return "Wings of Advance";
            case StarterProductSet.ZeonsRush:
                return "Zeon's Rush";
            case StarterProductSet.SeedStrike:
                return "SEED Strike";
            case StarterProductSet.IronBloom:
                return "Iron Bloom";
            case StarterProductSet.ClanUnity:
                return "Clan Unity";
            case StarterProductSet.CelestialDrive:
                return "Celestial Drive";
            case StarterProductSet.FlashOfRadiance:
                return "Flash of Radiance";
            case StarterProductSet.DestinyIgnition:
                return "Destiny Ignition";
            case StarterProductSet.GenerationPulse:
                return "Generation Pulse";
            case StarterProductSet.AquaticAssault:
                return "Aquatic Assault";
            case StarterProductSet.RagingOnslaught:
                return "Raging Onslaught";
            case StarterProductSet.SilentBarrage:
                return "Silent Barrage";
            case StarterProductSet.HeavyDominion:
                return "Heavy Dominion";
            default:
                return "-";
        }
    }

    public static string GetEternalDisplay(EternalBoosterProductSet set, bool japanese)
    {
        switch (set)
        {
            case EternalBoosterProductSet.EternalNexus:
                return "Eternal Nexus";
            default:
                return "-";
        }
    }

    public static string GetProductLineDisplay(CardProductLine line, bool japanese)
    {
        switch (line)
        {
            case CardProductLine.Booster:
                return japanese ? "ブースター" : "Booster";
            case CardProductLine.Starter:
                return japanese ? "スターター" : "Starter";
            case CardProductLine.EternalBooster:
                return "Eternal Booster";
            default:
                return japanese ? "未設定" : "Unknown";
        }
    }

    public static List<string> BuildBoosterDropdownOptions(bool japanese)
    {
        var list = new List<string> { "-" };
        Array values = Enum.GetValues(typeof(BoosterProductSet));
        for (int i = 0; i < values.Length; i++)
        {
            BoosterProductSet set = (BoosterProductSet)values.GetValue(i);
            if (set == BoosterProductSet.None)
            {
                continue;
            }

            list.Add(GetBoosterDisplay(set, japanese));
        }

        return list;
    }

    public static List<string> BuildStarterDropdownOptions(bool japanese)
    {
        var list = new List<string> { "-" };
        Array values = Enum.GetValues(typeof(StarterProductSet));
        for (int i = 0; i < values.Length; i++)
        {
            StarterProductSet set = (StarterProductSet)values.GetValue(i);
            if (set == StarterProductSet.None)
            {
                continue;
            }

            list.Add(GetStarterDisplay(set, japanese));
        }

        return list;
    }

    public static List<string> BuildEternalDropdownOptions(bool japanese)
    {
        var list = new List<string> { "-" };
        Array values = Enum.GetValues(typeof(EternalBoosterProductSet));
        for (int i = 0; i < values.Length; i++)
        {
            EternalBoosterProductSet set = (EternalBoosterProductSet)values.GetValue(i);
            if (set == EternalBoosterProductSet.None)
            {
                continue;
            }

            list.Add(GetEternalDisplay(set, japanese));
        }

        return list;
    }

    /// <summary>ドロップダウン index（0="-"＝未選択／全件）→ ブースター enum。</summary>
    public static BoosterProductSet BoosterFromDropdownIndex(int index)
    {
        if (index <= 0)
        {
            return BoosterProductSet.None;
        }

        return (BoosterProductSet)index;
    }

    public static StarterProductSet StarterFromDropdownIndex(int index)
    {
        if (index <= 0)
        {
            return StarterProductSet.None;
        }

        return (StarterProductSet)index;
    }

    public static EternalBoosterProductSet EternalFromDropdownIndex(int index)
    {
        if (index <= 0)
        {
            return EternalBoosterProductSet.None;
        }

        return (EternalBoosterProductSet)index;
    }

    public static bool MatchesBooster(CardData card, BoosterProductSet set)
    {
        if (card == null || set == BoosterProductSet.None)
        {
            return true;
        }

        if (card.boosterSet != BoosterProductSet.None)
        {
            return card.productLine == CardProductLine.Booster && card.boosterSet == set;
        }

        // 旧 version 互換（ブースターのみ）
        if (card.productLine == CardProductLine.Starter
            || card.productLine == CardProductLine.EternalBooster
            || card.sourceType == CardSourceType.Starter
            || card.sourceType == CardSourceType.EternalBooster)
        {
            return false;
        }

        return card.version == (int)set;
    }

    public static bool MatchesStarter(CardData card, StarterProductSet set)
    {
        if (card == null || set == StarterProductSet.None)
        {
            return true;
        }

        if (card.starterSet != StarterProductSet.None)
        {
            return card.productLine == CardProductLine.Starter && card.starterSet == set;
        }

        return card.sourceType == CardSourceType.Starter && card.version == (int)set;
    }

    public static bool MatchesEternal(CardData card, EternalBoosterProductSet set)
    {
        if (card == null || set == EternalBoosterProductSet.None)
        {
            return true;
        }

        if (card.eternalBoosterSet != EternalBoosterProductSet.None)
        {
            return card.productLine == CardProductLine.EternalBooster && card.eternalBoosterSet == set;
        }

        return card.sourceType == CardSourceType.EternalBooster && card.version == (int)set;
    }
}
