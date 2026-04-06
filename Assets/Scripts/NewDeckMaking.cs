using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.IO;
using System;
public class NewDeckMaking : MonoBehaviour
{
    [SerializeField] private GameObject DeckListPanel;
    [SerializeField] private Button NewDeckButton;
    [SerializeField] private GameObject DeckEditPanel;
    [SerializeField] private TextMeshProUGUI NewDeckText;

    [SerializeField] private Button DeckMakeButton;

    [SerializeField] private TMP_InputField DeckTitleInputField;

    [SerializeField] private Button DeckEditButton;
    
    [SerializeField] private Button DeckDeleteButton;
    

    // Start is called before the first frame update
    void Start()
    {
        NewDeckButton.onClick.AddListener(newDeckButtonClicked);
        
        DeckEditButton.onClick.AddListener(newDeckButtonClicked);
        DeckSettinObject.Instance.isDeckEditing = false;
        DeckMakeButton.onClick.AddListener(DeckMakeButtonClicked);
        DeckDeleteButton.onClick.AddListener(DeleteexecutionJsonFileToUseDeckSeetinObject);
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    private void DeleteexecutionJsonFileToUseDeckSeetinObject()
    {
        // 1. ファイルが存在するか確認
        DeckSettinObject.Instance.DeleteJsonFile();
        
    }
    private void newDeckButtonClicked()
    {
        
        if(DeckSettinObject.Instance.isDeckEditing)
        {
            DeckSettinObject.Instance.isDeckEditing = false;
            DeckEditPanel.gameObject.SetActive(false);
            DeckListPanel.gameObject.SetActive(true);
           
            // DeckSettinObject.Instance.ShowFileList();
            Debug.Log("デッキ編集モードを終了してデッキリストに戻ります。");
            NewDeckText.text = "NewDeck";

            // デッキリストを空にする
            DeckSettinObject.Instance.ClearDeckList();

            DeckSettinObject.Instance.ShowFileList();
            DeckTitleInputField.text = "";
            DeckTitleInputField.gameObject.SetActive(false);
        }
        else
        {
            DeckSettinObject.Instance.isDeckEditing = true;
            NewDeckText.text = "Editing Now ..";
            DeckListPanel.gameObject.SetActive(false);
            DeckTitleInputField.gameObject.SetActive(true);

            

            DeckEditPanel editPanel = DeckEditPanel.GetComponent<DeckEditPanel>();
            editPanel.LoadDeckToEditPanel();
            DeckEditPanel.gameObject.SetActive(true);
            return;


        }
        Debug.Log($"ボタン:{DeckSettinObject.Instance.isDeckEditing}");
        
        
        // DeckListPanel.gameObject.SetActive(false);
    }


    private void DeckMakeButtonClicked()
    {
        DeckSettinObject.Instance.CardDataToJson();
        return;
        Debug.Log("デッキ作成ボタンがクリックされました。");
        string path = Application.persistentDataPath;
        // ここでデッキデータをJSONに変換して保存する処理を実装します。
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

        // 2. ファイル名を作成
        string fileName = "Deck_" + timestamp + ".json";

        // 3. 保存先のフルパスを作成
        string fullPath = Path.Combine(Application.persistentDataPath, fileName);

        // 確認用ログ
        Debug.Log("保存パス: " + fullPath);
    }
}
