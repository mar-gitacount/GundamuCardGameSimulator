using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>ログインページ。未ログイン=ローカル保存、ログイン後=Cloud Save。</summary>
public class DeckAuthUI : MonoBehaviour
{
    [SerializeField] private bool buildUiAtRuntime = true;
    [SerializeField] private TMP_InputField usernameField;
    [SerializeField] private TMP_InputField passwordField;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Button signInButton;
    [SerializeField] private Button signUpButton;
    [SerializeField] private Button signOutButton;
    [SerializeField] private Button guestButton;
    [SerializeField] private Button closeButton;
    [SerializeField] private Button openLoginButton;
    [SerializeField] private TMP_Text accountBarLabel;
    [SerializeField] private GameObject loginOverlay;
    [SerializeField] private GameObject accountBar;

    private bool guestModeChosen;

    private void Awake()
    {
        if (buildUiAtRuntime && usernameField == null)
        {
            BuildUi();
        }

        EnsurePlayerAuthService();
        WireButtons();
        PlayerAuthService.Instance.AuthStateChanged += RefreshUi;
    }

    private void OnDestroy()
    {
        if (PlayerAuthService.Instance != null)
        {
            PlayerAuthService.Instance.AuthStateChanged -= RefreshUi;
        }
    }

    private async void Start()
    {
        RepositionAccountBar();

        await PlayerAuthService.Instance.InitializeAsync();
        RefreshUi();

        if (PlayerAuthService.Instance.UseCloudStorage)
        {
            guestModeChosen = true;
            SetLoginOverlayVisible(false);
        }
        else
        {
            SetLoginOverlayVisible(true);
        }

        if (DeckSettinObject.Instance != null)
        {
            DeckSettinObject.Instance.RefreshDeckListFromStorage();
        }
    }

    private void BuildUi()
    {
        RectTransform rootRect = GetComponent<RectTransform>();
        if (rootRect == null)
        {
            rootRect = gameObject.AddComponent<RectTransform>();
        }

        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;

        LoginPageUIBuilder.BuiltLoginPage built = LoginPageUIBuilder.Build(transform);
        loginOverlay = built.LoginOverlay;
        accountBar = built.AccountBar;
        usernameField = built.UsernameField;
        passwordField = built.PasswordField;
        statusText = built.StatusText;
        signInButton = built.SignInButton;
        signUpButton = built.SignUpButton;
        guestButton = built.GuestButton;
        closeButton = built.CloseButton;
        signOutButton = built.SignOutButton;
        openLoginButton = built.OpenLoginButton;
        accountBarLabel = built.AccountBarLabel;

        transform.SetAsLastSibling();
    }

    private void RepositionAccountBar()
    {
        if (accountBar == null)
        {
            return;
        }

        LoginPageUIBuilder.PositionAccountBarBelowDeckButton(accountBar.GetComponent<RectTransform>(), transform);
    }

    private void EnsurePlayerAuthService()
    {
        if (PlayerAuthService.Instance != null)
        {
            return;
        }

        GameObject go = new GameObject("PlayerAuthService");
        go.AddComponent<PlayerAuthService>();
    }

    private void WireButtons()
    {
        if (signInButton != null)
        {
            signInButton.onClick.AddListener(() => _ = SignInAsync());
        }

        if (signUpButton != null)
        {
            signUpButton.onClick.AddListener(() => _ = SignUpAsync());
        }

        if (signOutButton != null)
        {
            signOutButton.onClick.AddListener(OnSignOutClicked);
        }

        if (guestButton != null)
        {
            guestButton.onClick.AddListener(OnGuestContinueClicked);
        }

        if (closeButton != null)
        {
            closeButton.onClick.AddListener(OnCloseLoginClicked);
        }

        if (openLoginButton != null)
        {
            openLoginButton.onClick.AddListener(OnOpenLoginClicked);
        }
    }

    private async Task SignInAsync()
    {
        string username = usernameField != null ? usernameField.text : string.Empty;
        string password = passwordField != null ? passwordField.text : string.Empty;
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            SetStatus("Please enter username and password.");
            return;
        }

