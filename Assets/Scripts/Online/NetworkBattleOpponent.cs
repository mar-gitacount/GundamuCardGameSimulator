using UnityEngine;

/// <summary>
/// ネットワーク相手の入口。
/// ここでは CPU を動かさず、相手ターンの進行は P2P メッセージ待ちに切り替える。
/// </summary>
public sealed class NetworkBattleOpponent : IBattleOpponent
{
    public bool IsNetwork => true;

    public void OnEnterEnemyMainPhase(BattleGameMain battle)
    {
        if (battle == null)
        {
            Debug.LogWarning("[BattleOpponent] battle is null.");
            return;
        }

        battle.EnterRemoteEnemyMainPhase();
    }
}
