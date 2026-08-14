using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class Card : MonoBehaviour
{
    [SerializeField] private Canvas canvas;
    [SerializeField] private Button button;
    [SerializeField] private RectTransform FilterPanelPrefab;
    [SerializeField] private RectTransform DeckEditPrefab;
    [SerializeField] private RectTransform DeckEditPanel;


    [SerializeField] private GameObject target;
    
    private Image image;
    LayoutElement layout;
    private GameObject copy;
    public int CardId;

    private Button FilterPanelCloseButton;

   private DeckEdit deck;

    
    // カードデータのクラスも定義する。
    // Start is called before the first frame update
    void Start()
    {
      
        // 親キャンバスのサイズを取得
        canvas = GetComponentInParent<Canvas>().rootCanvas;
        RectTransform rect = canvas.GetComponent<RectTransform>();
        // 
        image = GetComponent<Image>();
        layout = GetComponent<LayoutElement>();
        float width  = rect.rect.width;
        float height = rect.rect.height;
        //  GetComponent<Button>().onClick.AddListener(clicked);
        button.onClick.AddListener(clicked);

        Debug.Log($"Canvas サイズ : 幅={width}, 高さ={height}");

        
        
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            Vector3 pos = Input.mousePosition;
            Debug.Log("カードテスト、クリック位置（スクリーン座標）: " + pos);
        }
    }
    private void OnMouseDown()
    {
        Debug.Log("Card clicked!");
    }
    private void clicked()
    {
        RectTransform FilterPanel = Instantiate(FilterPanelPrefab, canvas.transform);
        
        // FilterPanelCloseButton= FilterPanel.GetComponent<Button>();
        // FilterPanelCloseButton.onClick.AddListener(OnDestroy);
        Debug.Log($"ボタン:カード{DeckSettinObject.Instance.isDeckEditing}");
        // デッキ編集中ならデッキ追加リストを表示、static変数に代入する。
        // falseになった場合、変数を初期化する。
     
        FilterPanel.gameObject.SetActive(true);
        FilterPanel.SetParent(canvas.transform, false);
        FilterPanel.anchorMin = new Vector2(0, 0);
        FilterPanel.anchorMax = new Vector2(1, 1);
        FilterPanel.offsetMin = Vector2.zero;
        FilterPanel.offsetMax = Vector2.zero;

        // デッキに枚数を追加するパネルを追加する。
        // ユニットトークンはデッキに入れられないため、+/- カウンターを出さない。
        if (DeckSettinObject.Instance.isDeckEditing && !IsUnitTokenCard(CardId))
        {
            // RectTransform DeckEditPanel = Instantiate(DeckEditPrefab,canvas.transform);
            DeckEditPanel = Instantiate(DeckEditPrefab,canvas.transform);
            deck = DeckEditPanel.GetComponent<DeckEdit>();
            deck.cardId = CardId;
            // オブジェクトをわたす。
            deck.CardObj = gameObject;
            // RectTransform DeckEditPanel = Instantiate(DeckEditPrefab,canvas.transform).GetComponent<RectTransform>();
            // DeckEditPanel.gameObject.SetActive(true);
            DeckEditPanel.SetParent(FilterPanel.transform, false);
            DeckEditPanel.anchoredPosition = new Vector2(0,-30);
            DeckEditPanel.anchorMin = new Vector2(0f,1f);
            DeckEditPanel.anchorMax = new Vector2(1f,1f);
            DeckEditPanel.pivot = new Vector2(0.5f, 1f);
            DeckEditPanel.sizeDelta = new Vector2(0, 100);

            // 1枚以上あるカードならサムネ候補にできる
            Button setThumbBtn = FilterPanel.gameObject.CreateChildButton("サムネにする");
            RectTransform thumbRt = setThumbBtn.GetComponent<RectTransform>();
            thumbRt.anchorMin = new Vector2(0.5f, 0f);
            thumbRt.anchorMax = new Vector2(0.5f, 0f);
            thumbRt.pivot = new Vector2(0.5f, 0f);
            thumbRt.sizeDelta = new Vector2(220f, 48f);
            thumbRt.anchoredPosition = new Vector2(0f, 28f);
            Image thumbBtnImage = setThumbBtn.GetComponent<Image>();
            if (thumbBtnImage != null && thumbBtnImage.sprite == null)
            {
                Texture2D tex = Texture2D.whiteTexture;
                thumbBtnImage.sprite = Sprite.Create(
                    tex,
                    new Rect(0f, 0f, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f));
            }

            TextMeshProUGUI thumbLabel = setThumbBtn.GetComponentInChildren<TextMeshProUGUI>(true);
            if (thumbLabel != null)
            {
                thumbLabel.SetLocalizedText("サムネにする", "Set as thumbnail");
                thumbLabel.fontSize = 20f;
            }

            int capturedId = CardId;
            setThumbBtn.onClick.AddListener(() =>
            {
                if (DeckSettinObject.Instance == null)
                {
                    return;
                }

                if (DeckSettinObject.Instance.CardCount(capturedId) <= 0)
                {
                    Debug.LogWarning("[Deck] 枚数0のカードはサムネにできません。");
                    return;
                }

                DeckSettinObject.Instance.SetThumbnailCardId(capturedId);
            });
        }
        // 画像のコピーを作成して、フィルターパネルの子オブジェクトとして配置する
        copy = Instantiate(gameObject, canvas.transform);
        RectTransform CardCopyRect = copy.GetComponent<RectTransform>();
        CardCopyRect.SetParent(FilterPanel.transform, false);
        CardCopyRect.anchoredPosition = Vector2.zero;
        CardCopyRect.anchorMin = new Vector2(0.5f, 0.5f);
        CardCopyRect.anchorMax = new Vector2(0.5f, 0.5f);
        CardCopyRect.pivot = new Vector2(0.5f, 0.5f);
        CardCopyRect.sizeDelta = new Vector2(400, 600);

        PlaceDetailCloseButtonAboveCard(FilterPanel, CardCopyRect);
        return;
       
        
        // Canvas canvas = GetComponentInParent<Canvas>().rootCanvas;
        if (copy != null)
    {
        Debug.Log("既にカードが表示されているため、コピーを削除して新しいカードを表示します。");
        Destroy(copy);
        return;
    }
        Destroy(copy);
        copy = Instantiate(gameObject, canvas.transform);

        RectTransform rect = copy.GetComponent<RectTransform>();
        // RectTransform rect = image.GetComponent<RectTransform>();

        rect.SetParent(canvas.transform, false);
        rect.anchoredPosition = Vector2.zero;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(400, 600);


        // Debug.Log($"Card clicked! Current size: {rect.sizeDelta}");
        // rect.sizeDelta = new Vector2(200, 300); // 幅200 高さ300
        // Debug.Log($"Card clicked! New size: {rect.sizeDelta}");
        // Debug.Log("Card clicked!");
        // layout.preferredWidth = 200;
        // layout.preferredHeight = 300;

    }

    /// <summary>詳細表示の Close を拡大カードの少し上に置く。</summary>
    private static void PlaceDetailCloseButtonAboveCard(RectTransform filterPanel, RectTransform cardRt)
    {
        if (filterPanel == null || cardRt == null)
        {
            return;
        }

        RectTransform closeRt = FindDetailCloseButton(filterPanel);
        if (closeRt == null)
        {
            return;
        }

        const float gap = 12f;
        const float closeWidth = 160f;
        const float closeHeight = 40f;
        float cardTop = cardRt.sizeDelta.y * 0.5f;

        closeRt.SetParent(filterPanel, false);
        closeRt.anchorMin = new Vector2(0.5f, 0.5f);
        closeRt.anchorMax = new Vector2(0.5f, 0.5f);
        closeRt.pivot = new Vector2(0.5f, 0f);
        closeRt.sizeDelta = new Vector2(closeWidth, closeHeight);
        closeRt.anchoredPosition = new Vector2(0f, cardTop + gap);
        closeRt.SetAsLastSibling();
    }

    private static RectTransform FindDetailCloseButton(RectTransform filterPanel)
    {
        Transform named = filterPanel.Find("Button");
        if (named != null)
        {
            return named as RectTransform;
        }

        Button[] buttons = filterPanel.GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            Button btn = buttons[i];
            if (btn == null)
            {
                continue;
            }

            string n = btn.gameObject.name;
            if (n == "Button" || n == "Close" || n == "CloseButton")
            {
                return btn.GetComponent<RectTransform>();
            }

            TextMeshProUGUI label = btn.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null && label.text != null && label.text.IndexOf("Close", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return btn.GetComponent<RectTransform>();
            }
        }

        return null;
    }

    /// <summary>デッキ編集プレビュー複製時に、誤って OnDestroy 連鎖しないようセッション参照を外す。</summary>
    public void ClearDeckEditSession()
    {
        DeckEditPanel = null;
    }

    /// <summary>ユニットトークンはデッキ構築対象外。</summary>
    private static bool IsUnitTokenCard(int cardId)
    {
        if (cardId <= 0 || CardDatabase.Instance == null)
        {
            return false;
        }

        CardData data = CardDatabase.Instance.FindById(cardId);
        return data != null && data.IsUnitToken();
    }

    private const string NotUsedOnlineLabelName = "NotUsedOnlineLabel";

    /// <summary>カード一覧／サムネ上部に Not Used Online ラベルを付ける（対象カードのみ）。</summary>
    public static void EnsureNotUsedOnlineLabel(GameObject cardObject, CardData data)
    {
        if (cardObject == null)
        {
            return;
        }

        Transform existing = cardObject.transform.Find(NotUsedOnlineLabelName);
        if (data == null || !data.notUsedOnline)
        {
            if (existing != null)
            {
                existing.gameObject.SetActive(false);
            }

            return;
        }

        TextMeshProUGUI label;
        if (existing != null)
        {
            label = existing.GetComponent<TextMeshProUGUI>();
            if (label == null)
            {
                existing.gameObject.SetActive(true);
                return;
            }
        }
        else
        {
            GameObject labelGo = new GameObject(
                NotUsedOnlineLabelName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(cardObject.transform, false);
            label = labelGo.GetComponent<TextMeshProUGUI>();
        }

        label.gameObject.SetActive(true);
        label.text = "Not Used Online";
        label.fontSize = 14f;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color32(255, 90, 90, 255);
        label.enableWordWrapping = true;
        label.raycastTarget = false;

        if (TMP_Settings.defaultFontAsset != null)
        {
            label.font = TMP_Settings.defaultFontAsset;
        }

        RectTransform rt = label.rectTransform;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -4f);
        rt.sizeDelta = new Vector2(0f, 28f);
        label.transform.SetAsLastSibling();
    }

    private void OnDestroy()
    {
        DeckEditPanel = null;
    }
    // カードの移動先を設定するメソッド
    public void MoveToPosition(Vector3 newPosition)
    {
        transform.position = newPosition;
    }
}
