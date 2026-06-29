using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TMPro; // これを追加！
using UnityEngine.UI;

/// <summary>トラッシュ／除外は同一 UI 位置で切り替え表示する。</summary>
public enum DiscardZoneViewMode
{
    Trash,
    Exile
}

public class CardGameRule
{
    // 実際に「山札」として使うリスト
    private List<int> deckList = new List<int>();
    private List<int> trashList = new List<int>();
    private List<int> exileList = new List<int>();
    private DiscardZoneViewMode _discardZoneViewMode = DiscardZoneViewMode.Trash;
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
    private TextMeshProUGUI ExResourceText;
    
    private GameObject LvObj;

    private GameObject fieldPanel; // フィールドのパネルを管理する変数
    private GameObject PlayerMainFieldPanel; // プレイヤーのフィールドパネルを管理する変数
    private GameObject playerDeployPanel;
    private GameObject HandPanel;

    private GameObject ScrollPanel;
    private GameObject deckObjectPanel;
    private GameObject trashAreaPanel;
    private TextMeshProUGUI deckCountText;
    private TextMeshProUGUI discardZoneLabelText;
    private TextMeshProUGUI discardZoneCountText;
    private Button discardZoneToggleButton;
    private Button discardZoneCountButton;

    private GameObject shieldPanelRoot;
    private RectTransform shieldCardsContent;
    private GridLayoutGroup shieldGrid;
    private TextMeshProUGUI exBaseDisplayText;
    private TextMeshProUGUI shieldCountDisplayText;
    private RectTransform baseSlotContent;
    private CardController deployedBase;
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
        ExResourceText = ResourcePanel.GetComponent<ScrollRect>().content.gameObject.CreateChildTextCustom("ExResourceText", UIAnchor.TopLeft, 50, 50);

