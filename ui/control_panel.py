"""提取控制台：状态、进度、导出、AI 队列和运行动态。"""

import re

from PySide6.QtCore import Qt, QTimer
from PySide6.QtWidgets import (
    QFrame,
    QGridLayout,
    QHBoxLayout,
    QLabel,
    QListWidget,
    QListWidgetItem,
    QProgressBar,
    QPushButton,
    QScrollArea,
    QSizePolicy,
    QVBoxLayout,
    QWidget,
)

from config.version import APP_RELEASE
from ui.settings_dialog import SettingsDialog
from ui.theme import STATUS_INFO
from ui.widgets import ToggleSwitch


class ControlPanel(QFrame):
    """主窗口右侧控制台，并保留旧悬浮窗的兼容属性。"""

    EXPANDED_WIDTH = 338
    MIN_EXPANDED_HEIGHT = 248

    def __init__(self, parent=None, main_app=None):
        super().__init__(parent)
        self.main_app = main_app
        self.is_extracting = False
        self.is_minimized = False
        self._event_keys = set()
        self.setObjectName("controlPanel")
        self.setMinimumWidth(320)
        self.setMaximumWidth(self.EXPANDED_WIDTH)
        self.setSizePolicy(QSizePolicy.Preferred, QSizePolicy.Expanding)
        self._build_ui()

    def _build_ui(self):
        root = QVBoxLayout(self)
        root.setContentsMargins(14, 14, 14, 12)
        root.setSpacing(10)

        header = QHBoxLayout()
        header.setSpacing(8)
        self.lbl_title = QLabel(f"提取控制台 · {APP_RELEASE}")
        self.lbl_title.setObjectName("overlayTitle")
        self.lbl_key_status = QLabel("●")
        self.lbl_key_status.setObjectName("credentialStatus")
        self.lbl_key_status.setToolTip("本地凭据状态")
        self.btn_settings = self._make_button("设置", "btn_util")
        self.btn_min = self._make_button("⌃", "btn_min")
        self.btn_min.setToolTip("折叠控制台")
        header.addWidget(self.lbl_title)
        header.addStretch(1)
        header.addWidget(self.lbl_key_status)
        header.addWidget(self.btn_settings)
        header.addWidget(self.btn_min)
        root.addLayout(header)

        self.status_line = QFrame()
        self.status_line.setObjectName("statusLine")
        status_layout = QHBoxLayout(self.status_line)
        status_layout.setContentsMargins(10, 7, 10, 7)
        status_layout.setSpacing(7)
        self.status_dot = QLabel("●")
        self.status_dot.setObjectName("statusDot")
        self.status_label = QLabel("系统就绪")
        self.status_label.setObjectName("statusText")
        status_layout.addWidget(self.status_dot)
        status_layout.addWidget(self.status_label)
        status_layout.addStretch(1)
        root.addWidget(self.status_line)

        self.content_widget = QWidget()
        content_layout = QVBoxLayout(self.content_widget)
        content_layout.setContentsMargins(0, 0, 0, 0)
        content_layout.setSpacing(10)

        self.progress_card = QFrame()
        self.progress_card.setObjectName("progressCard")
        progress_layout = QVBoxLayout(self.progress_card)
        progress_layout.setContentsMargins(13, 12, 13, 12)
        progress_layout.setSpacing(9)

        progress_top = QHBoxLayout()
        self.lbl_current = QLabel("0")
        self.lbl_current.setObjectName("progressBig")
        self.lbl_total = QLabel("/ 0 题")
        self.lbl_total.setObjectName("progressTotal")
        self.lbl_percent = QLabel("0%")
        self.lbl_percent.setObjectName("progressTag")
        progress_top.addWidget(self.lbl_current)
        progress_top.addWidget(self.lbl_total)
        progress_top.addStretch(1)
        progress_top.addWidget(self.lbl_percent)
        progress_layout.addLayout(progress_top)

        self.progress_bar = QProgressBar()
        self.progress_bar.setObjectName("progressBar")
        self.progress_bar.setRange(0, 100)
        self.progress_bar.setValue(0)
        self.progress_bar.setTextVisible(False)
        self.progress_bar.setFixedHeight(7)
        progress_layout.addWidget(self.progress_bar)

        metrics = QGridLayout()
        metrics.setHorizontalSpacing(7)
        metrics.setVerticalSpacing(7)
        self.lbl_saved = self._metric(metrics, 0, "0", "已保存")
        self.lbl_ai = self._metric(metrics, 1, "0", "AI 待补")
        self.lbl_average = self._metric(metrics, 2, "—", "平均/题")
        progress_layout.addLayout(metrics)

        actions = QHBoxLayout()
        actions.setSpacing(7)
        self.btn_toggle = QPushButton("开始提取")
        self.btn_toggle.setObjectName("btn_primary")
        self.btn_toggle.setCursor(Qt.PointingHandCursor)
        self.btn_stop = QPushButton("停止")
        self.btn_stop.setObjectName("btn_stop")
        self.btn_clear = QPushButton("清空")
        self.btn_clear.setObjectName("btn_secondary")
        for button in (self.btn_stop, self.btn_clear):
            button.setCursor(Qt.PointingHandCursor)
        actions.addWidget(self.btn_toggle, 1)
        actions.addWidget(self.btn_stop)
        actions.addWidget(self.btn_clear)
        progress_layout.addLayout(actions)
        content_layout.addWidget(self.progress_card)

        self.lbl_progress = QLabel("当前题目：等待进入练习页面")
        self.lbl_progress.setObjectName("progressLabel")
        self.lbl_progress.setWordWrap(True)
        content_layout.addWidget(self.lbl_progress)

        practice_row = QFrame()
        practice_row.setObjectName("practiceRow")
        practice_layout = QHBoxLayout(practice_row)
        practice_layout.setContentsMargins(9, 6, 8, 6)
        practice_label = QLabel("练习版 · 隐藏答案与解析")
        practice_label.setObjectName("practiceLabel")
        self.chk_practice_export = ToggleSwitch()
        self.chk_practice_export.setToolTip("导出不含答案，适合打印练习")
        practice_layout.addWidget(practice_label)
        practice_layout.addStretch(1)
        practice_layout.addWidget(self.chk_practice_export)
        content_layout.addWidget(practice_row)

        export_title = QHBoxLayout()
        export_label = QLabel("快捷导出")
        export_label.setObjectName("sectionTitle")
        export_hint = QLabel("常用格式 · 一键完成")
        export_hint.setObjectName("sectionHint")
        export_title.addWidget(export_label)
        export_title.addStretch(1)
        export_title.addWidget(export_hint)
        content_layout.addLayout(export_title)

        export_row = QHBoxLayout()
        export_row.setSpacing(7)
        self.btn_export_pdf = self._export_button("PDF", "标准排版")
        self.btn_export = self._export_button("DOCX", "可继续编辑")
        self.btn_export.setObjectName("btn_export")
        self.btn_export_more = self._export_button("更多", "MD / TXT")
        export_row.addWidget(self.btn_export_pdf)
        export_row.addWidget(self.btn_export)
        export_row.addWidget(self.btn_export_more)
        content_layout.addLayout(export_row)

        self.ai_card = QFrame()
        self.ai_card.setObjectName("aiCard")
        ai_layout = QHBoxLayout(self.ai_card)
        ai_layout.setContentsMargins(11, 9, 11, 9)
        ai_copy = QVBoxLayout()
        ai_copy.setSpacing(2)
        ai_title = QLabel("AI 补全 · 智能队列")
        ai_title.setObjectName("aiTitle")
        self.lbl_ai_status = QLabel("队列 0 · 低置信度自动标记复核")
        self.lbl_ai_status.setObjectName("aiSubtitle")
        ai_copy.addWidget(ai_title)
        ai_copy.addWidget(self.lbl_ai_status)
        self.lbl_ai_tag = QLabel("待机")
        self.lbl_ai_tag.setObjectName("aiTag")
        ai_layout.addLayout(ai_copy, 1)
        ai_layout.addWidget(self.lbl_ai_tag)
        content_layout.addWidget(self.ai_card)

        event_title = QHBoxLayout()
        event_label = QLabel("运行动态")
        event_label.setObjectName("sectionTitle")
        event_hint = QLabel("关键事件自动归档")
        event_hint.setObjectName("sectionHint")
        event_title.addWidget(event_label)
        event_title.addStretch(1)
        event_title.addWidget(event_hint)
        content_layout.addLayout(event_title)

        self.events = QListWidget()
        self.events.setObjectName("eventList")
        self.events.setSpacing(2)
        self.events.setMinimumHeight(92)
        self.events.setMaximumHeight(138)
        self.events.setHorizontalScrollBarPolicy(Qt.ScrollBarAlwaysOff)
        self.events.setVerticalScrollBarPolicy(Qt.ScrollBarAsNeeded)
        content_layout.addWidget(self.events)

        self.lbl_status_mini = QLabel("系统就绪")
        self.lbl_status_mini.setObjectName("statusLabel")
        self.lbl_status_mini.setWordWrap(True)
        content_layout.addWidget(self.lbl_status_mini)

        scroll = QScrollArea()
        scroll.setObjectName("panelScroll")
        scroll.setWidgetResizable(True)
        scroll.setFrameShape(QFrame.NoFrame)
        scroll.setHorizontalScrollBarPolicy(Qt.ScrollBarAlwaysOff)
        scroll.setWidget(self.content_widget)
        root.addWidget(scroll, 1)

        footer = QHBoxLayout()
        footer.setContentsMargins(2, 0, 2, 0)
        footer_label = QLabel("本地任务自动保存")
        footer_label.setObjectName("panelFooter")
        footer.addWidget(footer_label)
        footer.addStretch(1)
        footer_settings = QPushButton("设置")
        footer_settings.setObjectName("linkButton")
        footer_settings.setCursor(Qt.PointingHandCursor)
        footer.addWidget(footer_settings)
        root.addLayout(footer)

        self.btn_settings.clicked.connect(self.open_settings)
        footer_settings.clicked.connect(self.open_settings)
        self.btn_toggle.clicked.connect(self.toggle_extraction)
        self.btn_stop.clicked.connect(self.stop_extraction)
        self.btn_clear.clicked.connect(self.clear_questions)
        self.btn_export_pdf.clicked.connect(lambda: self._export("PDF"))
        self.btn_export.clicked.connect(lambda: self._export("DOCX"))
        self.btn_export_more.clicked.connect(lambda: self._export(None))
        self.btn_min.clicked.connect(self.toggle_minimize)

        self._append_event("系统就绪，等待进入练习页面")
        QTimer.singleShot(0, self._refresh_expanded_size)

    @staticmethod
    def _make_button(text, object_name):
        button = QPushButton(text)
        button.setObjectName(object_name)
        button.setCursor(Qt.PointingHandCursor)
        return button

    @staticmethod
    def _metric(layout, column, value, caption):
        frame = QFrame()
        frame.setObjectName(f"metric{column}")
        frame_layout = QVBoxLayout(frame)
        frame_layout.setContentsMargins(6, 7, 6, 6)
        frame_layout.setSpacing(2)
        value_label = QLabel(value)
        value_label.setObjectName("metricValue")
        value_label.setAlignment(Qt.AlignCenter)
        caption_label = QLabel(caption)
        caption_label.setObjectName("metricCaption")
        caption_label.setAlignment(Qt.AlignCenter)
        frame_layout.addWidget(value_label)
        frame_layout.addWidget(caption_label)
        layout.addWidget(frame, 0, column)
        return value_label

    @staticmethod
    def _export_button(title, subtitle):
        button = QPushButton(f"{title}\n{subtitle}")
        button.setObjectName("exportButton")
        button.setCursor(Qt.PointingHandCursor)
        button.setMinimumHeight(48)
        return button

    def _export(self, export_format=None):
        if self.main_app is not None:
            self.main_app.export_basic_questions(export_format)

    def toggle_extraction(self):
        if self.main_app is None:
            return
        if not self.main_app.extraction_state.is_active:
            self.main_app.start_extraction()
        else:
            self.main_app.stop_extraction(status_text="提取已暂停")

    def stop_extraction(self):
        if self.main_app is not None:
            self.main_app.stop_extraction(status_text="提取已结束")

    def clear_questions(self):
        if self.main_app is not None:
            self.main_app.clear_extracted_questions()

    def open_settings(self):
        if self.main_app is None:
            return
        dialog = SettingsDialog(self.main_app)
        dialog.config_updated.connect(self.main_app.update_config)
        dialog.exec()

    def set_extracting(self, active):
        self.is_extracting = bool(active)
        self.btn_toggle.setProperty("extracting", self.is_extracting)
        self.btn_toggle.style().unpolish(self.btn_toggle)
        self.btn_toggle.style().polish(self.btn_toggle)
        self.btn_toggle.setText("停止提取" if self.is_extracting else "开始提取")
        self.status_label.setText("提取进行中" if self.is_extracting else self.status_label.text())

    def set_status(self, text, color=STATUS_INFO):
        self.status_label.setText(text)
        self.status_label.setStyleSheet(f"color: {color};")
        self.status_dot.setStyleSheet(f"color: {color};")

    def set_mini_status(self, text, color=STATUS_INFO):
        clean_text = str(text or "系统就绪")
        self.lbl_status_mini.setText(clean_text)
        self.lbl_status_mini.setStyleSheet(f"color: {color};")
        self.set_status(clean_text, color)
        self._append_event(clean_text)
        QTimer.singleShot(0, self._refresh_expanded_size)

    def set_progress_text(self, text):
        progress_text = str(text or "当前题目：等待进入练习页面")
        self.lbl_progress.setText(progress_text)
        self._parse_progress(progress_text)
        QTimer.singleShot(0, self._refresh_expanded_size)

    def set_run_metrics(self, current=None, total=None, saved=None, ai_pending=None, average=None):
        if current is not None:
            self.lbl_current.setText(str(current))
        if total is not None:
            self.lbl_total.setText(f"/ {total} 题")
        if saved is not None:
            self.lbl_saved.setText(str(saved))
        if ai_pending is not None:
            self.lbl_ai.setText(str(ai_pending))
            self.lbl_ai_status.setText(f"队列 {ai_pending} · 低置信度自动标记复核")
            self.lbl_ai_tag.setText("运行中" if ai_pending else "已完成")
        if average is not None:
            self.lbl_average.setText(f"{average:.1f}s" if isinstance(average, (int, float)) else str(average))
        try:
            current_value = int(self.lbl_current.text())
            total_value = int(str(self.lbl_total.text()).split()[1])
            percent = int(round(current_value / total_value * 100)) if total_value else 0
        except (IndexError, TypeError, ValueError, ZeroDivisionError):
            percent = 0
        self.progress_bar.setValue(max(0, min(100, percent)))
        self.lbl_percent.setText(f"{percent}%")

    def refresh_export_state(self, enabled):
        for button in (self.btn_export_pdf, self.btn_export, self.btn_export_more):
            button.setEnabled(bool(enabled))

    def _parse_progress(self, text):
        page_match = re.search(r"(\d+)\s*/\s*(\d+)", text)
        saved_match = re.search(r"已存\s*(\d+)", text)
        if page_match:
            current, total = page_match.groups()
            self.set_run_metrics(current=current, total=total)
        if saved_match:
            self.set_run_metrics(saved=saved_match.group(1))

    def _append_event(self, text):
        event_text = str(text).strip()
        if not event_text or event_text in self._event_keys:
            return
        self._event_keys.add(event_text)
        item = QListWidgetItem(event_text)
        self.events.insertItem(0, item)
        while self.events.count() > 8:
            removed = self.events.takeItem(self.events.count() - 1)
            if removed is not None:
                self._event_keys.discard(removed.text())

    def _refresh_expanded_size(self):
        if self.is_minimized:
            return
        self.setMinimumHeight(max(self.MIN_EXPANDED_HEIGHT, self.sizeHint().height()))

    def toggle_minimize(self):
        self.is_minimized = not self.is_minimized
        self.content_widget.setVisible(not self.is_minimized)
        self.btn_min.setText("⌄" if self.is_minimized else "⌃")
        self.btn_min.setToolTip("展开控制台" if self.is_minimized else "折叠控制台")
        if self.is_minimized:
            self.setMaximumHeight(76)
        else:
            self.setMaximumHeight(16777215)
            self._refresh_expanded_size()
