using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CardPanel : MonoBehaviour
{
    // カードを配置するパネル=位置を指定する
    [SerializeField] private Transform cardPanel;
    [SerializeField] private GameObject cardPrefab;

    
    // Start is called before the first frame update
    void Start()
    {
        // カードパネルのxy座標を取得
        Vector3 panelPosition = cardPanel.position;
        Debug.Log("Card Panel Position: " + panelPosition.x + ", " + panelPosition.y);
        // カードを5枚生成してパネルに配置する例
        for (int i = 0; i < 4; i++)
        {
            GameObject cardObj = Instantiate(cardPrefab, cardPanel);
            // カードパネルのxy座標を基準に配置



            // 元々のカードの画像を取得して設定する例
            SpriteRenderer cardSprite = cardObj.GetComponent<SpriteRenderer>();
            Sprite sr = cardSprite.sprite;
            if(i == 2)
            {
                cardObj.transform.localScale = new Vector3(0.5f, 0.5f, 1f);
            }
            // カードサイズ
            // float cardWidth = cardSprite.bounds.size.x;

            float cardWidth = sr.rect.width;

            // カードの位置を調整する（例: 横に並べる）
            cardObj.transform.localPosition = new Vector3(i * cardWidth + 10f, -100f, 0); // 110はカードの幅+間隔
        }
        
    }

    // Update is called once per frame
    void Update()
    {
        // クリック時、カードの大きさを変更する
    }
}
