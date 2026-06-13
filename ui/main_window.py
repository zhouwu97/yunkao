import os
import re
import keyring
import requests
from bs4 import BeautifulSoup
from PySide6.QtWidgets import (QMainWindow, QWidget, QVBoxLayout, QHBoxLayout, 
                               QPushButton, QLabel, QFileDialog, QMessageBox, QFrame, QProgressDialog)
from PySide6.QtWebEngineWidgets import QWebEngineView
from PySide6.QtWebChannel import QWebChannel
from PySide6.QtCore import QUrl, Qt, QFile, QTimer, QPoint, QThread, Signal

from config.settings import SERVICE_NAME, HARDCODED_SCHOOL_CODE, API_BASE_URL, load_config
from modules.js_bridge import ExtractorBridge
from modules.exporter import export_to_markdown, export_to_txt
from ui.settings_dialog import SettingsDialog

class ExportThread(QThread):
    finished = Signal(bool, str)
    progress = Signal(int, int, str)
    
    def __init__(self, questions, file_path, filter_type):
        super().__init__()
        self.questions = questions
        self.file_path = file_path
        self.filter_type = filter_type
        
    def run(self):
        def report_progress(current, total, message):
            self.progress.emit(current, total, message)
            
        try:
            if self.filter_type.startswith("Word"):
                from modules.exporter import export_to_docx
                export_to_docx(self.questions, self.file_path, progress_callback=report_progress)
            elif self.filter_type.startswith("Markdown"):
                from modules.exporter import export_to_markdown
                export_to_markdown(self.questions, self.file_path)
            else:
                from modules.exporter import export_to_txt
                export_to_txt(self.questions, self.file_path)
            self.finished.emit(True, self.file_path)
        except Exception as e:
            self.finished.emit(False, str(e))

