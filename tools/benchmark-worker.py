"""可复现的 Worker 压力测试；只使用仓库夹具，结果写入指定目录。"""
from __future__ import annotations

import argparse
import ctypes
from ctypes import wintypes
import json
from pathlib import Path
import queue
import statistics
import subprocess
import sys
import threading
import time

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
from worker.protocol import read_message, write_message


def private_memory_mb(pid):
    if sys.platform != "win32":
        return None
    class Counters(ctypes.Structure):
        _fields_ = [("cb", wintypes.DWORD), ("PageFaultCount", wintypes.DWORD)] + [
            (name, ctypes.c_size_t) for name in (
                "PeakWorkingSetSize", "WorkingSetSize", "QuotaPeakPagedPoolUsage",
                "QuotaPagedPoolUsage", "QuotaPeakNonPagedPoolUsage", "QuotaNonPagedPoolUsage",
                "PagefileUsage", "PeakPagefileUsage", "PrivateUsage")]
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel.OpenProcess.restype = wintypes.HANDLE
    kernel.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
    kernel.CloseHandle.argtypes = [wintypes.HANDLE]
    psapi = ctypes.WinDLL("psapi", use_last_error=True)
    psapi.GetProcessMemoryInfo.argtypes = [wintypes.HANDLE, ctypes.POINTER(Counters), wintypes.DWORD]
    handle = kernel.OpenProcess(0x1000 | 0x10, False, pid)
    if not handle:
        return None
    try:
        counters = Counters()
        counters.cb = ctypes.sizeof(counters)
        if psapi.GetProcessMemoryInfo(handle, ctypes.byref(counters), counters.cb):
            return round(counters.PrivateUsage / 1024 ** 2, 3)
    finally:
        kernel.CloseHandle(handle)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    launch = parser.add_mutually_exclusive_group()
    launch.add_argument("--worker-exe", type=Path)
    launch.add_argument("--worker-script", type=Path)
    parser.add_argument("--requests", type=int, default=5000)
    parser.add_argument("--output-dir", type=Path, default=ROOT / "build" / "qa-worker")
    parser.add_argument("--include-pdf", action="store_true", help="需要已安装的 Word 或 WPS")
    args = parser.parse_args()
    if args.requests <= 0:
        parser.error("--requests must be positive")
    args.output_dir.mkdir(parents=True, exist_ok=True)
    command = [str(args.worker_exe.resolve())] if args.worker_exe else [sys.executable, str((args.worker_script or ROOT / "worker" / "worker_main.py").resolve())]
    stderr_path = args.output_dir / "worker-stderr.log"
    responses = queue.Queue()
    report = {"command": command, "requests": args.requests}
    start = time.perf_counter()
    with stderr_path.open("wb") as stderr:
        process = subprocess.Popen(command, cwd=ROOT, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                   stderr=stderr, creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
        def receive():
            try:
                while (message := read_message(process.stdout)) is not None:
                    responses.put(message)
            finally:
                responses.put(None)
        threading.Thread(target=receive, daemon=True).start()
        sequence = 0
        def call(method, params, timeout=60):
            nonlocal sequence
            sequence += 1
            request_id = str(sequence)
            write_message({"protocol": 1, "id": request_id, "method": method, "params": params}, process.stdin)
            deadline = time.perf_counter() + timeout
            while True:
                response = responses.get(timeout=max(0.01, deadline - time.perf_counter()))
                if response is None:
                    raise RuntimeError("Worker 在响应前退出")
                if response.get("id") == request_id:
                    if not response.get("ok"):
                        raise RuntimeError(str(response.get("error")))
                    return response["result"]
                if time.perf_counter() > deadline:
                    raise TimeoutError(method)
        try:
            health = call("health", {})
            report["startup_ms"] = round((time.perf_counter() - start) * 1000, 2)
            pid = health["pid"]
            fixtures = [(ROOT / "tests" / "fixtures" / name).read_text(encoding="utf-8") for name in (
                "single.html", "multiple.html", "judgment.html", "mathjax.html", "long.html", "active_question.html")]
            for fixture in fixtures:
                assert call("parseQuestion", {"html": fixture})["question_id"]
            report["memory_before_mb"] = private_memory_mb(pid)
            durations = []
            checkpoints = []
            for index in range(args.requests):
                started = time.perf_counter()
                question = call("parseQuestion", {"html": fixtures[index % len(fixtures)]})
                assert question["question_id"]
                durations.append((time.perf_counter() - started) * 1000)
                if (index + 1) % 1000 == 0:
                    checkpoints.append({"requests": index + 1, "private_memory_mb": private_memory_mb(pid)})
            report["parse_mean_ms"] = round(statistics.mean(durations), 3)
            report["parse_p95_ms"] = round(sorted(durations)[min(len(durations) - 1, int(len(durations) * .95))], 3)
            report["parse_requests_per_second"] = round(args.requests / (sum(durations) / 1000), 2)
            report["memory_after_mb"] = private_memory_mb(pid)
            report["memory_checkpoints"] = checkpoints
            question = call("parseQuestion", {"html": fixtures[-1]})
            questions = [{**question, "question_id": f"qa-{index}"} for index in range(100)]
            exports = []
            for extension in (["md", "txt", "docx", "pdf"] if args.include_pdf else ["md", "txt", "docx"]):
                for include_answers in (True, False):
                    path = args.output_dir.resolve() / f"questions-{'answers' if include_answers else 'practice'}.{extension}"
                    started = time.perf_counter()
                    try:
                        call("export", {"format": extension, "filePath": str(path), "questions": questions,
                                        "includeAnswers": include_answers}, timeout=120)
                        assert path.is_file() and path.stat().st_size > 0
                        if extension in ("md", "txt"):
                            exported = path.read_text(encoding="utf-8")
                            assert "Content-Length" in exported
                            assert ("答案" in exported) == include_answers
                        elif extension == "docx":
                            from docx import Document
                            exported = "\n".join(p.text for p in Document(path).paragraphs)
                            assert "Content-Length" in exported
                            assert ("答案" in exported) == include_answers
                        else:
                            import fitz
                            with fitz.open(path) as document:
                                assert document.page_count > 0
                                exported = "\n".join(page.get_text() for page in document)
                                assert "Content-Length" in exported
                                assert ("答案" in exported) == include_answers
                        exports.append({"format": extension, "include_answers": include_answers,
                                        "milliseconds": round((time.perf_counter() - started) * 1000, 2),
                                        "bytes": path.stat().st_size, "verified": True})
                    except Exception as error:
                        exports.append({"format": extension, "include_answers": include_answers, "error": str(error)})
                        if process.poll() is not None:
                            break
            report["exports"] = exports
            call("shutdown", {})
            process.stdin.close()
            process.wait(timeout=10)
            assert process.returncode == 0
            report["shutdown_verified"] = True
        finally:
            if process.poll() is None:
                process.kill()
                process.wait(timeout=10)
            process.stdout.close()
            if not process.stdin.closed:
                process.stdin.close()
    (args.output_dir / "benchmark.json").write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
    print(json.dumps(report, indent=2, ensure_ascii=False))
    if any(not item.get("verified") for item in report.get("exports", [])):
        raise SystemExit(1)


if __name__ == "__main__":
    main()
