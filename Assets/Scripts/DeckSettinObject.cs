using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.Linq;
using TMPro;
using System.IO;
using System;

public class DeckSettinObject : MonoBehaviour
{
    public static DeckSettinObject Instance;
    public bool isDeckEditing;
    private Dictionary<int, int> cardData = new Dictionary<int, int>();
    [SerializeField] private GameObject DeckEditNowpanel;
    // テキストフィールド
    [SerializeField] private TMP_InputField DeckTitleInputField;


    [SerializeField] private TMP_Text NewDeckText;
    

    // 編集中のデッキの文字列
    public string deckPathName;
    // ?デッキデータプレハブ
    [SerializeField] private GameObject DeckDataPrefab;
    public GameObject DeckImagePrefab;  
    [SerializeField] private TextMeshProUGUI CardCountText;
    [SerializeField] private GameObject DeckListPanel;
    [SerializeField] private GameObject DeckinfoPanel;
    [Serializable]public class DeckSaveData
{
    public string title;
    public int thumbnailId; // サムネイル用のカードID
    public List<CardSlot> cards = new List<CardSlot>();
    private Dictionary<int, CardData> cardTable = new Dictionary<int, CardData>();
   
}

[Serializable]public class CardSlot
{
    public int id;
    public int count;
}





    void Awake()
    {
        Instance = this;
        ShowFileList();
    }

    // Update is called once per frame
    void Update()
    {
        
    }
    public void ClearDeckList()
    {
        cardData.Clear();
        DeckListPanel.transform.DetachChildren(); // デッキリストの子オブジェクトを全て削除
        // DeckListPanel の子オブジェクトを全て削除
        // foreach (Transform child in DeckListPanel.transform)
        // {
        //     Destroy(child.gameObject);
        // }
    }

    public Dictionary<int, int> LoadDeckReturn()
    {    
        return cardData;
    }
    // デッキパネル内のカードをjsonファイルに保存する処理
    public void SaveDeckToJson(Dictionary<int, int> cardData)
    {
        // 2. データをクラスに詰め替える
        DeckSaveData saveData = new DeckSaveData();
        saveData.title = DeckTitleInputField.text; // タイトルも保存する場合
        saveData.thumbnailId = cardData.Keys.FirstOrDefault(); // ?サムネイルIDを保存後で動的にする。
        foreach (var item in cardData)
        {
            Debug.Log($"保存するカードID: {item.Key}, 枚数: {item.Value}");
            // なぜかid0のカードが枚数0で保存されるため、ここで枚数0のカードは保存しないようにする。デッキ編集画面で枚数0のカードは表示されないため、保存も不要と判断。
            if (item.Value > 0) // 枚数が0以上のカードのみ保存する
            {
                saveData.cards.Add(new CardSlot { id = item.Key, count = item.Value });
            }
        }

        // 3. JSON文字列に変換 (trueにすると見やすく整形されます)
        string json = JsonUtility.ToJson(saveData, true);
        string fullPath;
        // 既存のファイル名を取得して、同じ名前があれば上書きするか新規作成するかのロジックを入れることもできますが、
        if(string.IsNullOrEmpty(deckPathName))
        {
            // 新規保存
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = "Deck_" + timestamp + ".json";
            fullPath = Path.Combine(Application.persistentDataPath, fileName);
        }
        else
        {
            Debug.Log($"既存のデッキを上書き保存します: {deckPathName}");
            // 上書き保存
            fullPath = deckPathName; // 既に保存されているファイルのパスを使用
        }
        File.WriteAllText(fullPath, string.Empty); // 既存の内容をクリアしてから書き込む
        // 5. 書き込み
        try
        {

            File.WriteAllText(fullPath, json);
            Debug.Log($"保存完了: {fullPath}");

            // !deckPathName = ""; // 保存後はパスをリセットして新規保存モードに戻す
        }
        catch (Exception e)
        {
            Debug.LogError($"保存失敗: {e.Message}");
        }
    }
    public int CardCount(int id)
    {
        // int count = cardData[id];
        if (cardData.TryGetValue(id, out int count))
        {
            return count;
        }
        return 0;
    }

