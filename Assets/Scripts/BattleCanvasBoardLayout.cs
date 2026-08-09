using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// バトルUIを設計アスペクト内に収める。
/// Width 固定480は縦長端末でキャンバス論理幅より広くなり横突き抜けの原因になるため、
/// 横は親いっぱい・高さは設計800・縦中央に置く。
/// </summary>
[DisallowMultipleComponent]
[ExecuteAlways]
public sealed class BattleCanvasBoardLayout : MonoBehaviour
{
    public const string ContentRootName = "BattleBoardRoot";

    [SerializeField] private float _designHeight = 800f;
    [SerializeField] private Vector2 _referenceResolution = new Vector2(480f, 800f);

    private RectTransform _canvasRect;
    private RectTransform _contentRoot;
    private Vector2 _lastParentSize;

    private void Awake()
    {
        Setup();
    }

    private void OnEnable()
    {
        Setup();
    }

    private void LateUpdate()
    {
        Apply(force: false);
    }

    private void OnRectTransformDimensionsChange()
    {
        Apply(force: true);
    }

    private void Setup()
    {
        _canvasRect = transform as RectTransform;
        ConfigureScaler();
        EnsureContentRoot();
        NormalizeBoardChildren();
        Apply(force: true);
    }

    private void ConfigureScaler()
    {
        CanvasScaler scaler = GetComponent<CanvasScaler>();
        if (scaler == null)
        {
            scaler = gameObject.AddComponent<CanvasScaler>();
        }

        // Expand は端末差で論理サイズが暴れやすい。高さ寄りで下空きを抑えつつ極端な横伸びを避ける。
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = _referenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        scaler.referencePixelsPerUnit = 100f;
    }

    private void EnsureContentRoot()
    {
        if (_canvasRect == null)
        {
            return;
        }

        _contentRoot = FindExistingBoardRoot();
        if (_contentRoot == null)
        {
            GameObject go = new GameObject(ContentRootName, typeof(RectTransform));
            _contentRoot = go.GetComponent<RectTransform>();
            _contentRoot.SetParent(transform, false);
            _contentRoot.localScale = Vector3.one;
        }
        else if (_contentRoot.name != ContentRootName)
        {
            _contentRoot.name = ContentRootName;
        }

        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (child == _contentRoot)
            {
                continue;
            }

            child.SetParent(_contentRoot, false);
            child.localScale = Vector3.one;
        }
    }

    private RectTransform FindExistingBoardRoot()
    {
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (child == null)
            {
                continue;
            }

            string n = child.name;
            if (n == ContentRootName
                || n == "BattleBiardCanvas"
                || n == "BattleBoardCanvas"
                || n == "BattleBoardRoot")
            {
                return child as RectTransform;
            }
        }

        return transform.Find(ContentRootName) as RectTransform;
    }

    private void NormalizeBoardChildren()
    {
        if (_contentRoot == null)
        {
            return;
        }

        for (int i = 0; i < _contentRoot.childCount; i++)
        {
            RectTransform child = _contentRoot.GetChild(i) as RectTransform;
            if (child == null)
            {
                continue;
            }

            child.localScale = Vector3.one;

            // 横ストレッチ子の Left/Right マイナスはみ出しを解消
            bool stretchX = Mathf.Abs(child.anchorMin.x - 0f) < 0.001f
                && Mathf.Abs(child.anchorMax.x - 1f) < 0.001f;
            if (stretchX)
            {
                Vector2 min = child.offsetMin;
                Vector2 max = child.offsetMax;
                min.x = 0f;
                max.x = 0f;
                child.offsetMin = min;
                child.offsetMax = max;
            }
        }
    }

    private void Apply(bool force)
    {
        if (_canvasRect == null)
        {
            _canvasRect = transform as RectTransform;
        }

        if (_contentRoot == null)
        {
            EnsureContentRoot();
        }

        if (_contentRoot == null || _canvasRect == null)
        {
            return;
        }

        Vector2 parentSize = _canvasRect.rect.size;
        if (!force && (parentSize - _lastParentSize).sqrMagnitude < 0.01f)
        {
            return;
        }

        _lastParentSize = parentSize;
        if (parentSize.y <= 1f)
        {
            return;
        }

        float height = Mathf.Min(_designHeight, parentSize.y);

        // 横は親いっぱい（固定480は縦長で親幅より広くなり突き抜ける）
        _contentRoot.anchorMin = new Vector2(0f, 0.5f);
        _contentRoot.anchorMax = new Vector2(1f, 0.5f);
        _contentRoot.pivot = new Vector2(0.5f, 0.5f);
        _contentRoot.anchoredPosition = Vector2.zero;
        _contentRoot.sizeDelta = new Vector2(0f, height);
        _contentRoot.offsetMin = new Vector2(0f, _contentRoot.offsetMin.y);
        _contentRoot.offsetMax = new Vector2(0f, _contentRoot.offsetMax.y);
        _contentRoot.localScale = Vector3.one;
        _contentRoot.localRotation = Quaternion.identity;

        NormalizeBoardChildren();
    }
}
