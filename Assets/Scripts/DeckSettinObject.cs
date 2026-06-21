using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.Linq;
using TMPro;
using System.IO;
using System;
using System.Threading.Tasks;

public class DeckSettinObject : MonoBehaviour
{
    public static DeckSettinObject Instance;
    public bool isDeckEditing;
    private Dictionary<int, int> cardData = new Dictionary<int, int>();
    [SerializeField] private GameObject DeckEditNowpanel;
    // テキストフィールド
    [SerializeField] private TMP_InputField DeckTitleInputField;

    [SerializeField] private Canvas MainCanvas;


    [SerializeField] private TMP_Text NewDeckText;

    // エネミーデッキのカードデータを保存するためのフィールド
    private Dictionary<int, int> enemyCardData = new Dictionary<int, int>();
    

    // 編集中のデッキの文字列
    public string deckPathName;
    // ?デッキデータプレハブ
    [SerializeField] private GameObject DeckDataPrefab;
    public GameObject DeckImagePrefab;  
    [SerializeField] private TextMeshProUGUI CardCountText;
    [SerializeField] private GameObject DeckListPanel;
    [SerializeField] private GameObject DeckinfoPanel;
    // バトルキャンバス
    [SerializeField] private Canvas BattleCanvas;
    // !デッキデータを保存するクラス
    // private DeckSaveData data = JsonUtility.FromJson<DeckSaveData>(json);

    // !カードIDからカードデータを取得するためのテーブル（辞書）
    // private Dictionary<int, CardData> cardTable = Resources.LoadAll<CardData>("Data/Cards").ToDictionary(data => data.id);
    

    // バトルボタン押下時に、他のデッキをエネミーデッキに入れるためのフラグ。
    // デッキ一覧内のデッキを押下した際に、このフラグが立っている場合は、押下されたデッキをエネミーデッキに入れる。そうでない場合は、通常通り編集デッキに入れる。
    private bool BattoleStartFlag = false;

    public void CopyJsonFile()
    {
        deckPathName = "";
        SaveDeckToJson(cardData);
        // デッキリストを空にする
        
    }

    void Awake()
    {
        Instance = this;
        EnsurePlayerAuthServiceExists();
        StartCoroutine(InitializeDeckStorageAndShowListCoroutine());
    }

    private void EnsurePlayerAuthServiceExists()
    {
        if (PlayerAuthService.Instance != null)
        {
            return;
        }

        GameObject go = new GameObject("PlayerAuthService");
        go.AddComponent<PlayerAuthService>();
    }

    private IEnumerator InitializeDeckStorageAndShowListCoroutine()
    {
        Task initTask = PlayerAuthService.Instance.InitializeAsync();
        while (!initTask.IsCompleted)
        {
            yield return null;
        }

        yield return ShowFileListCoroutine();
    }

    public void RefreshDeckListFromStorage()
    {
        StartCoroutine(ShowFileListCoroutine());
    }

    public void OnGuestModeActivated()
    {
        deckPathName = string.Empty;
        ClearDeckList();
        RefreshDeckListFromStorage();
    }

    public void OnCloudStorageActivated()
    {
        if (!string.IsNullOrEmpty(deckPathName)
            && !CloudDeckStorageProvider.IsValidCloudKey(CloudDeckStorageProvider.ToCloudKey(deckPathName)))
        {
            deckPathName = string.Empty;
        }

        RefreshDeckListFromStorage();
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
        Debug.Log($"デッキデータを返します。カードの種類数: {cardData.Count}");
        return cardData;
    }

    // 敵のデッキデータを返す関数
    public Dictionary<int, int> LoadEnemyDeckReturn()
    {
        return enemyCardData;
    }
    // デッキパネル内のカードを保存（ゲスト=ローカル JSON / ログイン=Cloud Save）
    public void SaveDeckToJson(Dictionary<int, int> cardData)
    {
        StartCoroutine(SaveDeckCoroutine(cardData));
    }

