using UnityEngine;
using UnityEngine.UI;
using TMPro;
public static class UIExtensions
{
    /// <summary>
    /// UIを親要素いっぱいに広げる（Stretch設定）
    /// </summary>
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