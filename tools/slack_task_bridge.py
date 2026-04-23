#!/usr/bin/env python3
"""
Slack -> Local Task Bridge (single-file, stdlib only).

What this does:
- Receives Slack Events API callbacks at /slack/events
- Stores incoming task-like messages to JSONL
- Optionally posts a short receipt notice to #deploy

Env vars:
- SLACK_BOT_TOKEN          (optional, for deploy notification)
- SLACK_SIGNING_SECRET     (optional but recommended)
- SLACK_TASK_CHANNEL_ID    (optional, only accept this channel)
- SLACK_DEPLOY_CHANNEL_ID  (optional, notify this channel)
- SLACK_TASK_STORE_PATH    (optional, default: ./tools/slack_tasks.jsonl)
- PORT                     (optional, default: 8787)
"""

from __future__ import annotations

import hashlib
import hmac
import json
import os
import time
import urllib.error
import urllib.request
from datetime import datetime, timezone
from http import HTTPStatus
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import Any, Dict


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

BOT_TOKEN = os.getenv("SLACK_BOT_TOKEN", "").strip()
SIGNING_SECRET = os.getenv("SLACK_SIGNING_SECRET", "").strip()
TASK_CHANNEL_ID = os.getenv("SLACK_TASK_CHANNEL_ID", "").strip()
DEPLOY_CHANNEL_ID = os.getenv("SLACK_DEPLOY_CHANNEL_ID", "").strip()
TASK_PREFIX = os.getenv("SLACK_TASK_PREFIX", "").strip()
PORT = int(os.getenv("PORT", "8787"))
STORE_PATH = Path(os.getenv("SLACK_TASK_STORE_PATH", "tools/slack_tasks.jsonl"))


def _verify_slack_signature(headers: Dict[str, str], raw_body: bytes) -> bool:
    if not SIGNING_SECRET:
        # Secret is optional for local prototype.
        return True

    slack_sig = headers.get("X-Slack-Signature", "")
    slack_ts = headers.get("X-Slack-Request-Timestamp", "")
    if not slack_sig or not slack_ts:
        return False

    try:
        ts_int = int(slack_ts)
    except ValueError:
        return False

    # Reject replays older than 5 minutes.
    if abs(int(time.time()) - ts_int) > 60 * 5:
        return False

    base = f"v0:{slack_ts}:{raw_body.decode('utf-8')}".encode("utf-8")
    my_sig = "v0=" + hmac.new(
        SIGNING_SECRET.encode("utf-8"), base, hashlib.sha256
    ).hexdigest()
    return hmac.compare_digest(my_sig, slack_sig)


def _post_slack_message(channel_id: str, text: str) -> None:
    if not BOT_TOKEN or not channel_id:
        return

    payload = json.dumps({"channel": channel_id, "text": text}).encode("utf-8")
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
                print(f"[WARN] chat.postMessage failed: {body}")
    except urllib.error.URLError as exc:
        print(f"[WARN] Slack post error: {exc}")


def _store_task(event: Dict[str, Any]) -> Dict[str, Any]:
    STORE_PATH.parent.mkdir(parents=True, exist_ok=True)
    item = {
        "stored_at": datetime.now(timezone.utc).isoformat(),
        "channel": event.get("channel"),
        "user": event.get("user"),
        "ts": event.get("ts"),
        "text": event.get("text", ""),
    }
    with STORE_PATH.open("a", encoding="utf-8") as f:
        f.write(json.dumps(item, ensure_ascii=False) + "\n")
    return item


class SlackHandler(BaseHTTPRequestHandler):
    def _send_json(self, status: int, data: Dict[str, Any]) -> None:
        body = json.dumps(data, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self) -> None:  # noqa: N802
        if self.path in ("/", "/healthz"):
            self._send_json(HTTPStatus.OK, {"ok": True, "service": "slack-task-bridge"})
            return
        self._send_json(HTTPStatus.NOT_FOUND, {"ok": False, "error": "not_found"})

    def do_POST(self) -> None:  # noqa: N802
        if self.path != "/slack/events":
            self._send_json(HTTPStatus.NOT_FOUND, {"ok": False, "error": "not_found"})
            return

        length = int(self.headers.get("Content-Length", "0"))
        raw = self.rfile.read(length)
        headers = {k: v for k, v in self.headers.items()}

        if not _verify_slack_signature(headers, raw):
            self._send_json(HTTPStatus.UNAUTHORIZED, {"ok": False, "error": "bad_signature"})
            return

        try:
            payload = json.loads(raw.decode("utf-8"))
        except json.JSONDecodeError:
            self._send_json(HTTPStatus.BAD_REQUEST, {"ok": False, "error": "invalid_json"})
            return

        # Slack URL verification challenge
        if payload.get("type") == "url_verification":
            self._send_json(HTTPStatus.OK, {"challenge": payload.get("challenge", "")})
            return

        event = payload.get("event", {})
        if event.get("type") != "message" or event.get("subtype"):
            self._send_json(HTTPStatus.OK, {"ok": True, "ignored": True})
            return

        if TASK_CHANNEL_ID and event.get("channel") != TASK_CHANNEL_ID:
            self._send_json(HTTPStatus.OK, {"ok": True, "ignored": "other_channel"})
            return

        text = str(event.get("text", ""))
        if TASK_PREFIX and not text.startswith(TASK_PREFIX):
            self._send_json(HTTPStatus.OK, {"ok": True, "ignored": "prefix_mismatch"})
            return

        stored = _store_task(event)
        text_preview = (stored["text"] or "").strip().replace("\n", " ")
        if len(text_preview) > 80:
            text_preview = text_preview[:77] + "..."

        _post_slack_message(
            DEPLOY_CHANNEL_ID,
            f"実装指示を受信: user={stored['user']} text=\"{text_preview}\"",
        )
        self._send_json(HTTPStatus.OK, {"ok": True, "stored": stored})


def main() -> None:
    print(f"[INFO] Slack task bridge starting on :{PORT}")
    print(f"[INFO] Task store: {STORE_PATH.resolve()}")
    server = ThreadingHTTPServer(("0.0.0.0", PORT), SlackHandler)
    server.serve_forever()


if __name__ == "__main__":
    main()
