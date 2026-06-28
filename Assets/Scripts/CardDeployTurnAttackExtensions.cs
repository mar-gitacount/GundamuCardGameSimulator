/// <summary>
/// 配備ターンから攻撃可能（<see cref="CardData.isDeployTurnAttack"/>）の判定。
/// </summary>
public static class CardDeployTurnAttackExtensions
{
    /// <summary>ユニットとして配備ターン攻撃を持つか。</summary>
    public static bool CanAttackOnDeployTurn(this CardData card)
    {
        return card != null && card.type == Type.Unit && card.isDeployTurnAttack;
    }
}
