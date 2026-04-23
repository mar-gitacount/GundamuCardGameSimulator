#!/usr/bin/env python3
"""
Slack task queue -> local command runner.

Reads new lines from tools/slack_tasks.jsonl and runs a local command
for each task (intended for Cursor CLI integration).

Stdlib only.
"""

from __future__ import annotations

import json
import os
import subprocess
import time
from pathlib import Path
import shutil
import urllib.error
import urllib.request
from typing import Dict, Optional


def load_env_file(path: Path) -> None:
    if not path.exists():
        return
    for raw in path.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        k, v = line.split("=", 1)
        key = k.strip()
        value = v.strip().strip('"').strip("'")
        if key and key not in os.environ:
            os.environ[key] = value


BASE_DIR = Path(__file__).resolve().parent
load_env_file(BASE_DIR / ".env")

TASK_STORE = Path(os.getenv("SLACK_TASK_STORE_PATH", str(BASE_DIR / "slack_tasks.jsonl")))
STATE_FILE = Path(os.getenv("SLACK_RUNNER_STATE_FILE", str(BASE_DIR / ".slack_runner_state.json")))
POLL_SECONDS = float(os.getenv("SLACK_RUNNER_POLL_SECONDS", "1.0"))
DRY_RUN = os.getenv("SLACK_RUNNER_DRY_RUN", "0").strip() in ("1", "true", "TRUE", "yes")
RUN_MODE = os.getenv("SLACK_RUN_MODE", "command").strip().lower()
BOT_TOKEN = os.getenv("SLACK_BOT_TOKEN", "").strip()
DEPLOY_CHANNEL_ID = os.getenv("SLACK_DEPLOY_CHANNEL_ID", "").strip()
COMMAND_TEMPLATE = os.getenv(
    "CURSOR_TASK_COMMAND_TEMPLATE",
    # Example: cursor-agent run --prompt "{text}"
    # Keep default harmless.
    'echo TASK_RECEIVED "{text}"',
)
CURSOR_EXE = os.getenv("CURSOR_EXE", "").strip()


def resolve_cursor_exe() -> str:
    if CURSOR_EXE:
        return CURSOR_EXE
    found = shutil.which("cursor")
    if found:
        return found
    local = os.getenv("LOCALAPPDATA", "")
    if local:
        candidate = Path(local) / "Programs" / "cursor" / "resources" / "app" / "bin" / "cursor.cmd"
        if candidate.exists():
            return str(candidate)
    return "cursor"


def load_state() -> Dict[str, str]:
    if not STATE_FILE.exists():
        return {"last_ts": ""}
    try:
        data = json.loads(STATE_FILE.read_text(encoding="utf-8"))
        if isinstance(data, dict):
            return {"last_ts": str(data.get("last_ts", ""))}
    except Exception:
        pass
    return {"last_ts": ""}


def save_state(last_ts: str) -> None:
    STATE_FILE.parent.mkdir(parents=True, exist_ok=True)
    STATE_FILE.write_text(json.dumps({"last_ts": last_ts}, ensure_ascii=False), encoding="utf-8")


def post_deploy_message(text: str) -> None:
    if not BOT_TOKEN or not DEPLOY_CHANNEL_ID:
        return
    payload = json.dumps({"channel": DEPLOY_CHANNEL_ID, "text": text}).encode("utf-8")
    req = urllib.request.Request(
        "https://slack.com/api/chat.postMessage",
        data=payload,
        method="POST",
        headers={
            "Authorization": f"Bearer {BOT_TOKEN}",
            "Content-Type": "application/json; charset=utf-8",
        },
    )
    try:
        with urllib.request.urlopen(req, timeout=10) as resp:
            body = json.loads(resp.read().decode("utf-8"))
            if not body.get("ok", False):
                print(f"[WARN] deploy post failed: {body}")
    except urllib.error.URLError as exc:
        print(f"[WARN] deploy post error: {exc}")


def _task_preview(text: str, limit: int = 80) -> str:
    one_line = (text or "").strip().replace("\n", " ")
    if len(one_line) <= limit:
        return one_line
    return one_line[: limit - 3] + "..."


