using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 言語切替ボタン。未配置ならホームキャンバス右上に自動生成する。
/// </summary>
[DisallowMultipleComponent]
public sealed class LanguageToggleUI : MonoBehaviour
{
    [SerializeField] private Button toggleButton;
    [SerializeField] private TMP_Text buttonLabel;
    [SerializeField] private bool autoCreateIfMissing = true;

    private static LanguageToggleUI _instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureInHome()
    {
        if (Object.FindFirstObjectByType<LanguageToggleUI>() != null)
        {
            return;
        }

        // Home / ルート Canvas に載せる
        Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        Canvas root = null;
        for (int i = 0; i < canvases.Length; i++)
        {
            if (canvases[i] != null && canvases[i].isRootCanvas)
            {
                root = canvases[i];
                break;
            }
        }

        if (root == null)
        {
            return;
        }

        GameObject go = new GameObject("LanguageToggleUI", typeof(RectTransform), typeof(LanguageToggleUI));
        go.transform.SetParent(root.transform, false);
        LanguageToggleUI ui = go.GetComponent<LanguageToggleUI>();
        ui.autoCreateIfMissing = true;
        ui.BuildButton(root.transform);
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            return;
        }

        _instance = this;
    }

    private void OnEnable()
    {
        GameLocale.LanguageChanged += OnLanguageChanged;
        if (toggleButton == null && autoCreateIfMissing)
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                BuildButton(canvas.rootCanvas.transform);
            }
        }

        WireButton();
        RefreshLabel();
        GameLocale.ApplyFontsToAllTmp();
        LocalizedTmpText.RefreshAll();
    }

    private void OnDisable()
    {
        GameLocale.LanguageChanged -= OnLanguageChanged;
        if (toggleButton != null)
        {
            toggleButton.onClick.RemoveListener(OnClicked);
        }
    }

    private void OnLanguageChanged(GameLanguage _)
    {
        RefreshLabel();
    }

    private void WireButton()
    {
        if (toggleButton == null)
        {
            return;
        }

        toggleButton.onClick.RemoveListener(OnClicked);
        toggleButton.onClick.AddListener(OnClicked);
    }

    private void OnClicked()
    {
        GameLocale.ToggleLanguage();
    }

    private void RefreshLabel()
    {
        if (buttonLabel == null && toggleButton != null)
        {
            buttonLabel = toggleButton.GetComponentInChildren<TMP_Text>(true);
        }

        if (buttonLabel == null)
        {
            return;
        }

        // いま日本語なら「English」と出し、押すと英語へ（逆も同様）
        buttonLabel.SetLocalizedText(
            GameLocale.TKey("lang.toggle"),
            GameLocale.TKey("lang.toggle"));
        // lang.toggle は (English, 日本語) なので現在言語で正しい表示になる
        buttonLabel.text = GameLocale.TKey("lang.toggle");
        GameLocale.ApplyFont(buttonLabel);
    }

    private void BuildButton(Transform canvasRoot)
    {
        if (toggleButton != null)
        {
            return;
        }

        GameObject btnGo = new GameObject(
            "LanguageToggleButton",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Button));
        btnGo.transform.SetParent(canvasRoot, false);
        RectTransform rt = btnGo.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-16f, -16f);
        rt.sizeDelta = new Vector2(140f, 44f);

        Image img = btnGo.GetComponent<Image>();
        img.color = new Color(0.12f, 0.22f, 0.38f, 0.92f);

        toggleButton = btnGo.GetComponent<Button>();

        GameObject labelGo = new GameObject(
            "Label",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(btnGo.transform, false);
        RectTransform labelRt = labelGo.GetComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;

        buttonLabel = labelGo.GetComponent<TextMeshProUGUI>();
        buttonLabel.fontSize = 20;
        buttonLabel.alignment = TextAlignmentOptions.Center;
        buttonLabel.color = Color.white;

        transform.SetAsLastSibling();
        btnGo.transform.SetAsLastSibling();
        WireButton();
        RefreshLabel();
    }
}
