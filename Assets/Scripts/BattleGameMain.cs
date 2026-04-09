using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BattleGameMain : MonoBehaviour
{
    // Start is called before the first frame update
    public bool isFirstPlayer;
     void Start()
    {
        Debug.Log("バトルゲームのメインシーン");
        // 先攻後攻を決める。
        DecideTurnOrder();
        // プレイヤーとエネミーのデッキデータを取得する。
        


        
    }
    public void DecideTurnOrder()
    {
        // 0か1をランダムに取得 (Rangeの第2引数は未満なので、0か1が出る)
        int result = Random.Range(0, 2);

        isFirstPlayer = (result == 0);

        if (isFirstPlayer)
        {
            Debug.Log("あなたが先攻です。");
        }
        else
        {
            Debug.Log("相手が先攻です。");
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
