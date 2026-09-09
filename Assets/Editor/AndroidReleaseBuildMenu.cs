using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// 実機接続なしで Android リリース成果物だけを出力する。
/// Build And Run ではなく Build 相当（AutoRunPlayer なし）。
/// </summary>
public static class AndroidReleaseBuildMenu
{
    private const string OutputRoot = "Builds/Android";

    [MenuItem("Build/Android/Release APK (実機へ入れない)", false, 100)]
    public static void BuildReleaseApk()
    {
        BuildAndroidRelease(appBundle: false);
    }

    [MenuItem("Build/Android/Release AAB (実機へ入れない・Play提出用)", false, 101)]
    public static void BuildReleaseAab()
    {
        BuildAndroidRelease(appBundle: true);
    }

    [MenuItem("Build/Android/出力フォルダを開く", false, 150)]
    public static void OpenOutputFolder()
    {
        string absolute = Path.GetFullPath(Path.Combine(Application.dataPath, "..", OutputRoot));
        Directory.CreateDirectory(absolute);
        EditorUtility.RevealInFinder(absolute);
    }

    private static void BuildAndroidRelease(bool appBundle)
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
        {
            bool switched = EditorUserBuildSettings.SwitchActiveBuildTarget(
                BuildTargetGroup.Android,
                BuildTarget.Android);
            if (!switched)
            {
                EditorUtility.DisplayDialog(
                    "Android ビルド",
                    "Build Target を Android に切り替えられませんでした。\nFile > Build Profiles で Android を確認してください。",
                    "OK");
                return;
            }
        }

        string packageName = PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android);
        if (string.IsNullOrWhiteSpace(packageName) || packageName == "com.Company.ProductName")
        {
            EditorUtility.DisplayDialog(
                "Android ビルド",
                "Android Package Name が未設定です。\nPlayer Settings > Other Settings > Package Name を設定してください。",
                "OK");
            return;
        }

        if (appBundle && !PlayerSettings.Android.useCustomKeystore)
        {
            bool continueAnyway = EditorUtility.DisplayDialog(
                "キーストア未設定",
                "正式キーストアが未設定です。\nGoogle Play 提出にはカスタムキーストア署名が必要です。\n\nこのまま（デバッグ署名）で AAB を作りますか？\nPlay 提出前に Publishing Settings でキーストアを作成・設定してください。",
                "デバッグ署名で続行",
                "キャンセル");
            if (!continueAnyway)
            {
                return;
            }
        }

        EditorUserBuildSettings.buildAppBundle = appBundle;
        EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;
        EditorUserBuildSettings.development = false;
        EditorUserBuildSettings.allowDebugging = false;
        EditorUserBuildSettings.connectProfiler = false;
        EditorUserBuildSettings.buildWithDeepProfilingSupport = false;

        string extension = appBundle ? "aab" : "apk";
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string fileName = $"{SanitizeFileName(PlayerSettings.productName)}_Release_{stamp}.{extension}";
        string absoluteDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", OutputRoot));
        Directory.CreateDirectory(absoluteDir);
        string outputPath = Path.Combine(absoluteDir, fileName);

        string[] scenes = EditorBuildSettings.scenes
            .Where(s => s != null && s.enabled && !string.IsNullOrEmpty(s.path))
            .Select(s => s.path)
            .ToArray();
        if (scenes.Length == 0)
        {
            EditorUtility.DisplayDialog(
                "Android ビルド",
                "有効な Scene が Build Settings にありません。",
                "OK");
            return;
        }

        Debug.Log($"[AndroidRelease] Build start → {outputPath}");

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outputPath,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            // AutoRunPlayer を付けない＝実機へインストールしない
            options = BuildOptions.None,
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;

        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"[AndroidRelease] Succeeded: {outputPath} ({summary.totalSize} bytes)");
            EditorUtility.RevealInFinder(outputPath);
            EditorUtility.DisplayDialog(
                "Android リリース完了",
                $"出力しました（実機へは入れていません）。\n\n{outputPath}",
                "OK");
            return;
        }

        Debug.LogError($"[AndroidRelease] Failed: {summary.result}");
        EditorUtility.DisplayDialog(
            "Android リリース失敗",
            $"ビルドに失敗しました。\nConsole を確認してください。\n結果: {summary.result}",
            "OK");
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "App";
        }

        foreach (char c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        return name.Trim();
    }
}
