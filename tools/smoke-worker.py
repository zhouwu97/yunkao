"""Smoke-test a packaged Worker from an unrelated directory, without repo imports."""

from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import queue
import subprocess
import sys
import tempfile
import threading
import zipfile

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
from worker.protocol import read_message, write_message  # noqa: E402


def smoke(worker: Path) -> dict:
    worker = worker.resolve(strict=True)
    responses = queue.Queue()
    with tempfile.TemporaryDirectory(prefix="yunkao packaged smoke ") as directory:
        root = Path(directory)
        with (root / "stderr.log").open("wb") as errors:
            process = subprocess.Popen(
                [str(worker)], cwd=root, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                stderr=errors, creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
                env={key: value for key, value in os.environ.items()
                     if key not in {"PYTHONPATH", "PYTHONHOME"}},
            )

            def read_responses():
                try:
                    while (message := read_message(process.stdout)) is not None:
                        responses.put(message)
                except Exception as error:
                    responses.put(error)
                finally:
                    responses.put(EOFError("Worker closed stdout"))

            reader = threading.Thread(target=read_responses, daemon=True)
            reader.start()
            counter = 0

            def call(method, params=None):
                nonlocal counter
                counter += 1
                request_id = str(counter)
                write_message({"protocol": 1, "id": request_id, "method": method,
                               "params": params or {}}, process.stdin)
                while True:
                    message = responses.get(timeout=30)
                    if isinstance(message, Exception):
                        raise message
                    if message.get("type") == "event":
                        continue
                    if message.get("id") != request_id or not message.get("ok"):
                        raise AssertionError(f"Unexpected Worker response: {message}")
                    return message["result"]

            try:
                assert call("health")["status"] == "ready"
                questions = []
                for fixture in ("single", "multiple", "judgment", "image", "mathjax", "long"):
                    question = call("parseQuestion", {
                        "html": (ROOT / "tests" / "fixtures" / f"{fixture}.html").read_text(encoding="utf-8"),
                        "baseUrl": "https://www.cctrcloud.net/practice/",
                    })
                    assert question["question_id"] == f"{fixture}-001"
                    questions.append(question)

                # Use text-only content to avoid network/Office dependencies.
                question = questions[0]
                question["answer"] = "ANSWER_SENTINEL_20261115"
                question["analysis"] = "ANALYSIS_SENTINEL_20261115"
                exports = 0
                for format_name in ("md", "txt", "docx"):
                    for include_answers in (True, False):
                        path = root / f"中文 题库 {format_name} {include_answers}.{format_name}"
                        result = call("export", {"format": format_name, "filePath": str(path),
                                      "questions": [question], "includeAnswers": include_answers})
                        assert result["count"] == 1 and path.stat().st_size > 0
                        if format_name == "docx":
                            with zipfile.ZipFile(path) as archive:
                                content = archive.read("word/document.xml").decode("utf-8")
                        else:
                            content = path.read_text(encoding="utf-8")
                        assert "Content-Length" in content
                        for sentinel in (question["answer"], question["analysis"]):
                            assert (sentinel in content) == include_answers
                        exports += 1

                # An image failure must stay on stderr and leave framing usable.
                broken = dict(question, title="图片容错 ![img]<data:image/png;base64,invalid>")
                call("export", {"format": "docx", "filePath": str(root / "broken.docx"),
                                "questions": [broken]})
                assert call("health")["status"] == "ready"
                call("shutdown")
                process.wait(timeout=5)
                assert process.returncode == 0
                return {"worker": str(worker), "parsedFixtures": len(questions),
                        "answerAndPracticeExports": exports, "imageFailureRecovery": "passed",
                        "shutdown": "passed"}
            finally:
                if process.poll() is None:
                    process.kill()
                    process.wait(timeout=5)
                reader.join(timeout=2)
                process.stdin.close()
                process.stdout.close()


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("worker", type=Path)
    args = parser.parse_args()
    print(json.dumps(smoke(args.worker), ensure_ascii=False, indent=2))
