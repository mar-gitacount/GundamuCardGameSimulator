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
        System.Action onComplete)
    {
        _actionStepSession = new ActionStepSessionState
        {
            SessionId = ++_actionStepSessionIdCounter,
            TurnPlayerSide = turnPlayerSide,
            IsAttackContext = isAttackContext,
            DefenderContext = defenderContext ?? string.Empty,
            AttackerContext = attackerContext ?? string.Empty,
            AttackingUnit = attackingUnit,
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

        isOnActionPopupOpen = activeOnActionPopupRoot != null || _activeLookDeckPopupRoot != null;
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
            Debug.LogWarning($"[ActionStep] UI could not open for {side} — treating as ActionEnd.");
            AdvanceActionStepSession(side, ActionStepPassKind.ActionEnd);
        }
    }

    private void ResolveActionStepUi(PlayerType side, ActionStepPassKind passKind, GameObject popupRoot)
    {
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
}
