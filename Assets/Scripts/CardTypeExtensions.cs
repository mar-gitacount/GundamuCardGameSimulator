using UnityEngine;

/// <summary><see cref="Type"/> の表示名・判定ヘルパー。</summary>
public static class CardTypeExtensions
{
    /// <summary>バトルゾーンに配備・戦闘するユニット系（通常ユニット＋ユニットトークン）。</summary>
    public static bool IsUnitLike(Type cardType)
    {
        return cardType == Type.Unit || cardType == Type.UnitToken;
    }

    public static bool IsUnitLike(this CardData card)
    {
        return card != null && IsUnitLike(card.type);
    }

    public static bool IsUnitToken(Type cardType)
    {
        return cardType == Type.UnitToken;
    }

    public static bool IsUnitToken(this CardData card)
    {
        return card != null && IsUnitToken(card.type);
    }

    /// <summary>破壊・バウンス・除外時にゾーンへ送らず場から消えるカード種類。</summary>
    public static bool LeavesPlayWithoutZone(this CardData card)
    {
        return card != null && IsUnitToken(card.type);
    }

    public static bool IsPilot(Type cardType)
    {
        return cardType == Type.Pilot;
    }

    public static bool IsPilot(this CardData card)
    {
        return card != null && IsPilot(card.type);
    }
   

    public static string CardFeature(this CardFeatureData cardFeature){
        return cardFeature?.displayName;
    }
    /// <summary>Inspector / UI 向けの日本語ラベル。</summary>
    public static string GetDisplayName(Type cardType)
    {
        switch (cardType)
        {
            case Type.Unit:
                return "ユニット";
            case Type.Pilot:
                return "パイロット";
            case Type.Command:
                return "コマンド";
            case Type.Base:
                return "ベース";
            case Type.ExResource:
                return "EXリソース";
            case Type.UnitToken:
                return "ユニットトークン";
            default:
                return cardType.ToString();
        }
    }

    public static string GetDisplayName(this CardData card)
    {
        return card != null ? GetDisplayName(card.type) : "?";
    }
}
