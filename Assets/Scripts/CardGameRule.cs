using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using TMPro; // これを追加！
using UnityEngine.UI;
public class CardGameRule
{
    // 実際に「山札」として使うリスト
    private List<int> deckList = new List<int>();
    private int resourcePoints = 0; // プレイヤーのリソースポイントを管理する変数
    private int resourceLevel = 0;
    // Exリソースポイントを管理する変数（必要に応じて使用）
    private int ExtraResourcePoints = 0; 

    private TextMeshProUGUI extraResourcePoint; // Exリソースポイントのクラス（必要に応じて使用）
    
    private TextMeshProUGUI ResourcePointText; // リソースポイント表示用のテキスト
    private TextMeshProUGUI LevelText;

    private GameObject fieldPanel; // フィールドのパネルを管理する変数
    private GameObject PlayerMainFieldPanel; // プレイヤーのフィールドパネルを管理する変数
    private GameObject HandPanel;

    private GameObject ScrollPanel;
    /// <summary>
    /// デッキデータを元に、シャッフルされた山札を作成する
    /// </summary>
    /// 
    private void Awake()
    {
        // デッキの初期化やリソースポイントの初期化など、必要なセットアップをここで行うことができます。
        // 例えば、ゲーム開始時にリソースポイントを0に設定するなど。
        // resourcePoints = 0;
        // resourceLevel = 0;
        // ExtraResourcePoints = 0;
        // !フィールドなどを生成する処理

    }
    public void SetUp(GameObject getfieldPanel)
    {
        this.fieldPanel = getfieldPanel;
        // プレイヤー > メイン 
        PlayerMainFieldPanel = fieldPanel.CreateChildPanelTop("PlayerMainField", 300); // プレイヤーのフィールドパネルを生成
        // プレイヤー > メイン > バトルフィールド
        GameObject DeployPanel = PlayerMainFieldPanel.CreateChildPanelCustom("PlayerDeployPanel", UIAnchor.TopCenter, 350, 250); // 配置パネルを生成
        // プレイヤー > メイン > リソースフィールド
        GameObject ResourcePanel = PlayerMainFieldPanel.CreateChildPanelCustom("PlayerResourcePanel", UIAnchor.BottomCenter, 350, 50); // リソースパネルを生成
        // プレイヤー > メイン > シールド
        GameObject ShieldPanel = PlayerMainFieldPanel.CreateChildPanelCustom("PlayerShieldPanel", UIAnchor.TopLeft, 65, 300); // シールドパネルを生成
        //  プレイヤー > デッキ＆トラッシュ
        GameObject DeckAndTrashPanel = PlayerMainFieldPanel.CreateChildPanelCustom("PlayerDeckAndTrashPanel", UIAnchor.TopRight, 65, 300); // シールドパネルを生成

        // プレイヤー > ハンド
         HandPanel = fieldPanel.CreateChildPanelCustom("PlayerHandPanel", UIAnchor.BottomStretch, 0, 100); // プレイヤーのハンドパネルを生成
        // プレイヤー > ハンド　> スクロール
        ScrollPanel = HandPanel.CreateGridScrollView(600,400);

    }
    public void CreateField(GameObject targetPanel )
    {
        
    }
    public void CreateShuffledDeck(Dictionary<int, int> cardData)
    {
        // 前回の残りを一旦クリア（念のため）
        deckList.Clear();

        Debug.Log($"デッキの数: {cardData.Count}枚");

        // 1. データを展開する（IDを枚数分リストに追加）
        foreach (var pair in cardData)
        {
            for (int i = 0; i < pair.Value; i++)
            {
                Debug.Log($"カードID {pair.Key} を追加");
                deckList.Add(pair.Key);
            }
        }

        // 2. シャッフルして、自分自身（deckList）を書き換える
        // OrderByでランダムに並べ替えたものをリストにして上書きします
        deckList = deckList.OrderBy(x => System.Guid.NewGuid()).ToList();

        Debug.Log($"山札を生成しました。枚数: {deckList.Count}");
    }

    public void ResourcAndLevelTextGet(TextMeshProUGUI resourceText, TextMeshProUGUI levelText, TextMeshProUGUI extraResourceText)
    {
        ResourcePointText = resourceText;
        LevelText = levelText;
        extraResourcePoint = extraResourceText;

    }

