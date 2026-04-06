using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;


public class DataSearch : MonoBehaviour
{
    [SerializeField] private TMP_InputField inputField;

    [SerializeField] private Button SearchButton;
    
    // Start is called before the first frame update
    void Start()
    {
        SearchButton.onClick.AddListener(() => Search(inputField.text));
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    void Search(string keyword)
    {
        Debug.Log("検索キーワード: " + keyword);
        var results = CardDatabase.Instance.FindByNameContains(keyword);
        foreach (var card in results)
        {
            
            Debug.Log($"検索結果: {card.id}, {card.cardName}");
        }
    }
}
