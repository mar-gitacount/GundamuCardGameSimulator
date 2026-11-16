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
    private const string PendingApkFileName = "pending-android-release-apk";
    private const string PendingAabFileName = "pending-android-release-aab";
    private const string SwitchAndroidFileName = "switch-android";

    private static bool _polling;

    [InitializeOnLoadMethod]
    private static void ResumePendingAndroidBuild()
    {
        if (_polling)
        {
            return;
        }

        _polling = true;
        EditorApplication.update += PollPending;
    }

    private static void PollPending()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlaying)
        {
            return;
        }

        string switchPath = GetTempPath(SwitchAndroidFileName);
        if (File.Exists(switchPath))
        {
            TryDelete(switchPath);
            EnsureAndroidTarget();
            return;
        }

        if (File.Exists(GetTempPath(PendingApkFileName)))
        {
            if (!EnsureAndroidTarget())
            {
                return;
            }

            TryDelete(GetTempPath(PendingApkFileName));
            BuildAndroidRelease(appBundle: false);
            return;
        }

        if (File.Exists(GetTempPath(PendingAabFileName)))
        {
            if (!EnsureAndroidTarget())
            {
                return;
            }

            TryDelete(GetTempPath(PendingAabFileName));
            BuildAndroidRelease(appBundle: true);
        }
    }

    [MenuItem("Build/Android/Release APK (実機へ入れない)", false, 100)]
    public static void BuildReleaseApk()
    {
        QueueAndroidRelease(appBundle: false);
    }

    [MenuItem("Build/Android/Release AAB (実機へ入れない・Play提出用)", false, 101)]
    public static void BuildReleaseAab()
    {
        QueueAndroidRelease(appBundle: true);
    }

    [MenuItem("Build/Android/出力フォルダを開く", false, 150)]
    public static void OpenOutputFolder()
    {
        string absolute = Path.GetFullPath(Path.Combine(Application.dataPath, "..", OutputRoot));
        Directory.CreateDirectory(absolute);
        EditorUtility.RevealInFinder(absolute);
    }

    /// <summary>
    /// プラットフォーム切替後はドメインリロードが走るため、同じ呼び出しでは BuildPlayer しない。
    /// </summary>
    private static void QueueAndroidRelease(bool appBundle)
    {
        string pendingName = appBundle ? PendingAabFileName : PendingApkFileName;
        Directory.CreateDirectory(Path.GetDirectoryName(GetTempPath(pendingName)) ?? "Temp");
        File.WriteAllText(GetTempPath(pendingName), DateTime.Now.ToString("o"));
        if (!EnsureAndroidTarget())
        {
            Debug.Log("[AndroidRelease] Android へ切替中。完了後にビルドを再開します。");
            return;
        }

        TryDelete(GetTempPath(pendingName));
        BuildAndroidRelease(appBundle);
    }

    private static bool EnsureAndroidTarget()
    {
        if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android)
        {
            return true;
        }

        bool switched = EditorUserBuildSettings.SwitchActiveBuildTarget(
            BuildTargetGroup.Android,
            BuildTarget.Android);
        if (!switched)
        {
            EditorUtility.DisplayDialog(
                "Android ビルド",
                "Build Target を Android に切り替えられませんでした。\nFile > Build Profiles で Android を確認してください。",
                "OK");
            return false;
        }

        // 切替成功時はドメインリロード後に PollPending が再開する。
        return false;
    }

    private static void BuildAndroidRelease(bool appBundle)
    {
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

        AndroidKeystorePasswordLoader.ApplyFromLocalFileIfPresent();

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

    private static string GetTempPath(string fileName)
    {
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
        return Path.Combine(projectRoot, "Temp", fileName);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[AndroidRelease] 一時ファイル削除に失敗: " + ex.Message);
        }
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
