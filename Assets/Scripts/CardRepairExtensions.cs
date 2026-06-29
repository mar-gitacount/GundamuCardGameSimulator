/// <summary>ターン終了時リペア（isRepair）の判定ヘルパー。</summary>
public static class CardRepairExtensions
{
    /// <summary>カード定義にリペア能力があるか（付与分は <see cref="CardController.GetTurnEndRepairAmount"/>）。</summary>
    public static bool HasRepairDefinition(this CardData card)
    {
        return card != null && card.isRepair && card.repairAmount > 0;
    }

    public static bool IsRepairEligibleUnit(this CardController unit)
    {
        if (unit == null || unit.Data == null || unit.CurrentHp <= 0)
        {
            return false;
        }

        return unit.Data.IsUnitLike() || unit.Data.type == Type.Base;
    }
}
