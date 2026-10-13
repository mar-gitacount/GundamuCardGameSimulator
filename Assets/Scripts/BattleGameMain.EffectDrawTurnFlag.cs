using System.Collections.Generic;

/// <summary>
/// 効果ドローのターン記録。GD06-080 コウ・ウラキ等の
/// 「このターン中、効果でドローしているなら」条件用。ターン開始ドローは含めない。
/// </summary>
public partial class BattleGameMain
{
    private readonly HashSet<PlayerType> _ownerDrewWithEffectThisTurn = new HashSet<PlayerType>();
    private int _effectDrawTurnFlagSuppressDepth;

    private void MarkOwnerDrewWithEffectThisTurn(PlayerType ownerType)
    {
        _ownerDrewWithEffectThisTurn.Add(ownerType);
    }

    private bool HasOwnerDrewWithEffectThisTurn(PlayerType ownerType)
    {
        return _ownerDrewWithEffectThisTurn.Contains(ownerType);
    }

    private void ClearOwnerDrewWithEffectThisTurn(PlayerType ownerType)
    {
        _ownerDrewWithEffectThisTurn.Remove(ownerType);
    }

    private void ClearAllOwnerDrewWithEffectThisTurn()
    {
        _ownerDrewWithEffectThisTurn.Clear();
    }

    /// <summary>開始手札・マリガン・ターン開始ドローなど、効果ドローではない山札引き。</summary>
    private void WithEffectDrawTurnFlagSuppressed(System.Action action)
    {
        _effectDrawTurnFlagSuppressDepth++;
        try
        {
            action?.Invoke();
        }
        finally
        {
            _effectDrawTurnFlagSuppressDepth--;
            if (_effectDrawTurnFlagSuppressDepth < 0)
            {
                _effectDrawTurnFlagSuppressDepth = 0;
            }
        }
    }

    private void NoteEffectDrawResolved(PlayerType ownerType, int drawnCount)
    {
        if (_effectDrawTurnFlagSuppressDepth > 0 || drawnCount <= 0)
        {
            return;
        }

        MarkOwnerDrewWithEffectThisTurn(ownerType);
    }
}
