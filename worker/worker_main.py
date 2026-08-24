"""YunKao.Worker：无 UI 的 Parser / Exporter 进程。"""

from __future__ import annotations

import os
import sys
import threading
import traceback
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path


WORKER_DIR = Path(__file__).resolve().parent
PROJECT_ROOT = WORKER_DIR.parent
sys.path.insert(0, str(PROJECT_ROOT))

from modules.exporter import (  # noqa: E402
    export_to_docx,
    export_to_markdown,
    export_to_pdf,
    export_to_txt,
)
from modules.question_parser import parse_active_question  # noqa: E402
from worker.protocol import read_message, write_message  # noqa: E402


class WorkerCancelled(Exception):
    """任务被 C# 端主动取消。"""


class WorkerRuntime:
    def __init__(self):
        self.output_lock = threading.Lock()
        self.cancel_events: dict[str, threading.Event] = {}
        self.cancel_lock = threading.Lock()
        self.stopping = threading.Event()

    def send(self, message):
        with self.output_lock:
            write_message(message)

    def response(self, request_id, result=None, error=None):
        payload = {
            "protocol": 1,
            "id": request_id,
            "ok": error is None,
        }
        if error is None:
            payload["result"] = result
        else:
            payload["error"] = error
        self.send(payload)

    def event(self, name, data):
        self.send({"type": "event", "event": name, "data": data})

    def cancellation_event(self, request_id):
        with self.cancel_lock:
            event = threading.Event()
            self.cancel_events[request_id] = event
            return event

    def remove_cancellation_event(self, request_id):
        with self.cancel_lock:
            self.cancel_events.pop(request_id, None)

    def cancel(self, request_id):
        with self.cancel_lock:
            event = self.cancel_events.get(request_id)
            if event:
                event.set()
                return True
        return False


def _ensure_not_cancelled(cancel_event):
    if cancel_event.is_set():
        raise WorkerCancelled()


def _export(params, runtime, cancel_event, request_id):
    questions = params.get("questions") or []
    file_path = str(params.get("filePath") or "")
    export_format = str(params.get("format") or "").lower()
    include_answers = bool(params.get("includeAnswers", True))
    watermark = bool(params.get("watermark", True))
    if not file_path:
        raise ValueError("filePath is required")

    total = len(questions)

    def progress(current, progress_total, message):
        _ensure_not_cancelled(cancel_event)
        runtime.event(
            "exportProgress",
            {
                "requestId": request_id,
                "current": int(current),
                "total": int(progress_total or total),
                "message": str(message or "正在生成..."),
            },
        )

    progress(0, total, "正在准备导出...")
    _ensure_not_cancelled(cancel_event)
    if export_format == "pdf":
        export_to_pdf(questions, file_path, progress, include_answers=include_answers)
    elif export_format == "docx":
        export_to_docx(
            questions,
            file_path,
            progress_callback=progress,
            watermark=watermark,
            include_answers=include_answers,
        )
    elif export_format in {"md", "markdown"}:
        export_to_markdown(questions, file_path, include_answers=include_answers)
    elif export_format == "txt":
        export_to_txt(questions, file_path, include_answers=include_answers)
    else:
        raise ValueError(f"unsupported export format: {export_format}")

    progress(total, total, "导出完成")
    return {"filePath": file_path, "format": export_format, "count": total}


def _run_request(request, runtime):
    request_id = str(request.get("id") or "")
    method = str(request.get("method") or "")
    params = request.get("params") or {}
    if method == "health":
        runtime.response(
            request_id,
            {
                "status": "ready",
                "version": "2.0.0-worker",
                "pid": os.getpid(),
            },
        )
        return
    if method == "cancel":
        target_id = str(params.get("targetId") or "")
        runtime.response(request_id, {"cancelled": runtime.cancel(target_id)})
        return
    if method == "shutdown":
        runtime.response(request_id, {"status": "shutting_down"})
        runtime.stopping.set()
        return

    cancel_event = runtime.cancellation_event(request_id)
    try:
        if method == "parseQuestion":
            html = str(params.get("html") or "")
            result = parse_active_question(html, str(params.get("baseUrl") or ""))
            if result is None:
                runtime.response(
                    request_id,
                    error={"code": "question_not_ready", "message": "active question was not found"},
                )
                return
            runtime.response(request_id, result)
        elif method == "export":
            runtime.response(request_id, _export(params, runtime, cancel_event, request_id))
        else:
            raise ValueError(f"unknown method: {method}")
    except WorkerCancelled:
        runtime.response(request_id, error={"code": "cancelled", "message": "任务已取消"})
    except Exception as error:  # noqa: BLE001
        print(f"worker request failed ({method}): {error}", file=sys.stderr, flush=True)
        runtime.response(
            request_id,
            error={"code": "parser_error" if method == "parseQuestion" else "worker_error", "message": str(error)},
        )
    finally:
        runtime.remove_cancellation_event(request_id)


def main():
    runtime = WorkerRuntime()
    executor = ThreadPoolExecutor(max_workers=4, thread_name_prefix="yunkao-worker")
    futures = []
    try:
        while not runtime.stopping.is_set():
            try:
                request = read_message()
            except Exception as error:  # noqa: BLE001
                print(f"worker protocol failed: {error}", file=sys.stderr, flush=True)
                break
            if request is None:
                break
            # shutdown 必须由读取线程直接处理，否则线程池设置 stopping 后，
            # 主线程仍会阻塞在下一次 stdin.readline()，进程无法自然退出。
            if str(request.get("method") or "") == "shutdown":
                runtime.response(str(request.get("id") or ""), {"status": "shutting_down"})
                runtime.stopping.set()
                break
            futures.append(executor.submit(_run_request, request, runtime))
    finally:
        executor.shutdown(wait=True, cancel_futures=True)
        for future in futures:
            if future.done() and future.exception():
                traceback.print_exception(future.exception(), file=sys.stderr)


if __name__ == "__main__":
    main()