# ==========================================
# 1. 油猴脚本风格的悬浮操作窗 (Overlay Widget)
# ==========================================
class TampermonkeyFloatingWindow(QFrame):
    def __init__(self, parent=None, main_app=None):
        super().__init__(parent)
        self.main_app = main_app
        self.setWindowFlags(Qt.SubWindow) # 设为子窗体
        self.is_extracting = False  # 提取状态开关
        self.is_minimized = False   # 最小化状态
        self.drag_position = QPoint()

        self.init_ui()

    def init_ui(self):
        # 悬浮窗视觉：高斯模糊/半透明玻璃质感、圆角与精致阴影
        self.setStyleSheet("""
            TampermonkeyFloatingWindow {
                background-color: rgba(30, 30, 30, 0.85);
                border: 1px solid rgba(255, 255, 255, 0.15);
                border-radius: 12px;
            }
            QLabel {
                color: #E0E0E0;
                font-family: 'Segoe UI', 'Microsoft YaHei';
            }
            QPushButton {
                background-color: #0078D4;
                color: white;
                border: none;
                border-radius: 6px;
                padding: 6px 12px;
                font-weight: bold;
                font-size: 13px;
            }
            QPushButton:hover { background-color: #106EBE; }
            QPushButton:pressed { background-color: #005A9E; }
            QPushButton:disabled { background-color: #555555; color: #AAAAAA; }
            #btn_export { background-color: #243A5E; border: 1px solid #0078D4; }
            #btn_export:hover { background-color: #106EBE; }
            #btn_util { background-color: transparent; border: 1px solid rgba(255,255,255,0.2); font-size: 11px; padding: 4px 8px; color: #CCCCCC;}
            #btn_util:hover { background-color: rgba(255,255,255,0.1); color: white;}
            #btn_min { background-color: transparent; color: #888888; font-size: 14px; font-weight: bold; padding: 0 4px; }
            #btn_min:hover { color: white; }
        """)

        self.main_layout = QVBoxLayout(self)
        self.main_layout.setContentsMargins(12, 10, 12, 12)
        self.main_layout.setSpacing(8)

        # 顶部标题区
        title_layout = QHBoxLayout()
        title_layout.setSpacing(5)
        self.lbl_title = QLabel("🧙 融智云考助手 (油猴版)")
        self.lbl_title.setStyleSheet("font-weight: bold; color: #569CD6; font-size: 13px;")
        
        self.lbl_key_status = QLabel("🔑")
        self.lbl_key_status.setToolTip("本地凭证已就绪 - 页面加载后将自动静默填充")
        
        self.btn_min = QPushButton("－")
        self.btn_min.setObjectName("btn_min")
        self.btn_min.setCursor(Qt.PointingHandCursor)
        self.btn_min.clicked.connect(self.toggle_minimize)
        
        title_layout.addWidget(self.lbl_title)
        title_layout.addStretch()
        title_layout.addWidget(self.lbl_key_status)
        title_layout.addWidget(self.btn_min)
        self.main_layout.addLayout(title_layout)

        # 内容区包裹，用于一键隐藏
        self.content_widget = QWidget()
        self.content_layout = QVBoxLayout(self.content_widget)
        self.content_layout.setContentsMargins(0, 0, 0, 0)
        self.content_layout.setSpacing(8)
        
        # 分割线
        line = QFrame()
        line.setFrameShape(QFrame.HLine)
        line.setStyleSheet("background-color: rgba(255,255,255,0.1); max-height: 1px;")
        self.content_layout.addWidget(line)

        # 实用工具栏 (返回与设置)
        util_layout = QHBoxLayout()
        self.btn_back = QPushButton("⬅️ 返回")
        self.btn_back.setObjectName("btn_util")
        self.btn_back.clicked.connect(lambda: self.main_app.browser.back())
        self.btn_back.setEnabled(False) # 默认禁用，等待状态更新
        
        self.btn_settings = QPushButton("⚙️ 设置")
        self.btn_settings.setObjectName("btn_util")
        self.btn_settings.clicked.connect(self.open_settings)
        
        util_layout.addWidget(self.btn_back)
        util_layout.addStretch()
        util_layout.addWidget(self.btn_settings)
        self.content_layout.addLayout(util_layout)

        # 进度面板
        self.lbl_progress = QLabel("当前进度: ⌛ 等待进入练习页面...")
        self.lbl_progress.setStyleSheet("font-size: 12px; color: #A7A7A7;")
        self.content_layout.addWidget(self.lbl_progress)

        # 迷你状态栏
        self.lbl_status_mini = QLabel("系统就绪.")
        self.lbl_status_mini.setStyleSheet("font-size: 11px; color: #6A9955;")
        self.content_layout.addWidget(self.lbl_status_mini)

        # 按钮组
        btn_layout = QHBoxLayout()
        self.btn_toggle = QPushButton("▶ 开始自动提取")
        self.btn_toggle.clicked.connect(self.toggle_extraction)
        
        self.btn_clear = QPushButton("🗑️ 清空")
        self.btn_clear.setToolTip("清空内存中已提取的题目，换科目时使用")
        self.btn_clear.clicked.connect(self.clear_questions)

        self.btn_export = QPushButton("💾 导出")
        self.btn_export.setObjectName("btn_export")
        self.btn_export.setEnabled(False)
        self.btn_export.clicked.connect(lambda: self.main_app.export_basic_questions())
        
        btn_layout.addWidget(self.btn_toggle)
        btn_layout.addWidget(self.btn_clear)
        btn_layout.addWidget(self.btn_export)
        self.content_layout.addLayout(btn_layout)

        self.main_layout.addWidget(self.content_widget)
        self.setFixedSize(280, 185)

    def toggle_minimize(self):
        if self.is_minimized:
            self.content_widget.show()
            self.setFixedSize(280, 185)
            self.btn_min.setText("－")
            self.is_minimized = False
            self.setWindowOpacity(1.0)
        else:
            self.content_widget.hide()
            self.setFixedSize(240, 40)
            self.btn_min.setText("＋")
            self.is_minimized = True

    def open_settings(self):
        dialog = SettingsDialog(self.main_app)
        dialog.config_updated.connect(self.main_app.update_config)
        dialog.exec()

    def toggle_extraction(self):
        if not self.is_extracting:
            self.is_extracting = True
            self.btn_toggle.setText("⏹ 停止提取")
            self.btn_toggle.setStyleSheet("background-color: #D83B01;")
            self.set_mini_status("🟢 自动运行中...", "#D83B01")
            self.btn_export.setEnabled(False)
            
            # 立即触发第一次提取
            self.main_app.trigger_extraction()
        else:
            self.is_extracting = False
            self.btn_toggle.setText("▶ 开始自动提取")
            self.btn_toggle.setStyleSheet("")
            self.set_mini_status("⏸️ 提取已暂停.", "#6A9955")
            if self.main_app.extracted_questions:
                self.btn_export.setEnabled(True)

    def clear_questions(self):
        self.main_app.extracted_questions.clear()
        self.lbl_progress.setText("当前进度: 已清空 0 题")
        self.btn_export.setEnabled(False)
        self.set_mini_status("🗑️ 题库缓存已清空", "#6A9955")

    def set_mini_status(self, text, color="#6A9955"):
        self.lbl_status_mini.setText(text)
        self.lbl_status_mini.setStyleSheet(f"font-size: 11px; color: {color};")

    def mousePressEvent(self, event):
        if event.button() == Qt.LeftButton:
            self.drag_position = event.globalPosition().toPoint() - self.frameGeometry().topLeft()
            event.accept()

    def mouseMoveEvent(self, event):
        if event.buttons() == Qt.LeftButton:
            self.move(event.globalPosition().toPoint() - self.drag_position)
            event.accept()

    def enterEvent(self, event):
        super().enterEvent(event)
        self.setWindowOpacity(1.0)

    def leaveEvent(self, event):
        super().leaveEvent(event)
        # 边缘吸附：折叠状态且靠近边缘时半透明
        if self.is_minimized:
            parent_rect = self.parentWidget().rect()
            my_rect = self.geometry()
            margin = 50
            if (my_rect.left() < margin or my_rect.right() > parent_rect.width() - margin or 
                my_rect.top() < margin or my_rect.bottom() > parent_rect.height() - margin):
                self.setWindowOpacity(0.4)

