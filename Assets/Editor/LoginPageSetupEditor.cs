#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Home シーンにログインページを手動配置するエディタメニュー。</summary>
public static class LoginPageSetupEditor
{
    private const string HomeScenePath = "Assets/Scenes/Home.unity";

    [MenuItem("Tools/Game/Setup Login Page in Home Scene")]
    public static void SetupLoginPageInHomeScene()
    {
        UnityEngine.SceneManagement.Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.path != HomeScenePath)
        {
            if (!EditorUtility.DisplayDialog(
                    "Open Home Scene",
                    "The login page is added to the Home scene. Open Home scene now?",
                    "Open",
                    "Cancel"))
            {
                return;
            }

            activeScene = EditorSceneManager.OpenScene(HomeScenePath);
        }

        if (Object.FindObjectOfType<DeckAuthUI>() != null)
        {
            EditorUtility.DisplayDialog("Login Page", "Home scene already has LoginPage / DeckAuthUI.", "OK");
            return;
        }

        Canvas canvas = Object.FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            EditorUtility.DisplayDialog("Login Page", "Canvas not found.", "OK");
            return;
        }

        GameObject loginPage = new GameObject("LoginPage", typeof(RectTransform), typeof(DeckAuthUI));
        RectTransform rect = loginPage.GetComponent<RectTransform>();
        rect.SetParent(canvas.transform, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.SetAsLastSibling();

        EditorSceneManager.MarkSceneDirty(activeScene);
        Selection.activeGameObject = loginPage;
        EditorUtility.DisplayDialog(
            "Login Page",
            "LoginPage was added to the Home scene Canvas.\nThe login screen appears when you press Play.",
            "OK");
    }
}
#endif
