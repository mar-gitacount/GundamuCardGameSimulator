using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// アクションステップの交互ターン（非ターンプレイヤー→ターンプレイヤー→…）と ActionEnd 管理。
/// </summary>
public partial class BattleGameMain
{
  private enum ActionStepPassKind
    {
        Pass = 0,
        ActionEnd = 1,
    }

    private sealed class ActionStepSessionState
    {
        public int SessionId;
        public PlayerType TurnPlayerSide;
        public bool IsAttackContext;
        public string DefenderContext = string.Empty;
        public string AttackerContext = string.Empty;
        public CardController AttackingUnit;
        /// <summary>攻撃フロー由来のアクション時のみ。最終バトル相手（ブロッカー優先）。</summary>
        public CardController DefendingUnit;
        public System.Action OnComplete;
        public bool PlayerEnded;
        public bool EnemyEnded;
        public PlayerType CurrentActorSide;
        public readonly HashSet<CardController> PlayerUsedCards = new HashSet<CardController>();
        public readonly HashSet<CardController> EnemyUsedCards = new HashSet<CardController>();

        public HashSet<CardController> UsedCardsFor(PlayerType side)
        {
            return side == PlayerType.Player ? PlayerUsedCards : EnemyUsedCards;
        }

        public bool IsEnded(PlayerType side)
        {
            return side == PlayerType.Player ? PlayerEnded : EnemyEnded;
        }

        public void MarkEnded(PlayerType side)
        {
            if (side == PlayerType.Player)
            {
                PlayerEnded = true;
            }
            else
            {
                EnemyEnded = true;
            }
        }

        public bool BothEnded => PlayerEnded && EnemyEnded;

        public string ContextFor(PlayerType side)
        {
            if (!IsAttackContext)
            {
                return side == PlayerType.Player ? "turn end:player-action" : "turn end:enemy-action";
            }

            return side == TurnPlayerSide ? AttackerContext : DefenderContext;
        }
    }

    private int _actionStepSessionIdCounter;
    private ActionStepSessionState _actionStepSession;

    private bool IsActionStepSessionActive => _actionStepSession != null;

    private bool IsActionStepCardUsedForSide(PlayerType side, CardController card)
    {
        if (card == null)
        {
            return false;
        }

        if (IsActionStepSessionActive)
        {
            return _actionStepSession.UsedCardsFor(side).Contains(card);
        }

        if (IsOnlineBattle() && side == PlayerType.Player)
        {
            return _onlineActionStepUsedCards.Contains(card);
        }

        return false;
    }

    private void MarkActionStepCardUsed(PlayerType side, CardController card)
    {
        if (card == null)
        {
            return;
        }

        if (IsActionStepSessionActive)
        {
            _actionStepSession.UsedCardsFor(side).Add(card);
            return;
        }

        if (IsOnlineBattle() && side == PlayerType.Player)
        {
            _onlineActionStepUsedCards.Add(card);
        }
    }

    private static PlayerType OpponentSide(PlayerType side)
    {
        return side == PlayerType.Player ? PlayerType.Enemy : PlayerType.Player;
    }

    private void BeginActionStepSession(
        PlayerType turnPlayerSide,
        PlayerType firstActorSide,
        bool isAttackContext,
        string defenderContext,
        string attackerContext,
        CardController attackingUnit,
        System.Action onComplete,
        CardController defendingUnit = null)
    {
        _actionStepSession = new ActionStepSessionState
        {
            SessionId = ++_actionStepSessionIdCounter,
            TurnPlayerSide = turnPlayerSide,
            IsAttackContext = isAttackContext,
            DefenderContext = defenderContext ?? string.Empty,
            AttackerContext = attackerContext ?? string.Empty,
            AttackingUnit = attackingUnit,
            DefendingUnit = defendingUnit,
            OnComplete = onComplete,
            CurrentActorSide = firstActorSide,
        };

        Debug.Log(
            $"[ActionStep] Session begin id:{_actionStepSession.SessionId} turn:{turnPlayerSide} "
            + $"first:{firstActorSide} attack:{isAttackContext}");
        ResetOnlineActionStepEndedTracking();
        _onlineActiveActionStepSessionId = _actionStepSession.SessionId;
        RunActionStepForSide(firstActorSide);
    }

    private void CompleteActionStepSession()
    {
        if (_actionStepSession == null)
        {
            return;
        }

        Debug.Log($"[ActionStep] Session complete id:{_actionStepSession.SessionId}");
        bool wasTurnEndActionStep = !_actionStepSession.IsAttackContext;
        System.Action complete = _actionStepSession.OnComplete;
        _actionStepSession = null;
        _onlineOnActionActiveContext = null;
        EndActionStepCommandResolve();
        complete?.Invoke();
        if (wasTurnEndActionStep)
        {
            TryAdvanceTurnAfterOnlineTurnEndActionStep();
        }
    }

