using System;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Authentication;

/// <summary>
/// UGS 認証。ゲスト（未ログイン）はローカル保存、ユーザー名ログイン時のみ Cloud Save を使う。
/// 同一ブラウザでは保存済みセッショントークンで自動ログインする。
/// </summary>
public class PlayerAuthService : MonoBehaviour
{
    public static PlayerAuthService Instance { get; private set; }

    public event Action AuthStateChanged;

    public bool IsInitialized { get; private set; }
    public bool IsSignedInWithAccount { get; private set; }
    public bool UseCloudStorage => IsSignedInWithAccount;
    public string SignedInUsername { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public async Task InitializeAsync()
    {
        if (IsInitialized)
        {
            return;
        }

        await UnityServices.InitializeAsync();
        IsInitialized = true;

        if (await TryRestoreCachedSessionAsync())
        {
            Debug.Log($"[Auth] Session restored automatically: {SignedInUsername}");
        }
        else
        {
            RefreshSignedInState();
            Debug.Log("[Auth] Unity Services initialized (guest/local mode until sign-in).");
        }
    }

    public async Task SignUpWithUsernamePasswordAsync(string username, string password)
    {
        await EnsureInitializedAsync();
        await AuthenticationService.Instance.SignUpWithUsernamePasswordAsync(username, password);
        await EnsurePlayerInfoAsync();
        RefreshSignedInState();
        AuthStateChanged?.Invoke();
        Debug.Log($"[Auth] Sign up succeeded: {SignedInUsername}");
    }

    public async Task SignInWithUsernamePasswordAsync(string username, string password)
    {
        await EnsureInitializedAsync();
        await AuthenticationService.Instance.SignInWithUsernamePasswordAsync(username, password);
        await EnsurePlayerInfoAsync();
        RefreshSignedInState();
        AuthStateChanged?.Invoke();
        Debug.Log($"[Auth] Sign in succeeded: {SignedInUsername}");
    }

    public void SignOut()
    {
        if (AuthenticationService.Instance.IsSignedIn)
        {
            AuthenticationService.Instance.SignOut(clearCredentials: true);
        }
        else if (AuthenticationService.Instance.SessionTokenExists)
        {
            AuthenticationService.Instance.ClearSessionToken();
        }

        RefreshSignedInState();
        AuthStateChanged?.Invoke();
        Debug.Log("[Auth] Signed out → guest/local storage.");
    }

    private async Task<bool> TryRestoreCachedSessionAsync()
    {
        if (!AuthenticationService.Instance.SessionTokenExists)
        {
            return false;
        }

        try
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
            await EnsurePlayerInfoAsync();
            RefreshSignedInState();

            if (!IsSignedInWithAccount)
            {
                ClearStoredSession();
                return false;
            }

            AuthStateChanged?.Invoke();
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Auth] Session restore failed: {e.Message}");
            ClearStoredSession();
            return false;
        }
    }

    private async Task EnsureInitializedAsync()
    {
        if (!IsInitialized)
        {
            await InitializeAsync();
        }
    }

    private static async Task EnsurePlayerInfoAsync()
    {
        if (!AuthenticationService.Instance.IsSignedIn)
        {
            return;
        }

        if (AuthenticationService.Instance.PlayerInfo != null
            && !string.IsNullOrEmpty(AuthenticationService.Instance.PlayerInfo.Username))
        {
            return;
        }

        try
        {
            await AuthenticationService.Instance.GetPlayerInfoAsync();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Auth] GetPlayerInfo failed: {e.Message}");
        }
    }

    private static void ClearStoredSession()
    {
        try
        {
            if (AuthenticationService.Instance.IsSignedIn)
            {
                AuthenticationService.Instance.SignOut(clearCredentials: true);
            }
            else if (AuthenticationService.Instance.SessionTokenExists)
            {
                AuthenticationService.Instance.ClearSessionToken();
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Auth] Failed to clear stored session: {e.Message}");
        }
    }

    private void RefreshSignedInState()
    {
        IsSignedInWithAccount = false;
        SignedInUsername = null;

        if (!AuthenticationService.Instance.IsSignedIn)
        {
            return;
        }

        PlayerInfo info = AuthenticationService.Instance.PlayerInfo;
        if (info != null && !string.IsNullOrEmpty(info.Username))
        {
            IsSignedInWithAccount = true;
            SignedInUsername = info.Username;
        }
    }
}
