using System;
using UnityEngine;

/// <summary>
/// ガンダムカードゲーム(2024)向けの基本ルール進行クラス。
/// 既存の BattleGameMain / CardGameRule から呼び出して使うことを想定。
/// </summary>
public class Gundam2024RuleScript
{
    public enum TurnPhase
    {
        Start,
        Resource,
        Main,
        Battle,
        End
    }

    public enum PlayerSide
    {
        Player,
        Enemy
    }

    [Serializable]
    public class RuleConfig
    {
        public int maxLevel = 10;
        /// <summary>初期シールド枚数は山札から5枚をシールドエリアに置く処理で上書きする。</summary>
        public int startingShield = 0;
        /// <summary>初期手札は BattleGameMain で物理ドローするため 0。オープニング後に SyncOpeningHandState で同期。</summary>
        public int startingHand = 0;
        public int drawPerTurn = 1;
        /// <summary>先攻・後攻に関わらずターン開始時にレベルへ加算（リソースはレベルに合わせてリフレッシュ）。</summary>
        public int levelGainPerTurn = 1;
    }

    [Serializable]
    public class PlayerState
    {
        public int level;
        public int resource;
        /// <summary>シールド枚数（山札からシールドエリアに置いたカード数と同期させる想定）。</summary>
        public int shield;
        /// <summary>EXベース（ルール上のカウンター。初期値はマリガン後のセットアップで設定）。</summary>
        public int exBase;
        public int handCount;
        public int deckCount;

        public void ResetForGameStart(RuleConfig config, int initialDeckCount)
        {
            level = 0;
            resource = 0;
            shield = 0;
            exBase = 0;
            handCount = config.startingHand;
            deckCount = initialDeckCount;
        }
    }

    public RuleConfig Config { get; } = new RuleConfig();
    public TurnPhase CurrentPhase { get; private set; } = TurnPhase.Start;
    public PlayerSide CurrentTurnPlayer { get; private set; } = PlayerSide.Player;

    public PlayerState Player { get; } = new PlayerState();
    public PlayerState Enemy { get; } = new PlayerState();

    public event Action<PlayerSide, TurnPhase> OnPhaseChanged;
    public event Action<PlayerSide> OnTurnChanged;

    public void InitializeGame(int playerDeckCount, int enemyDeckCount, PlayerSide firstPlayer)
    {
        CurrentTurnPlayer = firstPlayer;
        CurrentPhase = TurnPhase.Start;
        Player.ResetForGameStart(Config, playerDeckCount);
        Enemy.ResetForGameStart(Config, enemyDeckCount);
    }

    /// <summary>現在のターンプレイヤーをバトル側と一致させる（BeginTurn の対象判定用）。</summary>
    public void SetCurrentTurnPlayer(PlayerSide side)
    {
        CurrentTurnPlayer = side;
    }

    /// <summary>初期5枚ドロー後に、手札枚数・残り山札を実物に合わせる。</summary>
    public void SyncOpeningHandState(int playerHandCount, int playerDeckRemain, int enemyHandCount, int enemyDeckRemain)
    {
        Player.handCount = Mathf.Max(0, playerHandCount);
        Player.deckCount = Mathf.Max(0, playerDeckRemain);
        Enemy.handCount = Mathf.Max(0, enemyHandCount);
        Enemy.deckCount = Mathf.Max(0, enemyDeckRemain);
    }

    /// <summary>
    /// マリガン後：EXベースを設定し、山札からシールド用に引いた枚数分デッキを減らしシールド枚数を設定する。
    /// </summary>
    public void ApplyExBaseAndShieldAfterMulligan(PlayerSide side, int exBasePoints, int shieldCardCount, int deckRemainAfterShieldDraw)
    {
        PlayerState state = GetState(side);
        state.exBase = Mathf.Max(0, exBasePoints);
        state.shield = Mathf.Max(0, shieldCardCount);
        state.deckCount = Mathf.Max(0, deckRemainAfterShieldDraw);
    }

    public void BeginTurn()
    {
        CurrentPhase = TurnPhase.Start;
        PlayerState state = GetState(CurrentTurnPlayer);
        GainLevelAndRefreshResource(state);
        DrawCards(state, Config.drawPerTurn);
        OnPhaseChanged?.Invoke(CurrentTurnPlayer, CurrentPhase);
    }

    public void AdvancePhase()
    {
        CurrentPhase = CurrentPhase switch
        {
            TurnPhase.Start => TurnPhase.Resource,
            TurnPhase.Resource => TurnPhase.Main,
            TurnPhase.Main => TurnPhase.Battle,
            TurnPhase.Battle => TurnPhase.End,
            TurnPhase.End => TurnPhase.Start,
            _ => TurnPhase.Start
        };

        OnPhaseChanged?.Invoke(CurrentTurnPlayer, CurrentPhase);

        if (CurrentPhase == TurnPhase.Start)
        {
            // End -> Start に来たらターンプレイヤーを交代する。
            SwitchTurnPlayer();
        }
    }

    public bool CanPlayCard(PlayerSide side, CardData card)
    {
        if (card == null)
        {
            return false;
        }

        PlayerState state = GetState(side);
        return state.level >= card.level && state.resource >= card.cost;
    }

    public bool TryConsumeResource(PlayerSide side, int cost)
    {
        if (cost < 0)
        {
            return false;
        }

        PlayerState state = GetState(side);
        if (state.resource < cost)
        {
            return false;
        }

        state.resource -= cost;
        return true;
    }

    public void DamageShield(PlayerSide targetSide, int amount = 1)
    {
        if (amount <= 0)
        {
            return;
        }

        PlayerState target = GetState(targetSide);
        target.shield = Mathf.Max(0, target.shield - amount);
    }

    public bool IsDefeated(PlayerSide side)
    {
        return GetState(side).shield <= 0;
    }

    public bool IsGameOver(out PlayerSide winner)
    {
        if (IsDefeated(PlayerSide.Player))
        {
            winner = PlayerSide.Enemy;
            return true;
        }

        if (IsDefeated(PlayerSide.Enemy))
        {
            winner = PlayerSide.Player;
            return true;
        }

        winner = PlayerSide.Player;
        return false;
    }

    private void SwitchTurnPlayer()
    {
        CurrentTurnPlayer = CurrentTurnPlayer == PlayerSide.Player ? PlayerSide.Enemy : PlayerSide.Player;
        OnTurnChanged?.Invoke(CurrentTurnPlayer);
    }

    private void GainLevelAndRefreshResource(PlayerState state)
    {
        state.level = Mathf.Min(Config.maxLevel, state.level + Config.levelGainPerTurn);
        state.resource = state.level;
    }

    private void DrawCards(PlayerState state, int amount)
    {
        int drawAmount = Mathf.Max(0, amount);
        int actualDraw = Mathf.Min(drawAmount, state.deckCount);
        state.handCount += actualDraw;
        state.deckCount -= actualDraw;
    }

    private PlayerState GetState(PlayerSide side)
    {
        return side == PlayerSide.Player ? Player : Enemy;
    }
}