    private void AdvanceActionStepSession(PlayerType fromSide, ActionStepPassKind passKind)
    {
        if (_actionStepSession == null)
        {
            return;
        }

        if (passKind == ActionStepPassKind.ActionEnd)
        {
            _actionStepSession.MarkEnded(fromSide);
            Debug.Log(
                $"[ActionStep] {fromSide} ActionEnd "
                + $"(playerEnded:{_actionStepSession.PlayerEnded} enemyEnded:{_actionStepSession.EnemyEnded})");
        }
        else
        {
            Debug.Log($"[ActionStep] {fromSide} Pass");
        }

        CloseActionStepPopupState();

        if (_actionStepSession.BothEnded)
        {
            CompleteActionStepSession();
            return;
        }

        PlayerType next = OpponentSide(fromSide);
        if (_actionStepSession.IsEnded(next))
        {
            if (_actionStepSession.IsEnded(fromSide))
            {
                CompleteActionStepSession();
                return;
            }

            next = fromSide;
        }

        RunActionStepForSide(next);
    }

    private void CloseActionStepPopupState()
    {
        _onlineOnActionActiveContext = null;
        // Look UI は別ルート。アクション終了で破壊時 Look を消さない。
        if (activeOnActionPopupRoot != null && activeOnActionPopupRoot != _activeLookDeckPopupRoot)
        {
            DestroyActiveOnActionPopupIfAny();
        }
        else
        {
            activeOnActionPopupRoot = null;
        }

        isOnActionPopupOpen = activeOnActionPopupRoot != null
            || _activeLookDeckPopupRoot != null
            || _isActionStepCommandResolving
            || _activeResourcePaymentOverlay != null;
    }

    private static void SetActionStepButtonInteractable(Button btn, bool interactable)
    {
        if (btn == null)
        {
            return;
        }

        btn.interactable = interactable;
        Image img = btn.GetComponent<Image>();
        if (img != null)
        {
            img.color = interactable
                ? Color.white
                : new Color(0.42f, 0.42f, 0.42f, 0.85f);
        }

        TextMeshProUGUI label = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (label != null)
        {
            label.color = interactable ? Color.black : new Color(0.25f, 0.25f, 0.25f, 1f);
        }
    }

    private void RunActionStepForSide(PlayerType side)
    {
        if (_actionStepSession == null)
        {
            return;
        }

        if (_actionStepSession.IsEnded(side))
        {
            AdvanceActionStepSession(side, ActionStepPassKind.Pass);
            return;
        }

        _actionStepSession.CurrentActorSide = side;
        string context = _actionStepSession.ContextFor(side);
        CardController attackingUnit = _actionStepSession.AttackingUnit;

        if (IsOnlineBattle())
        {
            RunOnlineOnActionStepBody(side, context, null, attackingUnit);
            return;
        }

        if (side == PlayerType.Enemy)
        {
            if (TryExecuteEnemyForcedOnActionsIfAny(
                    () =>
                    {
                        AdvanceActionStepSession(PlayerType.Enemy, ActionStepPassKind.Pass);
                    },
                    attackingUnit))
            {
                return;
            }

            if (TryExecuteEnemyOnActionStep(context, () =>
                {
                    AdvanceActionStepSession(PlayerType.Enemy, ActionStepPassKind.Pass);
                }, attackingUnit))
            {
                return;
            }

            AdvanceActionStepSession(PlayerType.Enemy, ActionStepPassKind.ActionEnd);
            return;
        }

        if (!TryOpenOnActionCommandSelection(side, context, null, attackingUnit))
        {
            if (HasPendingForcedOnAction(side))
            {
                Debug.LogWarning(
                    $"[ActionStep] UI could not open for {side} but forced OnAction pending — retry.");
                return;
            }

            Debug.LogWarning($"[ActionStep] UI could not open for {side} — treating as ActionEnd.");
            AdvanceActionStepSession(side, ActionStepPassKind.ActionEnd);
        }
    }

