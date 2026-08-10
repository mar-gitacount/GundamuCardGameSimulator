using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// バトル盤面を縦スクロール化する。
/// 初期表示は自分フィールド中心。上へスクロールで相手盤面を見る。
/// </summary>
public static class BattleBoardScrollLayout
{
    public const string ScrollRootName = "BattleFieldScroll";
    public const string ContentName = "BattleFieldScrollContent";

    /// <summary>ビューポートに対する自分フィールドの高さ比率（半分より大きく）。</summary>
    private const float PlayerFieldViewportRatio = 0.74f;

    /// <summary>相手フィールドの高さ比率（スクロールで全体を見る）。</summary>
    private const float EnemyFieldViewportRatio = 0.74f;

    public static void Apply(RectTransform boardRoot, RectTransform playerField, RectTransform enemyField)
    {
        if (boardRoot == null || playerField == null || enemyField == null)
        {
            return;
        }

        float viewportHeight = Mathf.Max(1f, boardRoot.rect.height);
        float playerHeight = Mathf.Round(viewportHeight * PlayerFieldViewportRatio);
        float enemyHeight = Mathf.Round(viewportHeight * EnemyFieldViewportRatio);
        float contentHeight = playerHeight + enemyHeight;

        ScrollRect scroll = EnsureScroll(boardRoot, out RectTransform viewport, out RectTransform content);
        if (scroll == null || viewport == null || content == null)
        {
            return;
        }

        // 盤面直下にあったフィールドを Content 配下へ
        if (playerField.parent != content)
        {
            playerField.SetParent(content, false);
        }

        if (enemyField.parent != content)
        {
            enemyField.SetParent(content, false);
        }

        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = new Vector2(0f, contentHeight);
        content.anchoredPosition = Vector2.zero;

        // 上: 相手（180°） / 下: 自分
        PlaceField(enemyField, 0f, enemyHeight, rotate180: true);
        PlaceField(playerField, -enemyHeight, playerHeight, rotate180: false);

        enemyField.SetSiblingIndex(0);
        playerField.SetSiblingIndex(1);

        Canvas.ForceUpdateCanvases();
        // 初期位置は自分側（下端）
        scroll.verticalNormalizedPosition = 0f;
        scroll.velocity = Vector2.zero;
    }

    private static void PlaceField(RectTransform field, float topY, float height, bool rotate180)
    {
        field.localScale = Vector3.one;
        field.anchorMin = new Vector2(0f, 1f);
        field.anchorMax = new Vector2(1f, 1f);
        field.pivot = new Vector2(0.5f, 0.5f);
        field.sizeDelta = new Vector2(0f, height);
        // pivot 中央なので、上端が topY になるよう位置調整
        field.anchoredPosition = new Vector2(0f, topY - height * 0.5f);
        field.localRotation = Quaternion.Euler(0f, 0f, rotate180 ? 180f : 0f);

        Image image = field.GetComponent<Image>();
        if (image != null)
        {
            // 半透明のままスクロール領域を埋める
            Color c = image.color;
            c.a = Mathf.Max(c.a, 0.35f);
            image.color = c;
            image.raycastTarget = true;
        }
    }

    private static ScrollRect EnsureScroll(
        RectTransform boardRoot,
        out RectTransform viewport,
        out RectTransform content)
    {
        viewport = null;
        content = null;

        Transform existing = boardRoot.Find(ScrollRootName);
        GameObject scrollGo;
        if (existing != null)
        {
            scrollGo = existing.gameObject;
        }
        else
        {
            scrollGo = new GameObject(ScrollRootName, typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            scrollGo.transform.SetParent(boardRoot, false);
        }

        RectTransform scrollRt = scrollGo.GetComponent<RectTransform>();
        scrollRt.anchorMin = Vector2.zero;
        scrollRt.anchorMax = Vector2.one;
        scrollRt.offsetMin = Vector2.zero;
        scrollRt.offsetMax = Vector2.zero;
        scrollRt.pivot = new Vector2(0.5f, 0.5f);
        scrollRt.SetAsFirstSibling();

        Image scrollBg = scrollGo.GetComponent<Image>();
        scrollBg.color = new Color(0f, 0f, 0f, 0.01f);
        scrollBg.raycastTarget = true;

        ScrollRect scroll = scrollGo.GetComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.inertia = true;
        scroll.decelerationRate = 0.135f;
        scroll.scrollSensitivity = 40f;

        Transform viewportTf = scrollGo.transform.Find("Viewport");
        GameObject viewportGo;
        if (viewportTf != null)
        {
            viewportGo = viewportTf.gameObject;
        }
        else
        {
            viewportGo = new GameObject(
                "Viewport",
                typeof(RectTransform),
                typeof(Image),
                typeof(Mask));
            viewportGo.transform.SetParent(scrollGo.transform, false);
        }

        viewport = viewportGo.GetComponent<RectTransform>();
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = Vector2.zero;
        viewport.offsetMax = Vector2.zero;
        Image viewportImage = viewportGo.GetComponent<Image>();
        viewportImage.color = Color.white;
        viewportImage.raycastTarget = true;

        // 180° 回転した相手盤面は RectMask2D だと消えることがあるため、ステンシル Mask を使う
        RectMask2D rectMask = viewportGo.GetComponent<RectMask2D>();
        if (rectMask != null)
        {
            UnityEngine.Object.DestroyImmediate(rectMask);
        }

        Mask stencilMask = viewportGo.GetComponent<Mask>();
        if (stencilMask == null)
        {
            stencilMask = viewportGo.AddComponent<Mask>();
        }

        stencilMask.showMaskGraphic = false;
        stencilMask.enabled = true;

        Transform contentTf = viewport.Find(ContentName);
        if (contentTf == null)
        {
            // 旧 Content 名も吸収
            contentTf = viewport.Find("Content");
        }

        GameObject contentGo;
        if (contentTf != null)
        {
            contentGo = contentTf.gameObject;
            contentGo.name = ContentName;
        }
        else
        {
            contentGo = new GameObject(ContentName, typeof(RectTransform));
            contentGo.transform.SetParent(viewport, false);
        }

        content = contentGo.GetComponent<RectTransform>();
        scroll.viewport = viewport;
        scroll.content = content;
        return scroll;
    }
}
