import sys
from PySide6.QtWidgets import QApplication, QDialog
from ui.login_dialog import SoftwareLoginDialog
from ui.main_window import YunKaoExtractorApp

if __name__ == "__main__":
    app = QApplication(sys.argv)
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
