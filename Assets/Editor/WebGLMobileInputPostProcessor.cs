#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

/// <summary>WebGL ビルド後 index.html に unityInstance グローバル代入を追加する。</summary>
public static class WebGLMobileInputPostProcessor
{
    [PostProcessBuild(1)]
    public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
    {
        if (target != BuildTarget.WebGL)
        {
            return;
        }

        string indexPath = Path.Combine(pathToBuiltProject, "index.html");
        if (!File.Exists(indexPath))
        {
            return;
        }

        string html = File.ReadAllText(indexPath);
        if (html.Contains("window.unityInstance = unityInstance"))
        {
            return;
        }

        const string search = "}).then((unityInstance) => {";
        const string replace =
            "}).then((unityInstance) => {\n                window.unityInstance = unityInstance;";

        if (html.Contains(search))
        {
            html = html.Replace(search, replace);
            File.WriteAllText(indexPath, html);
            Debug.Log("[WebGLMobileInput] Patched index.html with window.unityInstance assignment.");
        }
    }
}
#endif
