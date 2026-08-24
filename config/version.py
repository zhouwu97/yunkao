"""应用版本与构建产物命名的单一来源。"""

from pathlib import Path


APP_NAME = "融智云考助手"
_VERSION_FILE = Path(__file__).resolve().parents[1] / "VERSION"
APP_VERSION = _VERSION_FILE.read_text(encoding="utf-8").strip() if _VERSION_FILE.exists() else "2.0.0"
APP_RELEASE = f"v{APP_VERSION}"
APP_VERSION_PARTS = tuple(int(part) for part in APP_VERSION.split("."))
APP_VERSION_TUPLE = (*APP_VERSION_PARTS, 0)
APP_ARTIFACT_NAME = f"yunkao-{APP_RELEASE}"

__version__ = APP_VERSION
