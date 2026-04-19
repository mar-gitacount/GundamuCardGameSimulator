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