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
        GameLocale.LanguageChanged += OnLanguageChanged;
    }

    private void OnDestroy()
    {
        if (PlayerAuthService.Instance != null)
        {
            PlayerAuthService.Instance.AuthStateChanged -= RefreshUi;
        }

        GameLocale.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged(GameLanguage _)
    {
        RefreshUi();
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
            RefreshDeckListAfterCloudAuth();
        }
        else
        {
            SetLoginOverlayVisible(true);
            RefreshDeckList();
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

        AttachMobileInputEnhancers(built);

        transform.SetAsLastSibling();
    }

    public static void AttachMobileInputEnhancers(LoginPageUIBuilder.BuiltLoginPage page)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        WebGLMobileInputReceiver.Attach(page.UsernameField);
        WebGLMobileInputReceiver.Attach(page.PasswordField);
#endif
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
            SetStatus(GameLocale.TKey("auth.enter_user_pass"));
            return;
        }

        try
        {
            SetStatus(GameLocale.TKey("auth.signing_in"));
            await PlayerAuthService.Instance.SignInWithUsernamePasswordAsync(username.Trim(), password);
            guestModeChosen = true;
            ClearPasswordField();
            SetStatus(GameLocale.T(
                $"サインインしました: {PlayerAuthService.Instance.SignedInUsername}（クラウド保存）",
                $"Signed in as {PlayerAuthService.Instance.SignedInUsername} (cloud save)"));
            SetLoginOverlayVisible(false);
            RefreshDeckListAfterCloudAuth();
        }
        catch (System.Exception e)
        {
            SetStatus(GameLocale.T("サインインに失敗: ", "Sign-in failed: ") + FormatAuthError(e));
            Debug.LogException(e);
        }
    }

    private async Task SignUpAsync()
    {
        string username = usernameField != null ? usernameField.text : string.Empty;
        string password = passwordField != null ? passwordField.text : string.Empty;
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            SetStatus(GameLocale.TKey("auth.enter_user_pass"));
            return;
        }

        if (!TryValidateSignUpPassword(password, out string passwordError))
        {
            SetStatus(passwordError);
            return;
        }

        try
        {
            SetStatus(GameLocale.TKey("auth.creating"));
            await PlayerAuthService.Instance.SignUpWithUsernamePasswordAsync(username.Trim(), password);
            guestModeChosen = true;
            ClearPasswordField();
            SetStatus(GameLocale.T(
                $"アカウント作成完了。サインイン中: {PlayerAuthService.Instance.SignedInUsername}（クラウド保存）",
                $"Account created. Signed in as {PlayerAuthService.Instance.SignedInUsername} (cloud save)"));
            SetLoginOverlayVisible(false);
            RefreshDeckListAfterCloudAuth();
        }
        catch (System.Exception e)
        {
            SetStatus(GameLocale.T("新規登録に失敗: ", "Sign-up failed: ") + FormatAuthError(e));
            Debug.LogException(e);
        }
    }

    private void OnSignOutClicked()
    {
        PlayerAuthService.Instance.SignOut();
        guestModeChosen = false;
        SetStatus(GameLocale.T("ゲストモード（ローカル保存）", "Guest mode (local save)"));
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
        SetStatus(GameLocale.T("ゲストモード（ローカル保存）", "Guest mode (local save)"));
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
            ? GameLocale.T(
                $"サインイン中: {PlayerAuthService.Instance.SignedInUsername}",
                $"Signed in as {PlayerAuthService.Instance.SignedInUsername}")
            : GameLocale.T("サインインするか、ゲストで続けてください。", "Sign in or continue as guest."));
    }

    private void RefreshUi()
    {
        bool signedIn = PlayerAuthService.Instance != null && PlayerAuthService.Instance.UseCloudStorage;

        if (accountBarLabel != null)
        {
            accountBarLabel.SetLocalizedText(
                signedIn
                    ? $"サインイン中: {PlayerAuthService.Instance.SignedInUsername}"
                    : "ゲスト（ローカル保存）",
                signedIn
                    ? $"Signed in: {PlayerAuthService.Instance.SignedInUsername}"
                    : "Guest (local save)");
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
            SetStatus(GameLocale.T("サインインするか、ゲストで続けてください。", "Sign in or continue as guest."));
        }
        else if (signedIn)
        {
            SetStatus(GameLocale.T(
                $"サインインしました: {PlayerAuthService.Instance.SignedInUsername}（クラウド保存）",
                $"Signed in as {PlayerAuthService.Instance.SignedInUsername} (cloud save)"));
        }
        else
        {
            SetStatus(GameLocale.T("ゲストモード（ローカル保存）", "Guest mode (local save)"));
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
            statusText.SetLocalizedText(message);
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