# ==========================================
# 2. 全屏沉浸式主窗体
# ==========================================
class YunKaoExtractorApp(QMainWindow):
    def __init__(self, current_user, jwt_token, user_data):
        super().__init__()
        self.current_user = current_user
        self.jwt_token = jwt_token
        self.user_data = user_data
        self.is_vip = False
        self.extracted_questions = []
        self.config = load_config()

        nickname = user_data.get('nickname', current_user)
        self.setWindowTitle(f"融智云考题库导出助手 - {nickname}")
        self.resize(1300, 850)

        # 100% 铺满的浏览器组件
        self.browser = QWebEngineView(self)
        self.browser.setZoomFactor(1.25)
        self.setCentralWidget(self.browser)
        self.browser.page().urlChanged.connect(self.on_url_changed)

        # 实例化悬浮窗
        self.overlay = TampermonkeyFloatingWindow(self.browser, main_app=self)
        self.overlay.move(30, 30)
        self.overlay.raise_()

        # JS 桥接配置
        self.channel = QWebChannel()
        self.bridge = ExtractorBridge(self)
        self.channel.registerObject("pybridge", self.bridge)
        self.browser.page().setWebChannel(self.channel)

        self.browser.page().loadFinished.connect(self.on_page_loaded)
        self.browser.load(QUrl("https://www.cctrcloud.net/practice/login.html"))

        self.check_vip_status()

    def update_config(self, new_config):
        self.config = new_config
        
    def on_url_changed(self, url):
        self.overlay.btn_back.setEnabled(self.browser.page().history().canGoBack())
        
        current_url = url.toString()
        # 如果进入了某个具体的练习页面，或者是完全不同的大页面，自动清空题库
        if "subject_practice.html" in current_url or "myself_practice.html" in current_url:
            if hasattr(self, 'last_practice_url') and self.last_practice_url != current_url:
                if self.extracted_questions:
                    self.overlay.clear_questions()
                    self.overlay.set_mini_status("🔄 换科啦！旧题库已自动清空", "#D83B01")
            self.last_practice_url = current_url

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
                if self.is_vip:
                    self.overlay.lbl_title.setText("🧙 融智云考助手 (👑 VIP)")
                    self.overlay.lbl_title.setStyleSheet("font-weight: bold; color: #DAA520; font-size: 13px;")
        except Exception:
            pass # 忽略报错，保持普通形态

    def on_page_loaded(self, ok):
        self.overlay.btn_back.setEnabled(self.browser.page().history().canGoBack())
        if ok:
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
                self.overlay.set_mini_status("✅ 桥接脚本注入成功", "#6A9955")

            # 静默注入密码
            self.trigger_auto_fill()

    def trigger_auto_fill(self):
        pwd = keyring.get_password(SERVICE_NAME, f"{HARDCODED_SCHOOL_CODE}_{self.current_user}")
        if not pwd:
            self.overlay.lbl_key_status.setStyleSheet("color: #888888;") # 置灰
            self.overlay.lbl_key_status.setToolTip("未找到本地存储的密码")
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
        def on_fill(result):
            if result and result > 0:
                self.overlay.lbl_key_status.setStyleSheet("color: #00FF00;")
                self.overlay.set_mini_status("✅ 账号静默填充完毕", "#6A9955")
        
        self.browser.page().runJavaScript(js_code, 0, on_fill)

    def trigger_extraction(self):
        self.overlay.set_mini_status("⏳ 正在读取页面源码...", "#D83B01")
        js_cmd = """
        if (window.pybridge) {
            window.pybridge.receiveRawHtml(document.body.innerHTML);
        }
        """
        self.browser.page().runJavaScript(js_cmd)

    def export_basic_questions(self):
        if not self.extracted_questions:
            QMessageBox.warning(self, "无数据", "当前没有提取到任何题目！")
            return
            
        # 默认保存到桌面，方便检测重名文件
        desktop_dir = os.path.join(os.path.expanduser("~"), "Desktop")
        default_dir = self.config.get("default_export_dir", desktop_dir)
        default_prefix = self.config.get("default_filename_prefix", "融智云考题库")
        
        # 自动生成不重复的文件名，避免覆盖提示
        base_path = os.path.join(default_dir, default_prefix)
        # 默认使用 docx 作为首选格式以方便阅读
        default_path = f"{base_path}.docx"
        counter = 1
        while os.path.exists(default_path) or os.path.exists(f"{base_path}({counter}).docx") or os.path.exists(f"{base_path}({counter}).md") or os.path.exists(f"{base_path}.txt") or os.path.exists(f"{base_path}({counter}).txt"):
            default_path = f"{base_path}({counter}).docx"
            counter += 1

        file_path, filter = QFileDialog.getSaveFileName(
            self, "导出基础题库", default_path, 
            "Word 文档 (*.docx);;Markdown 文件 (*.md);;文本文件 (*.txt)"
        )
        if not file_path:
            return
            
        # 显示加载动画进度条
        self.progress_dialog = QProgressDialog("正在准备导出...", None, 0, len(self.extracted_questions), self)
        self.progress_dialog.setWindowTitle("导出中")
        self.progress_dialog.setWindowModality(Qt.WindowModal)
        self.progress_dialog.setMinimumDuration(0)
        self.progress_dialog.setValue(0)
        self.progress_dialog.show()
        
        self.export_thread = ExportThread(self.extracted_questions, file_path, filter)
        self.export_thread.progress.connect(self._on_export_progress)
        self.export_thread.finished.connect(self._on_export_finished)
        self.export_thread.start()

    def _on_export_progress(self, current, total, message):
        if hasattr(self, 'progress_dialog'):
            self.progress_dialog.setLabelText(message)
            self.progress_dialog.setMaximum(total)
            self.progress_dialog.setValue(current)

    def _on_export_finished(self, success, result):
        if hasattr(self, 'progress_dialog'):
            self.progress_dialog.close()
            
        if success:
            file_path = result
            self.overlay.set_mini_status("✅ 导出成功", "#6A9955")
            QMessageBox.information(self, "导出成功", f"成功导出 {len(self.extracted_questions)} 道题目！\n{file_path}")
            if os.name == 'nt':
                os.startfile(file_path)
        else:
            QMessageBox.critical(self, "导出失败", f"导出过程中出错：{result}")
            self.overlay.set_mini_status("❌ 导出失败", "#D83B01")

    def extract_rich_text(self, element):
        if not element: return ""
        text = ""
        block_tags = {'p', 'div', 'tr', 'li', 'h1', 'h2', 'h3', 'h4', 'h5', 'h6'}
        
        for child in element.contents:
            if isinstance(child, str):
                text += child
            elif child.name == 'br':
                text += "\n"
            elif child.name == 'img':
                src = child.get('src', '')
                if src:
                    style = child.get('style', '')
                    v_align_match = re.search(r'vertical-align:\s*([-0-9.]+)(px|ex|em|pt)', style)
                    if v_align_match:
                        val, unit = v_align_match.groups()
                        text += f"![img]({src}|align:{val}{unit})"
                    else:
                        text += f"![img]({src})"
            elif hasattr(child, 'contents'):
                if child.name in block_tags and text and not text.endswith("\n"):
                    text += "\n"
                text += self.extract_rich_text(child)
                if child.name in block_tags and not text.endswith("\n"):
                    text += "\n"
        return text

    def process_html_with_bs4(self, html_content):
        # [DEBUG] Save the raw HTML to disk so we can analyze it
        try:
            with open(r"e:\AI\yunkao\debug_dom.html", "w", encoding="utf-8") as f:
                f.write(html_content)
        except Exception as e:
            print("Failed to save debug DOM:", e)

        if not html_content or html_content == "ERROR_NOT_FOUND":
            self.overlay.set_mini_status("⚠️ 未找到题目内容，已自动停止", "#D83B01")
            self.overlay.is_extracting = False
            self.overlay.btn_toggle.setText("▶ 开始自动提取")
            self.overlay.btn_toggle.setStyleSheet("")
            if self.extracted_questions:
                self.overlay.btn_export.setEnabled(True)
            return

        soup = BeautifulSoup(html_content, 'html.parser')
        
        target = soup.select_one('.swiper-slide-active')
        if not target: target = soup.select_one('.practice_slide_content')
        if not target:
            self.overlay.set_mini_status("⚠️ 当前页面没有题目", "#D83B01")
            return

        page_info = ""
        current_node = soup.select_one('.swiper-pagination-current')
        total_node = soup.select_one('#swiper-total')
        
        if current_node and total_node:
            page_info = f"{current_node.get_text(strip=True)}/{total_node.get_text(strip=True)}"
        else:
            matches = re.findall(r'(\d+)\s*/\s*(\d+)', soup.get_text())
            if matches:
                best_match = max(matches, key=lambda x: int(x[1]))
                page_info = f"{best_match[0]}/{best_match[1]}"
                
            if not page_info:
                pagination = soup.select_one('.swiper-pagination-fraction, .swiper-pagination, .page_num')
                page_info = pagination.get_text(strip=True) if pagination else ""

        # 提取高级答案功能
        answer_text = ""
        analysis_text = ""
        
        # 寻找真正的题目容器
        content_div = target if 'practice_slide_content' in target.get('class', []) else target.select_one('.practice_slide_content')
        
        # 优先从页面上显示的正确答案区块提取富文本
        ans_mark = target.select_one('.right_ans_mark')
        if ans_mark is not None:
            # 提取前移除可能的 "正确答案：" 标签 span 以防止重复
            label_span = ans_mark.select_one('.label, .title')
            if label_span and "正确答案" in label_span.get_text():
                label_span.decompose()
            answer_text = self.extract_rich_text(ans_mark)
            answer_text = re.sub(r'^正确答案[：:]?\s*', '', answer_text).strip('\r\n')

        # 备用逻辑：从其他解析区块获取
        if not answer_text:
            ans_node = target.select_one('.answer-text')
            if ans_node is not None:
                answer_text = self.extract_rich_text(ans_node).strip('\r\n')
        
        if not answer_text:
            if content_div and content_div.has_attr('data-answer'):
                answer_text = content_div['data-answer'].strip()
                    
        # 尝试精确定位真正的解析文本
        # 必须分步提取，不能用逗号并列，因为带有逗号的 select_one 会返回 DOM 中第一个匹配的元素，从而导致父容器被优先选中！
        analysis_node = target.select_one('.practice_analysis .analysis-content .desc')
        if not analysis_node:
            analysis_node = target.select_one('.analysis-content .desc')
            
        if analysis_node is not None:
            raw_analysis = self.extract_rich_text(analysis_node).strip('\r\n')
            if raw_analysis:
                analysis_text = raw_analysis
        for garbage in target.select('.right_ans_mark, .practice_analysis'):
            garbage.decompose()
            
        title_tag = target.select_one('.practice_slide_title .title') or target.select_one('.practice_slide_title .txt') or target.select_one('.practice_slide_title')
        title_text = self.extract_rich_text(title_tag).strip('\r\n') if title_tag else "未知题目"

        options = []
        correct_labels = []
        for i, li in enumerate(target.select('.option_content li, .options li')):
            auto_label = chr(65 + i)
            
            # 检查这是否是正确选项
            if li.select_one('input[data-isright="1"]') or 'is-right' in li.get('class', []) or 'correct' in li.get('class', []):
                correct_labels.append(auto_label)
                
            txt_tag = li.select_one('.txt')
            opt_elem = txt_tag if txt_tag else li
            opt_text = self.extract_rich_text(opt_elem).strip('\r\n')
            options.append(f"{auto_label}. {opt_text}")

        # 获取题型，用于后续特殊题型的答案提取
        type_tag = target.select_one('.practice_slide_title .type')
        question_type = type_tag.get_text() if type_tag else ""

        # 如果通过选项直接找到了正确答案，就覆盖之前的 answer_text
        if correct_labels:
            real_answer = "".join(correct_labels)
            # 处理判断题
            if '判断' in question_type:
                if real_answer == 'A': real_answer = '对'
                elif real_answer == 'B': real_answer = '错'
            answer_text = real_answer
        elif '填空' in question_type:
            # 填空题答案提取逻辑
            fill_answers = []
            for elem in target.select('.fill_option li .txt, .answer-input-result'):
                text = self.extract_rich_text(elem).strip('\r\n')
                if text: fill_answers.append(text)
            if fill_answers:
                answer_text = "；".join(fill_answers)
            # 如果上面没找到，再试试其他常见容器
            elif not answer_text or answer_text == 'B':
                ans_content = target.select_one('.subjective-answer, .answer-content, .answer-detail')
                if ans_content: answer_text = self.extract_rich_text(ans_content).strip('\r\n')
        elif '简答' in question_type or '计算' in question_type or '名词解释' in question_type or '论述' in question_type:
            # 主观题答案提取逻辑
            ans_content = target.select_one('.subjective-answer, .answer-content, .answer-detail')
            if ans_content:
                answer_text = self.extract_rich_text(ans_content).strip('\r\n')

        # 过滤掉作为干扰项的默认 B 答案（如果前面没成功提取到，且仍然是 B）
        if answer_text == 'B' and not correct_labels and ('填空' in question_type or '简答' in question_type or '计算' in question_type):
            answer_text = ""

        q_dict = {"title": title_text, "options": options}
        if answer_text:
            q_dict["answer"] = answer_text
        if analysis_text:
            q_dict["analysis"] = analysis_text
        
        # 避免重复提取
        is_duplicate = any(q['title'] == q_dict['title'] for q in self.extracted_questions)
        if not is_duplicate:
            self.extracted_questions.append(q_dict)
            prog_text = f"进度: {page_info} (已存 {len(self.extracted_questions)} 题)" if page_info else f"进度: 已存 {len(self.extracted_questions)} 题"
            self.overlay.lbl_progress.setText(prog_text)
            self.overlay.set_mini_status(f"✅ 解析成功: {title_text[:10]}...", "#6A9955")
        else:
            self.overlay.set_mini_status(f"ℹ️ 题目已存在，跳过", "#A7A7A7")

        # 解析当前页码和总页码，用于辅助判断是否真的到了最后一题
        current_page = 0
        total_page = 0
        if page_info:
            try:
                parts = page_info.split('/')
                current_page = int(parts[0].strip())
                total_page = int(parts[1].strip())
            except:
                pass

        # 如果开启了提取开关，则自动进入下一题
        if self.overlay.is_extracting:
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
                    # 点击成功，等待800ms后提取下一题
                    QTimer.singleShot(800, self.trigger_extraction)
                else:
                    # 按钮不可点，检查是否真的到了最后一题
                    if 0 < current_page < total_page:
                        # 还没到最后一题，说明可能是网页卡顿（尤其是多开时），动画未完成或DOM未更新
                        self.overlay.set_mini_status(f"⚠️ 页面加载卡顿，等待重试 ({current_page}/{total_page})...", "#D83B01")
                        QTimer.singleShot(2000, self.trigger_extraction)
                    else:
                        # 到了最后一题
                        self.overlay.set_mini_status("🛑 已到达最后一题，提取完毕", "#6A9955")
                        self.overlay.toggle_extraction()

            self.browser.page().runJavaScript(js_next, 0, on_next_result)

    def resizeEvent(self, event):
        super().resizeEvent(event)
        if hasattr(self, 'overlay'):
            self.overlay.raise_()
