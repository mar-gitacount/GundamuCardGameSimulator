using System;
using UnityEngine;

/// <summary>カードの配備地形（Zone）。Space と Earth は同時指定可。</summary>
[Flags]
public enum CardBattleZone
{
    [InspectorName("未設定（制限なし）")]
    None = 0,
    [InspectorName("宇宙（Space）")]
    Space = 1 << 0,
    [InspectorName("地上（Earth）")]
    Earth = 1 << 1,
    [InspectorName("宇宙＋地上")]
    SpaceAndEarth = Space | Earth
}

/// <summary><see cref="CardBattleZone"/> の判定ヘルパー。</summary>
public static class CardBattleZoneExtensions
{
    public const CardBattleZone SpaceAndEarth = CardBattleZone.Space | CardBattleZone.Earth;

    /// <summary>Space / Earth のいずれかが立っているか。</summary>
    public static bool HasAnySpecified(this CardBattleZone zones)
    {
        return (zones & SpaceAndEarth) != 0;
    }

    /// <summary>指定地形を含むか。未設定（None）は制限なしとして true。</summary>
    public static bool Allows(this CardBattleZone zones, CardBattleZone required)
    {
        if (required == CardBattleZone.None)
        {
            return true;
        }

        if (!zones.HasAnySpecified())
        {
            return true;
        }

        return (zones & required) == required;
    }

    public static bool AllowsSpace(this CardBattleZone zones)
    {
        return zones.Allows(CardBattleZone.Space);
    }

    public static bool AllowsEarth(this CardBattleZone zones)
    {
        return zones.Allows(CardBattleZone.Earth);
    }

    public static string GetDisplay(this CardBattleZone zones, bool japanese)
    {
        bool space = (zones & CardBattleZone.Space) != 0;
        bool earth = (zones & CardBattleZone.Earth) != 0;
        if (!space && !earth)
        {
            return japanese ? "未設定" : "Unset";
        }

        if (space && earth)
        {
            return japanese ? "宇宙／地上" : "Space / Earth";
        }

        if (space)
        {
            return japanese ? "宇宙" : "Space";
        }

        return japanese ? "地上" : "Earth";
    }

    public static bool Allows(this CardData card, CardBattleZone required)
    {
        if (card == null)
        {
            return true;
        }

        return card.battleZones.Allows(required);
    }
}
