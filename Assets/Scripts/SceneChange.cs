using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
public class SceneChange : MonoBehaviour
{
    [SerializeField] private GameObject sceneButton;
    [SerializeField] private Transform buttonParent;
    // Start is called before the first frame update
    // Scene内をループする
    void Start()
    {
        int currentIndex = SceneManager.GetActiveScene().buildIndex;
        int sceneCount = SceneManager.sceneCountInBuildSettings;
        Debug.Log("Current Scene Index: " + sceneCount);
        // GameObject buttonPrefab = Instantiate(sceneButton, buttonParent);
        for(int i = 0; i < sceneCount; i++)
        {
            int index = i; // ローカル変数にキャプチャ
            GameObject buttonObj = Instantiate(sceneButton, buttonParent);
            buttonObj.GetComponentInChildren<UnityEngine.UI.Text>().text = "Scene " + i;
            buttonObj.GetComponent<UnityEngine.UI.Button>().onClick.AddListener(() =>
            {
                SceneManager.LoadScene(index);
            });
        }

    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
