using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.Linq;
using System.Reflection;

public class DeckEditPanel : MonoBehaviour
{
    [SerializeField] private GameObject DeckEditNowpanel;
    [SerializeField] private GameObject CardPrefab; // 共通の土台プレハブ

    // IDをキーにしてCardDataを即座に引ける辞書
    private Dictionary<int, CardData> _cardDatabase;

    // 最初に一度だけ全カードデータをメモリにロード（辞書化）
    void InitializeDatabase() 
    {
        if (_cardDatabase != null) return;

        // Resources/Data/Cards 内の全ての ScriptableObject をロード
        CardData[] allCards = Resources.LoadAll<CardData>("Data/Cards");
        
        // idをキーにして辞書に登録（1000枚あっても一瞬で検索可能になる）
        _cardDatabase = allCards.ToDictionary(c => c.id, c => c);
        
        Debug.Log($"{_cardDatabase.Count} 枚のカードデータをデータベースに登録しました。");
    }
    public void LoadDeckToEditPanel()
    {
        // データベースが未初期化なら初期化する
        DeckEditNowpanel.transform.DetachChildren(); // 既存のカードオブジェクトを全て削除
        if(_cardDatabase == null)
        {
            InitializeDatabase();
        }
        CardDatabase db = CardDatabase.Instance;
        Dictionary<int, int> DeckData = DeckSettinObject.Instance.LoadDeckReturn();
        foreach (var data in DeckData)
        {
            int targetId = data.Key;
            int count = data.Value;
            CardData carddata = db.FindById(targetId);
            GameObject cardObj = Instantiate(CardPrefab, DeckEditNowpanel.transform);
            Card cardId = cardObj.GetComponent<Card>(); 
            cardId.CardId = targetId;

            Image img = cardObj.GetComponent<Image>();
            img.sprite = carddata.imageName;
            Debug.Log($"生成するカードID: {carddata.id}, 枚数: {count}");
        }
        Debug.Log("DeckEditPanel LoadDeckToEditPanel: デッキ編集パネルにデータをロードします。");
    }

    void Start()
    {
        Debug.Log("DeckEditPanel Start");
        return;
        // 1. データベース（辞書）を初期化
        InitializeDatabase();
        CardDatabase db = CardDatabase.Instance;
        // 2. デッキデータを取得
        Dictionary<int, int> DeckData = DeckSettinObject.Instance.LoadDeckReturn();
       
        foreach (var data in DeckData)
        {
            int targetId = data.Key;
            int count = data.Value;
            CardData carddata = db.FindById(targetId);
            GameObject cardObj = Instantiate(CardPrefab, DeckEditNowpanel.transform);
            Card cardId = cardObj.GetComponent<Card>(); 
            cardId.CardId = targetId;

            Image img = cardObj.GetComponent<Image>();
            img.sprite = carddata.imageName;
            Debug.Log($"生成するカードID: {carddata.id}, 枚数: {count}");
            // !消す3. 辞書からIDでデータを抽出
            if (_cardDatabase.TryGetValue(targetId, out CardData masterData))
            {
              
              
              
                // 4. for文を使わず枚数分生成 (LINQ)
                Enumerable.Repeat(masterData, count).ToList().ForEach(m => 
                {
                    // CardPrefab（共通プレハブ）を生成
                    // GameObject cardObj = Instantiate(CardPrefab, DeckEditNowpanel.transform);
                    // Card cardId = cardObj.GetComponent<Card>(); 
                    // Debug.Log($"生成するカードID: {m.id}, 枚数: {count}");
                    // 5. 生成したオブジェクトにデータを反映させる
                    // ※ CardEntityなどのスクリプトがプレハブについている想定
                    
                });
            }
            else
            {
                Debug.LogWarning($"ID: {targetId} のデータがResources内に見つかりません。");
            }
        }
    }
}