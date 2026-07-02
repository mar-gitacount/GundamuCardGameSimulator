using Epic.OnlineServices;
using PlayEveryWare.EpicOnlineServices;
using PlayEveryWare.EpicOnlineServices.Samples;
using UnityEngine;

/// <summary>
/// アプリ終了時に EOS P2P / ロビー / バトル購読を先に片付け、ネイティブ SDK の後処理クラッシュを防ぐ。
/// </summary>
[DefaultExecutionOrder(-10000)]
public class EosOnlineShutdownCoordinator : MonoBehaviour
{
    public static EosOnlineShutdownCoordinator Instance { get; private set; }
    public static bool IsShuttingDown { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void EnsureExists()
    {
        if (Instance != null)
        {
            return;
        }

        GameObject go = new GameObject(nameof(EosOnlineShutdownCoordinator));
        DontDestroyOnLoad(go);
        go.AddComponent<EosOnlineShutdownCoordinator>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        Application.wantsToQuit += OnWantsToQuit;
        Application.quitting += OnQuitting;
    }

    private void OnDestroy()
    {
        Application.wantsToQuit -= OnWantsToQuit;
        Application.quitting -= OnQuitting;
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void OnApplicationQuit()
    {
        RunShutdown("OnApplicationQuit");
    }

    private bool OnWantsToQuit()
    {
        RunShutdown("wantsToQuit");
        return true;
    }

    private void OnQuitting()
    {
        RunShutdown("quitting");
    }

    /// <summary>終了シーケンス。複数回呼ばれても安全。</summary>
    public static void RunShutdown(string reason)
    {
        if (IsShuttingDown)
        {
            return;
        }

        IsShuttingDown = true;
        Debug.Log($"[EOS Shutdown] Graceful shutdown started ({reason}).");

        string remotePeerId = EosOnlineMatchState.RemoteProductUserId;

        if (EosP2PTestService.Instance != null)
        {
            EosP2PTestService.Instance.ShutdownForQuit(remotePeerId);
        }

        EosOnlineMatchState.Clear();

        BattleGameMain battleMain = Object.FindObjectOfType<BattleGameMain>();
        if (battleMain != null)
        {
            battleMain.ShutdownOnlineNetworkingForQuit();
        }

        TryLeaveLobbyBestEffort();
    }

    private static void TryLeaveLobbyBestEffort()
    {
        if (EOSManager.Instance == null)
        {
            return;
        }

        EOSLobbyManager lobbyManager;
        try
        {
            lobbyManager = EOSManager.Instance.GetOrCreateManager<EOSLobbyManager>();
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[EOS Shutdown] LeaveLobby skipped: failed to get lobby manager. {ex.GetType().Name}: {ex.Message}");
            return;
        }

        Lobby lobby = lobbyManager?.GetCurrentLobby();
        if (lobby == null || !lobby.IsValid())
        {
            return;
        }

        ProductUserId localUserId;
        try
        {
            localUserId = EOSManager.Instance.GetProductUserId();
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[EOS Shutdown] LeaveLobby skipped: failed to read local EOS user. {ex.GetType().Name}: {ex.Message}");
            return;
        }

        if (!localUserId.IsValid())
        {
            Debug.Log("[EOS Shutdown] LeaveLobby skipped: local EOS user is invalid.");
            return;
        }

        try
        {
            lobbyManager.LeaveLobby(_ => { });
            Debug.Log("[EOS Shutdown] LeaveLobby requested.");
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[EOS Shutdown] LeaveLobby skipped after exception: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
