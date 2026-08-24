"""YunKao Worker 的 Content-Length JSON framing。stdout 只允许输出协议消息。"""

import json
import sys


MAX_PAYLOAD_BYTES = 64 * 1024 * 1024


def read_message(stream=None):
    """读取一条 LSP 风格消息；遇到 EOF 返回 None。"""
    stream = stream or sys.stdin.buffer
    content_length = None
    while True:
        line = stream.readline()
        if not line:
            return None
        if line in (b"\r\n", b"\n"):
            break
        name, separator, value = line.decode("ascii", errors="strict").partition(":")
        if separator and name.strip().lower() == "content-length":
            content_length = int(value.strip())

    if content_length is None or content_length < 0 or content_length > MAX_PAYLOAD_BYTES:
        raise ValueError("invalid Content-Length")
    payload = stream.read(content_length)
    if len(payload) != content_length:
        raise EOFError("worker input closed before message body")
    return json.loads(payload.decode("utf-8"))


def write_message(message, stream=None):
    """写一条协议消息并立即 flush，避免 UI 等待缓冲区。"""
    stream = stream or sys.stdout.buffer
    payload = json.dumps(message, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
    header = f"Content-Length: {len(payload)}\r\n\r\n".encode("ascii")
    stream.write(header)
    stream.write(payload)
    stream.flush()
