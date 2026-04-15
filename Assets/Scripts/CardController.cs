using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class CardController : MonoBehaviour,IPointerClickHandler
{
    [SerializeField] private Image cardImage;

    public CardData Data { get; private set; }

    public void SetUp(CardData carddata)
    {
        this.Data = carddata;

        Sprite cardSprite = Resources.Load<Sprite>($"Data/Images/{carddata.imageName.name}");
        cardImage.sprite = cardSprite;

    }
    public void OnPointerClick(PointerEventData eventData)
    {
        Debug.Log("クリックされました");
    }
}
