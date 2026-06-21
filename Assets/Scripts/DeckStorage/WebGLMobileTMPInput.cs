using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

/// <summary>
/// WebGL モバイル向け: HTML input を重ねてソフトキーボードを表示する。
/// </summary>
public class WebGLMobileInputReceiver : MonoBehaviour
{
    public static WebGLMobileInputReceiver Instance { get; private set; }

    private static Action<string> s_onInput;
    private static Action s_onBlur;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        UnityEngine.WebGLInput.captureAllKeyboardInput = false;
        EnsureReceiverExists();
#endif
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnhanceSceneInputFields()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (!ShouldUseMobileHtmlInput())
        {
            return;
        }

        TMP_InputField[] fields = UnityEngine.Object.FindObjectsOfType<TMP_InputField>(true);
        for (int i = 0; i < fields.Length; i++)
        {
            TMP_InputField field = fields[i];
            if (field == null || field.GetComponent<WebGLMobileTMPInputEnhancer>() != null)
            {
                continue;
            }

            field.gameObject.AddComponent<WebGLMobileTMPInputEnhancer>();
        }
#endif
    }

    public static bool ShouldUseMobileHtmlInput()
    {
        return Application.isMobilePlatform || Input.touchSupported;
    }

    public static void EnsureReceiverExists()
    {
        if (Instance != null)
        {
            return;
        }

        GameObject go = new GameObject("WebGLMobileInputReceiver");
        go.AddComponent<WebGLMobileInputReceiver>();
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
    }

    public static void RegisterCallbacks(Action<string> onInput, Action onBlur)
    {
        EnsureReceiverExists();
        s_onInput = onInput;
        s_onBlur = onBlur;
    }

    public static void ClearCallbacks()
    {
        s_onInput = null;
        s_onBlur = null;
    }

    public void OnHtmlInput(string value)
    {
        s_onInput?.Invoke(value);
    }

    public void OnHtmlBlur(string _)
    {
        s_onBlur?.Invoke();
        ClearCallbacks();
    }
}

[RequireComponent(typeof(TMP_InputField))]
public class WebGLMobileTMPInputEnhancer : MonoBehaviour, IPointerDownHandler
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void WebGLMobileTMPInput_Show(
        float x, float y, float width, float height, string text, bool isPassword, float fontSize);

    [DllImport("__Internal")]
    private static extern void WebGLMobileTMPInput_Hide();
#endif

    private TMP_InputField inputField;
    private bool isPasswordField;

    private void Awake()
    {
        inputField = GetComponent<TMP_InputField>();
        isPasswordField = inputField.contentType == TMP_InputField.ContentType.Password
            || inputField.contentType == TMP_InputField.ContentType.Pin;

#if UNITY_WEBGL && !UNITY_EDITOR
        if (!WebGLMobileInputReceiver.ShouldUseMobileHtmlInput())
        {
            enabled = false;
        }
#endif
    }

    private void OnDisable()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (enabled)
        {
            HideHtmlInput();
        }
#endif
    }

    public void OnPointerDown(PointerEventData eventData)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (!enabled || inputField == null || !inputField.interactable)
        {
            return;
        }

        ShowHtmlInput();
#endif
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    private void ShowHtmlInput()
    {
        WebGLMobileInputReceiver.EnsureReceiverExists();
        WebGLMobileInputReceiver.RegisterCallbacks(OnHtmlInputChanged, OnHtmlInputBlur);

        RectTransform rect = inputField.textComponent != null
            ? inputField.textComponent.rectTransform
            : inputField.GetComponent<RectTransform>();

        Vector3[] corners = new Vector3[4];
        rect.GetWorldCorners(corners);

        Vector2 bottomLeft = RectTransformUtility.WorldToScreenPoint(null, corners[0]);
        Vector2 topRight = RectTransformUtility.WorldToScreenPoint(null, corners[2]);

        float width = topRight.x - bottomLeft.x;
        float height = topRight.y - bottomLeft.y;
        float x = bottomLeft.x;
        float y = Screen.height - topRight.y;
        float fontSize = inputField.textComponent != null ? inputField.textComponent.fontSize : 16f;

        WebGLMobileTMPInput_Show(x, y, width, height, inputField.text ?? string.Empty, isPasswordField, fontSize);
        inputField.ActivateInputField();
    }

    private void HideHtmlInput()
    {
        WebGLMobileTMPInput_Hide();
        WebGLMobileInputReceiver.ClearCallbacks();
    }

    private void OnHtmlInputChanged(string value)
    {
        if (inputField != null)
        {
            inputField.text = value;
            inputField.caretPosition = value != null ? value.Length : 0;
        }
    }

    private void OnHtmlInputBlur()
    {
        if (inputField != null)
        {
            inputField.DeactivateInputField();
        }
    }
#endif
}
