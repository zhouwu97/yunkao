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
                "程序需要管理员权限才能运行。",
                "融智云考助手",
                0x10,
            )
        return False
    except Exception:
        return False


def silence_native_console():
    """Hide the console and redirect native Chromium output before Qt starts."""
    if sys.platform != "win32":
        return

    try:
        hwnd = ctypes.windll.kernel32.GetConsoleWindow()
        if hwnd:
            ctypes.windll.user32.ShowWindow(hwnd, 0)
    except Exception:
        pass

    try:
        null_out = open(os.devnull, "w", encoding="utf-8")
        os.dup2(null_out.fileno(), 1)
        os.dup2(null_out.fileno(), 2)
        sys.stdout = null_out
        sys.stderr = null_out
    except Exception:
        pass


if not ensure_windows_admin():
    sys.exit(0)

silence_native_console()

# Must be configured before importing QtWebEngine.
os.environ["QT_OPENGL"] = "software"
os.environ["QTWEBENGINE_CHROMIUM_FLAGS"] = (
    "--disable-gpu --disable-gpu-compositing --log-level=3 "
    "--disable-logging"
)

import requests

if getattr(sys, 'frozen', False):
    exe_dir = os.path.dirname(sys.executable)
    os.chdir(exe_dir)

from PySide6.QtWidgets import QApplication, QDialog
from config.settings import load_config, API_BASE_URL
from ui.login_dialog import SoftwareLoginDialog
from ui.unified_home import UnifiedHomePage

def check_token_validity(token):
    try:
        resp = requests.get(
            f"{API_BASE_URL}/api/vip/status",
            headers={"Authorization": f"Bearer {token}"},
            timeout=3
        )
        return resp.status_code == 200
    except Exception:
        return False

if __name__ == "__main__":
    app = QApplication(sys.argv)
    
    while True:
        # 每次循环重新读取配置，确保退出登录后 token 已清除
        cfg = load_config()
        saved_token = cfg.get('jwt_token')
        saved_user = cfg.get('user')
        saved_user_data = cfg.get('user_data', {})
        
        # 如果有令牌且验证依然有效，直接进入主界面
        if saved_token and saved_user and check_token_validity(saved_token):
            window = UnifiedHomePage(
                current_user=saved_user,
                jwt_token=saved_token,
                user_data=saved_user_data
            )
            window.show()
            app.exec()
            if getattr(window, 'needs_relogin', False):
                continue  # 退出登录 -> 重新显示登录弹窗
            break
        else:
            # 显示登录弹窗
            login_dialog = SoftwareLoginDialog()
            if login_dialog.exec() == QDialog.Accepted:
                window = UnifiedHomePage(
                    current_user=login_dialog.current_user,
                    jwt_token=login_dialog.jwt_token,
                    user_data=login_dialog.user_data
                )
                window.show()
                app.exec()
                if getattr(window, 'needs_relogin', False):
                    continue  # 退出登录 -> 重新显示登录弹窗
                break
            else:
                break
    
    sys.exit(0)