    // 一応山札をシャッフルする関数も用意しておく
    public void ShuffleDeck()
    {
        deckList = deckList.OrderBy(x => System.Guid.NewGuid()).ToList();
        Debug.Log("山札をシャッフルしました。");
    }

    // デッキの内容を返す
    public List<int> GetDeckList() => deckList;
    /// <summary>
    /// 山札の一番上からカードを1枚引く
    /// </summary>
    public int Draw()
    {
        if (deckList.Count == 0)
        {
            Debug.LogWarning("山札が空です！");
            return -1; // デッキ切れの合図
        }

        // 一番上のカードを取得して、リストから消す
        int topCardId = deckList[0];
        deckList.RemoveAt(0); 

        return topCardId;
    }
    public void StartTurn()
    {
        // ターン開始時の処理をここに書きます。
        // 例えば、リソースポイントのリセットやカードのドローなど。
        // RefreshResourcePoints(); // ターン開始時にリソースポイントをリセット
        int drawnCardId = Draw(); // ターン開始時にカードを1枚引く（必要に応じて枚数を増やすこともできます）
        CardData drawnCardData = DeckSettinObject.Instance.GetCardDataById(drawnCardId);
        Debug.Log($"ターン開始！引いたカードID: {drawnCardId}, カード名: {drawnCardData.cardName}");
    }

    // リソースポイントを増やす関数
    // デフォルトでは1ポイント増やすようにしていますが、引数で任意の値を指定できます。
    public void AddResourcePoints(int amount=1)
    {
        resourceLevel += amount;
        LevelText.text = resourceLevel.ToString(); // レベルテキストを更新";
        Debug.Log($"リソースレベルが{amount}増加しました。現在のレベル: {resourceLevel}");
    }

   public RectTransform PlayerFieldPanel => fieldPanel.GetComponent<RectTransform>();
//    public RectTransform PlayerHandPanel => HandPanel.GetComponent<RectTransform>();
   public RectTransform HandScrollContent => ScrollPanel.GetComponent<ScrollRect>().content;

    public void AddExtraResourcePoints(int amount)
    {
        ExtraResourcePoints += amount;
        extraResourcePoint.text = ExtraResourcePoints.ToString(); // Exリソースポイントテキストを更新
        // Exポイントの増加に応じてリソースレベルも増加させる
        AddResourcePoints(amount); 
        Debug.Log($"Exリソースポイントが{amount}増加しました。現在のExポイント: {ExtraResourcePoints}");
    }

    // リソースレベルを取得する関数
    public int GetResourcePoints()
    {
        return resourceLevel;
    }

    // リソースレベルを代入してリセットする。デフォではレベルに応じたポイントをリセットするようにしています。
    // シンアスカやスレッタの効果の場合は引数に1を入れて呼び出す予定。
    public void RefreshResourcePoints()
    {
        resourcePoints = resourceLevel; // レベルに応じたポイントをリセット
        ResourcePointText.text = resourcePoints.ToString(); // リソースポイントテキストを更新
        Debug.Log("リソースポイントがリセットされました。");
    }

    public bool UseResourcePoints(int amount)
    {
        if (amount > resourcePoints)
        {
            Debug.LogWarning($"リソースポイントが足りません！カードのコスト: {amount}現在のポイント: {resourcePoints}");
            return false; // 使用失敗
        }

        // resourcePoints -= amount;
        // ResourcePointText.text = resourcePoints.ToString(); // リソースポイントテキストを更新
        // Debug.Log($"{amount}ポイント使用しました。残りのポイント: {resourcePoints}");
        return true;
    }

    public void UseResourcePointsWithoutCheck(int amount)
    {
        resourcePoints -= amount;
        ResourcePointText.text = resourcePoints.ToString(); // リソースポイントテキストを更新
        Debug.Log($"{amount}ポイント使用しました。残りのポイント: {resourcePoints}");
    }

    public int returnResourcePoints() => resourcePoints;

    // 現在の残り枚数を知りたい場合に便利
    public int GetRemainingCount() => deckList.Count;

    // リソース関数もここに追加していく予定
    
}