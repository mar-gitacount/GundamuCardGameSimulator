using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>ランタイムでログインページ UI を組み立てるヘルパー。</summary>
public static class LoginPageUIBuilder
{
    private static TMP_FontAsset _font;

    public struct BuiltLoginPage
    {
        public GameObject Root;
        public GameObject LoginOverlay;
        public GameObject AccountBar;
        public TMP_InputField UsernameField;
        public TMP_InputField PasswordField;
        public TMP_Text StatusText;
        public Button SignInButton;
        public Button SignUpButton;
        public Button GuestButton;
        public Button CloseButton;
        public Button SignOutButton;
        public Button OpenLoginButton;
        public TMP_Text AccountBarLabel;
    }

    public static BuiltLoginPage Build(Transform rootTransform)
    {
        EnsureFont();

        BuiltLoginPage page = new BuiltLoginPage();
        page.Root = rootTransform.gameObject;

        page.LoginOverlay = CreateRect("LoginOverlay", rootTransform);
        StretchFull(page.LoginOverlay);
        Image dim = page.LoginOverlay.AddComponent<Image>();
        dim.color = new Color(0.05f, 0.08f, 0.14f, 0.92f);
        dim.raycastTarget = true;

        GameObject card = CreateRect("LoginCard", page.LoginOverlay.transform);
        RectTransform cardRect = card.GetComponent<RectTransform>();
        cardRect.anchorMin = new Vector2(0.5f, 0.5f);
        cardRect.anchorMax = new Vector2(0.5f, 0.5f);
        cardRect.pivot = new Vector2(0.5f, 0.5f);
        cardRect.sizeDelta = new Vector2(360f, 490f);
        cardRect.anchoredPosition = Vector2.zero;
        Image cardBg = card.AddComponent<Image>();
        cardBg.color = new Color(0.12f, 0.16f, 0.24f, 0.98f);
        cardBg.raycastTarget = true;

        page.CloseButton = CreateCloseButton(card.transform);

        VerticalLayoutGroup layout = card.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(24, 24, 28, 28);
        layout.spacing = 12f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        CreateLabel(card.transform, "Account", 28, FontStyles.Bold, new Color(0.95f, 0.97f, 1f));
        CreateLabel(card.transform, "Sign in to save your decks to the cloud", 16, FontStyles.Normal, new Color(0.75f, 0.82f, 0.92f));

        page.UsernameField = CreateInputField(card.transform, "Username", false);
        page.PasswordField = CreateInputField(card.transform, "Password", true);
        CreateLabel(
            card.transform,
            "Password: 8-30 chars, uppercase, lowercase, digit, and symbol",
            13,
            FontStyles.Normal,
            new Color(0.65f, 0.72f, 0.82f));
        page.StatusText = CreateLabel(card.transform, "Guest mode (local save)", 15, FontStyles.Normal, new Color(0.85f, 0.9f, 0.95f));
        page.StatusText.alignment = TextAlignmentOptions.Center;

        page.SignInButton = CreateButton(card.transform, "Sign In", new Color(0.18f, 0.45f, 0.82f));
        page.SignUpButton = CreateButton(card.transform, "Sign Up", new Color(0.2f, 0.55f, 0.45f));
        page.GuestButton = CreateButton(card.transform, "Continue as Guest", new Color(0.35f, 0.38f, 0.45f));

        page.AccountBar = CreateRect("AccountBar", rootTransform);

        HorizontalLayoutGroup barLayout = page.AccountBar.AddComponent<HorizontalLayoutGroup>();
        barLayout.spacing = 8f;
        barLayout.childAlignment = TextAnchor.MiddleRight;
        barLayout.childControlWidth = true;
        barLayout.childControlHeight = true;
        barLayout.childForceExpandWidth = false;
        barLayout.childForceExpandHeight = true;

        ContentSizeFitter barFitter = page.AccountBar.AddComponent<ContentSizeFitter>();
        barFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        barFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        page.AccountBarLabel = CreateLabel(page.AccountBar.transform, "Guest", 14, FontStyles.Normal, Color.white);
        LayoutElement labelLayout = page.AccountBarLabel.gameObject.AddComponent<LayoutElement>();
        labelLayout.preferredWidth = 200f;

        page.OpenLoginButton = CreateButton(page.AccountBar.transform, "Sign In", new Color(0.18f, 0.45f, 0.82f), 96f, 36f);
        page.SignOutButton = CreateButton(page.AccountBar.transform, "Sign Out", new Color(0.55f, 0.22f, 0.22f), 104f, 36f);

        PositionAccountBarBelowDeckButton(page.AccountBar.GetComponent<RectTransform>(), rootTransform);

        rootTransform.SetAsLastSibling();
        return page;
    }

