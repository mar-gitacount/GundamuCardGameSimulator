using System.Collections.Generic;

/// <summary>
/// 同一効果チェーン内で直前の効果が観測したカード（山札→トラッシュ等）を保持する。
/// </summary>
public sealed class EffectChainObservation
{
    private readonly List<CardData> _cards = new List<CardData>();

    public IReadOnlyList<CardData> Cards => _cards;

    public bool HasCards => _cards.Count > 0;

    public void Add(CardData cardData)
    {
        if (cardData != null)
        {
            _cards.Add(cardData);
        }
    }

    public void Clear()
    {
        _cards.Clear();
    }
}
