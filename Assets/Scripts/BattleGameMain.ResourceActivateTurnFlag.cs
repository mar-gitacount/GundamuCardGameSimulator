using System.Collections.Generic;

/// <summary>
/// 効果（ActivateResource）によるリソースアクティブ化のターン記録。
/// ST14-015「戦場を駆ける想い」等の「このターン中、効果でアクティブにしていない」条件用。
/// </summary>
public partial class BattleGameMain
{
    private readonly HashSet<PlayerType> _ownerActivatedResourceByEffectThisTurn =
        new HashSet<PlayerType>();

    private void MarkOwnerActivatedResourceByEffectThisTurn(PlayerType ownerType)
    {
        _ownerActivatedResourceByEffectThisTurn.Add(ownerType);
    }

    private bool HasOwnerActivatedResourceByEffectThisTurn(PlayerType ownerType)
    {
        return _ownerActivatedResourceByEffectThisTurn.Contains(ownerType);
    }

    private void ClearOwnerActivatedResourceByEffectThisTurn(PlayerType ownerType)
    {
        _ownerActivatedResourceByEffectThisTurn.Remove(ownerType);
    }
}