    private void ResolveActionStepUi(PlayerType side, ActionStepPassKind passKind, GameObject popupRoot)
    {
        if (ShouldBlockActionStepPassOrEnd(side, passKind, null)
            && passKind == ActionStepPassKind.ActionEnd)
        {
            Debug.LogWarning(
                $"[ForcedOnAction] ResolveActionStepUi blocked ActionEnd for {side} — reopen UI.");
            if (popupRoot != null)
            {
                Destroy(popupRoot);
            }

            CloseActionStepPopupState();
            if (IsActionStepSessionActive)
            {
                RunActionStepForSide(side);
            }

            return;
        }

        if (popupRoot != null)
        {
            Destroy(popupRoot);
        }

        CloseActionStepPopupState();

        if (!IsActionStepSessionActive)
        {
            return;
        }

        if (IsOnlineBattle() && side == PlayerType.Player)
        {
            int requestId = _pendingOnlineOnActionRequestId > 0
                ? _pendingOnlineOnActionRequestId
                : _onlineOnActionResponseRequestId;
            SendOnlineActionStepResolution(requestId, side, passKind);
            _pendingOnlineOnActionRequestId = 0;
            _onlineOnActionResponseRequestId = 0;
        }

        AdvanceActionStepSession(side, passKind);
    }

    private PlayerType MirrorOnlineActingZoneToLocalPlayerType(int actingZoneSide)
    {
        if (currentPlayerType == PlayerType.Player)
        {
            return actingZoneSide == (int)PlayerType.Player
                ? PlayerType.Enemy
                : PlayerType.Player;
        }

        return actingZoneSide == (int)PlayerType.Player
            ? PlayerType.Player
            : PlayerType.Enemy;
    }

    private void TryAdvanceActionStepSessionFromOnline(int actingZoneSide, ActionStepPassKind passKind)
    {
        if (!IsActionStepSessionActive)
        {
            return;
        }

        PlayerType actingSide = MirrorOnlineActingZoneToLocalPlayerType(actingZoneSide);
        AdvanceActionStepSession(actingSide, passKind);
    }

    /// <summary>ActionStep で Confirm 後に非表示にしているカード選択 UI（対象選択中に破棄しない）。</summary>
    private GameObject _actionStepHiddenSelectionRoot;

    /// <summary>カード確定直後に選択 UI を隠し、コスト支払い中に ActionStep 一覧が被らないようにする。</summary>
    private void BeginActionStepCommandResolve(GameObject selectionRoot)
    {
        _isActionStepCommandResolving = true;
        _actionStepHiddenSelectionRoot = selectionRoot;
        if (selectionRoot != null)
        {
            selectionRoot.SetActive(false);
        }
    }

    private void EndActionStepCommandResolve()
    {
        _isActionStepCommandResolving = false;
    }

    /// <summary>
    /// 対象なし／キャンセル／支払い失敗時：Pass せず Action 一覧へ戻す。
    /// （Pass すると相手 ActionEnd → 自分 UI 再表示が続き、同一カードでループする）
    /// </summary>
    private void RestoreActionStepSelectionAfterCommandAbort(PlayerType side)
    {
        EndActionStepCommandResolve();

        GameObject hidden = _actionStepHiddenSelectionRoot;
        _actionStepHiddenSelectionRoot = null;

        // 支払いオーバーレイが残っていれば閉じる（一覧復帰を阻害しない）
        if (_activeResourcePaymentOverlay != null)
        {
            CloseResourcePaymentOverlay(_activeResourcePaymentOverlay);
        }

        // 対象選択 UI などが active なら閉じる（隠した Action 一覧は残す）
        if (activeOnActionPopupRoot != null && activeOnActionPopupRoot != hidden)
        {
            Destroy(activeOnActionPopupRoot);
            activeOnActionPopupRoot = null;
        }

        if (hidden != null && hidden)
        {
            activeOnActionPopupRoot = hidden;
            hidden.SetActive(true);
            isOnActionPopupOpen = true;
            Debug.Log($"[ActionStep] Command abort — restored selection UI (side:{side})");
            return;
        }

        // 隠した一覧が既に破棄されている場合のみ、新規に開き直す
        if (IsActionStepSessionActive)
        {
            Debug.Log($"[ActionStep] Command abort — reopen selection UI (side:{side})");
            RunActionStepForSide(side);
        }
    }

    /// <summary>
    /// OnAction コマンド試行の完了。成功時のみ queue を進めて Pass。失敗時は一覧復帰（Pass しない）。
    /// </summary>
    private void FinishOnActionCommandAttempt(PlayerType side, System.Action queueOnDone, bool resolvedSuccessfully)
    {
        if (resolvedSuccessfully)
        {
            EndActionStepCommandResolve();
            queueOnDone?.Invoke();
            return;
        }

        if (IsActionStepSessionActive || _actionStepHiddenSelectionRoot != null || _isActionStepCommandResolving)
        {
            RestoreActionStepSelectionAfterCommandAbort(side);
            return;
        }

        queueOnDone?.Invoke();
    }
}
