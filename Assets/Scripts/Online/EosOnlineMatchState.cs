using System;
using System.Collections.Generic;

/// <summary>
/// EOS のオンライン対戦用に、ロビー/P2P からバトルへ渡す最小限の共有状態。
/// シーン遷移をまたがず Home 内でバトルキャンバスへ渡す用途でも使う。
/// </summary>
public static class EosOnlineMatchState
{
    public static bool HasActiveMatch { get; private set; }
    public static bool IsHost { get; private set; }
    public static bool LocalPlayerGoesFirst { get; private set; }
    public static int Seed { get; private set; }
    public static string LocalProductUserId { get; private set; }
    public static string RemoteProductUserId { get; private set; }

    /// <summary>
    /// ランダムマッチ／オンライン開始押下時点の自分デッキ。
    /// マッチング中のデッキ編集で上書きされないよう、バトル開始まで保持する。
    /// </summary>
    public static TestPlayDeckPick LockedPlayerDeck { get; private set; }

    public static event Action MatchStateChanged;

    public static void LockPlayerDeck(TestPlayDeckPick pick)
    {
        LockedPlayerDeck = pick;
        MatchStateChanged?.Invoke();
    }

    public static void ClearLockedPlayerDeck()
    {
        if (LockedPlayerDeck == null)
        {
            return;
        }

        LockedPlayerDeck = null;
        MatchStateChanged?.Invoke();
    }

    /// <summary>
    /// ランダムマッチ等でロック中のデッキ（storageKey 一致）は編集不可。
    /// </summary>
    public static bool IsStorageKeyLockedForEdit(string storageKey)
    {
        if (LockedPlayerDeck == null || string.IsNullOrEmpty(LockedPlayerDeck.StorageKey))
        {
            return false;
        }

        if (string.IsNullOrEmpty(storageKey))
        {
            return false;
        }

        return string.Equals(storageKey, LockedPlayerDeck.StorageKey, StringComparison.Ordinal);
    }

    /// <summary>ロック済みデッキのカードコピーを返す。無ければ false。</summary>
    public static bool TryGetLockedPlayerDeckCards(out Dictionary<int, int> cards)
    {
        cards = null;
        if (LockedPlayerDeck?.Cards == null || LockedPlayerDeck.Cards.Count == 0)
        {
            return false;
        }

        cards = TestPlayDeckPick.CopyCards(LockedPlayerDeck.Cards);
        return cards.Count > 0;
    }

    public static void BeginMatch(
        bool isHost,
        bool localPlayerGoesFirst,
        int seed,
        string localProductUserId,
        string remoteProductUserId)
    {
        TestPlayMatchState.Clear();
        HasActiveMatch = true;
        IsHost = isHost;
        LocalPlayerGoesFirst = localPlayerGoesFirst;
        Seed = seed;
        LocalProductUserId = localProductUserId ?? string.Empty;
        RemoteProductUserId = remoteProductUserId ?? string.Empty;
        MatchStateChanged?.Invoke();
    }

    public static void Clear()
    {
        HasActiveMatch = false;
        IsHost = false;
        LocalPlayerGoesFirst = false;
        Seed = 0;
        LocalProductUserId = string.Empty;
        RemoteProductUserId = string.Empty;
        ClearLockedPlayerDeck();
        MatchStateChanged?.Invoke();
    }
}
