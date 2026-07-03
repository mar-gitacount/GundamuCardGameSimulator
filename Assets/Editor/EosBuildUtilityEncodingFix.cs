#if UNITY_EDITOR_WIN
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

/// <summary>
/// EOS パッケージの vswhere JSON パース失敗（日本語 OS の Visual Studio description）を防ぐ。
/// PackageCache 更新後も Editor 起動時に自動でパッチを再適用する。
/// </summary>
[InitializeOnLoad]
internal static class EosBuildUtilityEncodingFix
{
    private const string PatchMarker = "SanitizeVsWhereJson";

    static EosBuildUtilityEncodingFix()
    {
        if (TryPatchBuildUtility())
        {
            CompilationPipeline.RequestScriptCompilation();
        }
    }

    private static bool TryPatchBuildUtility()
    {
        string packageCacheRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", "PackageCache"));
        if (!Directory.Exists(packageCacheRoot))
        {
            return false;
        }

        bool patchedAny = false;
        string[] files = Directory.GetFiles(packageCacheRoot, "BuildUtility.cs", SearchOption.AllDirectories);
        for (int i = 0; i < files.Length; i++)
        {
            string filePath = files[i];
            if (filePath.IndexOf("com.playeveryware.eos", System.StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            string content = File.ReadAllText(filePath);
            if (content.Contains(PatchMarker))
            {
                continue;
            }

            string updated = ApplyVsWhereJsonPatch(content);
            if (updated == content)
            {
                Debug.LogWarning($"[EOS Fix] BuildUtility patch could not be applied: {filePath}");
                continue;
            }

            File.WriteAllText(filePath, updated);
            patchedAny = true;
            Debug.Log($"[EOS Fix] Applied vswhere JSON sanitize patch: {filePath}");
        }

        return patchedAny;
    }

    private static string ApplyVsWhereJsonPatch(string content)
    {
        const string oldBlock =
            "            p.Start();\r\n\r\n" +
            "            // Read the output asynchronously and store it in a variable.\r\n" +
            "            var outputBuilder = new StringBuilder();\r\n" +
            "            p.OutputDataReceived += (sender, args) => outputBuilder.AppendLine(args.Data);\r\n" +
            "            p.BeginOutputReadLine();\r\n\r\n" +
            "            // Wait for the process to exit\r\n" +
            "            p.WaitForExit();\r\n\r\n" +
            "            // Alter the json output from vswhere so that it plays nice with JsonUtility.\r\n" +
            "            string vsWhereOutputString = @\"{\"\"installations\"\":\" + outputBuilder.ToString() + \"}\";\r\n\r\n" +
            "            // Continue with processing vsWhereOutput...\r\n" +
            "            VSWhereOutput vsWhereOutput = JsonUtility.FromJson<VSWhereOutput>(vsWhereOutputString);";

        const string newBlock =
            "            p.StartInfo.StandardOutputEncoding = Encoding.UTF8;\r\n\r\n" +
            "            p.Start();\r\n" +
            "            string rawOutput = p.StandardOutput.ReadToEnd();\r\n" +
            "            p.WaitForExit();\r\n\r\n" +
            "            string sanitizedOutput = SanitizeVsWhereJson(rawOutput);\r\n" +
            "            string vsWhereOutputString = @\"{\"\"installations\"\":\" + sanitizedOutput + \"}\";\r\n\r\n" +
            "            // Continue with processing vsWhereOutput...\r\n" +
            "            VSWhereOutput vsWhereOutput = JsonUtility.FromJson<VSWhereOutput>(vsWhereOutputString);";

        if (content.Contains(oldBlock))
        {
            content = content.Replace(oldBlock, newBlock);
        }
        else
        {
            const string utf8OnlyNeedle = "p.StartInfo.RedirectStandardOutput = true;\r\n            p.StartInfo.RedirectStandardError = true;";
            const string utf8Insert =
                "p.StartInfo.RedirectStandardOutput = true;\r\n            p.StartInfo.StandardOutputEncoding = Encoding.UTF8;\r\n            p.StartInfo.RedirectStandardError = true;";
            content = content.Replace(utf8OnlyNeedle, utf8Insert);

            const string partialOld =
                "            p.Start();\r\n" +
                "            string rawOutput = p.StandardOutput.ReadToEnd();\r\n" +
                "            p.WaitForExit();\r\n\r\n" +
                "            // Alter the json output from vswhere so that it plays nice with JsonUtility.\r\n" +
                "            string vsWhereOutputString = @\"{\"\"installations\"\":\" + outputBuilder.ToString() + \"}\";";
            if (content.Contains(partialOld))
            {
                return content;
            }

            const string partialOld2 =
                "            p.Start();\r\n" +
                "            string rawOutput = p.StandardOutput.ReadToEnd();\r\n" +
                "            p.WaitForExit();\r\n\r\n" +
                "            string vsWhereOutputString = @\"{\"\"installations\"\":\" + rawOutput + \"}\";";
            const string partialNew2 =
                "            p.Start();\r\n" +
                "            string rawOutput = p.StandardOutput.ReadToEnd();\r\n" +
                "            p.WaitForExit();\r\n\r\n" +
                "            string sanitizedOutput = SanitizeVsWhereJson(rawOutput);\r\n" +
                "            string vsWhereOutputString = @\"{\"\"installations\"\":\" + sanitizedOutput + \"}\";";
            content = content.Replace(partialOld2, partialNew2);
        }

        if (!content.Contains("private static string SanitizeVsWhereJson(string json)"))
        {
            const string insertBefore =
                "        /// <summary>\r\n" +
                "        /// For every Visual Studio installation, there are a set of PlatformToolsets installed.";

            const string sanitizeMethod =
                "        /// <summary>\r\n" +
                "        /// vswhere の JSON からロケール依存フィールドを除去する。\r\n" +
                "        /// 日本語 Windows 等で description が壊れ、Newtonsoft の JSON パースが失敗するのを防ぐ。\r\n" +
                "        /// </summary>\r\n" +
                "        private static string SanitizeVsWhereJson(string json)\r\n" +
                "        {\r\n" +
                "            if (string.IsNullOrWhiteSpace(json))\r\n" +
                "            {\r\n" +
                "                return json;\r\n" +
                "            }\r\n\r\n" +
                "            string[] localizedFields =\r\n" +
                "            {\r\n" +
                "                \"description\",\r\n" +
                "                \"displayName\",\r\n" +
                "                \"releaseNotes\",\r\n" +
                "                \"thirdPartyNotices\",\r\n" +
                "                \"installationName\",\r\n" +
                "            };\r\n\r\n" +
                "            for (int i = 0; i < localizedFields.Length; i++)\r\n" +
                "            {\r\n" +
                "                string field = localizedFields[i];\r\n" +
                "                json = Regex.Replace(\r\n" +
                "                    json,\r\n" +
                "                    $@\"\\s*\\\"{field}\\\"\\s*:\\s*\\\"[^\\\"\\r\\n]*\\\"\\s*,?\",\r\n" +
                "                    string.Empty,\r\n" +
                "                    RegexOptions.IgnoreCase | RegexOptions.Multiline);\r\n" +
                "            }\r\n\r\n" +
                "            return json;\r\n" +
                "        }\r\n\r\n" +
                insertBefore;

            if (content.Contains(insertBefore))
            {
                content = content.Replace(insertBefore, sanitizeMethod);
            }
        }

        return content;
    }
}
#endif
