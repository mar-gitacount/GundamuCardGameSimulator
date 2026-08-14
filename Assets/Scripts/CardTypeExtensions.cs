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

    /// <summary>パイロットとして搭乗できる種類（Pilot / CommandPilot）。</summary>
    public static bool IsPilot(Type cardType)
    {
        return cardType == Type.Pilot || cardType == Type.CommandPilot;
    }

    public static bool IsPilot(this CardData card)
    {
        return card != null && IsPilot(card.type);
    }

    /// <summary>コマンドとしてアクション／メインで使える種類（Command / CommandPilot）。</summary>
    public static bool IsCommand(Type cardType)
    {
        return cardType == Type.Command || cardType == Type.CommandPilot;
    }

    public static bool IsCommand(this CardData card)
    {
        return card != null && IsCommand(card.type);
    }

    /// <summary>
    /// 効果の targetCardType 絞り込み。
    /// Pilot / Command 指定時は兼用の CommandPilot も含める。
    /// </summary>
    public static bool MatchesTypeFilter(Type required, Type actual)
    {
        if (required == actual)
        {
            return true;
        }

        if (required == Type.Pilot)
        {
            return IsPilot(actual);
        }

        if (required == Type.Command)
        {
            return IsCommand(actual);
        }

        return false;
    }

    public static string CardFeature(this CardFeatureData cardFeature)
    {
        return cardFeature?.displayName;
    }

    /// <summary>Inspector / UI 向けの表示名（日英）。</summary>
    public static string GetDisplayName(Type cardType)
    {
        switch (cardType)
        {
            case Type.Unit:
                return GameLocale.T("ユニット", "Unit");
            case Type.Pilot:
                return GameLocale.T("パイロット", "Pilot");
            case Type.Command:
                return GameLocale.T("コマンド", "Command");
            case Type.Base:
                return GameLocale.T("ベース", "Base");
            case Type.ExResource:
                return GameLocale.T("EXリソース", "EX Resource");
            case Type.UnitToken:
                return GameLocale.T("ユニットトークン", "Unit Token");
            case Type.CommandPilot:
                return GameLocale.T("コマンドパイロット", "Command Pilot");
            default:
                return cardType.ToString();
        }
    }

    public static string GetDisplayName(this CardData card)
    {
        return card != null ? GetDisplayName(card.type) : "?";
    }
}
