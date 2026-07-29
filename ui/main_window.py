import os
import re
import copy
import keyring
import requests
from bs4 import BeautifulSoup
from PySide6.QtWidgets import (QMainWindow, QWidget, QVBoxLayout, QHBoxLayout, 
                               QPushButton, QLabel, QFileDialog, QMessageBox, QFrame,
                               QProgressDialog, QCheckBox, QGraphicsDropShadowEffect)
from PySide6.QtWebEngineWidgets import QWebEngineView
from PySide6.QtWebChannel import QWebChannel
from PySide6.QtCore import QUrl, Qt, QFile, QTimer, QPoint, QThread, Signal
from PySide6.QtGui import QColor
from PySide6.QtWebEngineWidgets import QWebEngineView
from PySide6.QtWebChannel import QWebChannel
from PySide6.QtCore import QUrl, Qt, QFile, QTimer, QPoint, QThread, Signal

from config.settings import SERVICE_NAME, HARDCODED_SCHOOL_CODE, load_config, save_config
from modules.ai_answer import infer_answer_with_ai, should_use_ai, is_placeholder_answer
from modules.js_bridge import ExtractorBridge
from modules.exporter import export_to_markdown, export_to_txt
from modules.extraction_state import ExtractionRunState
from modules.question_parser import parse_active_question
from ui.settings_dialog import SettingsDialog
from ui.theme import OVERLAY_STYLE
from ui.widgets import ToggleSwitch
import json

class ExportThread(QThread):
    finished = Signal(bool, str)
    progress = Signal(int, int, str)
    
    def __init__(self, questions, file_path, filter_type, include_answers=True):
        super().__init__()
        self.questions = questions
        self.file_path = file_path
        self.filter_type = filter_type
        self.include_answers = include_answers
        
    def run(self):
        def report_progress(current, total, message):
            self.progress.emit(current, total, message)
            
        try:
            if self.filter_type.startswith("Word"):
                from modules.exporter import export_to_docx
                export_to_docx(
                    self.questions,
                    self.file_path,
                    progress_callback=report_progress,
                    include_answers=self.include_answers,
                )
            elif self.filter_type.startswith("PDF"):
                from modules.exporter import export_to_pdf
                export_to_pdf(
                    self.questions,
                    self.file_path,
                    progress_callback=report_progress,
                    include_answers=self.include_answers,
                )
            elif self.filter_type.startswith("Markdown"):
                from modules.exporter import export_to_markdown
                export_to_markdown(
                    self.questions,
                    self.file_path,
                    include_answers=self.include_answers,
                )
            else:
                from modules.exporter import export_to_txt
                export_to_txt(
                    self.questions,
                    self.file_path,
                    include_answers=self.include_answers,
                )
            self.finished.emit(True, self.file_path)
        except Exception as e:
            self.finished.emit(False, str(e))


class AiFillThread(QThread):
    completed = Signal(int, int, dict)
    failed = Signal(int, int, str)

    def __init__(self, session_id, question_index, question, config, jwt_token):
        super().__init__()
        self.session_id = session_id
        self.question_index = question_index
        self.question = copy.deepcopy(question)
        self.config = dict(config)
        self.jwt_token = jwt_token

    def run(self):
        try:
            result = infer_answer_with_ai(self.question, self.config, jwt_token=self.jwt_token)
            self.completed.emit(self.session_id, self.question_index, result)
        except Exception as exc:
            self.failed.emit(self.session_id, self.question_index, str(exc))





