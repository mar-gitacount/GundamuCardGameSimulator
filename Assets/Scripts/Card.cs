using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

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
        if (DeckSettinObject.Instance.isDeckEditing)
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

    private void OnDestroy()
    {
        if (DeckEditPanel == null) return;
        Debug.Log("デストロイ実行");
        // DeckSettinObject.Instance.cardObj(gameObject);
        DeckEdit deck = DeckEditPanel.GetComponent<DeckEdit>();
        // 編集中デッキに追加する。
        deck.CardIdtoSettingObject(CardId);
       
        Destroy(gameObject);
    }
    // カードの移動先を設定するメソッド
    public void MoveToPosition(Vector3 newPosition)
    {
        transform.position = newPosition;
    }
}
