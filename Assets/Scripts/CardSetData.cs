using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(
    fileName = "CardSetData",
    menuName = "Game/Card Set"
)]
public class CardSetData : ScriptableObject
{
    [Header("基本情報")]
    public int setId;                 // 弾ID（JSONと対応）
    public string setName;             // 弾名（例：第1弾 覚醒の炎）
    [TextArea]
    public string description;         // 弾説明

    [Header("販売・解放情報")]
    public int price;                  // 価格（ゲーム内通貨）
    public bool isReleased;             // 解放済みか
    public string releaseDate;          // 発売日（表示用）

    [Header("ビジュアル")]
    public Sprite setIcon;              // 弾アイコン
    public Sprite setBanner;            // ショップ用バナー

    [Header("収録カード")]
    public List<int> cardIds;           // 収録カードID一覧（JSON参照）

    [Header("バージョン管理")]
    public int version;                 // データバージョン
    public CardSourceType sourceType;
    public FilterType filterType; // フィルタータイプを追加
    public CardColor color; // カードの色を追加
    // public enum CardSourceType
    // {
    //     Starter,
    //     Booster
    // }
}
