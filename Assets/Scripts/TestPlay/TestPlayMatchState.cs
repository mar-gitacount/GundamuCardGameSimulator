using System;
using UnityEngine;

/// <summary>
/// ソロ TestPlay 用のセッション状態。オンライン / AI 対戦とは独立。
/// AI バトル同様、プレイヤーデッキ選択 → TestPlay ボタン → 敵デッキ選択 → 開始。
/// </summary>
public static class TestPlayMatchState
{
    public static bool HasActiveSession { get; private set; }

    /// <summary>TestPlay ボタン押下後、敵デッキ選択待ち。</summary>
    public static bool IsAwaitingEnemyDeckPick { get; private set; }

    /// <summary>自分デッキ表示名（マリガン UI 用）。</summary>
    public static string PlayerDeckTitle { get; private set; } = string.Empty;

    /// <summary>相手デッキ表示名（マリガン UI 用）。</summary>
    public static string EnemyDeckTitle { get; private set; } = string.Empty;

    public static event Action SessionChanged;

    /// <summary>プレイヤーデッキ選択済みのあと、敵デッキ選択モードに入る。</summary>
    public static void BeginEnemyDeckPick()
    {
        EosOnlineMatchState.Clear();
        HasActiveSession = false;
        IsAwaitingEnemyDeckPick = true;
        PlayerDeckTitle = string.Empty;
        EnemyDeckTitle = string.Empty;
        SessionChanged?.Invoke();
        Debug.Log("[TestPlay] Select an enemy deck from the list to start.");
    }

    public static void Begin(string playerDeckTitle = null, string enemyDeckTitle = null)
    {
        EosOnlineMatchState.Clear();
        IsAwaitingEnemyDeckPick = false;
        HasActiveSession = true;
        PlayerDeckTitle = playerDeckTitle ?? string.Empty;
        EnemyDeckTitle = enemyDeckTitle ?? string.Empty;
        SessionChanged?.Invoke();
        Debug.Log(
            $"[TestPlay] Session began. playerDeck:'{PlayerDeckTitle}' enemyDeck:'{EnemyDeckTitle}'");
    }

    public static void Clear()
    {
        bool changed = HasActiveSession || IsAwaitingEnemyDeckPick;
        HasActiveSession = false;
        IsAwaitingEnemyDeckPick = false;
        PlayerDeckTitle = string.Empty;
        EnemyDeckTitle = string.Empty;
        if (changed)
        {
            SessionChanged?.Invoke();
            Debug.Log("[TestPlay] Session cleared.");
        }
    }
}
