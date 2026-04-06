using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class Scene : MonoBehaviour
{
    [SerializeField] private Button SceneButton;
    public string SceneName;
    // Start is called before the first frame update
    void Start()
    {
        // なにもなければHomeシーンに戻るようにする
        if (SceneButton != null)
        {
            SceneName = "SampleScene";
        }
        SceneButton.onClick.AddListener(() =>
        {
            SceneManager.LoadScene(SceneName);
        });

        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
