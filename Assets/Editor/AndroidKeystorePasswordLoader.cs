using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Unity はキーストアパスワードを ProjectSettings に残さない。
/// Publishing Settings に一度入れた内容を、この PC の EditorPrefs（Git に出ない）へ覚え、
/// 起動時と Android ビルド直前に戻す。
/// </summary>
[InitializeOnLoad]
public sealed class AndroidKeystorePasswordLoader : IPreprocessBuildWithReport
{
    private const string PrefsKeystore = "GundamCardGame.Android.keystorePass";
    private const string PrefsAlias = "GundamCardGame.Android.keyaliasPass";
    private const string RelativePath = "UserSettings/android-keystore.local.json";
    private const double CaptureIntervalSeconds = 1.5d;

    private static double _nextCaptureTime;
    private static bool _restoreQueued;

    public int callbackOrder => -1000;

    static AndroidKeystorePasswordLoader()
    {
        QueueRestore();
        EditorApplication.update -= CaptureFromPlayerSettingsIfPresent;
        EditorApplication.update += CaptureFromPlayerSettingsIfPresent;
    }

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report == null || report.summary.platform != BuildTarget.Android)
        {
            return;
        }

        RestoreToPlayerSettings();
        CaptureFromPlayerSettingsIfPresent(force: true);
    }

    [InitializeOnLoadMethod]
    private static void RestoreOnEditorLoad()
    {
        QueueRestore();
    }

    [MenuItem("Build/Android/キーストアパスワードをこのPCに保存", false, 180)]
    public static void OpenSaveWindow()
    {
        CaptureFromPlayerSettingsIfPresent(force: true);
        AndroidKeystorePasswordWindow.Open();
    }

    /// <summary>ビルド直前など、明示的に再適用する。</summary>
    public static bool ApplyFromLocalFileIfPresent()
    {
        return RestoreToPlayerSettings();
    }

    public static void SaveToLocalFile(string keystorePass, string keyaliasPass)
    {
        if (string.IsNullOrEmpty(keystorePass))
        {
            throw new ArgumentException("keystorePass が空です。");
        }

        Persist(keystorePass, string.IsNullOrEmpty(keyaliasPass) ? keystorePass : keyaliasPass);
        RestoreToPlayerSettings();
        Debug.Log("[AndroidKeystore] パスワードをこのPCに保存しました（EditorPrefs / UserSettings）。");
    }

    public static string GetLocalFilePath()
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", RelativePath));
    }

    public static void TryGetSavedPasswords(out string keystorePass, out string keyaliasPass)
    {
        TryReadSaved(out keystorePass, out keyaliasPass);
    }

    private static void QueueRestore()
    {
        if (_restoreQueued)
        {
            return;
        }

        _restoreQueued = true;
        EditorApplication.delayCall += () =>
        {
            _restoreQueued = false;
            RestoreToPlayerSettings();
        };
    }

    private static void CaptureFromPlayerSettingsIfPresent()
    {
        if (EditorApplication.timeSinceStartup < _nextCaptureTime)
        {
            return;
        }

        CaptureFromPlayerSettingsIfPresent(force: false);
    }

    private static void CaptureFromPlayerSettingsIfPresent(bool force)
    {
        _nextCaptureTime = EditorApplication.timeSinceStartup + CaptureIntervalSeconds;
        if (!PlayerSettings.Android.useCustomKeystore)
        {
            return;
        }

        string keystorePass = PlayerSettings.Android.keystorePass;
        string aliasPass = PlayerSettings.Android.keyaliasPass;
        if (string.IsNullOrEmpty(keystorePass))
        {
            return;
        }

        if (string.IsNullOrEmpty(aliasPass))
        {
            aliasPass = keystorePass;
        }

        if (!force
            && EditorPrefs.GetString(PrefsKeystore, string.Empty) == keystorePass
            && EditorPrefs.GetString(PrefsAlias, string.Empty) == aliasPass)
        {
            return;
        }

        Persist(keystorePass, aliasPass);
    }

    private static bool RestoreToPlayerSettings()
    {
        if (!TryReadSaved(out string keystorePass, out string aliasPass))
        {
            return false;
        }

        PlayerSettings.Android.useCustomKeystore = true;
        PlayerSettings.Android.keystorePass = keystorePass;
        PlayerSettings.Android.keyaliasPass = aliasPass;
        return true;
    }

    private static bool TryReadSaved(out string keystorePass, out string aliasPass)
    {
        keystorePass = EditorPrefs.GetString(PrefsKeystore, string.Empty);
        aliasPass = EditorPrefs.GetString(PrefsAlias, string.Empty);

        if (string.IsNullOrEmpty(keystorePass))
        {
            TryReadJsonFile(out keystorePass, out aliasPass);
        }

        if (string.IsNullOrEmpty(keystorePass))
        {
            keystorePass = Environment.GetEnvironmentVariable("ANDROID_KEYSTORE_PASS") ?? string.Empty;
            aliasPass = Environment.GetEnvironmentVariable("ANDROID_KEYALIAS_PASS") ?? string.Empty;
        }

        if (string.IsNullOrEmpty(keystorePass))
        {
            aliasPass = string.Empty;
            return false;
        }

        if (string.IsNullOrEmpty(aliasPass))
        {
            aliasPass = keystorePass;
        }

        return true;
    }

    private static void Persist(string keystorePass, string aliasPass)
    {
        EditorPrefs.SetString(PrefsKeystore, keystorePass);
        EditorPrefs.SetString(PrefsAlias, aliasPass);

        try
        {
            string path = GetLocalFilePath();
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var secrets = new LocalKeystoreSecrets
            {
                keystorePass = keystorePass,
                keyaliasPass = aliasPass,
            };
            File.WriteAllText(path, JsonUtility.ToJson(secrets, true));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[AndroidKeystore] UserSettings への保存に失敗: {e.Message}");
        }
    }

    private static bool TryReadJsonFile(out string keystorePass, out string aliasPass)
    {
        keystorePass = string.Empty;
        aliasPass = string.Empty;
        string path = GetLocalFilePath();
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            LocalKeystoreSecrets secrets = JsonUtility.FromJson<LocalKeystoreSecrets>(File.ReadAllText(path));
            if (secrets == null || string.IsNullOrEmpty(secrets.keystorePass))
            {
                return false;
            }

            keystorePass = secrets.keystorePass;
            aliasPass = string.IsNullOrEmpty(secrets.keyaliasPass) ? secrets.keystorePass : secrets.keyaliasPass;
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[AndroidKeystore] ローカルパスワードの読み込みに失敗: {e.Message}");
            return false;
        }
    }

    [Serializable]
    private sealed class LocalKeystoreSecrets
    {
        public string keystorePass;
        public string keyaliasPass;
    }
}