# ==========================================
# 1. 油猴脚本风格的悬浮操作窗 (Overlay Widget)
# ==========================================
class TampermonkeyFloatingWindow(QFrame):
    EXPANDED_WIDTH = 320
    MIN_EXPANDED_HEIGHT = 248
    COLLAPSED_WIDTH = 238
    COLLAPSED_HEIGHT = 42

    def __init__(self, parent=None, main_app=None):
        super().__init__(parent)
        self.main_app = main_app
        self.setWindowFlags(Qt.SubWindow) # 设为子窗体
        self.is_extracting = False  # 提取状态开关
        self.is_minimized = False   # 最小化状态
        self.drag_position = QPoint()

        self.init_ui()

    def init_ui(self):
        self.setStyleSheet(OVERLAY_STYLE)

        # 阴影让悬浮面板与网页内容形成清晰层级，不影响透明背景。
        shadow = QGraphicsDropShadowEffect(self)
        shadow.setBlurRadius(28)
        shadow.setOffset(0, 7)
        shadow.setColor(QColor(15, 23, 42, 90))
        self.setGraphicsEffect(shadow)

        self.main_layout = QVBoxLayout(self)
        self.main_layout.setContentsMargins(14, 11, 14, 13)
        self.main_layout.setSpacing(7)

        # 顶部标题区
        title_layout = QHBoxLayout()
        title_layout.setSpacing(5)
        self.lbl_title = QLabel("融智云考助手")
        self.lbl_title.setObjectName("overlayTitle")
        
        self.lbl_key_status = QLabel("●")
        self.lbl_key_status.setObjectName("credentialStatus")
        self.lbl_key_status.setToolTip("本地凭证已就绪 - 页面加载后将自动静默填充")
        
        self.btn_min = QPushButton("－")
        self.btn_min.setObjectName("btn_min")
        self.btn_min.setCursor(Qt.PointingHandCursor)
        self.btn_min.clicked.connect(self.toggle_minimize)
        
        title_layout.addWidget(self.lbl_title)
        title_layout.addStretch(1)
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
        line.setObjectName("overlayDivider")
        self.content_layout.addWidget(line)

        # 实用工具栏 (返回与设置)
        util_layout = QHBoxLayout()
        self.btn_back = QPushButton("返回")
        self.btn_back.setObjectName("btn_util")
        self.btn_back.clicked.connect(lambda: self.main_app.browser.back())
        self.btn_back.setEnabled(False) # 默认禁用，等待状态更新
        
        self.btn_settings = QPushButton("设置")
        self.btn_settings.setObjectName("btn_util")
        self.btn_settings.clicked.connect(self.open_settings)
        
        util_layout.addWidget(self.btn_back)
        util_layout.addStretch()
        util_layout.addWidget(self.btn_settings)
        self.content_layout.addLayout(util_layout)


        # 进度面板
        self.lbl_progress = QLabel("等待进入练习页面")
        self.lbl_progress.setObjectName("progressLabel")
        self.lbl_progress.setWordWrap(True)
        self.content_layout.addWidget(self.lbl_progress)

        # 迷你状态栏
        self.lbl_status_mini = QLabel("系统就绪")
        self.lbl_status_mini.setObjectName("statusLabel")
        self.lbl_status_mini.setWordWrap(True)
        self.content_layout.addWidget(self.lbl_status_mini)

        # 练习版使用轻量开关行，避免整块高饱和边框抢占注意力。
        practice_row = QFrame()
        practice_row.setObjectName("practiceRow")
        practice_layout = QHBoxLayout(practice_row)
        practice_layout.setContentsMargins(9, 5, 8, 5)
        practice_layout.setSpacing(8)
        practice_label = QLabel("练习版 · 隐藏答案与解析")
        practice_label.setObjectName("practiceLabel")
        self.chk_practice_export = ToggleSwitch()
        self.chk_practice_export.setChecked(
            self.main_app.config.get("export_without_answers", False)
        )
        self.chk_practice_export.setToolTip(
            "选择、判断、填空题保留紧凑作答位；主观题保留三行手写空间"
        )
        self.chk_practice_export.toggled.connect(
            self.main_app.update_practice_export_mode
        )
        practice_layout.addWidget(practice_label)
        practice_layout.addStretch(1)
        practice_layout.addWidget(self.chk_practice_export)
        self.content_layout.addWidget(practice_row)

        # 按钮组
        btn_layout = QHBoxLayout()
        self.btn_toggle = QPushButton("开始提取")
        self.btn_toggle.setObjectName("btn_primary")
        self.btn_toggle.clicked.connect(self.toggle_extraction)
        
        self.btn_clear = QPushButton("清空")
        self.btn_clear.setObjectName("btn_secondary")
        self.btn_clear.setToolTip("清空内存中已提取的题目，换科目时使用")
        self.btn_clear.clicked.connect(self.clear_questions)

        self.btn_export = QPushButton("导出")
        self.btn_export.setObjectName("btn_export")
        self.btn_export.setEnabled(False)
        self.btn_export.clicked.connect(lambda: self.main_app.export_basic_questions())
        
        btn_layout.addWidget(self.btn_toggle)
        btn_layout.addWidget(self.btn_clear)
        btn_layout.addWidget(self.btn_export)
        self.content_layout.addLayout(btn_layout)

        self.main_layout.addWidget(self.content_widget)
        self.setFixedWidth(self.EXPANDED_WIDTH)
        self._refresh_expanded_size()
        # 首次显示后 Qt 才能得到真实字体与 DPI 度量，再校正一次展开高度。
        QTimer.singleShot(0, self._refresh_expanded_size)

    def _refresh_expanded_size(self):
        """按当前文字和系统 DPI 重新计算展开态高度，避免底部控件被裁切。"""
        if self.is_minimized:
            return
        self.setMinimumHeight(0)
        self.setMaximumHeight(16777215)
        self.main_layout.activate()
        required_height = max(
            self.MIN_EXPANDED_HEIGHT,
            self.main_layout.sizeHint().height(),
        )
        self.resize(self.EXPANDED_WIDTH, required_height)

    def toggle_minimize(self):
        if self.is_minimized:
            self.is_minimized = False
            self.setMinimumSize(0, 0)
            self.setMaximumSize(16777215, 16777215)
            self.setFixedWidth(self.EXPANDED_WIDTH)
            self.content_widget.show()
            self._refresh_expanded_size()
            self.btn_min.setText("－")
            self.setWindowOpacity(1.0)
        else:
            self.is_minimized = True
            self.content_widget.hide()
            self.setFixedSize(self.COLLAPSED_WIDTH, self.COLLAPSED_HEIGHT)
            self.btn_min.setText("＋")

    def open_settings(self):
        dialog = SettingsDialog(self.main_app)
        dialog.config_updated.connect(self.main_app.update_config)
        dialog.exec()

    def toggle_extraction(self):
        if not self.main_app.extraction_state.is_active:
            self.main_app.start_extraction()
        else:
            self.main_app.stop_extraction(status_text="⏸️ 提取已暂停.")

    def clear_questions(self):
        self.main_app.clear_extracted_questions()

    def set_mini_status(self, text, color="#6A9955"):
        self.lbl_status_mini.setText(text)
        self.lbl_status_mini.setStyleSheet(f"color: {color}; font-size: 10px; padding: 0 2px;")
        QTimer.singleShot(0, self._refresh_expanded_size)

    def set_progress_text(self, text):
        """更新进度说明，并在内容换行时同步扩展悬浮窗。"""
        self.lbl_progress.setText(text)
        QTimer.singleShot(0, self._refresh_expanded_size)

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
        self.seen_question_keys = set()
        self.pending_ai_workers = {}
        self.ai_session_id = 0
        self.last_question_marker = ""
        self.extraction_state = ExtractionRunState()
        self.config = load_config()

        nickname = user_data.get('nickname', current_user)
        self.setWindowTitle(f"融智云考题库导出助手 - 免费使用 · 禁止倒卖 - {nickname}")
        self.resize(1300, 850)

        # 100% 铺满的浏览器组件
        self.browser = QWebEngineView(self)
        self.setCentralWidget(self.browser)

        # 浏览器事件、悬浮操作区和网页桥接必须一起初始化，缺少任一项都会使提取链路失效。
        self.browser.page().urlChanged.connect(self.on_url_changed)

        self.overlay = TampermonkeyFloatingWindow(self.browser, main_app=self)
        self.overlay.move(30, 30)
        self.overlay.show()
        self.overlay.raise_()

        self.channel = QWebChannel(self.browser.page())
        self.bridge = ExtractorBridge(self)
        self.channel.registerObject("pybridge", self.bridge)
        self.browser.page().setWebChannel(self.channel)

        self.browser.page().loadFinished.connect(self.on_page_loaded)
        self.browser.load(QUrl("https://www.cctrcloud.net/practice/login.html"))

    def refresh_export_button(self):
        pending_jobs = len(self.pending_ai_workers)
        can_export = (
            bool(self.extracted_questions)
            and not self.overlay.is_extracting
            and pending_jobs == 0
        )
        self.overlay.btn_export.setEnabled(can_export)

    def _set_extraction_ui(self, active):
        self.overlay.is_extracting = active
        self.overlay.btn_toggle.setProperty("extracting", active)
        self.overlay.btn_toggle.style().unpolish(self.overlay.btn_toggle)
        self.overlay.btn_toggle.style().polish(self.overlay.btn_toggle)
        if active:
            self.overlay.btn_toggle.setText("停止提取")
        else:
            self.overlay.btn_toggle.setText("开始提取")
        self.refresh_export_button()

    def start_extraction(self):
        run_id = self.extraction_state.start()
        self._parse_retry_count = 0
        self._set_extraction_ui(True)
        self.overlay.set_mini_status("🟢 自动运行中...", "#D83B01")
        self.trigger_extraction(run_id)
        return run_id

    def stop_extraction(self, run_id=None, status_text=None, color="#6A9955"):
        if not self.extraction_state.stop(run_id):
            return False
        self._parse_retry_count = 0
        self._set_extraction_ui(False)
        if status_text:
            self.overlay.set_mini_status(status_text, color)
        return True

    def _schedule_extraction(self, run_id, delay_ms):
        QTimer.singleShot(delay_ms, lambda: self.trigger_extraction(run_id))

    def clear_extracted_questions(self):
        self.ai_session_id += 1
        self.pending_ai_workers = {}
        self.extracted_questions.clear()
        self.seen_question_keys.clear()
        self.last_question_marker = ""
        self.overlay.set_progress_text("当前进度: 已清空 0 题")
        self.overlay.set_mini_status("🗑️ 题库缓存已清空", "#6A9955")
        self.refresh_export_button()

    def _build_question_key(self, question):
        question_id = str(question.get("question_id", "") or "").strip()
        if question_id:
            return f"id:{question_id}"
        options = question.get("options", []) or []
        signature = "|".join(str(item).strip() for item in options[:4])
        return f"sig:{question.get('question_type', '')}|{question.get('title', '').strip()}|{signature}"

    def _queue_ai_fill(self, question_index, question, page_info):
        worker = AiFillThread(
            self.ai_session_id,
            question_index,
            question,
            self.config,
            "",
        )
        key = (self.ai_session_id, question_index)
        self.pending_ai_workers[key] = worker
        worker.completed.connect(self._on_ai_fill_completed)
        worker.failed.connect(self._on_ai_fill_failed)
        worker.finished.connect(lambda key=key: self._cleanup_ai_fill_worker(key))
        self.overlay.set_mini_status("🤖 AI答题中...", "#D83B01")
        self.overlay.set_progress_text(
            f"进度: {page_info} (已存 {len(self.extracted_questions)} 题，AI答题中)"
            if page_info else f"进度: 已存 {len(self.extracted_questions)} 题，AI答题中"
        )
        self.refresh_export_button()
        worker.start()

    def _cleanup_ai_fill_worker(self, key):
        self.pending_ai_workers.pop(key, None)
        self.refresh_export_button()
        if not self.overlay.is_extracting and not self.pending_ai_workers and self.extracted_questions:
            self.overlay.set_mini_status("✅ AI 补全已全部完成", "#6A9955")

    def _on_ai_fill_completed(self, session_id, question_index, ai_result):
        if session_id != self.ai_session_id:
            return
        if not (0 <= question_index < len(self.extracted_questions)):
            return

        question = self.extracted_questions[question_index]
        answer = ai_result.get("answer", "")
        source = ai_result.get("source", "ai")
        if answer and not is_placeholder_answer(answer):
            question["answer"] = answer
            question["answer_source"] = "ai"
            question["answer_confidence"] = ai_result.get("confidence", 0.0)
            question["needs_review"] = ai_result.get("confidence", 0.0) < 0.75
            question["answer_route"] = source
            question["ai_usage"] = ai_result.get("usage", {})
            question["ai_billing"] = ai_result.get("billing", {})
            question["ai_model"] = ai_result.get("model", {})

            ai_analysis = (ai_result.get("analysis") or "").strip()
            if ai_analysis and not question.get("analysis"):
                question["analysis"] = ai_analysis
                question["analysis_source"] = "ai"

        usage = ai_result.get("usage", {}) or {}
        model_info = ai_result.get("model", {}) or {}
        total_tokens = int(usage.get("total_tokens", 0) or 0)
        cache_ref_tokens = int(usage.get("cache_reference_tokens", 0) or 0)
        cache_hit = ai_result.get("billing", {}).get("cache_hit", False)

        model_name = model_info.get("model_name", "")
        if source == "cache":
            route_text = f"缓存命中({model_name})" if model_name else "缓存命中"
        elif source == "ai":
            route_text = f"官方AI({model_name})" if model_name else "官方AI"
        else:
            route_text = "自定义API"

        usage_text = f"{total_tokens} tokens"
        if cache_hit and cache_ref_tokens > 0:
            usage_text = f"本次 0t / 参考 {cache_ref_tokens}t"
        if not cache_hit and source != "direct":
            usage_text = f"输入 {usage.get('prompt_tokens', 0)}t / 输出 {usage.get('completion_tokens', 0)}t"

        self.overlay.set_mini_status(
            f"🤖 {route_text}: {usage_text}",
            "#6A9955" if cache_hit else "#DAA520",
        )

    def _on_ai_fill_failed(self, session_id, question_index, error_text):
        if session_id != self.ai_session_id:
            return
        self.overlay.set_mini_status(f"⚠️ AI 答题失败: {error_text[:40]}", "#D83B01")

    def update_config(self, new_config):
        self.config = new_config
        if hasattr(self.overlay, "chk_practice_export"):
            self.overlay.chk_practice_export.blockSignals(True)
            self.overlay.chk_practice_export.setChecked(
                new_config.get("export_without_answers", False)
            )
            self.overlay.chk_practice_export.blockSignals(False)
        configured_user = str(new_config.get("yunkao_user", "") or "").strip()
        self.current_user = configured_user
        self.trigger_auto_fill()

    def update_practice_export_mode(self, enabled):
        """即时切换练习版导出，并保存到用户配置。"""
        self.config["export_without_answers"] = bool(enabled)
        save_config(self.config)
        mode_text = "练习版（不含答案）" if enabled else "含答案版"
        self.overlay.set_mini_status(f"📄 已切换为{mode_text}", "#569CD6")

    def on_url_changed(self, url):
        self.overlay.btn_back.setEnabled(self.browser.page().history().canGoBack())
        
        current_url = url.toString()

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
            # 登录表单可能由前端框架延迟挂载，短暂重试可保证字段最终写入。
            QTimer.singleShot(500, self.trigger_auto_fill)
            QTimer.singleShot(1500, self.trigger_auto_fill)

    def trigger_auto_fill(self):
        host = self.browser.url().host().lower()
        if host not in {"www.cctrcloud.net", "cctrcloud.net"}:
            self.overlay.lbl_key_status.setStyleSheet("color: #888888;")
            self.overlay.lbl_key_status.setToolTip("当前页面不是可信的融智云考域名，已跳过自动填充")
            return

        try:
            pwd = keyring.get_password(
                SERVICE_NAME,
                f"{HARDCODED_SCHOOL_CODE}_{self.current_user}",
            )
        except Exception:
            pwd = None

        if not pwd:
            self.overlay.lbl_key_status.setStyleSheet("color: #888888;") # 置灰
            self.overlay.lbl_key_status.setToolTip("学校编码和学号将自动填写；尚未保存密码")

        school_code_js = json.dumps(HARDCODED_SCHOOL_CODE)
        current_user_js = json.dumps(self.current_user)
        password_js = json.dumps(pwd if pwd else None)
        js_code = f"""
        (function() {{
            const schoolCode = {school_code_js};
            const studentNumber = {current_user_js};
            const password = {password_js};
            const inputs = document.querySelectorAll('input');
            let filled = 0;
            const nativeInputValueSetter = Object.getOwnPropertyDescriptor(
                window.HTMLInputElement.prototype,
                "value"
            ).set;

            function fillInput(input, value) {{
                if (value === null || value === undefined || value === '') return;
                nativeInputValueSetter.call(input, value);
                input.dispatchEvent(new Event('input', {{ bubbles: true }}));
                input.dispatchEvent(new Event('change', {{ bubbles: true }}));
                filled++;
            }}

            inputs.forEach(inp => {{
                const hint = [
                    inp.placeholder,
                    inp.name,
                    inp.id,
                    inp.autocomplete
                ].filter(Boolean).join(' ').toLowerCase();

                if (/学校|机构|school|college|org/.test(hint)) {{
                    fillInput(inp, schoolCode);
                }}
                else if (/学号|账号|用户名|student|account|username|user/.test(hint)) {{
                    fillInput(inp, studentNumber);
                }}
                else if (inp.type === 'password') {{
                    fillInput(inp, password);
                }}
            }});
            return filled;
        }})();
        """
        def on_fill(result):
            if result and result > 0:
                if pwd:
                    self.overlay.lbl_key_status.setStyleSheet("color: #00FF00;")
                    self.overlay.lbl_key_status.setToolTip("学校编码、学号和密码已自动填写")
                    self.overlay.set_mini_status("✅ 登录信息自动填写完毕", "#6A9955")
                else:
                    self.overlay.set_mini_status("✅ 学校编码和学号已自动填写", "#6A9955")
        
        self.browser.page().runJavaScript(js_code, 0, on_fill)

    def trigger_extraction(self, run_id=None):
        if run_id is None:
            run_id = self.extraction_state.active_run_id
        if not self.extraction_state.matches(run_id):
            return
        self.overlay.set_mini_status("⏳ 正在提取题目...", "#D83B01")
        js_cmd = f"""
        if (window.pybridge) {{
            window.pybridge.receiveRawHtmlForRun(document.body.innerHTML, {run_id});
        }}
        """
        self.browser.page().runJavaScript(js_cmd)

    def export_basic_questions(self):
        if not self.extracted_questions:
            QMessageBox.warning(self, "无数据", "当前没有提取到任何题目！")
            return
        if self.pending_ai_workers:
            QMessageBox.information(self, "请稍候", "仍有题目正在等待 AI 答题完成，请稍后再导出。")
            return

        if os.environ.get("YUNKAO_DEBUG_DUMP", "").strip() == "1":
            dump_dir = os.path.join(os.environ.get("LOCALAPPDATA", os.getcwd()), "YunKao", "debug")
            os.makedirs(dump_dir, exist_ok=True)
            dump_path = os.path.join(dump_dir, "questions_dump.json")
            with open(dump_path, 'w', encoding='utf-8') as f:
                json.dump(self.extracted_questions, f, ensure_ascii=False, indent=2)
            
        # 默认保存到桌面，方便检测重名文件
        desktop_dir = os.path.join(os.path.expanduser("~"), "Desktop")
        default_dir = self.config.get("default_export_dir", desktop_dir)
        default_prefix = self.config.get("default_filename_prefix", "融智云考题库")
        
        # 自动生成不重复的文件名，避免覆盖提示
        base_path = os.path.join(default_dir, default_prefix)
        counter = 1
        while any(os.path.exists(f"{base_path}{'(' + str(counter) + ')' if counter > 1 else ''}.{ext}") for ext in ['docx', 'pdf', 'md', 'txt']):
            counter += 1
            
        suffix = f"({counter})" if counter > 1 else ""
        default_path = f"{base_path}{suffix}"

        file_path, filter = QFileDialog.getSaveFileName(
            self, "导出基础题库", default_path, 
            "PDF 文件 (*.pdf);;Word 文档 (*.docx);;Markdown 文件 (*.md);;文本文件 (*.txt)"
        )
        if not file_path:
            return
            
        # 显示加载动画进度条
        self.progress_dialog = QProgressDialog("正在准备导出...", None, 0, max(len(self.extracted_questions), 1) + 2, self)
        self.progress_dialog.setWindowTitle("导出中")
        self.progress_dialog.setWindowModality(Qt.WindowModal)
        self.progress_dialog.setMinimumDuration(0)
        self.progress_dialog.setValue(0)
        self.progress_dialog.show()
        self.overlay.set_mini_status("📄 正在准备导出...", "#D83B01")
        
        include_answers = not self.config.get("export_without_answers", False)
        self.export_thread = ExportThread(
            self.extracted_questions,
            file_path,
            filter,
            include_answers=include_answers,
        )
        self.export_thread.progress.connect(self._on_export_progress)
        self.export_thread.finished.connect(self._on_export_finished)
        self.export_thread.start()

    def _on_export_progress(self, current, total, message):
        if hasattr(self, 'progress_dialog'):
            self.progress_dialog.setLabelText(message)
            self.progress_dialog.setMaximum(total)
            self.progress_dialog.setValue(current)
        self.overlay.set_mini_status(message, "#D83B01")

    def _on_export_finished(self, success, result):
        if hasattr(self, 'progress_dialog'):
            self.progress_dialog.close()
            
        if success:
            file_path = result
            self.overlay.set_mini_status("✅ 导出成功", "#6A9955")
            QMessageBox.information(self, "导出成功", f"成功导出 {len(self.extracted_questions)} 道题目！\n{file_path}")
            if os.name == 'nt' and self.config.get("auto_open_after_export", True):
                os.startfile(file_path)
        else:
            QMessageBox.critical(self, "导出失败", f"导出过程中出错：{result}")
            self.overlay.set_mini_status("❌ 导出失败", "#D83B01")

    def extract_rich_text(self, element):
        if not element: return ""
        text = ""
        block_tags = {'p', 'div', 'li', 'h1', 'h2', 'h3', 'h4', 'h5', 'h6'}
        
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
                    w_match = re.search(r'width:\s*([-0-9.]+)(px|ex|em|pt|%)', style)
                    h_match = re.search(r'height:\s*([-0-9.]+)(px|ex|em|pt|%)', style)
                    
                    w_val, w_unit, h_val, h_unit = None, None, None, None
                    if w_match: w_val, w_unit = w_match.groups()
                    else:
                        w_attr = child.get('width')
                        if w_attr and re.match(r'^[-0-9.]+$', str(w_attr)): w_val, w_unit = w_attr, 'px'
                        elif w_attr: 
                            m = re.match(r'^([-0-9.]+)(px|ex|em|pt|%)$', str(w_attr))
                            if m: w_val, w_unit = m.groups()
                            
                    if h_match: h_val, h_unit = h_match.groups()
                    else:
                        h_attr = child.get('height')
                        if h_attr and re.match(r'^[-0-9.]+$', str(h_attr)): h_val, h_unit = h_attr, 'px'
                        elif h_attr:
                            m = re.match(r'^([-0-9.]+)(px|ex|em|pt|%)$', str(h_attr))
                            if m: h_val, h_unit = m.groups()

                    align_str = f"|align:{v_align_match.group(1)}{v_align_match.group(2)}" if v_align_match else ""
                    w_str = f"|w:{w_val}{w_unit}" if w_val else ""
                    h_str = f"|h:{h_val}{h_unit}" if h_val else ""
                        
                    text += f"![img]<{src}{align_str}{w_str}{h_str}>"
            elif child.name == 'table':
                if text and not text.endswith("\n"):
                    text += "\n"
                for row in child.find_all('tr'):
                    row_data = []
                    for cell in row.find_all(['td', 'th']):
                        cell_text = self.extract_rich_text(cell).strip().replace('\n', ' ')
                        row_data.append(cell_text)
                    if row_data:
                        text += " \t".join(row_data) + "\n"
                text += "\n"
            elif hasattr(child, 'contents'):
                if child.name in block_tags and text and not text.endswith("\n"):
                    text += "\n"
                text += self.extract_rich_text(child)
                if child.name in block_tags and not text.endswith("\n"):
                    text += "\n"
        return text

    def process_html_with_bs4(self, html_content, run_id=None):
        if run_id is None:
            run_id = self.extraction_state.active_run_id
        if not self.extraction_state.matches(run_id):
            return

        if self.config.get("debug_save_dom"):
            try:
                with open(r"e:\AI\yunkao\debug_dom.html", "w", encoding="utf-8") as f:
                    f.write(html_content)
            except Exception as e:
                print("Failed to save debug DOM:", e)

        if not html_content or html_content == "ERROR_NOT_FOUND":
            self.stop_extraction(
                run_id,
                "⚠️ 未找到题目内容，已自动停止",
                "#D83B01",
            )
            return

        parsed_question = parse_active_question(html_content)
        if not parsed_question:
            # 页面可能仍在渲染，短暂等待后重试一次
            if getattr(self, '_parse_retry_count', 0) < 1:
                self._parse_retry_count = getattr(self, '_parse_retry_count', 0) + 1
                self.overlay.set_mini_status("⏳ 题目内容未就绪，等待重试...", "#D83B01")
                self._schedule_extraction(run_id, 800)
                return
            self._parse_retry_count = 0
            self.overlay.set_mini_status("⚠️ 题目解析失败，尝试跳到下一题...", "#D83B01")
            # 不静默死亡，改为触发翻页
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
                def on_next_fallback(result):
                    if not self.extraction_state.matches(run_id):
                        return
                    if result:
                        self._schedule_extraction(run_id, 800)
                    else:
                        self.stop_extraction(
                            run_id,
                            "⚠️ 无法翻页，已自动停止，请手动检查",
                            "#D83B01",
                        )
                self.browser.page().runJavaScript(js_next, 0, on_next_fallback)
            return
        self._parse_retry_count = 0

        page_info = parsed_question.pop("page_info", "")
        question_marker = parsed_question.pop("marker", "")
        question_key = self._build_question_key(parsed_question)

        if question_key not in self.seen_question_keys:
            self.seen_question_keys.add(question_key)
            self.extracted_questions.append(parsed_question)
            question_index = len(self.extracted_questions) - 1
            self.last_question_marker = question_marker or question_key

            if should_use_ai(parsed_question, self.config):
                self._queue_ai_fill(question_index, parsed_question, page_info)
            else:
                prog_text = (
                    f"进度: {page_info} (已存 {len(self.extracted_questions)} 题)"
                    if page_info else f"进度: 已存 {len(self.extracted_questions)} 题"
                )
                self.overlay.set_progress_text(prog_text)
                self.overlay.set_mini_status(
                    f"✅ 解析成功: {parsed_question.get('title', '')[:10]}...",
                    "#6A9955",
                )
            self.refresh_export_button()
        else:
            self.last_question_marker = question_marker or question_key
            self.overlay.set_mini_status("ℹ️ 题目已存在，跳过", "#A7A7A7")

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
                if not self.extraction_state.matches(run_id):
                    return
                if result:
                    # 点击成功，等待 800ms 让 swiper 动画完成后再提取下一题
                    self._schedule_extraction(run_id, 800)
                else:
                    # 按钮不可点，检查是否真的到了最后一题
                    if 0 < current_page < total_page:
                        # 还没到最后一题，可能是动画未完成或 DOM 未更新
                        self.overlay.set_mini_status(
                            f"⚠️ 页面加载卡顿，等待重试 ({current_page}/{total_page})...",
                            "#D83B01",
                        )
                        self._schedule_extraction(run_id, 2000)
                    else:
                        # 到了最后一题
                        self.stop_extraction(
                            run_id,
                            "🛑 已到达最后一题，提取完毕",
                        )

            self.browser.page().runJavaScript(js_next, 0, on_next_result)

    def resizeEvent(self, event):
        super().resizeEvent(event)
        if hasattr(self, 'overlay'):
            self.overlay.raise_()
