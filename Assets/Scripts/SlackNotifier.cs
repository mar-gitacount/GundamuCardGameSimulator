// UnityのConsoleエラーをSlackに飛ばす簡易スクリプト例
using System;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

public class SlackNotifier : MonoBehaviour {
    private const string WebhookEnvKey = "SLACK_WEBHOOK_URL";

    [SerializeField] private string webhookUrl;

    void Awake() {
        DontDestroyOnLoad(this.gameObject); // シーンが変わってもこのオブジェクトを消さない

        // 1) Inspector値 2) 環境変数 3) tools/.env の順でWebhookを解決する。
        if (string.IsNullOrWhiteSpace(webhookUrl)) {
            webhookUrl = Environment.GetEnvironmentVariable(WebhookEnvKey);
        }

        if (string.IsNullOrWhiteSpace(webhookUrl)) {
            webhookUrl = ResolveWebhookFromEnvFiles();
        }

        if (string.IsNullOrWhiteSpace(webhookUrl)) {
            Debug.LogWarning($"SlackNotifier: Webhook未設定です。tools/.env の {WebhookEnvKey} を設定してください。");
        }
    }

    void OnEnable() {
        Application.logMessageReceived += HandleLog;
    }

    void OnDisable() {
        Application.logMessageReceived -= HandleLog;
    }

    void HandleLog(string logString, string stackTrace, LogType type) {
        if ((type == LogType.Error || type == LogType.Exception) && !string.IsNullOrWhiteSpace(webhookUrl)) {
            StartCoroutine(SendToSlack("⚠️ Unityエラー発生: " + logString));
        }
    }

    System.Collections.IEnumerator SendToSlack(string message) {
        string json = "{\"text\":\"" + message + "\"}";
        using (UnityWebRequest request = new UnityWebRequest(webhookUrl, "POST")) {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            yield return request.SendWebRequest();
        }
    }

    private string ResolveWebhookFromEnvFiles() {
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
        if (string.IsNullOrWhiteSpace(projectRoot)) {
            return null;
        }

        string toolsEnvPath = Path.Combine(projectRoot, "tools", ".env");
        string fromFile = ReadEnvValue(toolsEnvPath, WebhookEnvKey);
        if (!string.IsNullOrWhiteSpace(fromFile)) {
            return fromFile;
        }

        // 互換用キー
        fromFile = ReadEnvValue(toolsEnvPath, "SLACK_WEBHOOK");
        if (!string.IsNullOrWhiteSpace(fromFile)) {
            return fromFile;
        }

        return null;
    }

    private string ReadEnvValue(string envPath, string key) {
        if (string.IsNullOrWhiteSpace(envPath) || !File.Exists(envPath)) {
            return null;
        }

        try {
            foreach (string raw in File.ReadAllLines(envPath)) {
                if (string.IsNullOrWhiteSpace(raw)) {
                    continue;
                }

                string line = raw.Trim();
                if (line.StartsWith("#") || !line.Contains("=")) {
                    continue;
                }

                int idx = line.IndexOf('=');
                string k = line.Substring(0, idx).Trim();
                if (!string.Equals(k, key, StringComparison.Ordinal)) {
                    continue;
                }

                string value = line.Substring(idx + 1).Trim().Trim('"').Trim('\'');
                if (!string.IsNullOrWhiteSpace(value)) {
                    return value;
                }
            }
        } catch (Exception ex) {
            Debug.LogWarning($"SlackNotifier: .env読込失敗 {envPath} : {ex.Message}");
        }

        return null;
    }
}