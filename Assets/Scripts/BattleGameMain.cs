using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class BattleGameMain : MonoBehaviour
{
    // Start is called before the first frame update
    public bool isFirstPlayer;

    // 現在のバトルフェイズを管理する列挙型
    public enum BattlePhase
{
    StartTurn,   // ターン開始（ドローなど）
    ActivePhase,  // アクティブフェイズ（リソースの獲得、カードのドローなど）
    DrawPhase,        // ドローフェイズ（カードを引く）
    ResourcePhase, // リソースフェイズ（リソースの獲得や管理）
    MainPhase,    // メイン（カードを出す、攻撃する）
    EndTurn,     // ターン終了処理
    OpponentTurn // 相手のターン
}
    public BattlePhase currentPhase;
    // プレイヤーとエネミーのデッキデータを取得する。
    private Dictionary<int,int> playerDeckData =  new Dictionary<int, int>();
    private Dictionary<int,int> enemyDeckData = new Dictionary<int, int>();
    private CardGameRule cardGameRule = new CardGameRule();
    private CardGameRule enemyCardGameRule = new CardGameRule();

    //true = プレイヤー false =    エネミー
    public bool currentPlayer;

    [SerializeField]private Button EndTurnButton;

    
    public enum PlayerType{Player,Enemy}

    public PlayerType currentPlayerType;
     void Start()
    {
        Debug.Log("バトルゲームのメインシーン");
        // 先攻後攻を決める。
        isFirstPlayer = DecideTurnOrder();

        playerDeckData = DeckSettinObject.Instance.LoadDeckReturn();
        enemyDeckData = DeckSettinObject.Instance.LoadEnemyDeckReturn();

        //! プレイヤーが先行、後攻の場合、なにがしかのアラートを後ほど実装。

        //! マリガンの後ほど実装

       
   
      


        // プレイヤーとエネミーの山札を作成する。
        cardGameRule.CreateShuffledDeck(playerDeckData);
        // int playerCardId = cardGameRule.Draw();
        // Debug.Log($"プレイヤーが引いたカードID: {playerCardId}");
        enemyCardGameRule.CreateShuffledDeck(enemyDeckData);
        // int enemyCardId = enemyCardGameRule.Draw();
        // Debug.Log($"エネミーが引いたカードID: {enemyCardId}");

        // ターン開始フェイズに移行する。
        ChangePhase(BattlePhase.StartTurn);

        //! エンドフェイズはボタンを押下したときに呼び出すようにする予定。
        EndTurnButton.onClick.AddListener(() => ChangePhase(BattlePhase.EndTurn));

        
    }
    public bool DecideTurnOrder()
    {
        // 0か1をランダムに取得 (Rangeの第2引数は未満なので、0か1が出る)
        int result = Random.Range(0, 2);

        isFirstPlayer = (result == 0);
        // 先攻後攻をランダムに決める
        currentPlayerType = (Random.value < 0.5f) ? PlayerType.Player : PlayerType.Enemy;
        
        
        // !以下不要
        if (isFirstPlayer)
        {
            
            Debug.Log("あなたが先攻です。");
            currentPlayer = true; // プレイヤーが先攻の場合はtrue
            return true;
        }
        else
        {
            currentPlayer = false; // エネミーが先攻の場合はfalse
            Debug.Log("相手が先攻です。");
            return false;
        }
    }

    public void ChangePhase(BattlePhase nextPhase)
    {
        currentPhase = nextPhase;
        switch (currentPhase)
        {
            case BattlePhase.StartTurn:
                Debug.Log("ターン開始フェイズに入りました。");
                // ターン開始フェイズの処理をここに書く
                ExcuteStartTurn();
                break;
            case BattlePhase.ActivePhase:
                Debug.Log("アクティブフェイズに入りました。");
                // アクティブフェイズの処理をここに書く
                break;
            case BattlePhase.DrawPhase:
                Debug.Log("ドローフェイズに入りました。");
                // ドローフェイズの処理をここに書く
                break;
            case BattlePhase.ResourcePhase:
                Debug.Log("リソースフェイズに入りました。");
                // リソースフェイズの処理をここに書く
                break;
            case BattlePhase.MainPhase:
                Debug.Log("メインフェイズに入りました。");
                // メインフェイズの処理をここに書く
                ExcuteMainPhase();
                break;
            case BattlePhase.EndTurn:
                Debug.Log("エンドフェイズに入りました。");
                // エンドフェイズの処理をここに書く
                // currentPlayer = !currentPlayer; // ターン終了時にプレイヤーを切り替える
                ExcueteEndTurn();
                break;
            case BattlePhase.OpponentTurn:
                Debug.Log("相手のターンに入りました。");
                break;
        }
        
    }
    void ExcuteStartTurn()
    {
        Debug.Log("ターン開始フェイズの処理を実行します。");
        // ターン開始フェイズの具体的な処理をここに書く

        if(currentPlayerType == PlayerType.Player)
        {
            Debug.Log("プレイヤーのターン開始処理を実行します。");
            // プレイヤーのターン開始処理をここに書く
            // 例: プレイヤーの手札を引く、リソースを獲得するなど
            int playerCardId = cardGameRule.Draw();
            Debug.Log($"プレイヤーが引いたカードID: {playerCardId}");
        }
        else
        {
            Debug.Log("エネミーのターン開始処理を実行します。");
            // エネミーのターン開始処理をここに書く
            // 例: エネミーの手札を引く、リソースを獲得するなど
            int enemyCardId = enemyCardGameRule.Draw();
            Debug.Log($"エネミーが引いたカードID: {enemyCardId}");
        }
        // メインフェイズに移行する
        ChangePhase(BattlePhase.MainPhase);
    }

    void ExcuteMainPhase()
    {
        Debug.Log("メインフェイズの処理を実行します。");
        // メインフェイズの具体的な処理をここに書く
        // 例: プレイヤーがカードを出す、攻撃するなど

     
        if(currentPlayerType == PlayerType.Player)
        {
            Debug.Log("プレイヤーのメインフェイズの処理を実行します。");

            // エンドフェイズに移行する
            // ChangePhase(BattlePhase.EndTurn);


            // プレイヤーのメインフェイズの処理をここに書く
            // 例: プレイヤーがカードを出す、攻撃するなど
        }
        else
        {
            Debug.Log("エネミーのメインフェイズの処理を実行します。");
            // エネミーのメインフェイズの処理をここに書く
            // 例: エネミーがカードを出す、攻撃するなど
            StartCoroutine(EnemyActionCoroutine());
        }


    }
    IEnumerator EnemyActionCoroutine()
    {

        Debug.Log("エネミーの行動を開始します。");
        // エネミーの行動をシミュレートするために、数秒待つ
        yield return new WaitForSeconds(2f);
        Debug.Log("エネミーの行動が終了しました。");
        // エンドフェイズに移行する
        ChangePhase(BattlePhase.EndTurn);
    }

    void ExcueteEndTurn()
    {
        // プレイヤーとエネミーのターンを切り替える
        currentPlayerType = (currentPlayerType == PlayerType.Player) ? PlayerType.Enemy : PlayerType.Player;

        Debug.Log("エンドフェイズの処理を実行します。");
        ChangePhase(BattlePhase.StartTurn);

    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
