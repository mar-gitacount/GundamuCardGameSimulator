using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>必殺技コマンド発動のターン記録（リンク中アタック条件など）。</summary>
public partial class BattleGameMain
{
    private readonly HashSet<PlayerType> _ownerSpecialMoveCommandActivatedThisTurn =
        new HashSet<PlayerType>();

    private void MarkOwnerSpecialMoveCommandActivatedThisTurn(PlayerType ownerType)
    {
        _ownerSpecialMoveCommandActivatedThisTurn.Add(ownerType);
    }

    private bool HasOwnerActivatedSpecialMoveCommandThisTurn(PlayerType ownerType)
    {
        return _ownerSpecialMoveCommandActivatedThisTurn.Contains(ownerType);
    }

    private void ClearOwnerSpecialMoveCommandActivatedThisTurn(PlayerType ownerType)
    {
        _ownerSpecialMoveCommandActivatedThisTurn.Remove(ownerType);
    }
}
