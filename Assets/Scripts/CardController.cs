using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System;
public class CardController : MonoBehaviour,IPointerClickHandler
{
    [SerializeField] private Image cardImage;
    

    // !バトルパネルの参照

    public CardData Data { get; private set; }
    private Action<CardController> onClickCallback;
    
    public Sprite cardSprite{ get; private set; }

    /// <summary>ランタイムの攻撃フラグ（カードデータのアセットは変更しない）。</summary>
    private AttackFlg _attackFlg = AttackFlg.False;
    public AttackFlg AttackFlgState => _attackFlg;

    public void SetUp(CardData carddata,Action<CardController> callback)
    {
        this.Data = carddata;
        
        this.onClickCallback = callback;
        cardSprite = Resources.Load<Sprite>($"Data/Images/{carddata.imageName.name}");
        cardImage.sprite = cardSprite;

        // 手札・新規生成時は常に False（ユニット以外は攻撃フラグを使わない）
        _attackFlg = AttackFlg.False;
    }

    /// <summary>攻撃フラグを設定し、デバッグログを出す。</summary>
    public void SetAttackFlg(AttackFlg value)
    {
        _attackFlg = value;
        string name = Data != null ? Data.cardName : "?";
        int id = Data != null ? Data.id : -1;
        Debug.Log($"[AttackFlg] {name} (id:{id}) => {_attackFlg}");
    }
    public void OnPointerClick(PointerEventData eventData)
    {
        Debug.Log($"カードがクリックされました。カード名前: {Data.cardName}");
        Debug.Log($"カードがクリックされました。カードコスト: {Data.cost}");
        Debug.Log("クリックされました");
        onClickCallback?.Invoke(this);
    }

    public int GetCardcost()
    {
        return Data.cost;
    }
}
