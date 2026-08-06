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

            // ?テストコード
            // DeckSettinObject.Instance.cardObj(gameObject);
            // ?
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
