/// <summary>
/// 先制攻撃（ユニット戦で相手より先に AP ダメージ。撃破時は反撃を受けない）の判定。
/// </summary>
public static class CardFirstStrikeExtensions
{
    public static bool HasFirstStrike(this CardController unit)
    {
        return unit != null && unit.HasFirstStrikeUntilEndOfTurnGrant;
    }

    public static bool HasOperationMeteorFeature(this CardData card)
    {
        return card != null && card.HasFeatureKey(CardFeatureKeys.OperationMeteor);
    }
}
