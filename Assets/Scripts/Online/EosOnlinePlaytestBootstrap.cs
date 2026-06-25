using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Home シーン起動時に EOS オンライン UI を生成する（初期は非表示。Online Battle ボタンで開く）。
/// </summary>
public static class EosOnlinePlaytestBootstrap
{
    private const string HomeSceneName = "Home";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        UnityEngine.SceneManagement.Scene scene = SceneManager.GetActiveScene();
        if (scene.name != HomeSceneName)
        {
            return;
        }

        Canvas canvas = UnityEngine.Object.FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            Debug.LogWarning("[EOS Online] Canvas not found on Home scene. Cannot add UI.");
            return;
        }

        EosOnlinePlaytestController.InstallOnCanvas(canvas);
    }
}
