import sys
import json
import os
import keyring
import requests
from PySide6.QtWidgets import (QApplication, QMainWindow, QVBoxLayout, QFormLayout, QDialogButtonBox,
                               QWidget, QPushButton, QLabel, QSplitter, QTextEdit, QDialog, QLineEdit, 
                               QMessageBox, QCheckBox, QHBoxLayout)
from PySide6.QtWebEngineWidgets import QWebEngineView
from PySide6.QtWebEngineCore import QWebEngineProfile, QWebEnginePage, QWebEngineScript
from PySide6.QtWebChannel import QWebChannel
from PySide6.QtCore import QUrl, Qt, Slot, QObject, QFile
from bs4 import BeautifulSoup

# ==========================================
# 全局配置
# ==========================================
CONFIG_FILE = "config.json"
SERVICE_NAME = "YunKaoDesktop"
HARDCODED_SCHOOL_CODE = "u101441"

# 后端 API 基础地址（生产环境部署后替换为真实域名）
API_BASE_URL = "http://101.42.27.44:8080"


def load_config():
    """加载本地配置文件"""
    if os.path.exists(CONFIG_FILE):
        try:
            with open(CONFIG_FILE, 'r', encoding='utf-8') as f:
                return json.load(f)
        except:
            pass
    return {}


def save_config(data):
    """保存配置到本地文件"""
    with open(CONFIG_FILE, 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, indent=2)


# ==========================================
# 阶段 2：商业化入口 - 软件登录窗口（接入真实后端）
# ==========================================
class SoftwareLoginDialog(QDialog):
    def __init__(self):
        super().__init__()
        self.setWindowTitle("融智云考导出系统 - 用户登录")
        self.setFixedSize(380, 340)
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
        self.input_main_pwd = QLineEdit()
        self.input_main_pwd.setEchoMode(QLineEdit.Password)
        self.input_main_pwd.setPlaceholderText("输入您的 APP 登录密码")

        form_layout.addRow("学号:", self.input_user)
        form_layout.addRow("密码:", self.input_main_pwd)
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
        self.input_yk_pwd = QLineEdit()
        self.input_yk_pwd.setEchoMode(QLineEdit.Password)
        self.input_yk_pwd.setPlaceholderText("云考独立密码 (与系统相同则留空)")
        form_layout_yk.addRow("云考密码:", self.input_yk_pwd)
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

            # 登录成功，保存非敏感信息
            save_config({'user': user})

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
            error_msg = resp.json().get("error", "登录失败")
            self.status_label.setText(f"❌ {error_msg}")
            self.status_label.setStyleSheet("color: #cc0000; font-size: 11px;")
            self.btn_login.setEnabled(True)


# ==========================================
# 通信桥梁：负责接收网页发来的原生 HTML 数据
# ==========================================
class ExtractorBridge(QObject):
    def __init__(self, main_window):
        super().__init__()
        self.main_window = main_window

    @Slot(str)
    def receiveRawHtml(self, html_content):
        self.main_window.log_msg("📥 成功接收网页数据，开始进行极速清洗...")
        self.main_window.process_html_with_bs4(html_content)


