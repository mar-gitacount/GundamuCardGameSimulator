using UnityEngine;

/// <summary>
/// カメラの Viewport を基準アスペクト比に合わせて中央寄せする（レターボックス／ピラーボックス）。
/// </summary>
[ExecuteAlways]
public class AspectKeeper : MonoBehaviour
{
    [SerializeField] private Camera targetCamera;

    [Tooltip("基準解像度（例: 480×800）。")]
    [SerializeField] private Vector2 aspectVec = new Vector2(480f, 800f);

    private void Awake()
    {
        if (targetCamera == null)
        {
            targetCamera = GetComponent<Camera>();
        }

        Apply();
    }

    private void Update()
    {
        Apply();
    }

    private void OnValidate()
    {
        if (targetCamera == null)
        {
            targetCamera = GetComponent<Camera>();
        }

        Apply();
    }

    private void Apply()
    {
        if (targetCamera == null || aspectVec.y <= 0.0001f || aspectVec.x <= 0.0001f)
        {
            return;
        }

        float screenAspect = Screen.width / (float)Screen.height;
        float targetAspect = aspectVec.x / aspectVec.y;
        float magRate = screenAspect / targetAspect;

        Rect viewportRect = new Rect(0f, 0f, 1f, 1f);
        if (magRate < 1f)
        {
            // 画面のほうが縦長 → 左右に余白
            viewportRect.width = magRate;
            viewportRect.x = 0.5f - viewportRect.width * 0.5f;
        }
        else if (magRate > 1f)
        {
            // 画面のほうが横長 → 上下に余白
            viewportRect.height = 1f / magRate;
            viewportRect.y = 0.5f - viewportRect.height * 0.5f;
        }

        targetCamera.rect = viewportRect;
    }
}
