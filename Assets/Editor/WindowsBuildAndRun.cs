#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>Windows スタンドアロンの Build And Run。</summary>
public static class WindowsBuildAndRun
{
    private const string OutputDir = "Builds/Windows";
    private const string TriggerFileName = "run-windows-build-and-run";

    private static bool _pollingTrigger;

    [InitializeOnLoadMethod]
    private static void ConsumeTriggerOnLoad()
    {
        if (_pollingTrigger)
        {
            return;
        }

        _pollingTrigger = true;
        EditorApplication.update += PollTrigger;
    }

    private static void PollTrigger()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            return;
        }

        string triggerPath = GetTriggerPath();
        if (!File.Exists(triggerPath))
        {
            return;
        }

        try
        {
            File.Delete(triggerPath);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[WindowsBuildAndRun] trigger 削除に失敗: " + ex.Message);
            return;
        }

        Debug.Log("[WindowsBuildAndRun] trigger を検出。Build And Run を開始します。");
        BuildAndRunInternal(exitEditorOnFinish: false);
    }

    [MenuItem("Build/Windows/Build And Run", false, 50)]
    public static void BuildAndRun()
    {
        BuildAndRunInternal(exitEditorOnFinish: false);
    }

    /// <summary>Unity -batchmode -executeMethod WindowsBuildAndRun.BuildAndRunBatch</summary>
    public static void BuildAndRunBatch()
    {
        BuildAndRunInternal(exitEditorOnFinish: true);
    }

    private static string GetTriggerPath()
    {
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
        return Path.Combine(projectRoot, "Temp", TriggerFileName);
    }

    private static void BuildAndRunInternal(bool exitEditorOnFinish)
    {
        BuildTarget previousTarget = EditorUserBuildSettings.activeBuildTarget;
        if (previousTarget != BuildTarget.StandaloneWindows64)
        {
            bool switched = EditorUserBuildSettings.SwitchActiveBuildTarget(
                BuildTargetGroup.Standalone,
                BuildTarget.StandaloneWindows64);
            if (!switched)
            {
                Fail("StandaloneWindows64 に切り替えできませんでした。", exitEditorOnFinish);
                return;
            }

            // 切替直後はモジュール未準備のため Addressables が失敗する。リロード後に再実行する。
            string pending = GetTriggerPath();
            Directory.CreateDirectory(Path.GetDirectoryName(pending) ?? "Temp");
            File.WriteAllText(pending, "after-platform-switch");
            Debug.Log("[WindowsBuildAndRun] Windows へ切替中。完了後に Build And Run を再開します。");
            return;
        }

        string[] scenes = EditorBuildSettings.scenes
            .Where(s => s != null && s.enabled && !string.IsNullOrEmpty(s.path))
            .Select(s => s.path)
            .ToArray();
        if (scenes.Length == 0)
        {
            Fail("有効な Scene が Build Settings にありません。", exitEditorOnFinish);
            return;
        }

        string product = string.IsNullOrWhiteSpace(PlayerSettings.productName)
            ? "GcgOnline"
            : PlayerSettings.productName;
        string absoluteDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", OutputDir));
        Directory.CreateDirectory(absoluteDir);
        string outputPath = Path.Combine(absoluteDir, product + ".exe");

        Debug.Log("[WindowsBuildAndRun] Build And Run start → " + outputPath);

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outputPath,
            target = BuildTarget.StandaloneWindows64,
            targetGroup = BuildTargetGroup.Standalone,
            options = BuildOptions.AutoRunPlayer
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;
        if (summary.result != BuildResult.Succeeded)
        {
            Fail("Failed: " + summary.result, exitEditorOnFinish);
            return;
        }

        Debug.Log("[WindowsBuildAndRun] Succeeded: " + outputPath + " size=" + summary.totalSize);
        if (exitEditorOnFinish)
        {
            EditorApplication.Exit(0);
        }
    }

    private static void Fail(string message, bool exitEditorOnFinish)
    {
        Debug.LogError("[WindowsBuildAndRun] " + message);
        if (exitEditorOnFinish)
        {
            EditorApplication.Exit(1);
        }
    }
}
#endif
