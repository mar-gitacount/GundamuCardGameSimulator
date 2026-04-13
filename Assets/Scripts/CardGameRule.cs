using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class CardGameRule
{
    // 実際に「山札」として使うリスト
    private List<int> deckList = new List<int>();

    /// <summary>
    /// デッキデータを元に、シャッフルされた山札を作成する
    /// </summary>
    public void CreateShuffledDeck(Dictionary<int, int> cardData)
    {
        // 前回の残りを一旦クリア（念のため）
        deckList.Clear();

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
    }

    // 一応山札をシャッフルする関数も用意しておく
    public void ShuffleDeck()
    {
        deckList = deckList.OrderBy(x => System.Guid.NewGuid()).ToList();
        Debug.Log("山札をシャッフルしました。");
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

        return topCardId;
    }

    // 現在の残り枚数を知りたい場合に便利
    public int GetRemainingCount() => deckList.Count;

    // リソース関数もここに追加していく予定
    
}