        ResourceText.text = "Resource:0";
        ResourceText.color = Color.black;
        ExResourceText.text = "EX:0";
        ExResourceText.color = Color.black;
        ExResourceText.GetComponent<RectTransform>().anchoredPosition = new Vector2(110f, 0f);


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
        CreateShuffledDeck(cardData, null);
    }

    public void CreateShuffledDeck(Dictionary<int, int> cardData, int? seed)
    {
        deckList.Clear();
        trashList.Clear();
        exileList.Clear();
        _discardZoneViewMode = DiscardZoneViewMode.Trash;

        Debug.Log($"デッキの数: {cardData.Count}枚");

        foreach (var pair in cardData)
        {
            for (int i = 0; i < pair.Value; i++)
            {
                deckList.Add(pair.Key);
            }
        }

        if (seed.HasValue)
        {
            System.Random rng = new System.Random(seed.Value);
            deckList = deckList.OrderBy(_ => rng.Next()).ToList();
        }
        else
        {
            deckList = deckList.OrderBy(x => System.Guid.NewGuid()).ToList();
        }

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

    /// <summary>山札の上から最大 count 枚のカード ID を返す（山札は変更しない）。</summary>
    public List<int> PeekTopCardIds(int count)
    {
        List<int> result = new List<int>();
        if (count <= 0 || deckList == null || deckList.Count == 0)
        {
            return result;
        }

        int take = Mathf.Min(count, deckList.Count);
        for (int i = 0; i < take; i++)
        {
            result.Add(deckList[i]);
        }

        return result;
    }

    /// <summary>指定インデックスのカードを山札から取り除き ID を返す。</summary>
    public bool TryTakeCardAtDeckIndex(int index, out int cardId)
    {
        cardId = -1;
        if (deckList == null || index < 0 || index >= deckList.Count)
        {
            return false;
        }

        cardId = deckList[index];
        deckList.RemoveAt(index);
        UpdateDeckAndTrashTexts();
        return true;
    }

    /// <summary>指定 ID のカードを山札から1枚取り除く（同 ID が複数あれば先頭の1枚）。</summary>
    public bool TryTakeCardById(int cardId, out int removedAtIndex)
    {
        removedAtIndex = -1;
        if (deckList == null || cardId < 0)
        {
            return false;
        }

        for (int i = 0; i < deckList.Count; i++)
        {
            if (deckList[i] != cardId)
            {
                continue;
            }

            removedAtIndex = i;
            deckList.RemoveAt(i);
            UpdateDeckAndTrashTexts();
            return true;
        }

        return false;
    }

    /// <summary>山札の上に、リスト先頭が一番上になるよう順番どおり挿入する。</summary>
    public void PrependCardsToTopInOrder(IReadOnlyList<int> cardIdsTopFirst)
    {
        if (deckList == null || cardIdsTopFirst == null || cardIdsTopFirst.Count == 0)
        {
            return;
        }

        for (int i = cardIdsTopFirst.Count - 1; i >= 0; i--)
        {
            deckList.Insert(0, cardIdsTopFirst[i]);
        }

        UpdateDeckAndTrashTexts();
    }

    /// <summary>山札の下にカードを追加する。</summary>
    public void AppendCardsToBottom(IReadOnlyList<int> cardIds)
    {
        if (deckList == null || cardIds == null || cardIds.Count == 0)
        {
            return;
        }

        for (int i = 0; i < cardIds.Count; i++)
        {
            deckList.Add(cardIds[i]);
        }

        UpdateDeckAndTrashTexts();
    }

    public bool ContainsCardId(int cardId)
    {
        if (deckList == null || cardId < 0)
        {
            return false;
        }

        return deckList.Contains(cardId);
    }

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
    public RectTransform BaseSlotContent => baseSlotContent;
    public CardController DeployedBase => deployedBase;

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
    /// オンライン対戦：相手クライアントから受け取ったシールドカード ID でゾーンを構築する。
    /// </summary>
    public void SetupShieldFromCardIds(
        GameObject cardPrefab,
        System.Action<CardController> onShieldCardClicked,
        IReadOnlyList<int> cardIds,
        int exBasePoints)
    {
        shieldCardIds.Clear();
        shieldControllersInDrawOrder.Clear();
        if (shieldCardsContent == null || cardPrefab == null)
        {
            Debug.LogWarning("シールド設置(同期): コンテナまたはカードプレハブがありません。");
            return;
        }

        for (int i = shieldCardsContent.childCount - 1; i >= 0; i--)
        {
            UnityEngine.Object.Destroy(shieldCardsContent.GetChild(i).gameObject);
        }

        SetExBaseDisplay(exBasePoints);

        if (cardIds == null)
        {
            return;
        }

        for (int i = 0; i < cardIds.Count; i++)
        {
            int id = cardIds[i];
            if (id < 0)
            {
                continue;
            }

            TryTakeCardById(id, out _);
            shieldCardIds.Add(id);
            CardData data = DeckSettinObject.Instance.GetCardDataById(id);
            if (data == null)
            {
                Debug.LogWarning($"シールド設置(同期): 不明なカード ID {id}");
                continue;
            }

            GameObject go = UnityEngine.Object.Instantiate(cardPrefab, shieldCardsContent);
            CardController cc = go.GetComponent<CardController>();
            if (cc != null)
            {
                cc.SetUp(data, onShieldCardClicked);
                RectTransform cardRect = go.GetComponent<RectTransform>();
                if (cardRect != null && shieldGrid != null)
                {
                    cardRect.localScale = Vector3.one;
                    cardRect.sizeDelta = shieldGrid.cellSize;
                }

                shieldControllersInDrawOrder.Add(cc);
                cc.SetShieldFaceHidden(true);
            }
        }

        UpdateDeckAndTrashTexts();
    }

    /// <summary>オンライン：相手の山札残数に合わせる（シールド ID 除去後に余剰を削る）。</summary>
    public void TrimDeckToRemainingCount(int targetRemainCount)
    {
        if (targetRemainCount < 0)
        {
            targetRemainCount = 0;
        }

        while (deckList.Count > targetRemainCount)
        {
            deckList.RemoveAt(deckList.Count - 1);
        }

        UpdateDeckAndTrashTexts();
    }

    /// <summary>指定 ID のシールドをゾーンから切り離す（同期用）。</summary>
    public bool TryDetachShieldCardById(int cardId, out ShieldBreakTaken taken, bool revealFace = true)
    {
        taken = default;
        for (int i = 0; i < shieldCardIds.Count; i++)
        {
            if (shieldCardIds[i] == cardId)
            {
                return TryDetachShieldCardAtZoneIndex(i, out taken, revealFace);
            }
        }

        return false;
    }

    /// <summary>
    /// シールドが破壊された枚数ぶん、先頭からカードをトラッシュへ送る（バースト UI なしの旧経路）。
    /// </summary>
    public void MoveTopShieldCardsToTrash(int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (!TryTakeTopShieldCardForBreak(out ShieldBreakTaken taken))
            {
                break;
            }

            CommitShieldCardToTrash(taken);
        }
    }

    /// <summary>破壊される先頭シールド1枚をリストから切り離し、表面を公開する。</summary>
    public bool TryTakeTopShieldCardForBreak(out ShieldBreakTaken taken)
    {
        return TryDetachShieldCardAtZoneIndex(0, out taken, revealFace: true);
    }

    /// <summary>シールドゾーンに残っている実カード枚数（一覧のゾンビエントリを除去してから数える）。</summary>
    public int GetShieldZoneCardCount()
    {
        PruneStaleShieldZoneEntries();
        return Mathf.Min(shieldControllersInDrawOrder.Count, shieldCardIds.Count);
    }

    /// <summary>破棄・ベース昇格などでゾーンを離れたが一覧に残っているエントリを除去する。</summary>
    private void PruneStaleShieldZoneEntries()
    {
        for (int i = shieldControllersInDrawOrder.Count - 1; i >= 0; i--)
        {
            CardController cc = shieldControllersInDrawOrder[i];
            if (cc == null
                || shieldCardsContent == null
                || !cc.transform.IsChildOf(shieldCardsContent))
            {
                shieldControllersInDrawOrder.RemoveAt(i);
                if (i < shieldCardIds.Count)
                {
                    shieldCardIds.RemoveAt(i);
                }
            }
        }

        while (shieldCardIds.Count > shieldControllersInDrawOrder.Count)
        {
            shieldCardIds.RemoveAt(shieldCardIds.Count - 1);
        }

        while (shieldControllersInDrawOrder.Count > shieldCardIds.Count)
        {
            shieldControllersInDrawOrder.RemoveAt(shieldControllersInDrawOrder.Count - 1);
        }
    }

    /// <summary>ゾーン内インデックスのカード情報（制圧選択 UI 用）。</summary>
    public bool TryGetShieldZoneCardAt(int zoneIndex, out ShieldBreakTaken taken)
    {
        taken = default;
        if (zoneIndex < 0 || zoneIndex >= shieldControllersInDrawOrder.Count || zoneIndex >= shieldCardIds.Count)
        {
            return false;
        }

        CardController cc = shieldControllersInDrawOrder[zoneIndex];
        int id = shieldCardIds[zoneIndex];
        taken = new ShieldBreakTaken
        {
            Controller = cc,
            CardId = id,
            Data = cc != null && cc.Data != null ? cc.Data : DeckSettinObject.Instance.GetCardDataById(id),
        };
        return taken.Data != null;
    }

    /// <summary>ゾーン内インデックスのカードを切り離す（制圧で順番確定後）。</summary>
    public bool TryDetachShieldCardAtZoneIndex(int zoneIndex, out ShieldBreakTaken taken, bool revealFace = true)
    {
        taken = default;
        if (zoneIndex < 0 || zoneIndex >= shieldControllersInDrawOrder.Count || zoneIndex >= shieldCardIds.Count)
        {
            return false;
        }

        CardController cc = shieldControllersInDrawOrder[zoneIndex];
        int id = shieldCardIds[zoneIndex];
        shieldControllersInDrawOrder.RemoveAt(zoneIndex);
        shieldCardIds.RemoveAt(zoneIndex);

        if (revealFace && cc != null)
        {
            cc.RevealShieldFace();
        }

        taken = new ShieldBreakTaken
        {
            Controller = cc,
            CardId = id,
            Data = cc != null && cc.Data != null ? cc.Data : DeckSettinObject.Instance.GetCardDataById(id),
        };
        return true;
    }

    /// <summary>公開・バースト処理後にトラッシュへ送る。</summary>
    public void CommitShieldCardToTrash(ShieldBreakTaken taken)
    {
        if (taken.CardId > 0)
        {
            AddCardToTrash(taken.CardId);
        }

        if (taken.Controller != null)
        {
            UnityEngine.Object.Destroy(taken.Controller.gameObject);
        }
    }

    /// <summary>手札の CardController をシールドゾーン末尾に追加する（face down）。</summary>
    public bool TryAttachShieldCardFromHand(CardController cc)
    {
        if (cc == null || cc.Data == null || shieldCardsContent == null)
        {
            return false;
        }

        if (cc.Data.IsUnitLike() || cc.Data.type == Type.Pilot || cc.Data.type == Type.Base)
        {
            Debug.LogWarning(
                $"[ShieldDeploy] ユニット/パイロット/ベースはシールドゾーンへ配備できません: {cc.Data.cardName}(type:{cc.Data.type})");
            return false;
        }

        shieldCardIds.Add(cc.Data.id);
        shieldControllersInDrawOrder.Add(cc);
        cc.transform.SetParent(shieldCardsContent, false);
        RectTransform cardRect = cc.GetComponent<RectTransform>();
        if (cardRect != null && shieldGrid != null)
        {
            cardRect.localScale = Vector3.one;
            cardRect.sizeDelta = shieldGrid.cellSize;
        }

        cc.SetShieldFaceHidden(true);
        cc.SetEligibleForShieldZoneDeploy(false);
        return true;
    }

    public bool HasShieldCardInZone => GetShieldZoneCardCount() > 0;

    /// <summary>シールドゾーン登録から外す（ベース配備などでゾーンを離れるとき）。</summary>
    public bool TryUnregisterShieldZoneCard(CardController cc)
    {
        if (cc == null)
        {
            return false;
        }

        int index = shieldControllersInDrawOrder.IndexOf(cc);
        if (index < 0 || index >= shieldCardIds.Count)
        {
            return false;
        }

        shieldControllersInDrawOrder.RemoveAt(index);
        shieldCardIds.RemoveAt(index);
        return true;
    }

    /// <summary>ベース枠にカードが残っているか（参照切れ対策）。</summary>
    public bool HasOccupantInBaseSlot()
    {
        if (baseSlotContent == null)
        {
            return deployedBase != null;
        }

        for (int i = 0; i < baseSlotContent.childCount; i++)
        {
            if (baseSlotContent.GetChild(i).GetComponent<CardController>() != null)
            {
                return true;
            }
        }

        return deployedBase != null;
    }

    /// <summary>シールドゾーン先頭1枚を手札へ移す（破壊・バースト UI なし）。</summary>
    public bool TryMoveTopShieldCardToHand(RectTransform handContent, out CardController movedCard)
    {
        movedCard = null;
        if (handContent == null || !HasShieldCardInZone)
        {
            return false;
        }

        CardController cc = shieldControllersInDrawOrder[0];
        shieldControllersInDrawOrder.RemoveAt(0);
        shieldCardIds.RemoveAt(0);
        if (cc == null)
        {
            return false;
        }

        cc.RevealShieldFace();
        cc.transform.SetParent(handContent, false);
        RectTransform cardRect = cc.GetComponent<RectTransform>();
        if (cardRect != null)
        {
            cardRect.localScale = Vector3.one;
        }

        movedCard = cc;
        return true;
    }

    public IReadOnlyList<int> GetShieldCardIds() => shieldCardIds;

    public IReadOnlyList<int> GetTrashCardIds() => trashList;

    public IReadOnlyList<int> GetExileCardIds() => exileList;

    public DiscardZoneViewMode DiscardZoneViewMode => _discardZoneViewMode;

    public DiscardZoneViewMode ToggleDiscardZoneView()
    {
        _discardZoneViewMode = _discardZoneViewMode == DiscardZoneViewMode.Trash
            ? DiscardZoneViewMode.Exile
            : DiscardZoneViewMode.Trash;
        UpdateDeckAndDiscardZoneTexts();
        return _discardZoneViewMode;
    }

    /// <summary>トラッシュ／除外ラベル押下（TRASH↔EXILE 切替のみ）。</summary>
    public void BindDiscardZoneToggleClick(Action onClick)
    {
        if (discardZoneToggleButton == null || onClick == null)
        {
            return;
        }

        discardZoneToggleButton.onClick.RemoveAllListeners();
        discardZoneToggleButton.onClick.AddListener(() => onClick());
    }

    /// <summary>トラッシュ／除外枚数押下（現在モードの一覧表示）。</summary>
    public void BindDiscardZoneCountClick(Action onClick)
    {
        if (discardZoneCountButton == null || onClick == null)
        {
            return;
        }

        discardZoneCountButton.onClick.RemoveAllListeners();
        discardZoneCountButton.onClick.AddListener(() => onClick());
    }

    /// <summary>互換エイリアス。<see cref="BindDiscardZoneCountClick"/> を使用してください。</summary>
    public void BindTrashAreaClick(Action onClick) => BindDiscardZoneCountClick(onClick);

    public void SetExBaseDisplay(int points)
    {
        if (exBaseDisplayText == null)
        {
            return;
        }

        if (deployedBase != null)
        {
            return;
        }

        exBaseDisplayText.gameObject.SetActive(true);
        exBaseDisplayText.text = $"EX Base:{points}";
    }

    public void SetDeployedBaseHeader(string text)
    {
        if (exBaseDisplayText == null)
        {
            return;
        }

        exBaseDisplayText.gameObject.SetActive(true);
        exBaseDisplayText.text = text;
    }

    /// <summary>ルール上の残りシールド枚数をシールドゾーン付近に表示する。</summary>
    public void SetShieldCountDisplay(int count)
    {
        if (shieldCountDisplayText == null)
        {
            return;
        }

        shieldCountDisplayText.gameObject.SetActive(true);
        shieldCountDisplayText.text = $"シールド:{Mathf.Max(0, count)}";
    }

    /// <summary>EX ベース枠にベースカードを配置する。旧ベースは呼び出し側でトラッシュすること。</summary>
    public void AttachDeployedBaseCard(CardController baseCard)
    {
        deployedBase = baseCard;
        if (baseCard == null || baseSlotContent == null)
        {
            return;
        }

        baseCard.transform.SetParent(baseSlotContent, false);
        RectTransform rt = baseCard.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(58f, 82f);
            rt.localScale = Vector3.one;
        }

        if (exBaseDisplayText != null)
        {
            exBaseDisplayText.gameObject.SetActive(false);
        }
    }

    public void ClearDeployedBaseCard()
    {
        deployedBase = null;
        if (exBaseDisplayText != null)
        {
            exBaseDisplayText.gameObject.SetActive(true);
        }
    }

    private void BuildShieldPanel()
    {
        shieldPanelRoot = PlayerMainFieldPanel.CreateChildPanelCustom("PlayerShieldPanel", UIAnchor.TopLeft,65, 300);
        exBaseDisplayText = shieldPanelRoot.CreateChildTextCustom("ExBaseText", UIAnchor.TopCenter, 65, 32);
        exBaseDisplayText.text = "EX Base:0";
        exBaseDisplayText.color = Color.black;
        exBaseDisplayText.fontSize = 20;

        GameObject baseSlot = shieldPanelRoot.CreateChildPanelCustom("BaseSlot", UIAnchor.TopCenter, 65, 86);
        baseSlotContent = baseSlot.GetComponent<RectTransform>();
        baseSlotContent.anchorMin = new Vector2(0f, 1f);
        baseSlotContent.anchorMax = new Vector2(1f, 1f);
        baseSlotContent.pivot = new Vector2(0.5f, 1f);
        baseSlotContent.offsetMin = new Vector2(4f, -86f);
        baseSlotContent.offsetMax = new Vector2(-4f, -2f);

        shieldCountDisplayText = shieldPanelRoot.CreateChildTextCustom("ShieldCountText", UIAnchor.TopCenter, 62, 20);
        shieldCountDisplayText.text = "シールド:0";
        shieldCountDisplayText.color = Color.black;
        shieldCountDisplayText.fontSize = 16;
        shieldCountDisplayText.alignment = TextAlignmentOptions.Center;
        RectTransform shieldCountRt = shieldCountDisplayText.GetComponent<RectTransform>();
        shieldCountRt.anchoredPosition = new Vector2(0f, -90f);

        GameObject shieldRow = shieldPanelRoot.CreateChildPanelCustom("ShieldCardsRow", UIAnchor.BottomStretch, 65, 270);
        shieldCardsContent = shieldRow.GetComponent<RectTransform>();
        shieldCardsContent.anchorMin = new Vector2(0f, 0f);
        shieldCardsContent.anchorMax = new Vector2(1f, 0.82f);
        shieldCardsContent.pivot = new Vector2(0.5f, 0.5f);
        shieldCardsContent.offsetMin = new Vector2(6f, 8f);
        shieldCardsContent.offsetMax = new Vector2(-6f, -58f);

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
        OnCardAddedToTrash?.Invoke(cardId);
    }

    /// <summary>トラッシュから指定 ID のカードを1枚除去（同 ID が複数あれば先頭1枚）。</summary>
    public bool TryRemoveCardFromTrash(int cardId, out int removedCardId)
    {
        removedCardId = -1;
        if (cardId < 0)
        {
            return false;
        }

        int index = trashList.IndexOf(cardId);
        if (index < 0)
        {
            return false;
        }

        removedCardId = trashList[index];
        trashList.RemoveAt(index);
        UpdateDeckAndTrashTexts();
        return true;
    }

    /// <summary>トラッシュの指定位置のカードを1枚除去。</summary>
    public bool TryRemoveCardFromTrashAt(int index, out int removedCardId)
    {
        removedCardId = -1;
        if (index < 0 || index >= trashList.Count)
        {
            return false;
        }

        removedCardId = trashList[index];
        trashList.RemoveAt(index);
        UpdateDeckAndTrashTexts();
        return true;
    }

    public void AddCardToExile(int cardId)
    {
        if (cardId < 0)
        {
            return;
        }

        exileList.Add(cardId);
        UpdateDeckAndTrashTexts();
        OnCardAddedToExile?.Invoke(cardId);
    }

    /// <summary>カードがトラッシュに追加されたとき（cardId）。プレイヤー側 AI 観測用。</summary>
    public event Action<int> OnCardAddedToTrash;

    /// <summary>カードが除外ゾーンに追加されたとき（cardId）。</summary>
    public event Action<int> OnCardAddedToExile;

    /// <summary>
    /// 外部のルールエンジンで確定したレベル/リソースを、このクラスの表示値へ同期する。
    /// </summary>
    public void ApplyExternalResourceState(int level, int resource, int exResource)
    {
        resourceLevel = Mathf.Max(0, level);
        resourcePoints = Mathf.Max(0, resource);
        ExtraResourcePoints = Mathf.Max(0, exResource);

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

        if (ExResourceText != null)
        {
            ExResourceText.text = $"EX:{ExtraResourcePoints}";
            ExResourceText.color = Color.black;
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

        if (extraResourcePoint != null)
        {
            extraResourcePoint.text = ExtraResourcePoints.ToString();
            extraResourcePoint.color = Color.black;
        }
    }

    // 現在の残り枚数を知りたい場合に便利
    public int GetRemainingCount() => deckList.Count;
    public int GetTrashCount() => trashList.Count;

    public int GetExileCount() => exileList.Count;

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

        // 下側: トラッシュ／除外（DECK と同じく上ラベル・下枚数）
        trashAreaPanel = deckAndTrashPanel.CreateChildPanelCustom("DiscardZonePanel", UIAnchor.BottomCenter, 60, 140);
        discardZoneLabelText = trashAreaPanel.CreateChildTextCustom("DiscardZoneLabel", UIAnchor.TopCenter, 60, 30);
        discardZoneLabelText.text = "TRASH";
        discardZoneLabelText.color = Color.black;
        discardZoneLabelText.raycastTarget = true;
        discardZoneToggleButton = discardZoneLabelText.gameObject.GetComponent<Button>();
        if (discardZoneToggleButton == null)
        {
            discardZoneToggleButton = discardZoneLabelText.gameObject.AddComponent<Button>();
        }

        discardZoneToggleButton.targetGraphic = discardZoneLabelText;
        ApplyTextButtonColors(discardZoneToggleButton);

        discardZoneCountText = trashAreaPanel.CreateChildTextCustom("DiscardZoneCountText", UIAnchor.BottomCenter, 60, 30);
        discardZoneCountText.text = "0";
        discardZoneCountText.color = Color.black;
        discardZoneCountText.raycastTarget = true;
        discardZoneCountButton = discardZoneCountText.gameObject.GetComponent<Button>();
        if (discardZoneCountButton == null)
        {
            discardZoneCountButton = discardZoneCountText.gameObject.AddComponent<Button>();
        }

        discardZoneCountButton.targetGraphic = discardZoneCountText;
        ApplyTextButtonColors(discardZoneCountButton);
        UpdateDeckAndDiscardZoneTexts();
    }

    private static void ApplyTextButtonColors(Button button)
    {
        if (button == null)
        {
            return;
        }

        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.85f, 0.9f, 1f, 1f);
        colors.pressedColor = new Color(0.75f, 0.82f, 0.95f, 1f);
        colors.selectedColor = colors.highlightedColor;
        button.colors = colors;
    }

    private void UpdateDeckAndDiscardZoneTexts()
    {
        bool showingExile = _discardZoneViewMode == DiscardZoneViewMode.Exile;
        int count = showingExile ? exileList.Count : trashList.Count;

        if (discardZoneLabelText != null)
        {
            discardZoneLabelText.text = showingExile ? "EXILE" : "TRASH";
            discardZoneLabelText.color = Color.black;
        }

        if (discardZoneCountText != null)
        {
            discardZoneCountText.text = count.ToString();
            discardZoneCountText.color = Color.black;
        }
    }

    private void UpdateDeckAndTrashTexts()
    {
        if (deckCountText != null)
        {
            deckCountText.text = deckList.Count.ToString();
            deckCountText.color = Color.black;
        }

        UpdateDeckAndDiscardZoneTexts();
    }
}