using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>カードの作品（シリーズ）タイトル。CardData は1つ選択。</summary>
public enum CardSourceTitle
{
    [InspectorName("-")]
    None = 0,
    [InspectorName("Mobile Suit Gundam")]
    MobileSuitGundam = 1,
    [InspectorName("Mobile Suit Z Gundam")]
    MobileSuitZGundam = 2,
    [InspectorName("Mobile Suit Gundam: Char's Counterattack")]
    CharsCounterattack = 3,
    [InspectorName("Mobile Suit Gundam 0080: War in the Pocket")]
    Gundam0080 = 4,
    [InspectorName("Mobile Suit V Gundam")]
    MobileSuitVGundam = 5,
    [InspectorName("Mobile Fighter G Gundam")]
    MobileFighterGGundam = 6,
    [InspectorName("Mobile Suit Gundam Wing")]
    GundamWing = 7,
    [InspectorName("After War Gundam X")]
    AfterWarGundamX = 8,
    [InspectorName("Mobile Suit Gundam Wing: Endless Waltz")]
    GundamWingEndlessWaltz = 9,
    [InspectorName("∀ Gundam")]
    TurnAGundam = 10,
    [InspectorName("Mobile Suit Gundam SEED")]
    GundamSeed = 11,
    [InspectorName("Mobile Suit Gundam SEED DESTINY")]
    GundamSeedDestiny = 12,
    [InspectorName("Mobile Suit Gundam 00")]
    Gundam00 = 13,
    [InspectorName("Mobile Suit Gundam Unicorn")]
    GundamUnicorn = 14,
    [InspectorName("Mobile Suit Gundam AGE")]
    GundamAge = 15,
    [InspectorName("Mobile Suit Gundam IRON-BLOODED ORPHANS")]
    IronBloodedOrphans = 16,
    [InspectorName("Mobile Suit Gundam: Hathaway's Flash")]
    HathawaysFlash = 17,
    [InspectorName("Mobile Suit Gundam the Witch from Mercury")]
    WitchFromMercury = 18,
    [InspectorName("Mobile Suit Gundam GQuuuuuuX")]
    GQuuuuuuX = 19,
    [InspectorName("SD Gundam G Generation ETERNAL")]
    SdGGenerationEternal = 20,
}

/// <summary>作品タイトルの表示・検索用。</summary>
public static class CardSourceTitleNames
{
    public static string GetDisplay(CardSourceTitle title)
    {
        switch (title)
        {
            case CardSourceTitle.MobileSuitGundam:
                return "Mobile Suit Gundam";
            case CardSourceTitle.MobileSuitZGundam:
                return "Mobile Suit Z Gundam";
            case CardSourceTitle.CharsCounterattack:
                return "Mobile Suit Gundam: Char's Counterattack";
            case CardSourceTitle.Gundam0080:
                return "Mobile Suit Gundam 0080: War in the Pocket";
            case CardSourceTitle.MobileSuitVGundam:
                return "Mobile Suit V Gundam";
            case CardSourceTitle.MobileFighterGGundam:
                return "Mobile Fighter G Gundam";
            case CardSourceTitle.GundamWing:
                return "Mobile Suit Gundam Wing";
            case CardSourceTitle.AfterWarGundamX:
                return "After War Gundam X";
            case CardSourceTitle.GundamWingEndlessWaltz:
                return "Mobile Suit Gundam Wing: Endless Waltz";
            case CardSourceTitle.TurnAGundam:
                return "∀ Gundam";
            case CardSourceTitle.GundamSeed:
                return "Mobile Suit Gundam SEED";
            case CardSourceTitle.GundamSeedDestiny:
                return "Mobile Suit Gundam SEED DESTINY";
            case CardSourceTitle.Gundam00:
                return "Mobile Suit Gundam 00";
            case CardSourceTitle.GundamUnicorn:
                return "Mobile Suit Gundam Unicorn";
            case CardSourceTitle.GundamAge:
                return "Mobile Suit Gundam AGE";
            case CardSourceTitle.IronBloodedOrphans:
                return "Mobile Suit Gundam IRON-BLOODED ORPHANS";
            case CardSourceTitle.HathawaysFlash:
                return "Mobile Suit Gundam: Hathaway's Flash";
            case CardSourceTitle.WitchFromMercury:
                return "Mobile Suit Gundam the Witch from Mercury";
            case CardSourceTitle.GQuuuuuuX:
                return "Mobile Suit Gundam GQuuuuuuX";
            case CardSourceTitle.SdGGenerationEternal:
                return "SD Gundam G Generation ETERNAL";
            default:
                return "-";
        }
    }

    public static List<CardSourceTitle> GetSelectableTitles()
    {
        var list = new List<CardSourceTitle>();
        Array values = Enum.GetValues(typeof(CardSourceTitle));
        for (int i = 0; i < values.Length; i++)
        {
            CardSourceTitle title = (CardSourceTitle)values.GetValue(i);
            if (title == CardSourceTitle.None)
            {
                continue;
            }

            list.Add(title);
        }

        return list;
    }

    public static bool MatchesAny(CardData card, IList<CardSourceTitle> selected)
    {
        if (card == null)
        {
            return false;
        }

        if (selected == null || selected.Count == 0)
        {
            return true;
        }

        for (int i = 0; i < selected.Count; i++)
        {
            if (selected[i] != CardSourceTitle.None && card.sourceTitle == selected[i])
            {
                return true;
            }
        }

        return false;
    }
}