    private IEnumerator SaveDeckCoroutine(Dictionary<int, int> sourceCardData)
    {
        string title = DeckTitleInputField != null ? DeckTitleInputField.text : string.Empty;
        DeckSaveData saveData = DeckStorageService.BuildSaveData(title, sourceCardData);
        string storageKey = DeckStorageService.PrepareStorageKeyForSave(deckPathName);

        Task saveTask = DeckStorageService.SaveDeckAsync(storageKey, saveData);
        while (!saveTask.IsCompleted)
        {
            yield return null;
        }

        if (saveTask.IsFaulted)
        {
            Debug.LogError($"[Deck] Save failed: {DeckStorageService.FormatStorageError(saveTask.Exception)}");
            yield break;
        }

        deckPathName = storageKey;
        string mode = DeckStorageService.IsUsingCloudStorage ? "Cloud" : "Local";
        Debug.Log($"[Deck] Save complete ({mode}): {storageKey}");
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

    public void ShowAllCanvasChildren(GameObject canvasObj)
    {
        canvasObj.gameObject.SetActive(true);
        return;
        foreach (Transform child in canvasObj.transform)
       {
        child.gameObject.SetActive(true);
       }
    }


    public void HideAllCanvasChildren(GameObject canvasObj)
    {
        canvasObj.gameObject.SetActive(false);
        return;
    // canvasObjの子要素（Transform）を一つずつループ
       foreach (Transform child in canvasObj.transform)
      {
        child.gameObject.SetActive(false);
      }
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

// 全ての保存されたデッキの一覧を取得する
public List<string> GetSaveFileNames()
{
        List<string> fileList = new List<string>();
        Task<List<DeckStorageEntry>> listTask = DeckStorageService.ListDecksAsync();
        listTask.Wait();
        List<DeckStorageEntry> entries = listTask.Result;
        for (int i = 0; i < entries.Count; i++)
        {
            fileList.Add(entries[i].StorageKey);
        }

        return fileList;
    }

    private DeckSaveData LoadDeckSaveData(string storageKey)
    {
        Task<DeckSaveData> loadTask = DeckStorageService.LoadDeckAsync(storageKey);
        loadTask.Wait();
        return loadTask.Result;
    }

public void battleStart()
{
    // 現在の値が true なら false、false なら true に入れ替える
    BattoleStartFlag = !BattoleStartFlag;
    
    Debug.Log($"バトル開始フラグ:{BattoleStartFlag}");
}

    public bool IsBattleCanvasVisible()
    {
        return BattleCanvas != null && BattleCanvas.gameObject.activeSelf;
    }

    private BattleGameMain ResolveBattleMain()
    {
        if (BattleCanvas == null)
        {
            return UnityEngine.Object.FindObjectOfType<BattleGameMain>();
        }

        BattleGameMain battle = BattleCanvas.GetComponentInChildren<BattleGameMain>(true);
        return battle != null ? battle : UnityEngine.Object.FindObjectOfType<BattleGameMain>();
    }

    /// <summary>バトル画面を表示し、前回の対戦状態を破棄して最初からセットアップする。</summary>
    public void EnterBattleFromMenu()
    {
        if (MainCanvas != null)
        {
            HideAllCanvasChildren(MainCanvas.gameObject);
        }

        if (BattleCanvas != null)
        {
            ShowAllCanvasChildren(BattleCanvas.gameObject);
        }

        BattleGameMain battle = ResolveBattleMain();
        if (battle != null)
        {
            battle.RestartBattleFromBeginning();
        }
        else
        {
            Debug.LogWarning("[Battle] BattleGameMain が見つからないため、新規セットアップをスキップしました。");
        }
    }

    /// <summary>バトルキャンバスを閉じ、デッキ編集などのメイン UI を表示する。</summary>
    public void ReturnToMainMenuFromBattle()
    {
        BattoleStartFlag = false;
        BattleGameMain battle = ResolveBattleMain();
        if (battle != null)
        {
            battle.TeardownBattleSessionForMainMenu();
        }

        if (BattleCanvas != null)
        {
            HideAllCanvasChildren(BattleCanvas.gameObject);
        }

        if (MainCanvas != null)
        {
            ShowAllCanvasChildren(MainCanvas.gameObject);
        }

        Debug.Log("[Battle] Returned to main menu (canvas switch).");
    }


public CardData GetCardDataById(int id)
{
    CardFeatureRegistry.EnsureLoaded();
    var cardTable = Resources.LoadAll<CardData>("Data/Cards").ToDictionary(data => data.id);
    if (cardTable.TryGetValue(id, out CardData card))
    {
        Debug.Log($"ID:{id} のカードデータを取得しました。カード名: {card.cardName}");
        return card;
    }
    else
    {
        Debug.LogError($"ID {id} のカードデータが見つかりません！");
        return null;
    }
}
public void DeleteJsonFile()
{
        if (string.IsNullOrEmpty(deckPathName))
        {
            Debug.LogWarning("[Deck] 削除対象のデッキが選択されていません。");
            return;
        }

        StartCoroutine(DeleteDeckCoroutine(deckPathName));
    }

    private IEnumerator DeleteDeckCoroutine(string storageKey)
    {
        Task deleteTask = DeckStorageService.DeleteDeckAsync(storageKey);
        while (!deleteTask.IsCompleted)
        {
            yield return null;
        }

        if (deleteTask.IsFaulted)
        {
            Debug.LogError($"[Deck] 削除失敗: {deleteTask.Exception?.GetBaseException().Message}");
            yield break;
        }

        Debug.Log($"[Deck] 削除完了: {storageKey}");
        deckPathName = string.Empty;
        ClearDeckList();
        yield return ShowFileListCoroutine();
    }

// 保存されたデッキ一覧を表示する
public void ShowFileList()
{
        StartCoroutine(ShowFileListCoroutine());
    }

    private IEnumerator ShowFileListCoroutine()
{
    if (DeckListPanel != null)
    {
        DeckListPanel.transform.DetachChildren();
    }

    Task<List<DeckStorageEntry>> listTask = DeckStorageService.ListDecksAsync();
    while (!listTask.IsCompleted)
    {
        yield return null;
    }

    if (listTask.IsFaulted)
    {
        Debug.LogError($"[Deck] 一覧取得失敗: {listTask.Exception?.GetBaseException().Message}");
        yield break;
    }

    List<DeckStorageEntry> entries = listTask.Result;
    Debug.Log($"保存されたデッキ数: {entries.Count} ({(DeckStorageService.IsUsingCloudStorage ? "Cloud" : "Local")})");

    for (int i = 0; i < entries.Count; i++)
    {
        DeckStorageEntry entry = entries[i];
        string storageKey = entry.StorageKey;
        string captureKey = storageKey;

        Task<DeckSaveData> loadTask = DeckStorageService.LoadDeckAsync(storageKey);
        while (!loadTask.IsCompleted)
        {
            yield return null;
        }

        if (loadTask.IsFaulted)
        {
            Debug.LogWarning($"[Deck] 読込スキップ {storageKey}: {loadTask.Exception?.GetBaseException().Message}");
            continue;
        }

        DeckSaveData data = loadTask.Result;
        if (data == null)
        {
            continue;
        }

    Sprite cardSprite = Resources.Load<Sprite>($"Data/Cards/{data.thumbnailId}");
  
    var cardTable = Resources.LoadAll<CardData>("Data/Cards").ToDictionary(c => c.id);
    Debug.Log($"カードテーブルの長さ: {cardTable.Count}");

    GameObject cardObj = Instantiate(DeckDataPrefab, DeckListPanel.transform);
    Image targetImg = cardObj.GetComponent<Image>();
    if (cardSprite != null)
    {
        targetImg.sprite = cardSprite;
    }

    Button btn = cardObj.GetComponentInChildren<Button>();
    if (btn != null)
    {
    btn.onClick.AddListener(() => {
        Debug.Log(cardObj.name + " がクリックされました！");
        deckPathName = captureKey;
        Debug.Log($"エネミーフラグ:{BattoleStartFlag}");
        if(BattoleStartFlag)
        {
            Debug.Log("バトル開始フラグが立っているため、クリックされたデッキをエネミーデッキに入れます。");
            enemyCardData.Clear();
            foreach (var card in data.cards)
            {
                Debug.Log($"エネミーデッキに入れるカードID: {card.id}, 枚数: {card.count}");
                enemyCardData[card.id] = card.count;
            }
            EnterBattleFromMenu();
            return;
        }
        cardData.Clear();
        DeckinfoPanel.SetActive(true);
        DeckTitleInputField.text = data.title;
        foreach (var card in data.cards)
        {
            Debug.Log($"クリックされたデッキのカードID: {card.id}, 枚数: {card.count}");
            cardData[card.id] = card.count;
        }
    });
    }

    if (cardTable.TryGetValue(data.thumbnailId, out CardData card))
    {
        string targetImageName = card.imageName.name;
        Debug.Log($"ID:{data.thumbnailId} の画像名は {targetImageName} です");
        Sprite sp = Resources.Load<Sprite>($"Data/Images/{targetImageName}");
        cardObj.GetComponent<Image>().sprite = sp;
    }
    else
    {
        Debug.LogError($"ID {data.thumbnailId} のデータがResources/Data/Cards 内に見つかりません！");
    }

    GameObject textGo = new GameObject("CardCountText");
    textGo.transform.SetParent(cardObj.transform);
    textGo.transform.localScale = new Vector3(1f, 1f, 1f); 

    TextMeshProUGUI myText = textGo.AddComponent<TextMeshProUGUI>();
    TMP_FontAsset loadedFont = Resources.Load<TMP_FontAsset>("SourceHanSansJP-Regular SDF");
    myText.font = loadedFont;
    myText.text = string.IsNullOrEmpty(data.title) ? entry.DisplayName : data.title; 
    myText.fontSize = 30;
    myText.alignment = TextAlignmentOptions.Center;
    myText.color = Color.black;

    RectTransform rect = textGo.GetComponent<RectTransform>();
    rect.anchoredPosition = Vector2.zero; 
    rect.sizeDelta = new Vector2(10, 10); 
    }
}
}
