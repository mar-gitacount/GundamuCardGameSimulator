using System;

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

    public static event Action MatchStateChanged;

    public static void BeginMatch(
        bool isHost,
        bool localPlayerGoesFirst,
        int seed,
        string localProductUserId,
        string remoteProductUserId)
    {
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
        MatchStateChanged?.Invoke();
    }
}
