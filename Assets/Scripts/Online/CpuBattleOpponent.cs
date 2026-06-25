using UnityEngine;

/// <summary>
/// 既存の Enemy AI コルーチンを呼ぶだけの CPU 相手実装。
/// </summary>
public sealed class CpuBattleOpponent : IBattleOpponent
{
    public bool IsNetwork => false;

    public void OnEnterEnemyMainPhase(BattleGameMain battle)
    {
        if (battle == null)
        {
            Debug.LogWarning("[BattleOpponent] battle is null.");
            return;
        }

        battle.StartEnemyAiTurn();
    }
}
