using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
public class DeckMake : MonoBehaviour
{
    [SerializeField] private Button DeckMakeButton;
    // Start is called before the first frame update
    void Start()
    {
        DeckMakeButton.onClick.AddListener(DeckDatatoJson);
    }

    // Update is called once per frame
    void Update()
    {
        
    }
    private void DeckDatatoJson()
    {
        Debug.Log("デッキデータをJSONに変換して保存する処理");
    }
}