        try
        {
            SetStatus("Signing in...");
            await PlayerAuthService.Instance.SignInWithUsernamePasswordAsync(username.Trim(), password);
            guestModeChosen = true;
            ClearPasswordField();
            SetStatus($"Signed in as {PlayerAuthService.Instance.SignedInUsername} (cloud save)");
            SetLoginOverlayVisible(false);
            RefreshDeckListAfterCloudAuth();
        }
        catch (System.Exception e)
        {
            SetStatus("Sign-in failed: " + FormatAuthError(e));
            Debug.LogException(e);
        }
    }

    private async Task SignUpAsync()
    {
        string username = usernameField != null ? usernameField.text : string.Empty;
        string password = passwordField != null ? passwordField.text : string.Empty;
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            SetStatus("Please enter username and password.");
            return;
        }

        if (!TryValidateSignUpPassword(password, out string passwordError))
        {
            SetStatus(passwordError);
            return;
        }

        try
        {
            SetStatus("Creating account...");
            await PlayerAuthService.Instance.SignUpWithUsernamePasswordAsync(username.Trim(), password);
            guestModeChosen = true;
            ClearPasswordField();
            SetStatus($"Account created. Signed in as {PlayerAuthService.Instance.SignedInUsername} (cloud save)");
            SetLoginOverlayVisible(false);
            RefreshDeckListAfterCloudAuth();
        }
        catch (System.Exception e)
        {
            SetStatus("Sign-up failed: " + FormatAuthError(e));
            Debug.LogException(e);
        }
    }

    private void OnSignOutClicked()
    {
        PlayerAuthService.Instance.SignOut();
        guestModeChosen = false;
        SetStatus("Guest mode (local save)");
        if (DeckSettinObject.Instance != null)
        {
            DeckSettinObject.Instance.OnGuestModeActivated();
        }

        RefreshUi();
        SetLoginOverlayVisible(true);
    }

    private void OnGuestContinueClicked()
    {
        guestModeChosen = true;
        SetStatus("Guest mode (local save)");
        SetLoginOverlayVisible(false);
        RefreshUi();
        RefreshDeckList();
    }

    private void OnCloseLoginClicked()
    {
        if (!guestModeChosen && !PlayerAuthService.Instance.UseCloudStorage)
        {
            guestModeChosen = true;
        }

        SetLoginOverlayVisible(false);
        RefreshUi();
    }

    private void OnOpenLoginClicked()
    {
        SetLoginOverlayVisible(true);
        SetStatus(PlayerAuthService.Instance.UseCloudStorage
            ? $"Signed in as {PlayerAuthService.Instance.SignedInUsername}"
            : "Sign in or continue as guest.");
    }

    private void RefreshUi()
    {
        bool signedIn = PlayerAuthService.Instance != null && PlayerAuthService.Instance.UseCloudStorage;

        if (accountBarLabel != null)
        {
            accountBarLabel.text = signedIn
                ? $"Signed in: {PlayerAuthService.Instance.SignedInUsername}"
                : "Guest (local save)";
        }

        if (openLoginButton != null)
        {
            openLoginButton.gameObject.SetActive(!signedIn);
        }

        if (signOutButton != null)
        {
            signOutButton.gameObject.SetActive(signedIn);
        }

        if (accountBar != null)
        {
            accountBar.SetActive(guestModeChosen || signedIn);
        }

        if (!signedIn && !guestModeChosen)
        {
            SetStatus("Sign in or continue as guest.");
        }
        else if (signedIn)
        {
            SetStatus($"Signed in as {PlayerAuthService.Instance.SignedInUsername} (cloud save)");
        }
        else
        {
            SetStatus("Guest mode (local save)");
        }
    }

    private void SetLoginOverlayVisible(bool visible)
    {
        if (loginOverlay != null)
        {
            loginOverlay.SetActive(visible);
        }
    }

    private void RefreshDeckList()
    {
        if (DeckSettinObject.Instance != null)
        {
            DeckSettinObject.Instance.RefreshDeckListFromStorage();
        }
    }

    private void RefreshDeckListAfterCloudAuth()
    {
        if (DeckSettinObject.Instance != null)
        {
            DeckSettinObject.Instance.OnCloudStorageActivated();
        }
    }

    private void ClearPasswordField()
    {
        if (passwordField != null)
        {
            passwordField.text = string.Empty;
        }
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }

        Debug.Log("[AuthUI] " + message);
    }

    private static string FormatAuthError(System.Exception e)
    {
        string message = e.GetBaseException().Message;
        if (message.Contains("usernamepassword") && message.Contains("PERMISSION_DENIED"))
        {
            return "Username & Password is disabled for this project. Enable it in Unity Dashboard → Player Authentication → Identity Providers.";
        }

        if (message.Contains("INVALID_PASSWORD") || message.Contains("Password does not match requirements"))
        {
            return "Password must be 8-30 characters and include uppercase, lowercase, digit, and symbol.";
        }

        return message;
    }

    private static bool TryValidateSignUpPassword(string password, out string errorMessage)
    {
        if (string.IsNullOrEmpty(password))
        {
            errorMessage = "Please enter username and password.";
            return false;
        }

        if (password.Length < 8 || password.Length > 30)
        {
            errorMessage = "Password must be 8-30 characters.";
            return false;
        }

        bool hasUpper = false;
        bool hasLower = false;
        bool hasDigit = false;
        bool hasSymbol = false;

        for (int i = 0; i < password.Length; i++)
        {
            char c = password[i];
            if (char.IsUpper(c))
            {
                hasUpper = true;
            }
            else if (char.IsLower(c))
            {
                hasLower = true;
            }
            else if (char.IsDigit(c))
            {
                hasDigit = true;
            }
            else
            {
                hasSymbol = true;
            }
        }

        if (!hasUpper || !hasLower || !hasDigit || !hasSymbol)
        {
            errorMessage = "Password must include uppercase, lowercase, digit, and symbol.";
            return false;
        }

        errorMessage = null;
        return true;
    }
}
