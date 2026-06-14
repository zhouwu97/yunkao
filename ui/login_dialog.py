import webbrowser
import keyring
import requests
from PySide6.QtWidgets import QDialog, QVBoxLayout, QHBoxLayout, QFormLayout, QLineEdit, QCheckBox, QLabel, QPushButton, QApplication
from PySide6.QtCore import Qt
from config.settings import load_config, save_config, SERVICE_NAME, HARDCODED_SCHOOL_CODE, API_BASE_URL

class SoftwareLoginDialog(QDialog):
    def __init__(self):
        super().__init__()
        self.setWindowTitle("融智云考导出系统 - 用户登录")
        self.setFixedSize(380, 420)
        self.jwt_token = None
        self.user_data = None

        layout = QVBoxLayout(self)

        # ---- 系统登录区 ----
        title_label = QLabel("🔐 登录您的沈理校园账号")
        title_label.setStyleSheet("font-size: 14px; font-weight: bold; color: #0078D7; margin-bottom: 5px;")
        layout.addWidget(title_label)

        form_layout = QFormLayout()
        self.input_user = QLineEdit()
        self.input_user.setPlaceholderText("请输入学号 (10位)")
        
        pwd_layout = QHBoxLayout()
        self.input_main_pwd = QLineEdit()
        self.input_main_pwd.setEchoMode(QLineEdit.Password)
        self.input_main_pwd.setPlaceholderText("输入您的 沈理校园 登录密码")
        
        self.btn_show_main_pwd = QPushButton("👁")
        self.btn_show_main_pwd.setFixedSize(24, 24)
        self.btn_show_main_pwd.setCheckable(True)
        self.btn_show_main_pwd.clicked.connect(lambda checked: self.input_main_pwd.setEchoMode(QLineEdit.Normal if checked else QLineEdit.Password))
        
        pwd_layout.addWidget(self.input_main_pwd)
        pwd_layout.addWidget(self.btn_show_main_pwd)
        pwd_layout.setContentsMargins(0, 0, 0, 0)
        
        form_layout.addRow("学号:", self.input_user)
        form_layout.addRow("校园密码:", pwd_layout)
        layout.addLayout(form_layout)

        # ---- 融智云考快捷登录区 ----
        separator = QLabel("─" * 40)
        separator.setStyleSheet("color: #ccc; margin-top: 8px;")
        layout.addWidget(separator)

        self.chk_remember_yunkao = QCheckBox("记住我的融智云考密码 (本地硬件级加密存储)")
        self.chk_remember_yunkao.setChecked(True)
        self.chk_remember_yunkao.setStyleSheet("color: #0078D7; font-weight: bold;")
        self.chk_remember_yunkao.toggled.connect(self.on_remember_toggled)
        layout.addWidget(self.chk_remember_yunkao)

        self.lbl_safe_tip = QLabel(
            "🔒 提示: 您的云考密码仅加密存储在本机硬件凭证库中，\n"
            "绝不会上传至任何云端服务器。学校编码自动填写。"
        )
        self.lbl_safe_tip.setStyleSheet("color: #888; font-size: 10px;")
        layout.addWidget(self.lbl_safe_tip)

        form_layout_yk = QFormLayout()
        yk_pwd_layout = QHBoxLayout()
        self.input_yk_pwd = QLineEdit()
        self.input_yk_pwd.setEchoMode(QLineEdit.Password)
        self.input_yk_pwd.setPlaceholderText("默认 123456，若修改过请填写")
        
        self.btn_show_yk_pwd = QPushButton("👁")
        self.btn_show_yk_pwd.setFixedSize(24, 24)
        self.btn_show_yk_pwd.setCheckable(True)
        self.btn_show_yk_pwd.clicked.connect(lambda checked: self.input_yk_pwd.setEchoMode(QLineEdit.Normal if checked else QLineEdit.Password))
        
        yk_pwd_layout.addWidget(self.input_yk_pwd)
        yk_pwd_layout.addWidget(self.btn_show_yk_pwd)
        yk_pwd_layout.setContentsMargins(0, 0, 0, 0)
        
        form_layout_yk.addRow("云考密码:", yk_pwd_layout)
        layout.addLayout(form_layout_yk)

        # ---- 登录按钮 ----
        self.btn_login = QPushButton("🚀 登录")
        self.btn_login.setFixedHeight(38)
        self.btn_login.setStyleSheet("""
            QPushButton {
                background-color: #0078D7; color: white; border-radius: 6px;
                font-size: 14px; font-weight: bold;
            }
            QPushButton:hover { background-color: #005fa3; }
        """)
        self.btn_login.clicked.connect(self.do_login)
        layout.addWidget(self.btn_login)

        self.status_label = QLabel("")
        self.status_label.setStyleSheet("color: #cc0000; font-size: 11px;")
        layout.addWidget(self.status_label)

        layout.addStretch()

        # ---- 注册引导区 ----
        register_sep = QLabel("─" * 40)
        register_sep.setStyleSheet("color: #ccc;")
        layout.addWidget(register_sep)

        self.lbl_no_account = QLabel("还没有沈理校园账号？")
        self.lbl_no_account.setAlignment(Qt.AlignCenter)
        self.lbl_no_account.setStyleSheet("font-size: 12px; color: #888; margin-top: 4px;")
        layout.addWidget(self.lbl_no_account)

        self.btn_register = QPushButton("📱 下载 SYLUlive 去注册")
        self.btn_register.setCursor(Qt.PointingHandCursor)
        self.btn_register.setFixedHeight(32)
        self.btn_register.setStyleSheet("""
            QPushButton {
                background-color: transparent;
                color: #0078D7;
                border: 1px solid #0078D7;
                border-radius: 6px;
                font-size: 12px;
                font-weight: bold;
            }
            QPushButton:hover {
                background-color: rgba(0, 120, 215, 0.08);
            }
        """)
        self.btn_register.clicked.connect(lambda: webbrowser.open("https://github.com/zhouwu97/SYLUlive"))
        layout.addWidget(self.btn_register)

        self.load_local_data()

    def on_remember_toggled(self, checked):
        self.input_yk_pwd.setVisible(checked)
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
