import keyring
import requests
from bs4 import BeautifulSoup
from PySide6.QtWidgets import (QMainWindow, QWidget, QVBoxLayout, QPushButton, QLabel, 
                               QSplitter, QTextEdit, QMessageBox, QCheckBox, QFileDialog)
from PySide6.QtWebEngineWidgets import QWebEngineView
from PySide6.QtWebChannel import QWebChannel
from PySide6.QtCore import QUrl, Qt, QFile, QTimer

from config.settings import SERVICE_NAME, HARDCODED_SCHOOL_CODE, API_BASE_URL
from modules.js_bridge import ExtractorBridge
from modules.exporter import export_to_markdown, export_to_txt

class YunKaoExtractorApp(QMainWindow):
    def __init__(self, current_user, jwt_token, user_data):
        super().__init__()
        self.current_user = current_user
        self.jwt_token = jwt_token
        self.user_data = user_data
        self.is_vip = False
        self.extracted_questions = []

        nickname = user_data.get('nickname', current_user)
        self.setWindowTitle(f"融智云考题库导出助手 - {nickname}")
        self.resize(1200, 800)

        splitter = QSplitter(Qt.Horizontal)
        self.setCentralWidget(splitter)

        # ===== 左侧控制面板 =====
        self.control_panel = QWidget()
        self.control_layout = QVBoxLayout(self.control_panel)

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
        
        # 提取相关
        self.btn_extract = QPushButton("🚀 提取当前题目 (免费)")
        self.chk_auto_next = QCheckBox("自动进入下一题 (循环提取)")
        self.chk_auto_next.setStyleSheet("color: #D32F2F; font-weight: bold;")
        self.lbl_counter = QLabel("已提取: 0 题")
        self.lbl_counter.setStyleSheet("color: #388E3C; font-weight: bold;")

        self.btn_export_basic = QPushButton("💾 导出基础题库 (免费)")
        self.btn_export_vip = QPushButton("✨ 高级美化排版 (VIP专享)")

        self.btn_auto_login.clicked.connect(self.trigger_auto_fill)
        self.btn_extract.clicked.connect(self.trigger_extraction)
        self.btn_export_basic.clicked.connect(self.export_basic_questions)
        self.btn_export_vip.clicked.connect(self.trigger_vip_export)

        self.control_layout.addWidget(self.btn_auto_login)
        self.control_layout.addWidget(self.btn_extract)
        self.control_layout.addWidget(self.chk_auto_next)
        self.control_layout.addWidget(self.lbl_counter)
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

        self.check_vip_status()

    def log_msg(self, msg):
        self.log_console.append(msg)

    def check_vip_status(self):
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
                    self.log_msg("ℹ️ 当前为普通用户。基础提取与导出免费。")
            else:
                self.vip_label.setText("⚠️ VIP 状态获取失败")
        except Exception as e:
            self.vip_label.setText("⚠️ 网络异常")

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

            self.trigger_auto_fill()

    def trigger_auto_fill(self):
        pwd = keyring.get_password(SERVICE_NAME, f"{HARDCODED_SCHOOL_CODE}_{self.current_user}")
        if not pwd:
            return

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
        self.browser.page().runJavaScript(js_code)

    def trigger_extraction(self):
        js_cmd = """
        if (window.pybridge) {
            let target = document.querySelector('.swiper-slide-active');
            if (!target) { target = document.querySelector('.practice_slide_content'); }
            if (target) { window.pybridge.receiveRawHtml(target.innerHTML); }
            else { alert("未找到题目内容元素，请确认是否在练习页面！"); }
        }
        """
        self.browser.page().runJavaScript(js_cmd)

    def trigger_vip_export(self):
        QMessageBox.information(self, "VIP 功能", "高级美化排版功能开发中，敬请期待！")

    def export_basic_questions(self):
        if not self.extracted_questions:
            QMessageBox.warning(self, "无数据", "当前没有提取到任何题目，请先提取！")
            return
            
        file_path, filter = QFileDialog.getSaveFileName(
            self, "导出基础题库", "", 
            "Markdown 文件 (*.md);;文本文件 (*.txt)"
        )
        if not file_path:
            return
            
        try:
            if filter.startswith("Markdown"):
                export_to_markdown(self.extracted_questions, file_path)
            else:
                export_to_txt(self.extracted_questions, file_path)
            QMessageBox.information(self, "导出成功", f"成功导出 {len(self.extracted_questions)} 道题目！")
        except Exception as e:
            QMessageBox.critical(self, "导出失败", f"导出过程中出错：{e}")

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

        q_dict = {"title": title_text, "options": options}
        
        # 避免重复提取
        is_duplicate = any(q['title'] == q_dict['title'] for q in self.extracted_questions)
        if not is_duplicate:
            self.extracted_questions.append(q_dict)
            self.lbl_counter.setText(f"已提取: {len(self.extracted_questions)} 题")
            self.log_msg(f"✅ 解析成功：{title_text[:20]}...")
        else:
            self.log_msg(f"ℹ️ 题目已存在：{title_text[:20]}...")

        # 自动下一题逻辑
        if self.chk_auto_next.isChecked():
            js_next = """
            (function() {
                let nextBtn = document.querySelector('.swiper-button-next');
                if (nextBtn && !nextBtn.classList.contains('swiper-button-disabled')) {
                    nextBtn.click();
                    return true;
                }
                return false;
            })();
            """
            def on_next_result(result):
                if result:
                    self.log_msg("🔄 自动进入下一题...")
                    QTimer.singleShot(800, self.trigger_extraction)
                else:
                    self.log_msg("🛑 已到达最后一题，自动提取结束。")
                    self.chk_auto_next.setChecked(False)

            self.browser.page().runJavaScript(js_next, 0, on_next_result)
