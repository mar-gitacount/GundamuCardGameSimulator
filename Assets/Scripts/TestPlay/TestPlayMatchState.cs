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

    public static event Action SessionChanged;

    /// <summary>プレイヤーデッキ選択済みのあと、敵デッキ選択モードに入る。</summary>
    public static void BeginEnemyDeckPick()
    {
        EosOnlineMatchState.Clear();
        HasActiveSession = false;
        IsAwaitingEnemyDeckPick = true;
        SessionChanged?.Invoke();
        Debug.Log("[TestPlay] Select an enemy deck from the list to start.");
    }

    public static void Begin()
    {
        EosOnlineMatchState.Clear();
        IsAwaitingEnemyDeckPick = false;
        HasActiveSession = true;
        SessionChanged?.Invoke();
        Debug.Log("[TestPlay] Session began.");
    }

    public static void Clear()
    {
        bool changed = HasActiveSession || IsAwaitingEnemyDeckPick;
        HasActiveSession = false;
        IsAwaitingEnemyDeckPick = false;
        if (changed)
        {
            SessionChanged?.Invoke();
            Debug.Log("[TestPlay] Session cleared.");
        }
    }
}
