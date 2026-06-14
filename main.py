import sys
import os
import ctypes

# 隐藏黑框 (Hide Console Window)
# 注意：由于 PyArmor 免费试用版会在启动时强制向控制台打印版权信息，如果在使用 PyInstaller 
# 打包时使用 console=False (无控制台模式)，会导致没有控制台句柄可以写入，从而引发 OSError 并导致程序瞬间崩溃闪退。
# 因此我们必须打包为 console=True 并在程序启动时使用 Windows API 瞬间隐藏黑框。
# 若购买了 PyArmor 正式版，打包时即可安全地使用 console=False，此代码也可移除。
if sys.platform == 'win32':
    try:
        kernel32 = ctypes.windll.kernel32
        user32 = ctypes.windll.user32
        hwnd = kernel32.GetConsoleWindow()
        if hwnd:
            user32.ShowWindow(hwnd, 0) # SW_HIDE = 0
    except Exception:
        pass
import requests

if getattr(sys, 'frozen', False):
    exe_dir = os.path.dirname(sys.executable)
    os.chdir(exe_dir)

from PySide6.QtWidgets import QApplication, QDialog
from config.settings import load_config, API_BASE_URL
from ui.login_dialog import SoftwareLoginDialog
from ui.main_window import YunKaoExtractorApp

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
    # Disable hardware acceleration to fix random black screens when maximizing
    os.environ["QT_OPENGL"] = "software"
    os.environ["QTWEBENGINE_CHROMIUM_FLAGS"] = "--disable-gpu"
    
    app = QApplication(sys.argv)
    
    while True:
        # 每次循环重新读取配置，确保退出登录后 token 已清除
        cfg = load_config()
        saved_token = cfg.get('jwt_token')
        saved_user = cfg.get('user')
        saved_user_data = cfg.get('user_data', {})
        
        # 如果有令牌且验证依然有效，直接进入主界面
        if saved_token and saved_user and check_token_validity(saved_token):
            window = YunKaoExtractorApp(
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
                window = YunKaoExtractorApp(
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
