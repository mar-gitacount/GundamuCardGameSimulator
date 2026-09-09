using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// オンライン対戦用デッキ・レギュレーション（暫定）。
/// ・メインデッキはちょうど 50 枚
/// ・色は 2 色まで（Colorless は色数に含めない）
/// </summary>
public static class OnlineDeckRegulation
{
    public const int RequiredMainDeckSize = 50;
    public const int MaxAllowedColors = 2;

    public readonly struct ValidationResult
    {
        public readonly bool IsValid;
        public readonly bool InvalidSize;
        public readonly bool TooManyColors;
        public readonly int TotalCards;
        public readonly int ColorCount;
        public readonly string MessageJa;
        public readonly string MessageEn;

        public ValidationResult(
            bool isValid,
            bool invalidSize,
            bool tooManyColors,
            int totalCards,
            int colorCount,
            string messageJa,
            string messageEn)
        {
            IsValid = isValid;
            InvalidSize = invalidSize;
            TooManyColors = tooManyColors;
            TotalCards = totalCards;
            ColorCount = colorCount;
            MessageJa = messageJa;
            MessageEn = messageEn;
        }
    }

    /// <summary>選択中デッキ（id→枚数）を検証する。</summary>
    public static ValidationResult Validate(IDictionary<int, int> cards)
    {
        int total = 0;
        var colors = new HashSet<CardColor>();

        if (cards != null)
        {
            foreach (KeyValuePair<int, int> entry in cards)
            {
                if (entry.Value <= 0)
                {
                    continue;
                }

                total += entry.Value;

                CardData data = ResolveCard(entry.Key);
                if (data == null)
                {
                    continue;
                }

                if (data.color != CardColor.Colorless)
                {
                    colors.Add(data.color);
                }
            }
        }

        bool invalidSize = total != RequiredMainDeckSize;
        bool tooManyColors = colors.Count > MaxAllowedColors;
        bool isValid = !invalidSize && !tooManyColors;

        string messageJa;
        string messageEn;
        BuildMessages(invalidSize, tooManyColors, total, colors.Count, out messageJa, out messageEn);

        return new ValidationResult(
            isValid,
            invalidSize,
            tooManyColors,
            total,
            colors.Count,
            messageJa,
            messageEn);
    }

    private static CardData ResolveCard(int cardId)
    {
        if (cardId <= 0)
        {
            return null;
        }

        if (CardDatabase.Instance != null)
        {
            return CardDatabase.Instance.FindById(cardId);
        }

        return null;
    }

    private static void BuildMessages(
        bool invalidSize,
        bool tooManyColors,
        int total,
        int colorCount,
        out string messageJa,
        out string messageEn)
    {
        var ja = new System.Text.StringBuilder();
        var en = new System.Text.StringBuilder();

        if (invalidSize)
        {
            ja.Append("デッキは").Append(RequiredMainDeckSize).Append("枚である必要があります（現在")
                .Append(total).Append("枚）。");
            en.Append("Deck must be exactly ").Append(RequiredMainDeckSize)
                .Append(" cards (current: ").Append(total).Append(").");
        }

        if (tooManyColors)
        {
            if (ja.Length > 0)
            {
                ja.Append('\n');
                en.Append('\n');
            }

            ja.Append("オンラインでは").Append(MaxAllowedColors).Append("色以内で使用できます（現在")
                .Append(colorCount).Append("色）。");
            en.Append("Online decks may use up to ").Append(MaxAllowedColors)
                .Append(" colors (current: ").Append(colorCount).Append(").");
        }

        if (ja.Length == 0)
        {
            messageJa = "デッキはレギュレーションを満たしています。";
            messageEn = "Deck meets online regulation.";
            return;
        }

        messageJa = ja.ToString();
        messageEn = en.ToString();
    }
}
