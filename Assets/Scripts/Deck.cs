using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
public class Deck : MonoBehaviour
{
    // ボタンを押すとフィルターが表示されてデッキ一覧が表示される。
    [SerializeField] private Button displayButton;
    // フィルターパネル=全体の半分の位置に設置する。
    // デッキリストを編集モードにしたらデッキ内のカードを表示する。
    // クリックしたらそのカードの詳細を表示する。
    [SerializeField] private RectTransform FilterPanelPrefab;
    [SerializeField] private Canvas canvas;

    [SerializeField] private  TextMeshProUGUI DeckText;

    [SerializeField] private Button AllViewButton;
    [SerializeField] private TextMeshProUGUI AllViewButtonText;
    // Start is called before the first frame update
    void Start()
    {
        // DeckText.text = "Deck";

        canvas = GetComponentInParent<Canvas>().rootCanvas;
        RectTransform rect = canvas.GetComponent<RectTransform>();
        float width  = rect.rect.width;
        float height = rect.rect.height;
        displayButton.onClick.AddListener(clicked);
        AllViewButton.onClick.AddListener(AllViewbuttonCliekd);

    }

    // Update is called once per frame
    void Update()
    {
        
    }
    private void clicked()
    {
       
       
        
        // ?フィルターパネルを生成して表示する。
        
        
        if (FilterPanelPrefab.gameObject.activeSelf)
        {
             DeckText.text = "Deck";
           
            FilterPanelPrefab.gameObject.SetActive(false);
             return;
        }
        else
        {
            FilterPanelPrefab.gameObject.SetActive(true);
             DeckText.text = "Close";
             return;
        }
        RectTransform FilterPanel = Instantiate(FilterPanelPrefab, canvas.transform).GetComponent<RectTransform>();
        // FilterPanel.SetParent(canvas.transform, false);
        FilterPanel.anchorMin = new Vector2(0, 0);
        FilterPanel.anchorMax = new Vector2(1, 1);
        FilterPanel.offsetMin = Vector2.zero;
        FilterPanel.offsetMax = Vector2.zero;
        // ?フィルターパネル処理ここまで
        FilterPanel.SetParent(canvas.transform,false);
        FilterPanel.anchoredPosition = Vector2.zero;
        FilterPanel.anchorMin = new Vector2(0.5f, 0.5f);
        FilterPanel.anchorMax = new Vector2(0.5f, 0.5f);
        FilterPanel.pivot = new Vector2(0.5f, 0.5f);
        FilterPanel.sizeDelta = new Vector2(400, 600);
        return;
       




    }
    private void AllViewbuttonCliekd()
    {
        Vector2 offset = FilterPanelPrefab.offsetMax;
        if (Mathf.Abs(FilterPanelPrefab.offsetMax.y + 100) < 0.01f)
        {
            Debug.Log("ほぼ-100");
             
            offset.y = -600;
            FilterPanelPrefab.offsetMax = offset;
            AllViewButtonText.text="AllView";

            
            return;
        }
       
        // Vector2 offset = FilterPanelPrefab.offsetMax;
        offset.y = -100;
        AllViewButtonText.text="small";
        FilterPanelPrefab.offsetMax = offset;
        Debug.Log("デッキをすべて表示する");
    }
}
