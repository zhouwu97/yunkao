"""应用版本与构建产物命名的单一来源。"""

APP_NAME = "融智云考助手"
APP_VERSION = "1.0.0"
APP_RELEASE = f"v{APP_VERSION}"
APP_VERSION_PARTS = tuple(int(part) for part in APP_VERSION.split("."))
APP_VERSION_TUPLE = (*APP_VERSION_PARTS, 0)
APP_ARTIFACT_NAME = f"yunkao-{APP_RELEASE}"

__version__ = APP_VERSION
