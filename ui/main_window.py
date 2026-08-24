import os
import re
import copy
import keyring
from PySide6.QtWidgets import (QMainWindow, QWidget, QVBoxLayout, QHBoxLayout, 
                               QFileDialog, QMessageBox, QProgressDialog)
from PySide6.QtWebEngineWidgets import QWebEngineView
from PySide6.QtWebChannel import QWebChannel
from PySide6.QtCore import QUrl, Qt, QFile, QTimer, QThread, Signal
from PySide6.QtGui import QDesktopServices

from config.settings import SERVICE_NAME, HARDCODED_SCHOOL_CODE, load_config, save_config
from config.version import APP_RELEASE
from modules.ai_answer import infer_answer_with_ai, should_use_ai, is_placeholder_answer
from modules.js_bridge import ExtractorBridge
from modules.extraction_state import ExtractionRunState
from modules.question_parser import parse_active_question
from ui.settings_dialog import SettingsDialog
from ui.theme import (
    APP_SHELL_STYLE,
    STATUS_AI,
    STATUS_ERROR,
    STATUS_INFO,
    STATUS_MUTED,
    STATUS_SUCCESS,
    STATUS_WARNING,
)
from ui.browser_shell import BrowserShell
from ui.control_panel import ControlPanel
from ui.navigation_rail import NavigationRail
from ui.title_bar import TitleBar
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

    def __init__(self, session_id, question_index, question, config, jwt_token, parent=None):
        super().__init__(parent)
        self.session_id = session_id
        self.question_index = question_index
        self.question = copy.deepcopy(question)
        self.config = dict(config)
        self.jwt_token = jwt_token

    def run(self):
        try:
            if self.isInterruptionRequested():
                return
            result = infer_answer_with_ai(self.question, self.config, jwt_token=self.jwt_token)
            if self.isInterruptionRequested():
                return
            self.completed.emit(self.session_id, self.question_index, result)
        except Exception as exc:
            self.failed.emit(self.session_id, self.question_index, str(exc))





