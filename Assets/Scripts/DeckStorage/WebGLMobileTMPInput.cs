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
        AttachToAllInputFields();
#endif
    }

    public static void AttachToAllInputFields()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        TMP_InputField[] fields = UnityEngine.Object.FindObjectsOfType<TMP_InputField>(true);
        for (int i = 0; i < fields.Length; i++)
        {
            Attach(fields[i]);
        }
#endif
    }

    public static void Attach(TMP_InputField field)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (field == null || field.GetComponent<WebGLMobileTMPInputEnhancer>() != null)
        {
            return;
        }

        field.gameObject.AddComponent<WebGLMobileTMPInputEnhancer>();
#endif
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
public class WebGLMobileTMPInputEnhancer : MonoBehaviour, IPointerDownHandler, IPointerClickHandler
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void WebGLMobileTMPInput_Show(
        float x,
        float y,
        float width,
        float height,
        string text,
        bool isPassword,
        float fontSize,
        float unityScreenHeight);

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
        inputField.onSelect.AddListener(OnTmpSelected);
#endif
    }

    private void OnDestroy()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (inputField != null)
        {
            inputField.onSelect.RemoveListener(OnTmpSelected);
        }
#endif
    }

    private void OnDisable()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        HideHtmlInput();
#endif
    }

    public void OnPointerDown(PointerEventData eventData)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        TryShowHtmlInput();
#endif
    }

    public void OnPointerClick(PointerEventData eventData)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        TryShowHtmlInput();
#endif
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    private void OnTmpSelected(string _)
    {
        TryShowHtmlInput();
    }

    private void TryShowHtmlInput()
    {
        if (inputField == null || !inputField.isActiveAndEnabled || !inputField.interactable)
        {
            return;
        }

        ShowHtmlInput();
    }

    private void ShowHtmlInput()
    {
        WebGLMobileInputReceiver.EnsureReceiverExists();
        WebGLMobileInputReceiver.RegisterCallbacks(OnHtmlInputChanged, OnHtmlInputBlur);

        RectTransform rect = inputField.GetComponent<RectTransform>();
        Canvas canvas = inputField.GetComponentInParent<Canvas>();
        Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera
            : null;

        Vector3[] corners = new Vector3[4];
        rect.GetWorldCorners(corners);

        Vector2 bottomLeft = RectTransformUtility.WorldToScreenPoint(eventCamera, corners[0]);
        Vector2 topRight = RectTransformUtility.WorldToScreenPoint(eventCamera, corners[2]);

        float width = Mathf.Max(topRight.x - bottomLeft.x, 120f);
        float height = Mathf.Max(topRight.y - bottomLeft.y, 44f);
        float fontSize = inputField.textComponent != null ? inputField.textComponent.fontSize : 16f;

        WebGLMobileTMPInput_Show(
            bottomLeft.x,
            bottomLeft.y,
            width,
            height,
            inputField.text ?? string.Empty,
            isPasswordField,
            fontSize,
            Screen.height);

        inputField.ActivateInputField();
    }

    private void HideHtmlInput()
    {
        WebGLMobileTMPInput_Hide();
        WebGLMobileInputReceiver.ClearCallbacks();
    }

    private void OnHtmlInputChanged(string value)
    {
        if (inputField == null)
        {
            return;
        }

        inputField.text = value;
        inputField.caretPosition = value != null ? value.Length : 0;
        inputField.ForceLabelUpdate();
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
