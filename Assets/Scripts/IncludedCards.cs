using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class IncludedCards : MonoBehaviour
{
    [SerializeField] private Toggle togglePrefab;
    [SerializeField] private ToggleGroup toggleGroup;
    [SerializeField] private Transform toggleParent;
    [SerializeField] private string cardSetResourcePath = "Data/CardSetData";
    [SerializeField] private GameObject includeSectionTextPrefab;

    void Awake()
    {
        // ここでカードデータを読み込む処理を実装可能
        // 例: CardDatabase.Instance.LoadAllCards();
        CreateCardSetToggles();


    }
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
    private void OnCardSetSelected(CardSetData set)
{
    Debug.Log($"選択されたカードセット: {set.setName}");
    Debug.Log($"カードのID: {set.setId}");
}
   public List<Toggle> GetOnToggles()
    {
        var result = new List<Toggle>();

        Toggle[] toggles = toggleParent.GetComponentsInChildren<Toggle>();
        
        foreach (var toggle in toggles)
        {
            if (toggle.isOn)
            {
                result.Add(toggle);
            }
        }
        return result;
    }
public List<CardData> GetSelectedCards(List<CardData> cards)
{
    List<CardData> result = new List<CardData>();
    List<Toggle> onToggles = GetOnToggles();
    if (onToggles.Count == 0)
    {
        Debug.Log("ONのトグルがないため、全てのカードを返します。");
        return cards; // ONのトグルがない場合は全てのカードを返す
    }
    // Predicate<CardData> predicate = card => true;

    Dictionary<FilterType, Predicate<CardData>> groupPredicates
        = new Dictionary<FilterType, Predicate<CardData>>();
    
    // Predicate<CardData> predicate = null;
    


    foreach (var toggle in onToggles)
    {
         // 既存トグルを全削除
   

        ToggleDatail detail = toggle.GetComponent<ToggleDatail>();
        if(detail == null) continue;
        Predicate<CardData> condition = null;
        Debug.Log($"トグルのフィルタータイプ: {detail.filterType}, ,バージョンID: {detail.id}, sourceType: {detail.sourceType}, color: {detail.color}");
        
       
        switch (detail.filterType)
        {
            // フィルター=バージョンの場合
            case FilterType.Version:
                condition = card => card.version == detail.id;
                Debug.Log($"フィルタリング: {detail.filterType}, version: {detail.id}");
                break;
            // フィルター=ソースタイプの場合
            case FilterType.SourceType:
               condition = card => card.sourceType == detail.sourceType;
              
                break;
            // フィルター=カードの色の場合
            case FilterType.Color:
                condition = card => card.color == detail.color;
                Debug.Log($"フィルタリング: {detail.filterType}, color: {detail.color}");
          
                
                break;
            default:
                Debug.LogWarning($"未対応のフィルタータイプ: {detail.filterType}");
                break;
        }
            // switch (detail.color)
            // {
            //     case CardColor.Red:
            //         condition = card => card.color == CardColor.Red;
            //         break;
            //     case CardColor.Blue:
            //         condition = card => card.color == CardColor.Blue;
            //         break;
            //     case CardColor.Green:
            //         condition = card => card.color == CardColor.Green;
            //         break;
            //     // 他の色もここに追加可能
            // }

        if (condition == null) continue;
        
        if (!groupPredicates.ContainsKey(detail.filterType))
        {
            groupPredicates[detail.filterType] = condition;
        }
        else
        {
            groupPredicates[detail.filterType]
                = groupPredicates[detail.filterType].Or(condition);
        }
    }
    Predicate<CardData> finalPredicate = card => true;

    foreach (var predicate in groupPredicates.Values)
    {
        finalPredicate = finalPredicate.And(predicate);
    }

    return cards.FindAll(finalPredicate);
    // return result;
}


    void CreateCardSetToggles()
{
    // CardSetData[] cardSets =
    //     Resources.LoadAll<CardSetData>("Data/CardSetData");
    CardSetData[] cardSets = Resources.LoadAll<CardSetData>(cardSetResourcePath);
    string FiltertypeText = "";
    //! セクションテキストを生成
    if (includeSectionTextPrefab != null)
    {
        GameObject IncludeSectionText = Instantiate(includeSectionTextPrefab, toggleParent);
        Debug.Log("カードテキストのフィルタータイプ: " + cardSets[0].filterType);
        TMP_Text text = IncludeSectionText.GetComponentInChildren<TMP_Text>();
        text.text = cardSets[0].filterType.ToString();
        FiltertypeText = cardSets[0].filterType.ToString();
    }

    foreach (var set in cardSets)
    {
        // !セクションテキスト再設定
        if(includeSectionTextPrefab != null)
        {
            if (set.filterType.ToString() != FiltertypeText)
            {
                FiltertypeText = set.filterType.ToString();
                GameObject sectionText = Instantiate(includeSectionTextPrefab, toggleParent);
                TMP_Text text = sectionText.GetComponentInChildren<TMP_Text>();
                text.text = set.filterType.ToString();
            }
        }
        Toggle toggle = Instantiate(togglePrefab, toggleParent);
        // ToggleGroup に所属させる
        // toggle.group = toggleGroup;
        ToggleDatail detail = toggle.GetComponent<ToggleDatail>();
        detail.id = set.setId;
        detail.filterType = set.filterType;
        
        detail.sourceType = set.sourceType;
        detail.color = set.color;
        Debug.Log("トグルのカードのソースタイプ: " + set.sourceType + " フィルタータイプ: " + set.filterType + " カードセットID: " + set.setId);
        // detail
        // ラベル設定（Text / TMP 両対応）
        var label = toggle.GetComponentInChildren<UnityEngine.UI.Text>();
        if (label != null)
        {
            label.text = set.setName;
        }

        // ON/OFF時の処理
        toggle.onValueChanged.AddListener(isOn =>
        {
            if (isOn)
            {
                OnCardSetSelected(set);
            }
        });
    }
}

}
