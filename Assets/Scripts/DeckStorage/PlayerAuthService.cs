using System;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Authentication;

/// <summary>
/// UGS 認証。ゲスト（未ログイン）はローカル保存、ユーザー名ログイン時のみ Cloud Save を使う。
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
        RefreshSignedInState();
        Debug.Log("[Auth] Unity Services initialized (guest/local mode until sign-in).");
    }

    public async Task SignUpWithUsernamePasswordAsync(string username, string password)
    {
        await EnsureInitializedAsync();
        await AuthenticationService.Instance.SignUpWithUsernamePasswordAsync(username, password);
        RefreshSignedInState();
        AuthStateChanged?.Invoke();
        Debug.Log($"[Auth] Sign up succeeded: {SignedInUsername}");
    }

    public async Task SignInWithUsernamePasswordAsync(string username, string password)
    {
        await EnsureInitializedAsync();
        await AuthenticationService.Instance.SignInWithUsernamePasswordAsync(username, password);
        RefreshSignedInState();
        AuthStateChanged?.Invoke();
        Debug.Log($"[Auth] Sign in succeeded: {SignedInUsername}");
    }

    public void SignOut()
    {
        if (AuthenticationService.Instance.IsSignedIn)
        {
            AuthenticationService.Instance.SignOut();
        }

        RefreshSignedInState();
        AuthStateChanged?.Invoke();
        Debug.Log("[Auth] Signed out → guest/local storage.");
    }

    private async Task EnsureInitializedAsync()
    {
        if (!IsInitialized)
        {
            await InitializeAsync();
        }
    }

    private void RefreshSignedInState()
    {
        IsSignedInWithAccount = AuthenticationService.Instance.IsSignedIn;
        SignedInUsername = null;

        if (!IsSignedInWithAccount)
        {
            return;
        }

        PlayerInfo info = AuthenticationService.Instance.PlayerInfo;
        if (info != null && !string.IsNullOrEmpty(info.Username))
        {
            SignedInUsername = info.Username;
        }
        else
        {
            SignedInUsername = "Player";
        }
    }
}
