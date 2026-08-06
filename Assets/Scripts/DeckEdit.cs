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

        // ユニットトークンはデッキ構築対象外のため、カウンター UI を出さない。
        if (IsUnitTokenCard(cardId))
        {
            HideCountUi();
            return;
        }

        int count = DeckSettinObject.Instance.CardCount(cardId);
        CardCount.text = count.ToString();

    }

    private static bool IsUnitTokenCard(int id)
    {
        if (id <= 0 || CardDatabase.Instance == null)
        {
            return false;
        }

        CardData data = CardDatabase.Instance.FindById(id);
        return data != null && data.IsUnitToken();
    }

    private void HideCountUi()
    {
        if (CardCount != null)
        {
            CardCount.gameObject.SetActive(false);
        }

        if (add != null)
        {
            add.gameObject.SetActive(false);
        }

        if (subtranct != null)
        {
            subtranct.gameObject.SetActive(false);
        }
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
        SyncDeckEditPreview(count);
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
        SyncDeckEditPreview(count);
    }

    private void SyncDeckEditPreview(int count)
    {
        if (DeckSettinObject.Instance == null)
        {
            return;
        }

        DeckSettinObject.Instance.Deckedit(cardId, count);
        DeckSettinObject.Instance.cardObj(cardId);
    }

    private void SyncDeckEditPreviewOnPanelClose()
    {
        if (DeckSettinObject.Instance == null || CardCount == null)
        {
            return;
        }

        if (!int.TryParse(CardCount.text, out int count))
        {
            return;
        }

        SyncDeckEditPreview(count);
    }

    private void OnDestroy()
    {
        SyncDeckEditPreviewOnPanelClose();
    }

    public string CountTextNum()
    {
        return CardCount.text;
    }
    public void CardIdtoSettingObject(int id)
    {
        Debug.Log($"カードの数{CardCount.text},id{id}");

        if (!int.TryParse(CardCount.text, out int count))
        {
            return;
        }

        cardData[id] = count;

        DeckSettinObject.Instance.Deckedit(id, count);
        DeckSettinObject.Instance.cardObj(id);
    }

   
}