internal sealed class AndroidKeystorePasswordWindow : EditorWindow
{
    private string _keystorePass = string.Empty;
    private string _keyaliasPass = string.Empty;
    private bool _aliasSameAsKeystore = true;

    public static void Open()
    {
        AndroidKeystorePasswordWindow window = GetWindow<AndroidKeystorePasswordWindow>(
            true,
            "キーストアパスワード");
        window.minSize = new Vector2(420f, 200f);
        window.Show();
    }

    private void OnEnable()
    {
        AndroidKeystorePasswordLoader.TryGetSavedPasswords(out _keystorePass, out _keyaliasPass);
        _aliasSameAsKeystore = string.IsNullOrEmpty(_keyaliasPass) || _keyaliasPass == _keystorePass;
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Publishing Settings にパスワードを入れると、この PC に自動保存されます。\n"
            + "空のときはここで一度入れて保存してください。Git には出ません。",
            MessageType.Info);

        _keystorePass = EditorGUILayout.PasswordField("Keystore password", _keystorePass);
        _aliasSameAsKeystore = EditorGUILayout.Toggle("Key alias も同じ", _aliasSameAsKeystore);
        using (new EditorGUI.DisabledScope(_aliasSameAsKeystore))
        {
            _keyaliasPass = EditorGUILayout.PasswordField("Key alias password", _keyaliasPass);
        }

        EditorGUILayout.Space();
        if (GUILayout.Button("このPCに保存して適用", GUILayout.Height(28f)))
        {
            if (string.IsNullOrEmpty(_keystorePass))
            {
                EditorUtility.DisplayDialog("キーストア", "Keystore password を入力してください。", "OK");
                return;
            }

            try
            {
                AndroidKeystorePasswordLoader.SaveToLocalFile(
                    _keystorePass,
                    _aliasSameAsKeystore ? _keystorePass : _keyaliasPass);
                EditorUtility.DisplayDialog(
                    "キーストア",
                    "保存しました。次回の Unity 起動から自動入力されます。",
                    "OK");
                Close();
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("キーストア", e.Message, "OK");
            }
        }
    }
}
