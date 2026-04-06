using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class displayActive : MonoBehaviour
{

    [SerializeField] private Button displayButton;
    [SerializeField] private GameObject displayObject;
    void Awake()
    {
        displayButton.onClick.AddListener(buttonClicked);
    }
    // Start is called before the first frame update
    void Start()
    {
        
    }

    private void buttonClicked()
    {
        Debug.Log("deckButton Clicked");
        if (displayObject.activeSelf)
            displayObject.SetActive(false);
        else
        displayObject.SetActive(true);
    }
    // Update is called once per frame
    void Update()
    {
        
    }
}
