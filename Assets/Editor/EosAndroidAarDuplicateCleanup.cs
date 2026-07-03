#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// EOS AndroidBuilder はビルド前に PlatformSpecificAssets から
/// Assets/Plugins/Android/aar/eos-sdk.aar へコピーする。
/// 直下の eos-sdk.aar が残っていると同名プラグイン衝突になるため除去する。
/// </summary>
[InitializeOnLoad]
internal static class EosAndroidAarDuplicateCleanup
{
    private const string DuplicateRootAar = "Assets/Plugins/Android/eos-sdk.aar";

    static EosAndroidAarDuplicateCleanup()
    {
        RemoveRootDuplicateIfPresent();
    }

    internal static void RemoveRootDuplicateIfPresent()
    {
        string absolutePath = GetAbsolutePath(DuplicateRootAar);
        if (!File.Exists(absolutePath))
        {
            return;
        }

        if (AssetDatabase.DeleteAsset(DuplicateRootAar))
        {
            Debug.Log("[EOS Android] Removed duplicate eos-sdk.aar from Assets/Plugins/Android/ (EOS uses aar/ subfolder).");
        }
        else
        {
            File.Delete(absolutePath);
            string metaPath = absolutePath + ".meta";
            if (File.Exists(metaPath))
            {
                File.Delete(metaPath);
            }

            AssetDatabase.Refresh();
            Debug.Log("[EOS Android] Removed duplicate eos-sdk.aar from disk.");
        }
    }

    private static string GetAbsolutePath(string assetPath)
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
    }
}

internal sealed class EosAndroidAarDuplicateBuildPreprocessor : IPreprocessBuildWithReport
{
    public int callbackOrder => -10_000;

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.Android)
        {
            return;
        }

        EosAndroidAarDuplicateCleanup.RemoveRootDuplicateIfPresent();
    }
}
#endif
