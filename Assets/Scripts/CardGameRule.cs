using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TMPro; // これを追加！
using UnityEngine.UI;
public class CardGameRule
{
    // 実際に「山札」として使うリスト
    private List<int> deckList = new List<int>();
    private List<int> trashList = new List<int>();
    private int resourcePoints = 0; // プレイヤーのリソースポイントを管理する変数
    private int resourceLevel = 0;
    // Exリソースポイントを管理する変数（必要に応じて使用）
    private int ExtraResourcePoints = 0; 

    private TextMeshProUGUI extraResourcePoint; // Exリソースポイントのクラス（必要に応じて使用）
    
    private TextMeshProUGUI ResourcePointText; // リソースポイント表示用のテキスト
    private TextMeshProUGUI LevelText;

    private TMPro.TextMeshProUGUI levelText;

    private TextMeshProUGUI LvText;

    private TextMeshProUGUI ResourceText;
    
    private GameObject LvObj;

    private GameObject fieldPanel; // フィールドのパネルを管理する変数
    private GameObject PlayerMainFieldPanel; // プレイヤーのフィールドパネルを管理する変数
    private GameObject playerDeployPanel;
    private GameObject HandPanel;

    private GameObject ScrollPanel;
    private GameObject deckObjectPanel;
    private GameObject trashAreaPanel;
    private TextMeshProUGUI deckCountText;
    private TextMeshProUGUI trashCountText;

    private GameObject shieldPanelRoot;
    private RectTransform shieldCardsContent;
    private GridLayoutGroup shieldGrid;
    private TextMeshProUGUI exBaseDisplayText;
    private readonly List<int> shieldCardIds = new List<int>();
    private readonly List<CardController> shieldControllersInDrawOrder = new List<CardController>();
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
        // GameObject DeployPanel = PlayerMainFieldPanel.CreateChildPanelCustom("PlayerDeployPanel", UIAnchor.TopCenter, 350, 250); // 配置パネルを生成
        GameObject DeployAndResourcePanel = PlayerMainFieldPanel.CreateChildPanelCustom("PlayerDeployResourcePanel", UIAnchor.TopCenter, 350, 300); // 配置パネルを生成
        playerDeployPanel = DeployAndResourcePanel.CreateChildPanelCustom("PlayerDeployPanel",UIAnchor.TopCenter, 350, 250);
        var deployGrid = playerDeployPanel.AddComponent<GridLayoutGroup>();
        deployGrid.cellSize = new Vector2(100, 100);
        deployGrid.spacing = new Vector2(10, 10);
        deployGrid.padding = new RectOffset(10, 10, 10, 10);
        deployGrid.childAlignment = TextAnchor.UpperLeft;
        deployGrid.startAxis = GridLayoutGroup.Axis.Horizontal;
        deployGrid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        GameObject ResourcePanel = DeployAndResourcePanel.CreateGridScrollView(350,50,UIAnchor.BottomCenter);
        // プレイヤー > メイン > リソースフィールド
        // GameObject ResourcePanel = PlayerMainFieldPanel.CreateChildPanelCustom("PlayerResourcePanel", UIAnchor.BottomCenter, 350, 50); // リソースパネルを生成
        // GameObject ResourcePanel = PlayerMainFieldPanel.CreateGridScrollView(350, 50,UIAnchor.TopCenter);
        // プレイヤー > メイン > リソースフィールド > レベルテキスト
        // LvText = ResourcePanel.CreateChildPanelCustom("LevelText", UIAnchor.TopLeft, 30, 30);
        LvText = ResourcePanel.GetComponent<ScrollRect>().content.gameObject.CreateChildTextCustom("LevelText",UIAnchor.TopLeft,50 ,50);
        LvText.text = "LV:0";
        LvText.color = Color.black;
        ResourceText =  ResourcePanel.GetComponent<ScrollRect>().content.gameObject.CreateChildTextCustom("ResourceText",UIAnchor.TopLeft,50 ,50);
        
        ResourceText.text = "Resource:0";
        ResourceText.color = Color.black;


        // ScrollPanel = HandPanel.CreateGridScrollView(600,400);
        //public RectTransform HandScrollContent => ScrollPanel.GetComponent<ScrollRect>().content;


       
       
        // LvObj = new GameObject("testLvText");
        // LvObj.transform.SetParent(ResourcePanel.transform, false);
        // levelText = LvObj.AddComponent<TMPro.TextMeshProUGUI>();
        // levelText.text = "1";
        
        
        // var test = LvText.AddComponent<TextMeshProUGUI>();
        // test.text = "test";
        