    public static void PositionAccountBarBelowDeckButton(RectTransform accountBar, Transform loginPageRoot)
    {
        if (accountBar == null)
        {
            return;
        }

        RectTransform deckButton = FindDeckButtonRect(loginPageRoot);
        const float horizontalMargin = 12f;
        const float verticalGap = 8f;

        accountBar.anchorMin = new Vector2(1f, 1f);
        accountBar.anchorMax = new Vector2(1f, 1f);
        accountBar.pivot = new Vector2(1f, 1f);

        if (deckButton == null)
        {
            accountBar.anchoredPosition = new Vector2(-horizontalMargin, -42f);
            return;
        }

        Canvas.ForceUpdateCanvases();

        Canvas canvas = accountBar.GetComponentInParent<Canvas>();
        Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera
            : null;

        Vector3[] corners = new Vector3[4];
        deckButton.GetWorldCorners(corners);
        Vector3 deckBottomRight = corners[3];

        RectTransform parent = accountBar.parent as RectTransform;
        if (parent == null)
        {
            accountBar.anchoredPosition = new Vector2(-horizontalMargin, -42f);
            return;
        }

        Vector2 localBottomRight;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parent,
                RectTransformUtility.WorldToScreenPoint(eventCamera, deckBottomRight),
                eventCamera,
                out localBottomRight))
        {
            accountBar.anchoredPosition = new Vector2(-horizontalMargin, -42f);
            return;
        }

        float parentTopY = parent.rect.height * 0.5f;
        float distanceFromTop = parentTopY - localBottomRight.y;
        accountBar.anchoredPosition = new Vector2(-horizontalMargin, -distanceFromTop - verticalGap);
    }

    private static RectTransform FindDeckButtonRect(Transform loginPageRoot)
    {
        Canvas canvas = loginPageRoot.GetComponentInParent<Canvas>();
        if (canvas == null)
        {
            return null;
        }

        Transform[] transforms = canvas.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i].name == "DeckButton")
            {
                return transforms[i] as RectTransform;
            }
        }

        return null;
    }

    private static void EnsureFont()
    {
        if (_font != null)
        {
            return;
        }

        _font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF - Fallback");
        if (_font == null)
        {
            _font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        }
    }

    private static GameObject CreateRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private static void StretchFull(GameObject go)
    {
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f);
    }

    private static TMP_Text CreateLabel(Transform parent, string text, int fontSize, FontStyles style, Color color)
    {
        GameObject go = CreateRect("Label", parent);
        LayoutElement layout = go.AddComponent<LayoutElement>();
        layout.minHeight = fontSize + 12f;

        TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();
        label.font = _font;
        label.text = text;
        label.fontSize = fontSize;
        label.fontStyle = style;
        label.color = color;
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = true;
        return label;
    }

    private static TMP_InputField CreateInputField(Transform parent, string placeholder, bool isPassword)
    {
        GameObject root = CreateRect(placeholder + "Field", parent);
        LayoutElement layout = root.AddComponent<LayoutElement>();
        layout.minHeight = 44f;
        layout.preferredHeight = 44f;

        Image bg = root.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.1f, 0.14f, 1f);

        GameObject textArea = CreateRect("Text Area", root.transform);
        RectTransform textAreaRect = textArea.GetComponent<RectTransform>();
        textAreaRect.anchorMin = Vector2.zero;
        textAreaRect.anchorMax = Vector2.one;
        textAreaRect.offsetMin = new Vector2(12f, 6f);
        textAreaRect.offsetMax = new Vector2(-12f, -6f);

        GameObject placeholderGo = CreateRect("Placeholder", textArea.transform);
        StretchFull(placeholderGo);
        TextMeshProUGUI placeholderText = placeholderGo.AddComponent<TextMeshProUGUI>();
        placeholderText.font = _font;
        placeholderText.text = placeholder;
        placeholderText.fontSize = 18;
        placeholderText.fontStyle = FontStyles.Italic;
        placeholderText.color = new Color(0.55f, 0.6f, 0.68f, 0.85f);

        GameObject textGo = CreateRect("Text", textArea.transform);
        StretchFull(textGo);
        TextMeshProUGUI inputText = textGo.AddComponent<TextMeshProUGUI>();
        inputText.font = _font;
        inputText.fontSize = 18;
        inputText.color = Color.white;

        TMP_InputField field = root.AddComponent<TMP_InputField>();
        field.textViewport = textAreaRect;
        field.textComponent = inputText;
        field.placeholder = placeholderText;
        field.targetGraphic = bg;
        field.contentType = isPassword ? TMP_InputField.ContentType.Password : TMP_InputField.ContentType.Standard;
        field.lineType = TMP_InputField.LineType.SingleLine;
        field.shouldHideMobileInput = false;
        field.interactable = true;
        field.readOnly = false;

        WebGLMobileInputReceiver.Attach(field);

        return field;
    }

    private static Button CreateButton(Transform parent, string label, Color color, float width = 0f, float height = 44f)
    {
        GameObject go = CreateRect(label + "Button", parent);
        LayoutElement layout = go.AddComponent<LayoutElement>();
        layout.minHeight = height;
        layout.preferredHeight = height;
        if (width > 0f)
        {
            layout.preferredWidth = width;
            layout.minWidth = width;
        }

        Image image = go.AddComponent<Image>();
        image.color = color;

        Button button = go.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.highlightedColor = color * 1.08f;
        colors.pressedColor = color * 0.85f;
        button.colors = colors;

        GameObject textGo = CreateRect("Text", go.transform);
        StretchFull(textGo);
        TextMeshProUGUI text = textGo.AddComponent<TextMeshProUGUI>();
        text.font = _font;
        text.text = label;
        text.fontSize = 18;
        text.fontStyle = FontStyles.Bold;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        return button;
    }

    private static Button CreateCloseButton(Transform cardTransform)
    {
        GameObject go = CreateRect("CloseButton", cardTransform);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-8f, -8f);
        rect.sizeDelta = new Vector2(36f, 36f);

        Image image = go.AddComponent<Image>();
        image.color = new Color(0.28f, 0.32f, 0.4f, 1f);

        Button button = go.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(0.4f, 0.44f, 0.52f, 1f);
        colors.pressedColor = new Color(0.2f, 0.24f, 0.3f, 1f);
        button.colors = colors;

        GameObject textGo = CreateRect("Text", go.transform);
        StretchFull(textGo);
        TextMeshProUGUI text = textGo.AddComponent<TextMeshProUGUI>();
        text.font = _font;
        text.text = "×";
        text.fontSize = 28;
        text.fontStyle = FontStyles.Bold;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        return button;
    }
}
