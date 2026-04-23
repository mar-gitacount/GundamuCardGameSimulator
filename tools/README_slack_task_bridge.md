# Slack Task Bridge (Python)

`tools/slack_task_bridge.py` は、Slack の指示メッセージをローカルに保存する最小ブリッジです。

## できること

- Slack Events API から `/slack/events` で受信
- メッセージを `tools/slack_tasks.jsonl` に保存
- 任意で `#deploy` へ「受信した」通知

## 起動（受信サーバー）

```bash
python tools/slack_task_bridge.py
```

## 推奨環境変数

- `SLACK_SIGNING_SECRET` : Slack App の Signing Secret
- `SLACK_BOT_TOKEN` : Bot Token (`xoxb-...`)
- `SLACK_TASK_CHANNEL_ID` : 指示を受けるチャンネルID（例: `C0123...`）
- `SLACK_DEPLOY_CHANNEL_ID` : 受信通知を出すチャンネルID（例: `#deploy` の ID）
- `SLACK_TASK_PREFIX` : この接頭辞で始まる投稿だけをタスク化（例: `指示:`）
- `PORT` : 受け口ポート（デフォルト `8787`）

PowerShell 例:

```powershell
$env:SLACK_SIGNING_SECRET="..."
$env:SLACK_BOT_TOKEN="xoxb-..."
$env:SLACK_TASK_CHANNEL_ID="Cxxxxxxxx"
$env:SLACK_DEPLOY_CHANNEL_ID="C0B069U937A"
$env:SLACK_TASK_PREFIX="指示:"
$env:PORT="8787"
python tools/slack_task_bridge.py
```

## Slack App 側設定

1. **Event Subscriptions** を ON
2. Request URL を `https://<公開URL>/slack/events` に設定  
   - ローカル検証は `ngrok` 等でトンネル公開
3. Subscribe to bot events に `message.channels` を追加
4. App をワークスペースへ再インストール

## 出力データ

`tools/slack_tasks.jsonl` に1行1JSONで追記されます。

---

## Python -> Cursor 自動実行

`tools/slack_cursor_runner.py` は `slack_tasks.jsonl` の新着タスクを監視し、
`CURSOR_TASK_COMMAND_TEMPLATE` で指定したコマンドを実行します。

### 追加の環境変数

- `CURSOR_TASK_COMMAND_TEMPLATE` : タスクごとに実行するコマンド
  - 使えるプレースホルダ: `{text}`, `{ts}`, `{user}`, `{channel}`
- `SLACK_RUN_MODE` : `command` または `chat`
  - `chat` の場合、`cursor --chat -` にタスク本文を stdin で渡して Cursor Chat を開く
- `SLACK_RUNNER_DRY_RUN` : `1` なら実行せずログのみ
- `SLACK_RUNNER_POLL_SECONDS` : 監視間隔（デフォルト `1.0`）
- `SLACK_RUNNER_STATE_FILE` : 最後に処理した ts の保存先

PowerShell 例（まずは Dry Run 推奨）:

```powershell
$env:SLACK_RUNNER_DRY_RUN="1"
$env:CURSOR_TASK_COMMAND_TEMPLATE='echo CURSOR_TASK "{text}"'
& "C:\Users\user\AppData\Local\Programs\Python\Python313\python.exe" "tools/slack_cursor_runner.py"
```

### 本番実行例

#### A. Cursor Chat に直接流す（おすすめ）

```powershell
$env:SLACK_RUNNER_DRY_RUN="0"
$env:SLACK_RUN_MODE="chat"
& "C:\Users\user\AppData\Local\Programs\Python\Python313\python.exe" "tools/slack_cursor_runner.py"
```

#### B. 任意コマンド実行モード

```powershell
$env:SLACK_RUNNER_DRY_RUN="0"
$env:SLACK_RUN_MODE="command"
$env:CURSOR_TASK_COMMAND_TEMPLATE='echo CURSOR_TASK "{text}"'
& "C:\Users\user\AppData\Local\Programs\Python\Python313\python.exe" "tools/slack_cursor_runner.py"
```
