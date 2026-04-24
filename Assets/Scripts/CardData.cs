
using UnityEngine;   // ← これが必須
using System;        // ← これも必要（[Serializable]属性のため）
using System.Collections.Generic; // ← これも必要（List<T>のため）
[CreateAssetMenu(menuName = "Game/Card")]
public class CardData : ScriptableObject
{
    public int id;
    public string cardName;
    public int cost;
    public int level;
    public int power;
    public int hp;
    public Sprite imageName;
    public Sprite image;
    public int version;
    public CardSourceType sourceType;
    public FilterType filterType;
    public CardColor color;
    public Type type;
    /// <summary>ユニット（Type.Unit）向け。アセット上の既定値。実行時は CardController で上書き。</summary>
    [Tooltip("ユニットのみ有効。配備時は False、自分ターン開始で True に更新（BattleGameMain）。")]
    public AttackFlg attackFlg = AttackFlg.False;
    [Tooltip("カード効果定義（タイミング別）。")]
    public List<TimedEffectData> timedEffects = new List<TimedEffectData>();

}


[Serializable]
public class CardJson
{
    public int id;
    public string cardName;
    public int cost;
    public int level;
    public int power;
    public int hp;
    public string imageName;
    public int version;
    public int sourceType;
    public int color; // カードの色を追加

}

[Serializable]
public class CardMasterJson
{
    public List<CardJson> cards = new List<CardJson>();
}