# ==========================================
# 主窗口：承载 UI 与 WebEngine
# ==========================================
class YunKaoExtractorApp(QMainWindow):
    def __init__(self, current_user, jwt_token, user_data):
        super().__init__()
        self.current_user = current_user
        self.jwt_token = jwt_token
        self.user_data = user_data
        self.is_vip = False  # 启动后异步检查

        nickname = user_data.get('nickname', current_user)
        self.setWindowTitle(f"融智云考题库导出助手 - {nickname}")
        self.resize(1200, 800)

        splitter = QSplitter(Qt.Horizontal)
        self.setCentralWidget(splitter)

        # ===== 左侧控制面板 =====
        self.control_panel = QWidget()
        self.control_layout = QVBoxLayout(self.control_panel)

        # 用户信息头
        self.user_info_label = QLabel(f"👤 {nickname} ({current_user})")
        self.user_info_label.setStyleSheet("font-weight: bold; font-size: 13px; color: #0078D7;")
        self.control_layout.addWidget(self.user_info_label)

        self.vip_label = QLabel("⏳ VIP 状态检查中...")
        self.vip_label.setStyleSheet("font-size: 11px; color: #888;")
        self.control_layout.addWidget(self.vip_label)

        self.status_label = QLabel("状态: 准备就绪")
        self.control_layout.addWidget(self.status_label)

        # 按钮区
        self.btn_auto_login = QPushButton("🔑 一键填充云考账号")
        self.btn_extract = QPushButton("🚀 提取当前题目 (免费)")
        self.btn_export_basic = QPushButton("💾 导出基础题库 (免费)")
        self.btn_export_vip = QPushButton("✨ 高级美化排版 (VIP专享)")

        self.btn_auto_login.clicked.connect(self.trigger_auto_fill)
        self.btn_extract.clicked.connect(self.trigger_extraction)
        self.btn_export_vip.clicked.connect(self.trigger_vip_export)

        self.control_layout.addWidget(self.btn_auto_login)
        self.control_layout.addWidget(self.btn_extract)
        self.control_layout.addWidget(self.btn_export_basic)
        self.control_layout.addWidget(self.btn_export_vip)

        # 日志输出台
        self.log_console = QTextEdit()
        self.log_console.setReadOnly(True)
        self.control_layout.addWidget(QLabel("控制台输出:"))
        self.control_layout.addWidget(self.log_console)

        # ===== 右侧浏览器与桥接设置 =====
        self.browser = QWebEngineView()

        self.channel = QWebChannel()
        self.bridge = ExtractorBridge(self)
        self.channel.registerObject("pybridge", self.bridge)
        self.browser.page().setWebChannel(self.channel)

        self.browser.page().loadFinished.connect(self.on_page_loaded)
        self.browser.load(QUrl("https://www.cctrcloud.net/practice/login.html"))

        splitter.addWidget(self.control_panel)
        splitter.addWidget(self.browser)
        splitter.setSizes([300, 900])

        # 启动后立即检查 VIP 状态
        self.check_vip_status()

    def log_msg(self, msg):
        self.log_console.append(msg)

    def check_vip_status(self):
        """向后端查询当前用户的 VIP 状态"""
        try:
            resp = requests.get(
                f"{API_BASE_URL}/api/vip/status",
                headers={"Authorization": f"Bearer {self.jwt_token}"},
                timeout=5
            )
            if resp.status_code == 200:
                data = resp.json()
                self.is_vip = data.get("is_vip", False)
                expiry = data.get("vip_expiry", "")
                if self.is_vip:
                    self.vip_label.setText(f"⭐ VIP 有效期至: {expiry}")
                    self.vip_label.setStyleSheet("font-size: 11px; color: #DAA520; font-weight: bold;")
                    self.log_msg(f"⭐ VIP 用户，高级功能已解锁！到期时间: {expiry}")
                else:
                    self.vip_label.setText("🔒 普通用户 (高级功能需 VIP)")
                    self.vip_label.setStyleSheet("font-size: 11px; color: #888;")
                    self.log_msg("ℹ️ 当前为普通用户。基础提取与导出免费，高级美化排版需要 VIP。")
            else:
                self.vip_label.setText("⚠️ VIP 状态获取失败")
                self.log_msg("⚠️ 无法获取 VIP 状态，高级功能暂不可用。")
        except Exception as e:
            self.vip_label.setText("⚠️ 网络异常")
            self.log_msg(f"⚠️ VIP 状态检查失败: {e}")

    def on_page_loaded(self, ok):
        if ok:
            self.log_msg("🌐 页面加载完成...")
            qfile = QFile(":/qtwebchannel/qwebchannel.js")
            if qfile.open(QFile.ReadOnly):
                qwebchannel_js = bytes(qfile.readAll()).decode('utf-8')
                qfile.close()
                setup_code = """
                new QWebChannel(qt.webChannelTransport, function(channel) {
                    window.pybridge = channel.objects.pybridge;
                });
                """
                self.browser.page().runJavaScript(qwebchannel_js + setup_code)
                self.log_msg("✅ 桥接脚本注入成功！")

            # 自动尝试填充云考登录
            self.trigger_auto_fill()

    def trigger_auto_fill(self):
        """从本地加密凭据库读取云考密码并自动填充"""
        pwd = keyring.get_password(SERVICE_NAME, f"{HARDCODED_SCHOOL_CODE}_{self.current_user}")
        if not pwd:
            self.log_msg("ℹ️ 未存储云考密码，请手动登录。")
            return

        self.log_msg("🤖 正在为您自动填充云考账号...")

        js_code = f"""
        (function() {{
            let inputs = document.querySelectorAll('input');
            let filled = 0;
            inputs.forEach(inp => {{
                let p = inp.placeholder || '';
                if (p.includes('学校') || p.includes('机构')) {{
                    inp.value = '{HARDCODED_SCHOOL_CODE}';
                    inp.dispatchEvent(new Event('input', {{ bubbles: true }}));
                    filled++;
                }}
                else if (p.includes('学号') || p.includes('账号') || p.includes('用户名')) {{
                    inp.value = '{self.current_user}';
                    inp.dispatchEvent(new Event('input', {{ bubbles: true }}));
                    filled++;
                }}
                else if (inp.type === 'password') {{
                    inp.value = '{pwd}';
                    inp.dispatchEvent(new Event('input', {{ bubbles: true }}));
                    filled++;
                }}
            }});
            return filled;
        }})();
        """

        def on_js_done(filled_count):
            if filled_count > 0:
                self.log_msg(f"✅ 成功填入 {filled_count} 个输入框！学校编码: {HARDCODED_SCHOOL_CODE}")

        self.browser.page().runJavaScript(js_code, 0, on_js_done)

    def trigger_extraction(self):
        self.log_msg("🚀 发起提取指令...")
        js_cmd = """
        if (window.pybridge) {
            let target = document.querySelector('.swiper-slide-active');
            if (!target) { target = document.querySelector('.practice_slide_content'); }
            if (target) { window.pybridge.receiveRawHtml(target.innerHTML); }
            else { alert("未找到题目内容元素，请确认是否在练习页面！"); }
        } else {
            alert("通信桥梁未就绪，请等待页面完全加载后重试。");
        }
        """
        self.browser.page().runJavaScript(js_cmd)

    def trigger_vip_export(self):
        """VIP 高级导出按钮 - 实时向后端校验 VIP 权限"""
        self.log_msg("🔍 正在向服务器校验 VIP 权限...")
        try:
            resp = requests.get(
                f"{API_BASE_URL}/api/vip/status",
                headers={"Authorization": f"Bearer {self.jwt_token}"},
                timeout=5
            )
            if resp.status_code == 200 and resp.json().get("is_vip"):
                self.log_msg("⭐ VIP 验证通过！正在执行高级美化排版...")
                # TODO: 接入 AI 智能排版和水印功能
                QMessageBox.information(self, "VIP 功能", "高级美化排版功能开发中，敬请期待！")
            else:
                QMessageBox.warning(
                    self, "权限不足",
                    "⭐ 高级美化排版为 VIP 专享功能。\n\n"
                    "请前往沈理校园 APP 或网页端升级 VIP，\n"
                    "即可解锁 AI 智能排版、自定义水印等高级功能。"
                )
                self.log_msg("🔒 VIP 校验未通过，高级功能已拦截。")
        except Exception as e:
            QMessageBox.warning(self, "网络异常", f"无法连接服务器校验权限: {e}")

    def process_html_with_bs4(self, html_content):
        soup = BeautifulSoup(html_content, 'html.parser')
        for garbage in soup.select('.right_ans_mark, .practice_analysis'):
            garbage.decompose()
        title_tag = soup.select_one('.practice_slide_title .txt, .practice_slide_title')
        title_text = title_tag.get_text(strip=True) if title_tag else "未知题目"

        options = []
        for i, li in enumerate(soup.select('.option_content li')):
            auto_label = chr(65 + i)
            txt_tag = li.select_one('.txt')
            opt_text = txt_tag.get_text(strip=True) if txt_tag else li.get_text(strip=True)
            options.append(f"{auto_label}. {opt_text}")

        self.log_msg(f"✅ 解析成功：\n[题目] {title_text}\n[选项] {options}")
        self.log_msg("------------------------")


if __name__ == "__main__":
    app = QApplication(sys.argv)

    # 启动登录界面，接入真实后端鉴权
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
