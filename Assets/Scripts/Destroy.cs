using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
public class Destroy : MonoBehaviour
{
    [SerializeField] private Button closeButton;
    
    // Start is called before the first frame update
    void Start()
    {
        closeButton.onClick.AddListener(OnDestroy);
    }

    private void OnDestroy()
    {
        Debug.Log("closeボタンが押された");
        Destroy(gameObject);
    }
    // Update is called once per frame
    void Update()
    {
        
    }
}
