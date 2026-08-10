using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>シーン読み込み時に、現行言語のフォントを全 TMP へ適用する。</summary>
public static class TmpLocaleFontBootstrap
{
    private static bool _hooked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        if (!_hooked)
        {
            _hooked = true;
            SceneManager.sceneLoaded += (_, __) =>
            {
                GameLocale.ApplyFontsToAllTmp();
                LocalizedTmpText.RefreshAll();
            };
            GameLocale.LanguageChanged += _ =>
            {
                // SetLanguage 内でも適用するが、二重でも害は少ない
            };
        }

        GameLocale.ApplyFontsToAllTmp();
        LocalizedTmpText.RefreshAll();
    }
}
