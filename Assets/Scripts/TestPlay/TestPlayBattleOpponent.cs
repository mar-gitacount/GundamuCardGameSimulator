using UnityEngine;

/// <summary>
/// TestPlay 用の相手実装。AI は呼ばず、人間が Enemy 側を操作する。
/// </summary>
public sealed class TestPlayBattleOpponent : IBattleOpponent
{
    public bool IsNetwork => false;

    public void OnEnterEnemyMainPhase(BattleGameMain battle)
    {
        if (battle == null)
        {
            Debug.LogWarning("[TestPlay] battle is null.");
            return;
        }

        battle.EnterTestPlayControlledMainPhase(BattleGameMain.PlayerType.Enemy);
    }
}