    // 編集中のデッキ→カードをクリックしてデッキに入れる処理
    public void Deckedit(int id , int count)
    {
        // Debug.Log($"デッキデータ{cardData[id]}枚");
        Debug.Log($"デッキデータ{id}のカードを{count}枚入れました。");
        if (cardData.ContainsKey(id))
        {
           Debug.Log($"デッキデータ{cardData[id]}枚");
        }

        cardData[id] = count;
        Debug.Log($"デッキデータ{cardData[id]}枚");
        // DeckTitleInputField.gameObject.SetActive(true);
        DeckEditNowpanel.SetActive(true);
        // return count;
    }
    public void RemoveCardById(int targetId)
{
    // DeckEditNowpanel の子から CardId が一致するものを検索
    // (true) を入れることで、コンポーネントが OFF になっているオブジェクトも対象にする
    Card targetCard = DeckEditNowpanel.GetComponentsInChildren<Card>(true).FirstOrDefault(c => c.CardId == targetId);
    
    
    if (targetCard != null)
    {
        // GameObject を削除
        Destroy(targetCard.gameObject);
        // カードデータからも削除
        cardData.Remove(targetId);
        Debug.Log($"CardId: {targetId} のオブジェクトを削除しました。");
    }
    else
    {
        Debug.LogWarning($"CardId: {targetId} は見つかりませんでした。");
    }
}
public GameObject FindCardById(int targetId)
{
    // 1. 子要素からすべての Card コンポーネントを取得 (非アクティブも含む場合は true)
    Card[] allCards = DeckEditNowpanel.GetComponentsInChildren<Card>(true);

    // 2. LINQで CardId が一致する最初のものを探す
    Card targetCard = allCards.FirstOrDefault(c => c.CardId == targetId);
    foreach (Card card in allCards)
    {
        // ログにオブジェクト名とIDを表示
        Debug.Log($"[IDログ] Name: {card.gameObject.name}, CardId: {card.CardId}, Enabled: {card.enabled}");

        // if (card.CardId == targetId)
        // {
        //     Debug.Log($"<color=green>一致するIDを発見しました: {targetId}</color>");
        //     return card.gameObject;
        // }
    }
    if (targetCard != null)
    {
        Debug.Log($"カード発見: {targetCard.gameObject.name} カードid{targetId}");
        return targetCard.gameObject;
    }

    Debug.LogWarning($"CardId: {targetId} は見つかりませんでした。");
    return null;
}

// カードをクリックしたときにデッキ編集パネルにカードオブジェクトを追加する処理。
public void cardObj(GameObject obj)
{
    
    Debug.Log("サムネ追加");
   
   
    int cardid = obj.GetComponent<Card>().CardId;
    int count = cardData[cardid];

    
    // デッキ編集エリアに存在する場合、追加しない。
    if(FindCardById(cardid) != null)
    {
        GameObject copyobj = FindCardById(cardid);
        // ?カウントテキストチェック
        // obj が GameObject の場合、.transform をつける
        // Instantiate(CardCountText, obj.transform);
        bool textFound = false;

       // 2. copyobj の「子要素」をループしてテキストコンポーネントを探す
       foreach (Transform child in copyobj.transform)
       {
         if (child.TryGetComponent(out TextMeshProUGUI text))
         {
            text.text = count.ToString();
            text.enabled = true; // 前回の「消える」対策
            text.gameObject.SetActive(true);
            
            Debug.Log($"カードID {cardid} の枚数を {count} に更新しました。");
            textFound = true;
            break; // 見つかったらループを抜ける
         }
       }
    //    if(copyobj.TryGetComponent(out TextMeshProUGUI text))
    //     {
    //         text.text = count.ToString();
    //         Debug.Log($"カードid{cardid}の枚数を{count}に更新しました。");
    //     }
    //     else
    //     {
    //         Debug.LogWarning("TextMeshProUGUI コンポーネントが見つかりませんでした。");
    //     }
        // デッキ編集エリアに存在してかつ、個数が0の場合削除する。
        if(count == 0)
        {
            Debug.Log($"カードID {cardid} の枚数が0になったため、オブジェクトを削除します。");
            RemoveCardById(cardid);
        }
        return;
    }

    // 新規で個数が0の場合、関数を抜ける。
    if(count == 0)
    {
        return;
    }
    
    // 存在した場合、フィールドに追加しない。
    if (cardData.ContainsKey(cardid))
    {

        // Debug.Log($"カードiDの数{cardData[cardid]}");
        // Destroy(copy);
        // return;
    }
    // オブジェクト数=0の場合、元オブジェクトを削除する。
    GameObject copy = Instantiate(obj, DeckEditNowpanel.transform);

   TextMeshProUGUI countText = Instantiate(CardCountText, copy.transform);
   if (countText.TryGetComponent(out TextMeshProUGUI tmpro))
   {
    tmpro.alignment = TextAlignmentOptions.Center; // 上下左右中央
    tmpro.text = count.ToString();
    
    // 前回の「消える」対策：強制有効化
    tmpro.enabled = true;
    }
    Debug.Log($"サムネid{obj.GetComponent<Card>().CardId}");
    RectTransform rect = copy.GetComponent<RectTransform>();
    rect.anchoredPosition = Vector2.zero;
    // rect.localScale = Vector3.one
    rect.anchorMin = new Vector2(0.5f, 0.5f);
    rect.anchorMax = new Vector2(0.5f, 0.5f);
    rect.pivot = new Vector2(0.5f, 0.5f);
    rect.sizeDelta = new Vector2(40, 60);
// rect.offsetMin = Vector2.zero;
// rect.offsetMax = Vector2.zero;

    Image img = copy.GetComponentInChildren<Image>();

    if (img == null)
    {
        Debug.Log("Imageない");
    }
    else
    {
        Debug.Log("Sprite: " + img.sprite);
    }
}
 public void CardDataToJson()
    {
        // !以下はあとで編集デッキの場合は名前を検知して保存するようにする。
        SaveDeckToJson(cardData);
    }

// 全ての保存されたデッキのファイル名を取得する関数
public List<string> GetSaveFileNames()
{
        string path = Application.persistentDataPath;
        List<string> fileList = new List<string>();

        if (Directory.Exists(path))
        {
            // 1. "Deck_" で始まり ".json" で終わるファイルのみを取得
            string[] files = Directory.GetFiles(path, "Deck_*.json");

            // 2. パスからファイル名だけを抜き出してリストに追加
            foreach (string fullPath in files)
            {
                fileList.Add(Path.GetFileName(fullPath));
            }

            // 3. (オプション) 日付順（新しい順）に並び替える
            // ファイル名に yyyyMMdd_HHmmss が入っているので文字列ソートでOK
            fileList = fileList.OrderByDescending(f => f).ToList();
        }

        return fileList;
    }


DeckSaveData jsonData(string path)
    {
        string json = File.ReadAllText(path);
        return JsonUtility.FromJson<DeckSaveData>(json);

    }

// 保存されたデッキのファイル名をリスト表示する関数
public void ShowFileList()
{
    var files = GetSaveFileNames();
    string path = Application.persistentDataPath; // パスを再定義

    Debug.Log($"保存されたデッキ数: {files.Count}");

    foreach (var fileName in files)
    {
        // ★修正ポイント：パスとファイル名を結合して「フルパス」を作る
        string fullPath = Path.Combine(path, fileName);
        string capturePath = fullPath;

   if (File.Exists(fullPath))
{
    string json = File.ReadAllText(fullPath);
    Debug.Log($"ファイルパスの内容: {fullPath} {json}");
    // JSONファイルを更新
    DeckSaveData data = JsonUtility.FromJson<DeckSaveData>(json);

    Sprite cardSprite= Resources.Load<Sprite>($"Data/Cards/{data.thumbnailId}");
    var cardTable = Resources.LoadAll<CardData>("Data/Cards").ToDictionary(data => data.id);


 

    // 1. 親（カード枠）を生成して 5倍にする
    GameObject cardObj = Instantiate(DeckDataPrefab, DeckListPanel.transform);
    Image targetImg = cardObj.GetComponent<Image>();
    targetImg.sprite = cardSprite;
    // デッキオブジェクトのボタンを取得
    Button btn = cardObj.GetComponentInChildren<Button>();
    if (btn != null)
    {
    // 3. クリックイベントを登録
    btn.onClick.AddListener(() => {
        Debug.Log(cardObj.name + " がクリックされました！");
        // ここに実行したい処理を書く
        // クリック時にcardDataにjsonのカードIDと枚数を渡す
        cardData.Clear();
        // 編集中のデッキの文字列を更新
        deckPathName = fullPath;
        Debug.Log($"{cardData.Count}枚のカードデータを読み込みました。");
        Debug.Log($"クリックされたデッキのパス: {fullPath}");
        // NewDeckText.text = "Edit Existing Deck";
        data = jsonData(fullPath);
        DeckinfoPanel.SetActive(true);
        DeckTitleInputField.text = data.title; // タイトルを入力フィールドに表示
        foreach (var card in data.cards)
        {
            Debug.Log($"クリックされたデッキのカードID: {card.id}, 枚数: {card.count}");
            cardData[card.id] = card.count;
        }
    });
    }
    

    if (cardTable.TryGetValue(data.thumbnailId, out CardData card))
    {
        // IDが見つかった場合、card 変数に中身が入る
        string targetImageName = card.imageName.name; // 例: "Fireball.png" から拡張子を除いた "Fireball" を取得
        Debug.Log($"ID:{data.thumbnailId} の画像名は {targetImageName} です");

        // そのまま画像ロードに使う例
        Sprite sp = Resources.Load<Sprite>($"Data/Images/{targetImageName}");
        cardObj.GetComponent<Image>().sprite = sp;
    }
    else
    {
        // IDが辞書に登録されていない場合
        Debug.LogError($"ID {data.thumbnailId} のデータがResources/Data/Cards 内に見つかりません！");
    }

    // cardObj.transform.localScale = new Vector3(5f, 5f, 5f);
    // 2. 子（テキスト用オブジェクト）を作成
    GameObject textGo = new GameObject("CardCountText");
    textGo.transform.SetParent(cardObj.transform);

    // ★重要：親が5倍なので、子は 1/5（0.2）にすると
    // 画面上ではちょうど「等倍(1)」の見た目になります。
    // もしテキストも巨大にしたいなら 1f のままでOKです。
    textGo.transform.localScale = new Vector3(1f, 1f, 1f); 

    // 3. テキストコンポーネントの設定
    TextMeshProUGUI myText = textGo.AddComponent<TextMeshProUGUI>();
    TMP_FontAsset loadedFont = Resources.Load<TMP_FontAsset>("SourceHanSansJP-Regular SDF");
    myText.font = loadedFont;

    // 固定の "x5" ではなく、読み込んだタイトルを表示する場合はこちら
    myText.text = data.title; 
    
    myText.fontSize = 30;
    myText.alignment = TextAlignmentOptions.Center;
    myText.color = Color.black;

    // 4. UIの位置とサイズ調整
    RectTransform rect = textGo.GetComponent<RectTransform>();
    rect.anchoredPosition = Vector2.zero; 
    
    // sizeDeltaが 10x10 だと文字が入り切らず消える（Maskされる）可能性があるため
    // 少し余裕を持たせたサイズにすることをおすすめします。
    rect.sizeDelta = new Vector2(10, 10); 
}
    }
}
}
