using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>このターン中のユニットアタック宣言記録（A Miracle 等）。</summary>
public partial class BattleGameMain
{
    /// <summary>GD06-102 A Miracle。</summary>
    private const int Gd06102AMiracleCardId = 1000934;

    /// <summary>Player 側ユニットがこのターンアタック宣言した。</summary>
    private bool _playerUnitAttackDeclaredThisTurn;

    /// <summary>Enemy 側ユニットがこのターンアタック宣言した。</summary>
    private bool _enemyUnitAttackDeclaredThisTurn;

    private void MarkUnitAttackDeclaredThisTurn(PlayerType attackerOwner)
    {
        if (attackerOwner == PlayerType.Player)
        {
            if (_playerUnitAttackDeclaredThisTurn)
            {
                return;
            }

            _playerUnitAttackDeclaredThisTurn = true;
        }
        else
        {
            if (_enemyUnitAttackDeclaredThisTurn)
            {
                return;
            }

            _enemyUnitAttackDeclaredThisTurn = true;
        }

        Debug.Log($"[AttackTurnFlag] {attackerOwner} のユニットがこのターンアタック宣言した");
    }

    private bool HasUnitAttackDeclaredThisTurn(PlayerType attackerOwner)
    {
        return attackerOwner == PlayerType.Player
            ? _playerUnitAttackDeclaredThisTurn
            : _enemyUnitAttackDeclaredThisTurn;
    }

    /// <summary>
    /// 攻撃中のアクション一覧か。この間は「相手ユニットがアタックしていない」条件は満たさない。
    /// </summary>
    private bool IsAttackOnActionUiContext()
    {
        if (_actionStepSession != null && _actionStepSession.IsAttackContext)
        {
            return true;
        }

        if (attackFlowStrikeKind != AttackFlowStrikeKind.None || attackFlowAttackerUnit != null)
        {
            return true;
        }

        if (!string.IsNullOrEmpty(_onlineOnActionActiveContext)
            && _onlineOnActionActiveContext.IndexOf("attack", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        return false;
    }

    /// <summary>ownerType から見て、相手ユニットがこのターン1度でもアタックした（進行中含む）か。</summary>
    private bool HasOpponentUnitsAttackedThisTurn(PlayerType ownerType)
    {
        PlayerType opponent = OpponentSide(ownerType);
        if (HasUnitAttackDeclaredThisTurn(opponent))
        {
            return true;
        }

        if (attackFlowAttackerUnit != null && attackFlowStrikeKind != AttackFlowStrikeKind.None)
        {
            if (attackFlowAttackerOwner == opponent)
            {
                return true;
            }
        }

        if (_actionStepSession != null && _actionStepSession.IsAttackContext)
        {
            if (_actionStepSession.TurnPlayerSide == opponent)
            {
                return true;
            }

            CardController attacking = _actionStepSession.AttackingUnit;
            if (attacking != null && ResolveCardOwner(attacking.transform) == opponent)
            {
                return true;
            }
        }

        return false;
    }

    private void ClearAllUnitAttackDeclaredThisTurn()
    {
        _playerUnitAttackDeclaredThisTurn = false;
        _enemyUnitAttackDeclaredThisTurn = false;
    }

    private static bool TimedRequiresOpponentUnitsHaveNotAttackedThisTurn(TimedEffectData timed)
    {
        if (timed?.activationConditions == null)
        {
            return false;
        }

        for (int i = 0; i < timed.activationConditions.Count; i++)
        {
            EffectActivationCondition c = timed.activationConditions[i];
            if (c != null && c.checkKind == EffectActivationCheckKind.EnemyUnitsHaveNotAttackedThisTurn)
            {
                return true;
            }
        }

        return false;
    }

    private static bool CardRequiresOpponentUnitsHaveNotAttackedThisTurn(CardData data)
    {
        if (data?.timedEffects == null)
        {
            return false;
        }

        for (int i = 0; i < data.timedEffects.Count; i++)
        {
            TimedEffectData timed = data.timedEffects[i];
            if (timed != null
                && timed.timing == EffectTiming.OnAction
                && TimedRequiresOpponentUnitsHaveNotAttackedThisTurn(timed))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsGd06102AMiracle(CardController card)
    {
        return card != null && card.Data != null && card.Data.id == Gd06102AMiracleCardId;
    }

    /// <summary>
    /// 相手のターン中、かつ相手ユニットがこのターン1度もアタック宣言していない。
    /// ターン終了アクションは切替前なので、相手ターン終了時も相手ターンとして扱う。
    /// </summary>
    private bool CanActivateGd06102AMiracleNow(PlayerType ownerType)
    {
        if (IsAttackOnActionUiContext())
        {
            return false;
        }

        if (IsOwnerTurnForEffectActivation(ownerType))
        {
            return false;
        }

        return !HasOpponentUnitsAttackedThisTurn(ownerType);
    }

    private IEnumerator CoExecuteGd06102AMiracle(
        PlayerType side,
        CardController command,
        System.Action onDone)
    {
        if (!CanActivateGd06102AMiracleNow(side))
        {
            Debug.Log(
                $"[GD06-102] skip — ownerTurn:{IsOwnerTurnForEffectActivation(side)} "
                + $"opponentAttacked:{HasOpponentUnitsAttackedThisTurn(side)} side:{side}");
            FinishOnActionCommandAttempt(side, onDone, resolvedSuccessfully: false);
            yield break;
        }

        int cost = GetOnActionPlayCost(command, side);
        int requiredLevel = GetOnActionRequiredLevelForConsume(command, side);
        Gundam2024RuleScript.PlayerState state = GetRuleState(ToRuleSide(side));
        int exToUse = state != null ? Gundam2024RuleScript.GetExNeededForCost(state, cost) : 0;
        if (!TryConsumeResourceForCommandPlay(side, command, "OnAction", exToUse))
        {
            Debug.Log("[GD06-102] リソース不足");
            FinishOnActionCommandAttempt(side, onDone, resolvedSuccessfully: false);
            yield break;
        }

        CardGameRule rule = side == PlayerType.Player ? cardGameRule : enemyCardGameRule;
        int drawn = 0;
        for (int i = 0; i < 2; i++)
        {
            CardAddtoHand(rule, side);
            drawn++;
        }

        NoteEffectDrawResolved(side, drawn);
        MarkActionStepCardUsed(side, command);
        SendUsedCommandToTrash(command, side);
        Debug.Log($"[GD06-102] A Miracle 解決: 2枚ドロー side:{side} drawn:{drawn}");
        FinishOnActionCommandAttempt(side, onDone, resolvedSuccessfully: true);
    }
}
