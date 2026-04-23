// UnityのConsoleエラーをSlackに飛ばす簡易スクリプト例
using System;
using UnityEngine;
using UnityEngine.Networking;

public class SlackNotifier : MonoBehaviour {
    private const string WebhookEnvKey = "SLACK_WEBHOOK_URL";

    [SerializeField] private string webhookUrl;

    void Awake() {
        DontDestroyOnLoad(this.gameObject); // シーンが変わってもこのオブジェクトを消さない

        // 1) Inspector値 2) 環境変数 の順でWebhookを解決する。
        if (string.IsNullOrWhiteSpace(webhookUrl)) {
            webhookUrl = Environment.GetEnvironmentVariable(WebhookEnvKey);
        }

        if (string.IsNullOrWhiteSpace(webhookUrl)) {
            Debug.LogWarning($"SlackNotifier: Webhook未設定です。環境変数 {WebhookEnvKey} を設定してください。");
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
}