# ==========================================
# 主窗体
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
        self._live_ai_workers = {}
        self.ai_session_id = 0
        self._closing = False
        self._close_after_ai_workers = False
        self._browser_fit_timer = QTimer(self)
        self._browser_fit_timer.setSingleShot(True)
        self._browser_fit_timer.setInterval(250)
        self._browser_fit_timer.timeout.connect(self._fit_browser_content)
        self.last_question_marker = ""
        self.extraction_state = ExtractionRunState()
        self.config = load_config()

        nickname = user_data.get('nickname', current_user)
        self.setWindowTitle(
            f"融智云考题库导出助手 {APP_RELEASE} - 免费使用 · 禁止倒卖 - {nickname}"
        )
        self.setWindowFlags(Qt.FramelessWindowHint | Qt.Window)
        self.setStyleSheet(APP_SHELL_STYLE)
        self.resize(1300, 850)

        self._build_shell()

        # 浏览器事件、控制台和网页桥接必须一起初始化，缺少任一项都会使提取链路失效。
        self.browser.page().urlChanged.connect(self.on_url_changed)

        self.channel = QWebChannel(self.browser.page())
        self.bridge = ExtractorBridge(self)
        self.channel.registerObject("pybridge", self.bridge)
        self.browser.page().setWebChannel(self.channel)

        self.browser.page().loadFinished.connect(self.on_page_loaded)
        self.browser.load(QUrl("https://www.cctrcloud.net/practice/login.html"))

    def _build_shell(self):
        """装配标题栏、导航、浏览器和控制台，不触碰提取业务对象。"""
        shell = QWidget()
        shell.setObjectName("appShell")
        shell_layout = QVBoxLayout(shell)
        shell_layout.setContentsMargins(0, 0, 0, 0)
        shell_layout.setSpacing(0)

        self.title_bar = TitleBar(shell)
        self.title_bar.minimize_requested.connect(self.showMinimized)
        self.title_bar.maximize_requested.connect(self._toggle_maximized)
        self.title_bar.close_requested.connect(self.close)
        shell_layout.addWidget(self.title_bar)

        workspace = QWidget()
        workspace_layout = QHBoxLayout(workspace)
        workspace_layout.setContentsMargins(10, 10, 10, 10)
        workspace_layout.setSpacing(10)

        self.navigation = NavigationRail(workspace)
        self.browser_shell = BrowserShell(workspace)
        self.control_panel = ControlPanel(workspace, main_app=self)

        # overlay 是历史业务层使用的兼容名称，实际对象已经是停靠式控制台。
        self.overlay = self.control_panel
        self.overlay.btn_back = self.browser_shell.btn_back
        self.overlay.chk_practice_export.blockSignals(True)
        self.overlay.chk_practice_export.setChecked(
            self.config.get("export_without_answers", False)
        )
        self.overlay.chk_practice_export.blockSignals(False)
        self.overlay.chk_practice_export.toggled.connect(self.update_practice_export_mode)

        self.browser = self.browser_shell.browser
        workspace_layout.addWidget(self.navigation)
        workspace_layout.addWidget(self.browser_shell, 1)
        workspace_layout.addWidget(self.control_panel)
        shell_layout.addWidget(workspace, 1)
        self.setCentralWidget(shell)

        self.navigation.page_requested.connect(self._on_navigation_page_requested)
        self.navigation.btn_dock.clicked.connect(self.control_panel.toggle_minimize)
        self.browser_shell.external_open_requested.connect(self._open_external_browser)

    def _toggle_maximized(self):
        if self.isMaximized():
            self.showNormal()
        else:
            self.showMaximized()

    def _on_navigation_page_requested(self, page_id):
        if page_id == "settings":
            self.overlay.open_settings()
            return
        if page_id == "workspace":
            self.overlay.set_mini_status("已返回练习工作台", STATUS_INFO)
            return
        labels = {
            "history": "提取记录",
            "exports": "导出中心",
            "diagnostics": "运行诊断",
        }
        self.overlay.set_mini_status(
            f"{labels.get(page_id, page_id)}功能开发中，当前任务仍在本地运行",
            STATUS_AI,
        )

    def _open_external_browser(self):
        """在系统默认浏览器打开当前页面。"""
        url = self.browser.url()
        if not url.isValid() or not url.toString():
            return
        if QDesktopServices.openUrl(url):
            self.overlay.set_mini_status("已在系统浏览器打开当前页面", STATUS_INFO)
        else:
            self.overlay.set_mini_status("无法打开系统浏览器", STATUS_ERROR)

    def refresh_export_button(self):
        pending_jobs = len(self.pending_ai_workers)
        self.overlay.set_run_metrics(
            saved=len(self.extracted_questions),
            ai_pending=pending_jobs,
        )
        can_export = (
            bool(self.extracted_questions)
            and not self.overlay.is_extracting
            and pending_jobs == 0
        )
        self.overlay.refresh_export_state(can_export)

    def _set_extraction_ui(self, active):
        self.overlay.set_extracting(active)
        self.title_bar.set_status(
            "提取进行中" if active else "本地运行 · 凭据受保护",
            STATUS_INFO if active else STATUS_MUTED,
        )
        self.refresh_export_button()

    def start_extraction(self):
        run_id = self.extraction_state.start()
        self._parse_retry_count = 0
        self._set_extraction_ui(True)
        self.overlay.set_mini_status("自动运行中…", STATUS_INFO)
        self.trigger_extraction(run_id)
        return run_id

    def stop_extraction(self, run_id=None, status_text=None, color=STATUS_INFO):
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
        # 只作废当前会话；正在执行的 worker 必须保留引用到 finished，避免 QThread 生命周期悬空。
        self.pending_ai_workers.clear()
        self.extracted_questions.clear()
        self.seen_question_keys.clear()
        self.last_question_marker = ""
        self.overlay.set_progress_text("当前进度: 已清空 0 题")
        self.overlay.set_run_metrics(current=0, total=0, saved=0, ai_pending=0, average="—")
        self.overlay.set_mini_status("题库缓存已清空", STATUS_INFO)
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
            parent=self,
        )
        key = (self.ai_session_id, question_index)
        self.pending_ai_workers[key] = worker
        self._live_ai_workers[key] = worker
        worker.completed.connect(self._on_ai_fill_completed)
        worker.failed.connect(self._on_ai_fill_failed)
        worker.finished.connect(lambda key=key: self._cleanup_ai_fill_worker(key))
        self.overlay.set_mini_status("AI 答题中…", STATUS_AI)
        self.overlay.set_progress_text(
            f"进度: {page_info} (已存 {len(self.extracted_questions)} 题，AI答题中)"
            if page_info else f"进度: 已存 {len(self.extracted_questions)} 题，AI答题中"
        )
        self.refresh_export_button()
        worker.start()

    def _cleanup_ai_fill_worker(self, key):
        self.pending_ai_workers.pop(key, None)
        worker = self._live_ai_workers.pop(key, None)
        if worker is not None:
            worker.deleteLater()
        self.refresh_export_button()
        if not self.overlay.is_extracting and not self.pending_ai_workers and self.extracted_questions:
            self.overlay.set_mini_status("AI 补全已全部完成", STATUS_SUCCESS)
        if self._closing and not any(worker.isRunning() for worker in self._live_ai_workers.values()):
            QTimer.singleShot(0, self.close)

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
            f"AI · {route_text}: {usage_text}",
            STATUS_SUCCESS if cache_hit else STATUS_WARNING,
        )

    def _on_ai_fill_failed(self, session_id, question_index, error_text):
        if session_id != self.ai_session_id:
            return
        self.overlay.set_mini_status(f"AI 答题失败: {error_text[:40]}", STATUS_ERROR)

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
        self.overlay.set_mini_status(f"已切换为{mode_text}", STATUS_INFO)

    def on_url_changed(self, url):
        self.overlay.btn_back.setEnabled(self.browser.page().history().canGoBack())
        self._schedule_browser_fit()
        
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
                self.overlay.set_mini_status("桥接脚本注入成功", STATUS_SUCCESS)

            # 静默注入密码
            self.trigger_auto_fill()
            # 登录表单可能由前端框架延迟挂载，短暂重试可保证字段最终写入。
            QTimer.singleShot(500, self.trigger_auto_fill)
            QTimer.singleShot(1500, self.trigger_auto_fill)
            self._schedule_browser_fit()
            # 云考首页的卡片由前端异步挂载，首次 loadFinished 时宽度可能还未稳定。
            QTimer.singleShot(900, self._schedule_browser_fit)
            QTimer.singleShot(2200, self._schedule_browser_fit)

    def _schedule_browser_fit(self):
        """页面路由或窗口尺寸变化后，延迟一次自适应网页缩放。"""
        if hasattr(self, "_browser_fit_timer") and hasattr(self, "browser"):
            self._browser_fit_timer.start()

    def _fit_browser_content(self):
        """按网页实际内容宽度缩放，避免首页卡片被右侧裁切。"""
        self.browser.page().runJavaScript(
            """
            (() => {
                const root = document.documentElement;
                const body = document.body;
                return {
                    viewport: Math.max(
                        window.innerWidth || 0,
                        root ? root.clientWidth : 0,
                    ),
                    content: Math.max(
                        root ? root.scrollWidth : 0,
                        body ? body.scrollWidth : 0,
                    ),
                };
            })();
            """,
            self._apply_browser_fit,
        )

    def _apply_browser_fit(self, metrics):
        """应用网页宽度比例，保留可读性并消除不必要的横向滚动。"""
        if not isinstance(metrics, dict):
            return
        try:
            viewport = float(metrics.get("viewport", 0) or 0)
            content = float(metrics.get("content", 0) or 0)
        except (TypeError, ValueError):
            return
        if viewport <= 0 or content <= 0:
            return

        target_zoom = 1.0 if content <= viewport * 1.02 else viewport / content
        target_zoom = round(max(0.6, min(1.0, target_zoom)), 2)
        if abs(self.browser.zoomFactor() - target_zoom) >= 0.01:
            self.browser.setZoomFactor(target_zoom)

    def trigger_auto_fill(self):
        host = self.browser.url().host().lower()
        if host not in {"www.cctrcloud.net", "cctrcloud.net"}:
            self.overlay.lbl_key_status.setStyleSheet(f"color: {STATUS_MUTED};")
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
            self.overlay.lbl_key_status.setStyleSheet(f"color: {STATUS_MUTED};")
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
                    self.overlay.lbl_key_status.setStyleSheet(f"color: {STATUS_SUCCESS};")
                    self.overlay.lbl_key_status.setToolTip("学校编码、学号和密码已自动填写")
                    self.overlay.set_mini_status("登录信息自动填写完毕", STATUS_SUCCESS)
                else:
                    self.overlay.set_mini_status("学校编码和学号已自动填写", STATUS_SUCCESS)
        
        self.browser.page().runJavaScript(js_code, 0, on_fill)

    def trigger_extraction(self, run_id=None):
        if run_id is None:
            run_id = self.extraction_state.active_run_id
        if not self.extraction_state.matches(run_id):
            return
        self.overlay.set_mini_status("正在提取题目…", STATUS_INFO)
        js_cmd = f"""
        if (window.pybridge) {{
            window.pybridge.receiveRawHtmlForRun(document.body.innerHTML, {run_id});
        }}
        """
        self.browser.page().runJavaScript(js_cmd)

    def export_basic_questions(self, export_format=None):
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
        
        format_options = {
            "PDF": ("PDF 文件 (*.pdf)", ".pdf"),
            "DOCX": ("Word 文档 (*.docx)", ".docx"),
        }
        direct_format = str(export_format or "").upper()
        direct_filter, direct_extension = format_options.get(direct_format, (None, ""))

        # 自动生成不重复的文件名，避免覆盖提示
        base_path = os.path.join(default_dir, default_prefix)
        counter = 1
        while any(os.path.exists(f"{base_path}{'(' + str(counter) + ')' if counter > 1 else ''}.{ext}") for ext in ['docx', 'pdf', 'md', 'txt']):
            counter += 1
            
        suffix = f"({counter})" if counter > 1 else ""
        default_path = f"{base_path}{suffix}{direct_extension}"

        available_filters = (
            direct_filter
            if direct_filter
            else "PDF 文件 (*.pdf);;Word 文档 (*.docx);;Markdown 文件 (*.md);;文本文件 (*.txt)"
        )
        file_path, selected_filter = QFileDialog.getSaveFileName(
            self, "导出基础题库", default_path, available_filters
        )
        if not file_path:
            return
        if direct_extension and not file_path.lower().endswith(direct_extension):
            file_path += direct_extension
            
        # 显示加载动画进度条
        self.progress_dialog = QProgressDialog("正在准备导出...", None, 0, max(len(self.extracted_questions), 1) + 2, self)
        self.progress_dialog.setWindowTitle("导出中")
        self.progress_dialog.setWindowModality(Qt.WindowModal)
        self.progress_dialog.setMinimumDuration(0)
        self.progress_dialog.setValue(0)
        self.progress_dialog.show()
        self.overlay.set_mini_status("正在准备导出…", STATUS_INFO)
        
        include_answers = not self.config.get("export_without_answers", False)
        self.export_thread = ExportThread(
            self.extracted_questions,
            file_path,
            selected_filter or direct_filter or "",
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
        self.overlay.set_mini_status(message, STATUS_INFO)

    def _on_export_finished(self, success, result):
        if hasattr(self, 'progress_dialog'):
            self.progress_dialog.close()
            
        if success:
            file_path = result
            self.overlay.set_mini_status("导出成功", STATUS_SUCCESS)
            QMessageBox.information(self, "导出成功", f"成功导出 {len(self.extracted_questions)} 道题目！\n{file_path}")
            if os.name == 'nt' and self.config.get("auto_open_after_export", True):
                os.startfile(file_path)
        else:
            QMessageBox.critical(self, "导出失败", f"导出过程中出错：{result}")
            self.overlay.set_mini_status("导出失败", STATUS_ERROR)

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
                debug_root = os.environ.get("LOCALAPPDATA") or os.path.join(
                    os.path.expanduser("~"), "AppData", "Local"
                )
                debug_dir = os.path.join(debug_root, "YunKao", "debug")
                os.makedirs(debug_dir, exist_ok=True)
                debug_path = os.path.join(debug_dir, "debug_dom.html")
                with open(debug_path, "w", encoding="utf-8") as f:
                    f.write(html_content)
            except Exception as e:
                print("Failed to save debug DOM:", e)

        if not html_content or html_content == "ERROR_NOT_FOUND":
            self.stop_extraction(
                run_id,
                "未找到题目内容，已自动停止",
                STATUS_ERROR,
            )
            return

        parsed_question = parse_active_question(html_content)
        if not parsed_question:
            # 页面可能仍在渲染，短暂等待后重试一次
            if getattr(self, '_parse_retry_count', 0) < 1:
                self._parse_retry_count = getattr(self, '_parse_retry_count', 0) + 1
                self.overlay.set_mini_status("题目内容未就绪，等待重试…", STATUS_WARNING)
                self._schedule_extraction(run_id, 800)
                return
            self._parse_retry_count = 0
            self.overlay.set_mini_status("题目解析失败，尝试跳到下一题…", STATUS_WARNING)
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
                            "无法翻页，已自动停止，请手动检查",
                            STATUS_ERROR,
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
                    f"解析成功: {parsed_question.get('title', '')[:10]}…",
                    STATUS_SUCCESS,
                )
            self.refresh_export_button()
        else:
            self.last_question_marker = question_marker or question_key
            self.overlay.set_mini_status("题目已存在，跳过", STATUS_MUTED)

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
                            f"页面加载卡顿，等待重试 ({current_page}/{total_page})…",
                            STATUS_WARNING,
                        )
                        self._schedule_extraction(run_id, 2000)
                    else:
                        # 到了最后一题
                        self.stop_extraction(
                            run_id,
                            "已到达最后一题，提取完毕",
                        )

            self.browser.page().runJavaScript(js_next, 0, on_next_result)

    def resizeEvent(self, event):
        super().resizeEvent(event)
        if hasattr(self, 'overlay'):
            self.overlay.raise_()
        self._schedule_browser_fit()

    def closeEvent(self, event):
        """等待 AI worker 完成后再销毁窗口，避免 QThread 生命周期警告。"""
        self._closing = True
        self.extraction_state.stop()
        running_workers = [
            worker for worker in self._live_ai_workers.values() if worker.isRunning()
        ]
        if running_workers:
            event.ignore()
            self._close_after_ai_workers = True
            self.hide()
            for worker in running_workers:
                worker.requestInterruption()
            QTimer.singleShot(100, self._poll_ai_workers_before_close)
            return
        event.accept()

    def _poll_ai_workers_before_close(self):
        if not self._close_after_ai_workers:
            return
        if any(worker.isRunning() for worker in self._live_ai_workers.values()):
            QTimer.singleShot(100, self._poll_ai_workers_before_close)
            return
        self._close_after_ai_workers = False
        self.close()
