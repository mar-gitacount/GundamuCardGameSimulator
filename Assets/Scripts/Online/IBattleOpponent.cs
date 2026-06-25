/// <summary>
/// BattleGameMain から相手種別を切り替えるための最小責務。
/// まずは CPU と Network の入口だけ分ける。
/// </summary>
public interface IBattleOpponent
{
    bool IsNetwork { get; }
    void OnEnterEnemyMainPhase(BattleGameMain battle);
}
