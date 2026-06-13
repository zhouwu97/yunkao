import sys
import requests
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
    app = QApplication(sys.argv)
    
    # 尝试读取本地保存的登录状态
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
        sys.exit(app.exec())
    else:
        # 否则显示登录弹窗
        login_dialog = SoftwareLoginDialog()
        if login_dialog.exec() == QDialog.Accepted:
            window = YunKaoExtractorApp(
                current_user=login_dialog.current_user,
                jwt_token=login_dialog.jwt_token,
                user_data=login_dialog.user_data
            )
            window.show()
            sys.exit(app.exec())
        else:
            sys.exit(0)
