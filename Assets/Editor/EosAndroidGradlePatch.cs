#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// EOS AndroidBuilder.PreBuild が毎回 PlatformSpecificAssets から jcenter() 入り build.gradle を
/// 上書きコピーするため、EOS 処理の後に Gradle 9 対応版へ差し替える。
/// </summary>
internal static class EosAndroidGradlePatch
{
    private const string SourceGradleAssetPath =
        "Assets/Plugins/Android/EOS/eos_dependencies.androidlib/build.gradle";

    private const string PatchedGradleContents = @"apply plugin: 'com.android.library'

android {
    namespace ""com.pew.eos_dependencies""

    sourceSets {
        main {
            manifest.srcFile 'AndroidManifest.xml'
            java.srcDirs = ['src']
            res.srcDirs = ['res']
            assets.srcDirs = ['assets']
            jniLibs.srcDirs = ['libs']
        }
    }

    compileSdkVersion 36
    defaultConfig {
        targetSdkVersion 36
    }
}

dependencies {
    implementation 'androidx.appcompat:appcompat:1.5.1'
    implementation 'androidx.constraintlayout:constraintlayout:2.1.4'
    implementation 'androidx.security:security-crypto:1.0.0'
    implementation 'androidx.browser:browser:1.4.0'
    //api fileTree(dir: 'libs', include: ['*.aar'])
}
";

    [MenuItem("EOS Plugin/Advanced/Android/Patch Gradle 9 eos_dependencies")]
    private static void PatchGradleFromMenu()
    {
        ApplyAllAndroidGradlePatches("menu");
        Debug.Log("[EOS Android] Manual Gradle 9 patch completed.");
    }

    internal static void ApplyAllAndroidGradlePatches(string contextLabel)
    {
        ApplyIfNeeded(GetSourceGradleAbsolutePath(), $"{contextLabel}:source");
        TryPatchEosPackageTemplates(contextLabel);

        string libraryRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library"));
        if (!Directory.Exists(libraryRoot))
        {
            return;
        }

        string[] generatedFiles = Directory.GetFiles(
            libraryRoot,
            "build.gradle",
            SearchOption.AllDirectories);

        for (int i = 0; i < generatedFiles.Length; i++)
        {
            string normalized = generatedFiles[i].Replace('\\', '/');
            if (!normalized.Contains("/eos_dependencies.androidlib/build.gradle"))
            {
                continue;
            }

            ApplyIfNeeded(generatedFiles[i], $"{contextLabel}:generated");
        }
    }

    internal static void ApplyIfNeeded(string gradleFilePath, string contextLabel)
    {
        if (string.IsNullOrEmpty(gradleFilePath) || !File.Exists(gradleFilePath))
        {
            return;
        }

        string current = File.ReadAllText(gradleFilePath);
        if (!NeedsPatch(current))
        {
            return;
        }

        File.WriteAllText(gradleFilePath, PatchedGradleContents);
        Debug.Log($"[EOS Android] Patched eos_dependencies build.gradle for Gradle 9 ({contextLabel}).");
    }

    private static bool NeedsPatch(string contents)
    {
        return contents.Contains("jcenter()")
            || contents.Contains("com.android.tools.build:gradle:3.6.0")
            || !contents.Contains("namespace \"com.pew.eos_dependencies\"");
    }

    internal static string GetSourceGradleAbsolutePath()
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", SourceGradleAssetPath));
    }

    private static void TryPatchEosPackageTemplates(string contextLabel)
    {
        string packageCacheRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", "PackageCache"));
        if (!Directory.Exists(packageCacheRoot))
        {
            return;
        }

        string[] packageDirs = Directory.GetDirectories(packageCacheRoot, "com.playeveryware.eos@*");
        for (int i = 0; i < packageDirs.Length; i++)
        {
            string templateGradle = Path.Combine(
                packageDirs[i],
                "PlatformSpecificAssets~",
                "EOS",
                "Android",
                "eos_dependencies.androidlib",
                "build.gradle");

            ApplyIfNeeded(templateGradle, $"{contextLabel}:package-template");
        }
    }
}

/// <summary>EOS BuildRunner(callbackOrder=1) より後に実行する。</summary>
internal sealed class EosAndroidGradlePreprocessBuild : IPreprocessBuildWithReport
{
    public int callbackOrder => 1_000_000;

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.Android)
        {
            return;
        }

        EosAndroidGradlePatch.ApplyAllAndroidGradlePatches("preprocess");
    }
}

internal sealed class EosAndroidGradlePostGenerate : IPostGenerateGradleAndroidProject
{
    public int callbackOrder => 1_000_000;

    public void OnPostGenerateGradleAndroidProject(string path)
    {
        string generatedGradle = Path.Combine(path, "eos_dependencies.androidlib", "build.gradle");
        EosAndroidGradlePatch.ApplyIfNeeded(generatedGradle, "post-generate");
        EosAndroidGradlePatch.ApplyAllAndroidGradlePatches("post-generate-scan");
    }
}
#endif