        // プレイヤー > メイン > シールド（EXベース表示＋シールド用カード5枚並び）
        BuildShieldPanel();
        //  プレイヤー > デッキ＆トラッシュ
        GameObject DeckAndTrashPanel = PlayerMainFieldPanel.CreateChildPanelCustom("PlayerDeckAndTrashPanel", UIAnchor.TopRight, 65, 300); // シールドパネルを生成
        CreateDeckAndTrashArea(DeckAndTrashPanel);

        // プレイヤー > ハンド
        HandPanel = fieldPanel.CreateChildPanelCustom("PlayerHandPanel", UIAnchor.BottomStretch, 0, 100); // プレイヤーのハンドパネルを生成
        // プレイヤー > ハンド　> スクロール
        ScrollPanel = HandPanel.CreateGridScrollView(600,400);
        ScrollPanel.ConfigureGridCellFromViewportHeight(0.75f, 64f);

    }
    public void CreateField(GameObject targetPanel )
    {
        
    }
    public void CreateShuffledDeck(Dictionary<int, int> cardData)
    {
        // 前回の残りを一旦クリア（念のため）
        deckList.Clear();
        trashList.Clear();

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
        UpdateDeckAndTrashTexts();
    }

    public void ResourcAndLevelTextGet(TextMeshProUGUI resourceText, TextMeshProUGUI levelText, TextMeshProUGUI extraResourceText)
    {
        ResourcePointText = resourceText;
        LevelText = levelText;
        extraResourcePoint = extraResourceText;
        if (ResourcePointText != null) ResourcePointText.color = Color.black;
        if (LevelText != null) LevelText.color = Color.black;
        if (extraResourcePoint != null) extraResourcePoint.color = Color.black;

    }

    // 一応山札をシャッフルする関数も用意しておく
    public void ShuffleDeck()
    {
        deckList = deckList.OrderBy(x => System.Guid.NewGuid()).ToList();
        Debug.Log("山札をシャッフルしました。");
    }

    /// <summary>
    /// マリガン：手札として持っていたカードIDを山札に戻し、シャッフルする（ルール上は手札を山札に戻して再構築）。
    /// </summary>
    public void ReturnCardIdsToDeckAndShuffle(IReadOnlyList<int> cardIds)
    {
        if (cardIds == null || cardIds.Count == 0)
        {
            ShuffleDeck();
            UpdateDeckAndTrashTexts();
            return;
        }

        for (int i = 0; i < cardIds.Count; i++)
        {
            deckList.Add(cardIds[i]);
        }

        ShuffleDeck();
        UpdateDeckAndTrashTexts();
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
        UpdateDeckAndTrashTexts();

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
        // LevelText.text = resourceLevel.ToString(); // レベルテキストを更新";
        LvText.text = "LV:"+resourceLevel.ToString();
        Debug.Log($"リソースレベルが{amount}増加しました。現在のレベル: {resourceLevel}");
    }

   public RectTransform PlayerFieldPanel => fieldPanel.GetComponent<RectTransform>();
   public RectTransform PlayerDeployPanel => playerDeployPanel != null ? playerDeployPanel.GetComponent<RectTransform>() : fieldPanel.GetComponent<RectTransform>();
   public RectTransform PlayerHandPanel => HandPanel.GetComponent<RectTransform>();
   public RectTransform HandScrollContent => ScrollPanel.GetComponent<ScrollRect>().content;
    public RectTransform ShieldCardsContent => shieldCardsContent;

    /// <summary>
    /// マリガン完了後：EXベース表示を更新し、山札上から指定枚数をシールドエリアに並べる（手札には加えない）。
    /// </summary>
    public void SetupShieldFromDeckAfterMulligan(GameObject cardPrefab, System.Action<CardController> onShieldCardClicked, int shieldCardCount, int exBasePoints)
    {
        shieldCardIds.Clear();
        shieldControllersInDrawOrder.Clear();
        if (shieldCardsContent == null || cardPrefab == null)
        {
            Debug.LogWarning("シールド設置: コンテナまたはカードプレハブがありません。");
            return;
        }

        for (int i = shieldCardsContent.childCount - 1; i >= 0; i--)
        {
            UnityEngine.Object.Destroy(shieldCardsContent.GetChild(i).gameObject);
        }

        SetExBaseDisplay(exBasePoints);

        for (int i = 0; i < shieldCardCount; i++)
        {
            int id = Draw();
            if (id < 0)
            {
                Debug.LogWarning("シールド設置: 山札が不足しました。");
                break;
            }
            shieldCardIds.Add(id);
            CardData data = DeckSettinObject.Instance.GetCardDataById(id);
            GameObject go = UnityEngine.Object.Instantiate(cardPrefab, shieldCardsContent);
            CardController cc = go.GetComponent<CardController>();
            if (cc != null)
            {
                cc.SetUp(data, onShieldCardClicked);
                RectTransform cardRect = go.GetComponent<RectTransform>();
                if (cardRect != null && shieldGrid != null)
                {
                    // ShieldCardsRow のサイズを変えず、カード側をセルに合わせて確実に収める
                    cardRect.localScale = Vector3.one;
                    cardRect.sizeDelta = shieldGrid.cellSize;
                }

                shieldControllersInDrawOrder.Add(cc);
                cc.SetShieldFaceHidden(true);
            }
        }
    }

    /// <summary>
    /// シールドが破壊された枚数ぶん、先頭からカードをトラッシュへ送り UI から取り除く（<see cref="Gundam2024RuleScript.DamageShield"/> と連動）。
    /// </summary>
    public void MoveTopShieldCardsToTrash(int count)
    {
        if (count <= 0)
        {
            return;
        }

        for (int i = 0; i < count; i++)
        {
            if (shieldControllersInDrawOrder.Count == 0 || shieldCardIds.Count == 0)
            {
                break;
            }

            CardController cc = shieldControllersInDrawOrder[0];
            int id = shieldCardIds[0];
            shieldControllersInDrawOrder.RemoveAt(0);
            shieldCardIds.RemoveAt(0);
            AddCardToTrash(id);
            if (cc != null)
            {
                UnityEngine.Object.Destroy(cc.gameObject);
            }
        }
    }

    public IReadOnlyList<int> GetShieldCardIds() => shieldCardIds;

    public IReadOnlyList<int> GetTrashCardIds() => trashList;

    /// <summary>トラッシュエリアクリックで一覧を開くためのリスナーを登録する。</summary>
    public void BindTrashAreaClick(Action onClick)
    {
        if (trashAreaPanel == null || onClick == null)
        {
            return;
        }

        Image bg = trashAreaPanel.GetComponent<Image>();
        if (bg == null)
        {
            bg = trashAreaPanel.AddComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, 0.04f);
        }

        bg.raycastTarget = true;

        Button btn = trashAreaPanel.GetComponent<Button>();
        if (btn == null)
        {
            btn = trashAreaPanel.AddComponent<Button>();
        }

        btn.transition = Selectable.Transition.None;
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(() => onClick());
    }

    public void SetExBaseDisplay(int points)
    {
        if (exBaseDisplayText != null)
        {
            exBaseDisplayText.text = $"EX Base:{points}";
        }
    }

    private void BuildShieldPanel()
    {
        shieldPanelRoot = PlayerMainFieldPanel.CreateChildPanelCustom("PlayerShieldPanel", UIAnchor.TopLeft,65, 300);
        exBaseDisplayText = shieldPanelRoot.CreateChildTextCustom("ExBaseText", UIAnchor.TopCenter, 65, 32);
        exBaseDisplayText.text = "EX Base:0";
        exBaseDisplayText.color = Color.black;
        exBaseDisplayText.fontSize = 20;

        // GameObject shieldRow = new GameObject("ShieldCardsRow", typeof(RectTransform));
        GameObject shieldRow = shieldPanelRoot.CreateChildPanelCustom("ShieldCardsRow", UIAnchor.BottomStretch, 65, 270);
        // shieldRow.transform.SetParent(shieldPanelRoot.transform, false);
        shieldCardsContent = shieldRow.GetComponent<RectTransform>();
        shieldCardsContent.anchorMin = new Vector2(0f, 0f);
        shieldCardsContent.anchorMax = new Vector2(1f, 0.82f);
        shieldCardsContent.pivot = new Vector2(0.5f, 0.5f);
        shieldCardsContent.offsetMin = new Vector2(6f, 8f);
        shieldCardsContent.offsetMax = new Vector2(-6f, -40f);

        shieldGrid = shieldRow.AddComponent<GridLayoutGroup>();
        shieldGrid.cellSize = new Vector2(46f, 26f);
        shieldGrid.spacing = new Vector2(0f, 2f);
        shieldGrid.padding = new RectOffset(0, 0, 0, 0);
        shieldGrid.childAlignment = TextAnchor.UpperCenter;
        shieldGrid.constraint = GridLayoutGroup.Constraint.FixedRowCount;
        shieldGrid.constraintCount = 6;
        shieldGrid.startAxis = GridLayoutGroup.Axis.Vertical; 
    }

    public void SetHandScrollRightPadding(int rightPadding)
    {
        if (ScrollPanel == null)
        {
            return;
        }

        ScrollRect scrollRect = ScrollPanel.GetComponent<ScrollRect>();
        if (scrollRect == null || scrollRect.content == null)
        {
            return;
        }

        GridLayoutGroup grid = scrollRect.content.GetComponent<GridLayoutGroup>();
        if (grid == null)
        {
            return;
        }

        int safePadding = Mathf.Max(0, rightPadding);
        grid.padding = new RectOffset(grid.padding.left, safePadding, grid.padding.top, grid.padding.bottom);
    }

    public void SetHandScrollRightMargin(float rightMargin)
    {
        if (ScrollPanel == null)
        {
            return;
        }

        RectTransform scrollRect = ScrollPanel.GetComponent<RectTransform>();
        if (scrollRect == null)
        {
            return;
        }

        float safeMargin = Mathf.Max(0f, rightMargin);
        Vector2 offsetMax = scrollRect.offsetMax;
        offsetMax.x = -safeMargin;
        scrollRect.offsetMax = offsetMax;
    }

    public float GetHandMinimumWidthForVisibleCards(int visibleCardCount)
    {
        if (visibleCardCount <= 0 || ScrollPanel == null)
        {
            return 0f;
        }

        ScrollRect scrollRect = ScrollPanel.GetComponent<ScrollRect>();
        if (scrollRect == null || scrollRect.content == null)
        {
            return 0f;
        }

        GridLayoutGroup grid = scrollRect.content.GetComponent<GridLayoutGroup>();
        if (grid == null)
        {
            return 0f;
        }

        float cellWidth = grid.cellSize.x;
        float spacingX = grid.spacing.x;
        return grid.padding.left + grid.padding.right + (cellWidth * visibleCardCount) + (spacingX * (visibleCardCount - 1));
    }

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
        ResourceText.text = $"Resource:{resourcePoints.ToString()}";
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

    public void AddCardToTrash(int cardId)
    {
        if (cardId < 0)
        {
            return;
        }

        trashList.Add(cardId);
        UpdateDeckAndTrashTexts();
    }

    /// <summary>
    /// 外部のルールエンジンで確定したレベル/リソースを、このクラスの表示値へ同期する。
    /// </summary>
    public void ApplyExternalResourceState(int level, int resource)
    {
        resourceLevel = Mathf.Max(0, level);
        resourcePoints = Mathf.Max(0, resource);

        if (LvText != null)
        {
            LvText.text = $"LV:{resourceLevel}";
            LvText.color = Color.black;
        }

        if (ResourceText != null)
        {
            ResourceText.text = $"Resource:{resourcePoints}";
            ResourceText.color = Color.black;
        }

        if (ResourcePointText != null)
        {
            ResourcePointText.text = resourcePoints.ToString();
            ResourcePointText.color = Color.black;
        }

        if (LevelText != null)
        {
            LevelText.text = $"LV:{resourceLevel}";
            LevelText.color = Color.black;
        }
    }

    // 現在の残り枚数を知りたい場合に便利
    public int GetRemainingCount() => deckList.Count;
    public int GetTrashCount() => trashList.Count;

    // リソース関数もここに追加していく予定

    private void CreateDeckAndTrashArea(GameObject deckAndTrashPanel)
    {
        // 上側: デッキ
        deckObjectPanel = deckAndTrashPanel.CreateChildPanelCustom("DeckObjectPanel", UIAnchor.TopCenter, 60, 140);
        var deckLabel = deckObjectPanel.CreateChildTextCustom("DeckLabel", UIAnchor.TopCenter, 60, 30);
        deckLabel.text = "DECK";
        deckLabel.color = Color.black;
        deckCountText = deckObjectPanel.CreateChildTextCustom("DeckCountText", UIAnchor.BottomCenter, 60, 30);
        deckCountText.text = "0";
        deckCountText.color = Color.black;

        // 下側: トラッシュ
        trashAreaPanel = deckAndTrashPanel.CreateChildPanelCustom("TrashAreaPanel", UIAnchor.BottomCenter, 60, 140);
        var trashLabel = trashAreaPanel.CreateChildTextCustom("TrashLabel", UIAnchor.TopCenter, 60, 30);
        trashLabel.text = "TRASH";
        trashLabel.color = Color.black;
        trashCountText = trashAreaPanel.CreateChildTextCustom("TrashCountText", UIAnchor.BottomCenter, 60, 30);
        trashCountText.text = "0";
        trashCountText.color = Color.black;
    }

    private void UpdateDeckAndTrashTexts()
    {
        if (deckCountText != null)
        {
            deckCountText.text = deckList.Count.ToString();
            deckCountText.color = Color.black;
        }

        if (trashCountText != null)
        {
            trashCountText.text = trashList.Count.ToString();
            trashCountText.color = Color.black;
        }
    }
}