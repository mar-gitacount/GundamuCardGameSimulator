/// <summary>攻撃・ターン終了時のアクションステップ（blockthink タイトル用ヘルパー含む）。</summary>
// アクションタイミングのコメントテスト
public partial class BattleGameMain
{
    private string GetActionStepThinkSubtitle(PlayerType side, string context)
    {
        bool isAttackContext = !string.IsNullOrEmpty(context) && context.Contains("attack");
        if (isAttackContext)
        {
            bool isAttackerSide = _actionStepSession != null
                ? side == _actionStepSession.TurnPlayerSide
                : side == currentPlayerType;
            return isAttackerSide
                ? "Attacker action step"
                : "Defender action step";
        }

        return side == PlayerType.Player
            ? "Player action step"
            : "Opponent action step";
    }

    /// <summary>カードが無くてもアクションステップ UI を開く（Close で通過）。</summary>
    private void TryRunMandatoryOnActionStepPhase(
        PlayerType side,
        string context,
        System.Action onStepDone,
        CardController attackingUnitInAttackFlow = null)
    {
        if (IsOnlineBattle())
        {
            RunOnlineOnActionStepBody(side, context, onStepDone, attackingUnitInAttackFlow);
            return;
        }

        if (side == PlayerType.Enemy)
        {
            if (!TryExecuteEnemyOnActionStep(context, onStepDone, attackingUnitInAttackFlow))
            {
                onStepDone?.Invoke();
            }

            return;
        }

        if (!TryOpenOnActionCommandSelection(side, context, onStepDone, attackingUnitInAttackFlow))
        {
            onStepDone?.Invoke();
        }
    }
}
