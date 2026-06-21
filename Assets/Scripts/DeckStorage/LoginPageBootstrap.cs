using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Home シーンにログインページが無ければ自動追加する。</summary>
public static class LoginPageBootstrap
{
    private const string HomeSceneName = "Home";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallOnHomeScene()
    {
        UnityEngine.SceneManagement.Scene scene = SceneManager.GetActiveScene();
        if (scene.name != HomeSceneName)
        {
            return;
        }

        if (Object.FindObjectOfType<DeckAuthUI>() != null)
        {
            return;
        }

        Canvas canvas = Object.FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            Debug.LogWarning("[LoginPage] Canvas not found. Cannot create login page.");
            return;
        }

        GameObject loginPage = new GameObject("LoginPage", typeof(RectTransform));
        RectTransform loginRect = loginPage.GetComponent<RectTransform>();
        loginRect.SetParent(canvas.transform, false);
        loginRect.anchorMin = Vector2.zero;
        loginRect.anchorMax = Vector2.one;
        loginRect.offsetMin = Vector2.zero;
        loginRect.offsetMax = Vector2.zero;
        loginPage.AddComponent<DeckAuthUI>();
        Debug.Log("[LoginPage] Login page added to Home scene.");
    }
}