def iter_tasks() -> list[Dict[str, str]]:
    if not TASK_STORE.exists():
        return []
    rows: list[Dict[str, str]] = []
    for raw in TASK_STORE.read_text(encoding="utf-8").splitlines():
        if not raw.strip():
            continue
        try:
            obj = json.loads(raw)
            if isinstance(obj, dict):
                rows.append(
                    {
                        "ts": str(obj.get("ts", "")),
                        "text": str(obj.get("text", "")),
                        "user": str(obj.get("user", "")),
                        "channel": str(obj.get("channel", "")),
                    }
                )
        except json.JSONDecodeError:
            continue
    return rows


def _is_reasonable_ts(ts: str) -> bool:
    """Reject obviously invalid/future timestamps (e.g. manual test values)."""
    v = _ts_value(ts)
    if v <= 0:
        return False
    # Allow small clock drift, but ignore values far in the future.
    return v <= (time.time() + 60 * 60 * 24)


def build_command(task: Dict[str, str]) -> str:
    return COMMAND_TEMPLATE.format(
        text=task["text"].replace('"', '\\"'),
        ts=task["ts"],
        user=task["user"],
        channel=task["channel"],
    )


def run_task(task: Dict[str, str]) -> int:
    if RUN_MODE == "chat":
        cursor_exe = resolve_cursor_exe()
        print(f"[RUN] {cursor_exe} --chat -")
        if DRY_RUN:
            return 0
        # Launch chat non-blocking so the runner can keep processing next tasks.
        proc = subprocess.Popen(
            [cursor_exe, "--chat", "-"],
            stdin=subprocess.PIPE,
            text=True,
            creationflags=getattr(subprocess, "DETACHED_PROCESS", 0)
            | getattr(subprocess, "CREATE_NEW_PROCESS_GROUP", 0),
        )
        if proc.stdin is not None:
            proc.stdin.write(task["text"])
            proc.stdin.close()
        return 0

    cmd = build_command(task)
    print(f"[RUN] {cmd}")
    if DRY_RUN:
        return 0
    completed = subprocess.run(cmd, shell=True, check=False)
    return int(completed.returncode)


def get_new_task(last_ts: str) -> Optional[Dict[str, str]]:
    tasks = [t for t in iter_tasks() if _is_reasonable_ts(t["ts"])]
    if not tasks:
        return None
    if not last_ts:
        # First boot: consume only newest to avoid replay flood.
        return tasks[-1]
    last = _ts_value(last_ts)
    candidates = [t for t in tasks if _ts_value(t["ts"]) > last]
    if not candidates:
        return None
    # Process from the earliest newer timestamp (in-order catch-up).
    candidates.sort(key=lambda t: _ts_value(t["ts"]))
    return candidates[0]


def _ts_value(ts: str) -> float:
    try:
        return float(ts)
    except Exception:
        return 0.0


def main() -> None:
    print("[INFO] slack_cursor_runner started")
    print(f"[INFO] task store: {TASK_STORE}")
    print(f"[INFO] state file: {STATE_FILE}")
    print(f"[INFO] dry run: {DRY_RUN}")
    print(f"[INFO] run mode: {RUN_MODE}")
    state = load_state()
    last_ts = state["last_ts"]

    while True:
        task = get_new_task(last_ts)
        if task is None:
            time.sleep(POLL_SECONDS)
            continue

        preview = _task_preview(task["text"])
        post_deploy_message(f"開発着手: {preview}")
        code = run_task(task)
        if code == 0:
            last_ts = task["ts"]
            save_state(last_ts)
            print(f"[OK] processed ts={last_ts}")
            post_deploy_message(f"実装完了！ {preview}")
        else:
            print(f"[WARN] command failed code={code}, will retry ts={task['ts']}")
            post_deploy_message(f"実装失敗: {preview} (code={code})")
            time.sleep(max(2.0, POLL_SECONDS))


if __name__ == "__main__":
    main()
