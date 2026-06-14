import webbrowser
import keyring
import requests
from PySide6.QtWidgets import (QDialog, QVBoxLayout, QHBoxLayout, QFormLayout, 
                               QLineEdit, QCheckBox, QLabel, QPushButton, 
                               QApplication, QFrame, QGraphicsDropShadowEffect)
from PySide6.QtGui import QColor, QFont, QCursor
from PySide6.QtCore import Qt
from config.settings import load_config, save_config, SERVICE_NAME, HARDCODED_SCHOOL_CODE, API_BASE_URL

class SoftwareLoginDialog(QDialog):
    def __init__(self):
        super().__init__()
        self.setWindowTitle("融智云考导出系统 - 用户登录")
        self.setFixedSize(420, 620)
        self.setStyleSheet("QDialog { background-color: #f0f2f5; font-family: 'Segoe UI', 'Microsoft YaHei'; }")
        self.jwt_token = None
        self.user_data = None

        # Main layout
        main_layout = QVBoxLayout(self)
        main_layout.setContentsMargins(20, 20, 20, 20)

        # Card Container
        card = QFrame()
        card.setObjectName("LoginCard")
        card.setStyleSheet("""
            #LoginCard {
                background-color: white;
                border-radius: 16px;
            }
        """)
        
        shadow = QGraphicsDropShadowEffect(self)
        shadow.setBlurRadius(25)
        shadow.setColor(QColor(0, 0, 0, 25))
        shadow.setOffset(0, 8)
        card.setGraphicsEffect(shadow)

        layout = QVBoxLayout(card)
        layout.setContentsMargins(35, 40, 35, 30)
        layout.setSpacing(16)
        main_layout.addWidget(card)

        # ---- Title ----
        title_label = QLabel("欢迎登录")
        title_label.setAlignment(Qt.AlignCenter)
        title_label.setStyleSheet("font-size: 24px; font-weight: bold; color: #1a1a1a; margin-bottom: 5px;")
        layout.addWidget(title_label)
        
        subtitle_label = QLabel("沈理校园账号安全授权")
        subtitle_label.setAlignment(Qt.AlignCenter)
        subtitle_label.setStyleSheet("font-size: 13px; color: #888888; margin-bottom: 15px;")
        layout.addWidget(subtitle_label)

        # Input Style
        input_style = """
            QLineEdit {
                border: 1px solid #dcdfe6;
                border-radius: 8px;
                padding: 0 15px;
                min-height: 40px;
                font-size: 14px;
                background-color: #f8f9fa;
                color: #333333;
            }
            QLineEdit:focus {
                border: 2px solid #0078D7;
                background-color: #ffffff;
            }
        """

        # ---- 账号输入 ----
        self.input_user = QLineEdit()
        self.input_user.setPlaceholderText("请输入学号 (10位)")
        self.input_user.setStyleSheet(input_style)
        layout.addWidget(self.input_user)

        # ---- 密码输入 ----
        pwd_layout = QHBoxLayout()
        pwd_layout.setSpacing(8)
        pwd_layout.setContentsMargins(0, 0, 0, 0)
        
        self.input_main_pwd = QLineEdit()
        self.input_main_pwd.setEchoMode(QLineEdit.Password)
        self.input_main_pwd.setPlaceholderText("请输入沈理校园密码")
        self.input_main_pwd.setStyleSheet(input_style)
        
        self.btn_show_main_pwd = QPushButton("显示")
        self.btn_show_main_pwd.setFixedSize(50, 40)
        self.btn_show_main_pwd.setCheckable(True)
        self.btn_show_main_pwd.setStyleSheet("""
            QPushButton {
                background-color: transparent;
                border: 1px solid #dcdfe6;
                border-radius: 8px;
                color: #666;
                font-size: 13px;
            }
            QPushButton:hover { background-color: #f0f2f5; }
            QPushButton:checked { color: #0078D7; border-color: #0078D7; }
        """)
        
        def toggle_main_pwd(checked):
            self.input_main_pwd.setEchoMode(QLineEdit.Normal if checked else QLineEdit.Password)
            self.btn_show_main_pwd.setText("隐藏" if checked else "显示")
            
        self.btn_show_main_pwd.clicked.connect(toggle_main_pwd)
        
        pwd_layout.addWidget(self.input_main_pwd)
        pwd_layout.addWidget(self.btn_show_main_pwd)
        layout.addLayout(pwd_layout)

        # ---- 云考快捷登录区 ----
        separator = QFrame()
        separator.setFrameShape(QFrame.HLine)
        separator.setFrameShadow(QFrame.Sunken)
        separator.setStyleSheet("background-color: #eeeeee; max-height: 1px; margin: 10px 0;")
        layout.addWidget(separator)

        self.chk_remember_yunkao = QCheckBox("记住融智云考密码 (本地加密)")
        self.chk_remember_yunkao.setChecked(True)
        self.chk_remember_yunkao.setStyleSheet("""
            QCheckBox {
                color: #0078D7; 
                font-size: 13px;
                font-weight: bold;
            }
        """)
        self.chk_remember_yunkao.toggled.connect(self.on_remember_toggled)
        layout.addWidget(self.chk_remember_yunkao)

        yk_pwd_layout = QHBoxLayout()
        yk_pwd_layout.setSpacing(8)
        yk_pwd_layout.setContentsMargins(0, 0, 0, 0)
        self.input_yk_pwd = QLineEdit()
        self.input_yk_pwd.setEchoMode(QLineEdit.Password)
        self.input_yk_pwd.setPlaceholderText("云考密码 (默认 123456)")
        self.input_yk_pwd.setStyleSheet(input_style)
        
        self.btn_show_yk_pwd = QPushButton("显示")
        self.btn_show_yk_pwd.setFixedSize(50, 40)
        self.btn_show_yk_pwd.setCheckable(True)
        self.btn_show_yk_pwd.setStyleSheet(self.btn_show_main_pwd.styleSheet())
        
        def toggle_yk_pwd(checked):
            self.input_yk_pwd.setEchoMode(QLineEdit.Normal if checked else QLineEdit.Password)
            self.btn_show_yk_pwd.setText("隐藏" if checked else "显示")
            
        self.btn_show_yk_pwd.clicked.connect(toggle_yk_pwd)
        
        yk_pwd_layout.addWidget(self.input_yk_pwd)
        yk_pwd_layout.addWidget(self.btn_show_yk_pwd)
        
        self.yk_pwd_container = QFrame()
        self.yk_pwd_container.setLayout(yk_pwd_layout)
        self.yk_pwd_container.setContentsMargins(0, 0, 0, 0)
        layout.addWidget(self.yk_pwd_container)
        
        self.lbl_safe_tip = QLabel("🔒 密码仅加密存储于本机，绝不上云")
        self.lbl_safe_tip.setStyleSheet("color: #999999; font-size: 11px;")
        layout.addWidget(self.lbl_safe_tip)

        layout.addSpacing(10)

        # ---- 登录按钮 ----
        self.btn_login = QPushButton("登  录")
        self.btn_login.setFixedHeight(44)
        self.btn_login.setCursor(Qt.PointingHandCursor)
        self.btn_login.setStyleSheet("""
            QPushButton {
                background-color: qlineargradient(x1:0, y1:0, x2:1, y2:0, stop:0 #0052D4, stop:0.5 #4364F7, stop:1 #6FB1FC);
                color: white; 
                border-radius: 8px;
                font-size: 16px; 
                font-weight: bold;
                letter-spacing: 2px;
            }
            QPushButton:hover { 
                background-color: qlineargradient(x1:0, y1:0, x2:1, y2:0, stop:0 #0042a4, stop:0.5 #3354e7, stop:1 #5fa1ec);
            }
            QPushButton:pressed {
                background-color: #0042a4;
            }
            QPushButton:disabled {
                background-color: #cccccc;
            }
        """)
        self.btn_login.clicked.connect(self.do_login)
        layout.addWidget(self.btn_login)

        self.status_label = QLabel("")
        self.status_label.setAlignment(Qt.AlignCenter)
        self.status_label.setStyleSheet("color: #e53935; font-size: 12px; min-height: 16px;")
        layout.addWidget(self.status_label)

        layout.addStretch()

        # ---- 注册引导区 ----
        register_layout = QHBoxLayout()
        register_layout.setContentsMargins(0, 0, 0, 0)
        
        self.lbl_no_account = QLabel("没有账号？")
        self.lbl_no_account.setStyleSheet("font-size: 13px; color: #666;")
        register_layout.addWidget(self.lbl_no_account, alignment=Qt.AlignRight)

        self.btn_register = QPushButton("去注册")
        self.btn_register.setCursor(Qt.PointingHandCursor)
        self.btn_register.setStyleSheet("""
            QPushButton {
                background-color: transparent;
                color: #0078D7;
                border: none;
                font-size: 13px;
                font-weight: bold;
                text-align: left;
            }
            QPushButton:hover {
                color: #005fa3;
                text-decoration: underline;
            }
        """)
        self.btn_register.clicked.connect(lambda: webbrowser.open("https://github.com/zhouwu97/SYLUlive"))
        register_layout.addWidget(self.btn_register, alignment=Qt.AlignLeft)
        
        layout.addLayout(register_layout)

        self.load_local_data()

    def on_remember_toggled(self, checked):
        self.yk_pwd_container.setVisible(checked)
        self.lbl_safe_tip.setVisible(checked)

    def load_local_data(self):
        cfg = load_config()
        if cfg.get('user'):
            self.input_user.setText(cfg['user'])
        # 尝试回填保存的云考密码
        user = self.input_user.text().strip()
        if user:
            pwd = keyring.get_password(SERVICE_NAME, f"{HARDCODED_SCHOOL_CODE}_{user}")
            if pwd:
                self.input_yk_pwd.setText(pwd)
            else:
                self.input_yk_pwd.setText("123456")
        else:
            self.input_yk_pwd.setText("123456")

    def do_login(self):
        user = self.input_user.text().strip()
        main_pwd = self.input_main_pwd.text().strip()
        yk_pwd = self.input_yk_pwd.text().strip()

        if not user or not main_pwd:
            self.status_label.setText("⚠️ 请输入学号和密码")
            return

        self.btn_login.setEnabled(False)
        self.status_label.setText("正在连接服务器验证身份...")
        self.status_label.setStyleSheet("color: #0078D7; font-size: 11px;")
        QApplication.processEvents()

        # ========== 真实后端 API 调用 ==========
        try:
            resp = requests.post(
                f"{API_BASE_URL}/api/login",
                json={"student_id": user, "password": main_pwd},
                timeout=10
            )
        except requests.exceptions.ConnectionError:
            self.status_label.setText("❌ 无法连接到服务器，请检查网络。")
            self.status_label.setStyleSheet("color: #cc0000; font-size: 11px;")
            self.btn_login.setEnabled(True)
            return
        except requests.exceptions.Timeout:
            self.status_label.setText("❌ 服务器响应超时，请稍后再试。")
            self.status_label.setStyleSheet("color: #cc0000; font-size: 11px;")
            self.btn_login.setEnabled(True)
            return

        if resp.status_code == 200:
            data = resp.json()
            self.jwt_token = data.get("token")
            self.user_data = data.get("user", {})
            self.current_user = user

            # 登录成功，保存登录状态
            cfg = load_config()
            cfg['user'] = user
            cfg['jwt_token'] = self.jwt_token
            cfg['user_data'] = self.user_data
            save_config(cfg)

            # 处理云考密码的本地加密存储
            if self.chk_remember_yunkao.isChecked():
                save_pwd = yk_pwd if yk_pwd else main_pwd
                try:
                    keyring.set_password(SERVICE_NAME, f"{HARDCODED_SCHOOL_CODE}_{user}", save_pwd)
                except Exception:
                    pass  # 存储失败不阻塞登录
            else:
                try:
                    keyring.delete_password(SERVICE_NAME, f"{HARDCODED_SCHOOL_CODE}_{user}")
                except:
                    pass

            self.accept()
        else:
            try:
                error_msg = resp.json().get("error", "登录失败")
                if "密码" in error_msg or "password" in error_msg.lower():
                    error_msg = "沈理校园账号密码错误，请检查！"
            except Exception:
                error_msg = f"HTTP {resp.status_code}: 服务器异常或接口不存在"
            self.status_label.setText(f"❌ {error_msg}")
            self.status_label.setStyleSheet("color: #cc0000; font-size: 11px;")
            self.btn_login.setEnabled(True)
