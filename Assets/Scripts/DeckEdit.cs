using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.IO;
using System;

public class DeckEdit : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI CardCount;
    [SerializeField] private Button add;
    [SerializeField] private Button subtranct;
    [SerializeField] private Dictionary<int, int> cardData = new Dictionary<int, int>();
    public int cardId;
    public GameObject CardObj;
    void Start()
    {
        add.onClick.AddListener(addCard);
        subtranct.onClick.AddListener(subtractCard);
        // if()
        // CardCount.text = DeckSettinObject.Instance.CardCount.ToString();
        Debug.Log($"デッキ編集オブジェクトに渡された{cardId}");
        int count = DeckSettinObject.Instance.CardCount(cardId);
        CardCount.text = count.ToString();

    }

    // Update is called once per frame
    void Update()
    {
        
    }
    private void addCard()
    {
        int count = int.Parse(CardCount.text);
        if (count == 4)
        {
            return;
        }
        count += 1;
        CardCount.text = count.ToString();
    }
    private void subtractCard()
    {
          
        int count = int.Parse(CardCount.text);
        if(count == 0)
        {
            return;
        }
        count -= 1;
        CardCount.text = count.ToString();
    }

    public string CountTextNum()
    {
        return CardCount.text;
    }
    public void CardIdtoSettingObject(int id)
    {
        Debug.Log($"カードの数{CardCount.text},id{id}");

        int count = int.Parse(CardCount.text);
        // if(count == 0)
        // {
        //     Debug.Log("カード枚数は0");
        //     return;
        // }

        cardData[id] = int.Parse(CardCount.text);
        
        DeckSettinObject.Instance.Deckedit(id,int.Parse(CardCount.text));

        DeckSettinObject.Instance.cardObj(CardObj);
        // return id;
    }

   
}
