import sys
import os
import ctypes


def _quote_windows_arg(value):
    value = str(value)
    if not value or any(char in value for char in ' \t"'):
        return '"' + value.replace('"', '\\"') + '"'
    return value


def ensure_windows_admin():
    if sys.platform != "win32":
        return True
    if os.environ.get("YUNKAO_REQUIRE_ADMIN", "").strip() != "1":
        return True

    try:
        if ctypes.windll.shell32.IsUserAnAdmin():
            return True

        if getattr(sys, "frozen", False):
            executable = sys.executable
            arguments = " ".join(_quote_windows_arg(arg) for arg in sys.argv[1:])
        else:
            executable = sys.executable
            arguments = " ".join(
                [_quote_windows_arg(os.path.abspath(__file__))]
                + [_quote_windows_arg(arg) for arg in sys.argv[1:]]
            )

        result = ctypes.windll.shell32.ShellExecuteW(
            None, "runas", executable, arguments, os.getcwd(), 1
        )
        if result <= 32:
            ctypes.windll.user32.MessageBoxW(
                None,
                "当前环境配置为需要管理员权限才能运行。",
                "融智云考助手",
                0x10,
            )
        return False
    except Exception:
        return False


def hide_native_console():
    """Hide the console window without discarding stdout/stderr."""
    if sys.platform != "win32":
        return

    try:
        hwnd = ctypes.windll.kernel32.GetConsoleWindow()
        if hwnd:
            ctypes.windll.user32.ShowWindow(hwnd, 0)
    except Exception:
        pass


if not ensure_windows_admin():
    sys.exit(0)

hide_native_console()

# Must be configured before importing QtWebEngine. 默认保留 GPU，只有显式兼容模式才切软件渲染。
if os.environ.get("YUNKAO_COMPAT_MODE", "").strip() == "1":
    os.environ["QT_OPENGL"] = "software"
    os.environ["QTWEBENGINE_CHROMIUM_FLAGS"] = (
        "--disable-gpu --disable-gpu-compositing --log-level=3 "
        "--disable-logging"
    )
else:
    os.environ.setdefault("QT_OPENGL", "desktop")
    os.environ.setdefault(
        "QTWEBENGINE_CHROMIUM_FLAGS",
        "--log-level=3 --disable-logging",
    )

import requests

if getattr(sys, 'frozen', False):
    exe_dir = os.path.dirname(sys.executable)
    os.chdir(exe_dir)

from PySide6.QtWidgets import QApplication
from config.settings import load_config
from config.version import APP_NAME, APP_VERSION
from ui.main_window import YunKaoExtractorApp

if __name__ == "__main__":
    app = QApplication(sys.argv)
    app.setApplicationName(APP_NAME)
    app.setApplicationDisplayName(APP_NAME)
    app.setApplicationVersion(APP_VERSION)
    app.setOrganizationName("zhouwu97")
    
    cfg = load_config()
    current_user = str(cfg.get("yunkao_user") or "").strip()
    user_data = {
        "nickname": cfg.get("nickname") or "本地用户",
        "role": "local",
    }
    
    window = YunKaoExtractorApp(
        current_user=current_user,
        jwt_token="",
        user_data=user_data,
    )
    window.show()
    
    sys.exit(app.exec())
