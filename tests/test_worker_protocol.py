"""Worker framing、解析和轻量导出的端到端回归测试。"""

from __future__ import annotations

import io
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import threading
from concurrent.futures import ThreadPoolExecutor
from unittest.mock import patch

from worker.protocol import read_message, write_message
from worker.worker_main import WorkerRuntime, WorkerCancelled, dispatch_request


ROOT = Path(__file__).resolve().parents[1]


class WorkerProtocolTests(unittest.TestCase):
    def test_cancellation_and_health_bypass_a_saturated_worker_pool(self):
        runtime = WorkerRuntime()
        messages = []
        runtime.send = messages.append
        started = threading.Event()
        exported = []

        def blocking_export(params, runtime, cancel_event, request_id):
            exported.append(request_id)
            started.set()
            if cancel_event.wait(timeout=3):
                raise WorkerCancelled()
            raise AssertionError("control request failed to cancel the running export")

        def request(request_id, method, params=None):
            return {"id": request_id, "method": method, "params": params or {}}

        with patch("worker.worker_main._export", blocking_export), ThreadPoolExecutor(max_workers=1) as executor:
            active = dispatch_request(request("active", "export"), runtime, executor)
            self.assertTrue(started.wait(timeout=2))
            queued = dispatch_request(request("queued", "export"), runtime, executor)
            dispatch_request(request("cancel-queued", "cancel", {"targetId": "queued"}), runtime, executor)
            dispatch_request(request("health", "health"), runtime, executor)
            self.assertTrue(next(message for message in messages if message["id"] == "health")["ok"])
            self.assertTrue(next(message for message in messages if message["id"] == "cancel-queued")["result"]["cancelled"])
            dispatch_request(request("cancel-active", "cancel", {"targetId": "active"}), runtime, executor)
            active.result(timeout=2)
            queued.result(timeout=2)

        self.assertEqual(exported, ["active"])
        self.assertEqual(next(message for message in messages if message["id"] == "queued")["error"]["code"], "cancelled")
        self.assertFalse(runtime.cancel_events)

    def test_unicode_content_length_round_trip(self):
        stream = io.BytesIO()
        write_message({"message": "中文题目 ✅"}, stream)
        stream.seek(0)
        self.assertEqual(read_message(stream), {"message": "中文题目 ✅"})

    def test_worker_health_parse_and_markdown_export(self):
        fixture = (ROOT / "tests" / "fixtures" / "active_question.html").read_text(encoding="utf-8")
        with tempfile.TemporaryDirectory() as temp_dir:
            output_path = Path(temp_dir) / "questions.md"
            process = subprocess.Popen(
                [sys.executable, str(ROOT / "worker" / "worker_main.py")],
                cwd=ROOT,
                stdin=subprocess.PIPE,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                env={**os.environ, "PYTHONUNBUFFERED": "1"},
            )
            assert process.stdin is not None
            assert process.stdout is not None
            try:
                self._send(process, {"protocol": 1, "id": "health", "method": "health", "params": {}})
                health = self._read_until(process, "health")
                self.assertTrue(health["ok"])
                self.assertEqual(health["result"]["status"], "ready")

                self._send(
                    process,
                    {
                        "protocol": 1,
                        "id": "parse",
                        "method": "parseQuestion",
                        "params": {"html": fixture},
                    },
                )
                parsed = self._read_until(process, "parse")
                self.assertTrue(parsed["ok"])
                self.assertEqual(parsed["result"]["question_id"], "fixture-001")
                self.assertEqual(parsed["result"]["answer"], "A")
                self.assertEqual(parsed["result"]["options"], ["A. Content-Length 帧", "B. 依赖 stdout 日志"])

                self._send(
                    process,
                    {
                        "protocol": 1,
                        "id": "export",
                        "method": "export",
                        "params": {
                            "format": "md",
                            "filePath": str(output_path),
                            "questions": [parsed["result"]],
                            "includeAnswers": True,
                        },
                    },
                )
                exported = self._read_until(process, "export")
                self.assertTrue(exported["ok"])
                self.assertTrue(output_path.exists())
                self.assertIn("Content-Length", output_path.read_text(encoding="utf-8"))

                self._send(process, {"protocol": 1, "id": "shutdown", "method": "shutdown", "params": {}})
                self.assertTrue(self._read_until(process, "shutdown")["ok"])
                process.wait(timeout=5)
            finally:
                if process.poll() is None:
                    process.kill()
                    process.wait(timeout=5)
                if process.stdin is not None:
                    process.stdin.close()
                if process.stdout is not None:
                    process.stdout.close()
                if process.stderr is not None:
                    process.stderr.close()

    def test_worker_protocol_parses_all_supported_question_fixtures(self):
        fixtures = {
            "single.html": "single-001",
            "multiple.html": "multiple-001",
            "judgment.html": "judgment-001",
            "image.html": "image-001",
            "mathjax.html": "mathjax-001",
            "long.html": "long-001",
        }
        process = subprocess.Popen(
            [sys.executable, str(ROOT / "worker" / "worker_main.py")],
            cwd=ROOT,
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            env={**os.environ, "PYTHONUNBUFFERED": "1"},
        )
        assert process.stdin is not None
        assert process.stdout is not None
        try:
            for index, (name, question_id) in enumerate(fixtures.items(), start=1):
                self._send(
                    process,
                    {
                        "protocol": 1,
                        "id": f"fixture-{index}",
                        "method": "parseQuestion",
                        "params": {
                            "html": (ROOT / "tests" / "fixtures" / name).read_text(encoding="utf-8"),
                            "baseUrl": "https://www.cctrcloud.net/practice/",
                        },
                    },
                )
                response = self._read_until(process, f"fixture-{index}")
                self.assertTrue(response["ok"], name)
                self.assertEqual(response["result"]["question_id"], question_id)

            self._send(process, {"protocol": 1, "id": "shutdown", "method": "shutdown", "params": {}})
            self.assertTrue(self._read_until(process, "shutdown")["ok"])
            process.wait(timeout=5)
        finally:
            if process.poll() is None:
                process.kill()
                process.wait(timeout=5)
            if process.stdin is not None:
                process.stdin.close()
            if process.stdout is not None:
                process.stdout.close()
            if process.stderr is not None:
                process.stderr.close()

    @staticmethod
    def _send(process: subprocess.Popen, message: dict) -> None:
        assert process.stdin is not None
        write_message(message, process.stdin)

    @staticmethod
    def _read_until(process: subprocess.Popen, request_id: str) -> dict:
        assert process.stdout is not None
        while True:
            message = read_message(process.stdout)
            if message is None:
                raise AssertionError(f"Worker 在响应 {request_id} 前退出")
            if message.get("type") == "event":
                continue
            if message.get("id") == request_id:
                return message


if __name__ == "__main__":
    unittest.main()
