using UnityEngine;
using UnityEngine.UI;
using TMPro;

 public enum UIAnchor
    {
        TopStretch,
        BottomStretch,
        CenterStretch,
        FullStretch,
        TopCenter,
        TopLeft,
        TopRight,
        BottomCenter,
        FullSize
    }
public static class UIExtensions
{
    /// <summary>
    /// UIを親要素いっぱいに広げる（Stretch設定）
    /// </summary>
   
    public static void SetAnchor(this RectTransform rect, UIAnchor anchor)
{
    switch (anchor)
    {
        case UIAnchor.FullSize:
        // アンカーを「全方向ストレッチ」にする
    rect.anchorMin = Vector2.zero; // (0, 0)
    rect.anchorMax = Vector2.one;  // (1, 1)

    // 余白（Left, Right, Top, Bottom）をすべて 0 にする
    rect.offsetMin = Vector2.zero; // Left, Bottom
    rect.offsetMax = Vector2.zero; // Right, Top
    break;
        case UIAnchor.CenterStretch:
            rect.anchorMin = new Vector2(0, 0.5f);
            rect.anchorMax = new Vector2(1, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            break;
        case UIAnchor.TopStretch:
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(0.5f, 1f);
            break;
        case UIAnchor.BottomStretch:
            rect.anchorMin = new Vector2(0, 0);
            rect.anchorMax = new Vector2(1, 0);
            rect.pivot = new Vector2(0.5f, 0f);
            break;
        case UIAnchor.FullStretch:
            rect.anchorMin = new Vector2(0, 0);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(0.5f, 0.5f);
            break;
        case UIAnchor.TopCenter:
            rect.anchorMin = new Vector2(0.5f, 1);
            rect.anchorMax = new Vector2(0.5f, 1);
            rect.pivot = new Vector2(0.5f, 1f);
            break;
        case UIAnchor.TopLeft:
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1f);
            break;
        case UIAnchor.TopRight:
            rect.anchorMin = new Vector2(1, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(1, 1f);
            break;
        case UIAnchor.BottomCenter:
            rect.anchorMin = new Vector2(0.5f, 0);
            rect.anchorMax = new Vector2(0.5f, 0);
            rect.pivot = new Vector2(0.5f, 0f);
            break;

    }
}
    public static GameObject CreateGridScrollView(this GameObject parent, int width, int height,UIAnchor anchor = UIAnchor.FullSize )
    {
        // --- 1. Root (ScrollView本体) ---
        GameObject scrollRoot = new GameObject("GridScrollView", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ScrollRect));
        scrollRoot.transform.SetParent(parent.transform, false);
        var rootRect = scrollRoot.GetComponent<RectTransform>();
        rootRect.sizeDelta = new Vector2(width, height);
        rootRect.SetAnchor(anchor); // 中央上に配置
        scrollRoot.GetComponent<Image>().color = new Color32(255, 255, 255, 100);

        // --- 2. Viewport (表示窓) ---
        GameObject viewport = new GameObject("Viewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask));
        viewport.transform.SetParent(scrollRoot.transform, false);
        var viewRect = viewport.GetComponent<RectTransform>();
        viewRect.anchorMin = Vector2.zero;
        viewRect.anchorMax = Vector2.one;
        viewRect.sizeDelta = Vector2.zero;
        viewRect.pivot = new Vector2(0.5f, 1f);
        viewport.GetComponent<Mask>().showMaskGraphic = false;

        // --- 3. Content (中身) ---
        GameObject content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(viewport.transform, false);
        var contentRect = content.GetComponent<RectTransform>();

        // 【設定】Top Stretch (横幅は親に合わせる)
        contentRect.anchorMin = new Vector2(0, 1);
        contentRect.anchorMax = new Vector2(1, 1);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = new Vector2(0, 0); // 高さはFitterに任せる

        // --- 4. GridLayoutGroup の追加と設定 ---
        var grid = content.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(100, 100); // 【設定】セルサイズ 100x100
        grid.spacing = new Vector2(10, 10);     // 任意の隙間
        grid.padding = new RectOffset(10, 10, 10, 10); // 外側の余白
        grid.childAlignment = TextAnchor.UpperLeft;    // 左上から並べる
        grid.startAxis = GridLayoutGroup.Axis.Horizontal; // 横方向に並べていく

        // --- 5. 自動高さ調整 (ContentSizeFitter) ---
        // これを入れないと、アイテムが増えてもスクロールしません
        var fitter = content.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // --- 6. ScrollRect の紐付け ---
        ScrollRect sr = scrollRoot.GetComponent<ScrollRect>();
        sr.viewport = viewRect;
        sr.content = contentRect;
        sr.horizontal = false; // 縦スクロールのみ
        sr.vertical = true;

        return scrollRoot;
    }

    /// <summary>
    /// ScrollView内のGridセルを、表示エリアの高さ基準で設定する。
    /// </summary>
    public static void ConfigureGridCellFromViewportHeight(this GameObject scrollRoot, float heightRatio = 0.8f, float minCellSize = 48f)
    {
        if (scrollRoot == null)
        {
            return;
        }

        ScrollRect sr = scrollRoot.GetComponent<ScrollRect>();
        if (sr == null || sr.content == null || sr.viewport == null)
        {
            return;
        }

        GridLayoutGroup grid = sr.content.GetComponent<GridLayoutGroup>();
        if (grid == null)
        {
            return;
        }

        float viewportHeight = sr.viewport.rect.height;
        float availableHeight = Mathf.Max(0f, viewportHeight - grid.padding.top - grid.padding.bottom);
        float ratio = Mathf.Clamp01(heightRatio);
        float cell = Mathf.Max(minCellSize, availableHeight * ratio);
        grid.cellSize = new Vector2(cell, cell);
    }


    public static void SetRotation(this RectTransform rect, float angleZ)
    {
        rect.localRotation = Quaternion.Euler(0, 0, angleZ);
    }


    public static void SetFullSize(this RectTransform rect)
    {
        if (rect == null) return;

        rect.anchorMin = Vector2.zero;      // (0, 0)
        rect.anchorMax = Vector2.one;       // (1, 1)
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
    }

    /// <summary>
    /// GameObjectから直接全画面化を呼べるようにする
    /// </summary>
    public static void SetFullSize(this GameObject obj)
    {
        var rect = obj.GetComponent<RectTransform>();
        if (rect != null)
        {
            rect.SetFullSize();
        }
    }

    // Component用：ImageやButton、CardControllerからも直接呼べるようになる！
    public static void SetFullSize(this Component comp)
    {
        comp.GetComponent<RectTransform>()?.SetFullSize();
    }

    // UIExtensions.cs に追加

    // public static GameObject SetChildPanel(this GameObject parent, this GameObject child ,UIAnchor anchor)
    // {
        
    // }
    public static TextMeshProUGUI CreateChildTextCustom(this GameObject parent, string name, UIAnchor anchor, int width, int height)
    {
        // 1. オブジェクト生成 (Imageは含めない)
        GameObject textObj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObj.transform.SetParent(parent.transform, false);

        // 2. TextMeshProUGUI コンポーネントの取得と初期設定
        var tmp = textObj.GetComponent<TextMeshProUGUI>();
        tmp.text = "New Text";
        tmp.fontSize = 24;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center; // デフォルトで中央揃え

        // 3. RectTransform の設定
        var rect = textObj.GetComponent<RectTransform>();
        rect.SetAnchor(anchor);
        rect.sizeDelta = new Vector2(width, height);

        return tmp;
    }
    public static GameObject CreateChildPanelCustom(this GameObject parent, string name, UIAnchor anchor, int width, int height)
{
    // 1. オブジェクト生成
    GameObject panelObj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
    panelObj.transform.SetParent(parent.transform, false);
    
    // 2. 色の設定
    panelObj.GetComponent<Image>().color = new Color32(255, 255, 255, 100);

    // 3. RectTransform の取得
    var rect = panelObj.GetComponent<RectTransform>();
    
    // 4. 【ここで使用】自作の SetAnchor を呼び出す
    rect.SetAnchor(anchor);
    
    // 5. サイズの適用
    // Stretch系の場合は sizeDelta.x=0 になるよう SetAnchor 側で調整が必要ですが、
    // ここでは単純に引数の値をセットします
    rect.sizeDelta = new Vector2(width, height);

    return panelObj;
}
public static GameObject CreateChildPanelTop(this GameObject parent, string name, int height,UIAnchor anchor = UIAnchor.TopStretch)
{
    GameObject panelObj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
    panelObj.transform.SetParent(parent.transform, false);
    
    var rect = panelObj.GetComponent<RectTransform>();

    // 色を設定する。
    var img = panelObj.GetComponent<Image>();
    if(img != null)
    {
        img.color = new Color32(255, 255, 255, 100);
    }
    
    // Stretch Top 設定
    // rect.anchorMin = new Vector2(0, 1);
    // rect.anchorMax = new Vector2(1, 1);
    // rect.pivot = new Vector2(0.5f, 1f);

    rect.SetAnchor(anchor); // 引数で指定されたアンカー設定を適用
    
    // 横幅は親と同じ(0)、高さだけ指定
    rect.sizeDelta = new Vector2(0, height);
    // 位置を上端にリセット
    rect.anchoredPosition = Vector2.zero;

    return panelObj;
}
public static GameObject CreateChildImageFrom(this GameObject parent, GameObject sourceObj)
{
    // 1. 新しい画像オブジェクト作成
    GameObject newImgObj = new GameObject("ChildImage", typeof(RectTransform), typeof(UnityEngine.UI.Image));
    
    // 2. 親子関係をセット
    newImgObj.transform.SetParent(parent.transform, false);
    
    // 3. 画像をコピー
    var sourceImg = sourceObj.GetComponent<UnityEngine.UI.Image>();
    var targetImg = newImgObj.GetComponent<UnityEngine.UI.Image>();
    if (sourceImg != null && targetImg != null) targetImg.sprite = sourceImg.sprite;
    
    return newImgObj; // あとでサイズ調整できるように戻り値を返す
}
    public static Button CreateChildButton(this GameObject parent, string labelText)
    {
        // 1. ボタン本体の作成（Imageがないとクリックに反応しません）
        GameObject btnObj = new GameObject("GeneratedButton", 
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        btnObj.transform.SetParent(parent.transform, false);

        // 2. テキスト（子要素）の作成
        GameObject textObj = new GameObject("Text (TMP)", 
            typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObj.transform.SetParent(btnObj.transform, false);

        // 3. テキストの設定
        var tmp = textObj.GetComponent<TextMeshProUGUI>();
        tmp.text = labelText;
        tmp.fontSize = 24;
        tmp.color = Color.black;
        tmp.alignment = TextAlignmentOptions.Center;
        
        // テキストをボタンいっぱいに広げる
        textObj.GetComponent<RectTransform>().SetFullSize();

        // 4. ボタンのコンポーネントを返す（あとで onClick を設定するため）
        return btnObj.GetComponent<Button>();
    }

    
}