from PySide6.QtWidgets import (QDialog, QVBoxLayout, QHBoxLayout, QLabel,
                                QLineEdit, QPushButton, QCheckBox, QFileDialog, QMessageBox,
                                QGroupBox, QFrame, QScrollArea, QWidget, QSpacerItem, QSizePolicy,
                                QToolButton, QStackedWidget, QButtonGroup)
from PySide6.QtCore import Signal, Qt, QUrl
from PySide6.QtNetwork import QNetworkAccessManager, QNetworkReply, QNetworkRequest
import json
import os
import keyring
from config.settings import (
    AI_KEYRING_SERVICE,
    load_config,
    save_config,
    SERVICE_NAME,
    HARDCODED_SCHOOL_CODE,
)
from config.version import APP_RELEASE
from ui.widgets import NoWheelComboBox, ToggleSwitch
from ui.theme import SETTINGS_DIALOG_STYLE


def extract_model_ids(payload):
    """从常见 OpenAI 兼容响应中提取模型标识。"""
    candidates = payload
    for _ in range(3):
        if isinstance(candidates, list):
            break
        if not isinstance(candidates, dict):
            return []
        next_candidates = None
        for key in ("data", "models", "items", "result"):
            value = candidates.get(key)
            if isinstance(value, (list, dict)):
                next_candidates = value
                break
        if next_candidates is None:
            return []
        candidates = next_candidates

    if not isinstance(candidates, list):
        return []

    model_ids = []
    seen = set()
    for item in candidates:
        if isinstance(item, str):
            model_id = item.strip()
        elif isinstance(item, dict):
            model_id = str(
                item.get("id")
                or item.get("model")
                or item.get("model_name")
                or item.get("name")
                or ""
            ).strip()
        else:
            continue
        if model_id and model_id not in seen:
            seen.add(model_id)
            model_ids.append(model_id)
    return model_ids


class SettingsDialog(QDialog):
    config_updated = Signal(dict)

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setWindowTitle(f"融智云考助手 · 设置 · {APP_RELEASE}")
        self.setMinimumSize(760, 560)
        self.resize(860, 620)
        self.setStyleSheet(SETTINGS_DIALOG_STYLE)

        self.config = load_config()
        configured_user = self.config.get("yunkao_user") or self.config.get("user")
        self.original_yunkao_user = str(configured_user or "").strip()
        self.network_manager = QNetworkAccessManager(self)
        self.model_reply = None
        self.init_ui()

    def init_ui(self):
        root_layout = QHBoxLayout(self)
        root_layout.setContentsMargins(0, 0, 0, 0)
        root_layout.setSpacing(0)

        sidebar = QFrame()
        sidebar.setObjectName("sidebar")
        sidebar.setFixedWidth(168)
        sidebar_layout = QVBoxLayout(sidebar)
        sidebar_layout.setContentsMargins(0, 28, 0, 20)
        sidebar_layout.setSpacing(4)

        brand_title = QLabel("融智云考助手")
        brand_title.setObjectName("brandTitle")
        self.lbl_brand_subtitle = QLabel(f"本地设置 · {APP_RELEASE}")
        self.lbl_brand_subtitle.setObjectName("brandSubtitle")
        sidebar_layout.addWidget(brand_title, 0, Qt.AlignHCenter)
        sidebar_layout.addWidget(self.lbl_brand_subtitle, 0, Qt.AlignHCenter)
        sidebar_layout.addSpacing(24)

        self.nav_group = QButtonGroup(self)
        self.nav_group.setExclusive(True)
        self.nav_buttons = []
        for page_index, text in enumerate(("账号", "导出", "AI")):
            button = QPushButton(text)
            button.setObjectName("navButton")
            button.setCheckable(True)
            button.clicked.connect(
                lambda _checked=False, index=page_index: self.settings_pages.setCurrentIndex(index)
            )
            self.nav_group.addButton(button, page_index)
            self.nav_buttons.append(button)
            sidebar_layout.addWidget(button)
        sidebar_layout.addStretch(1)

        content = QFrame()
        content.setObjectName("contentPanel")
        content_layout = QVBoxLayout(content)
        content_layout.setContentsMargins(36, 28, 36, 24)
        content_layout.setSpacing(0)

        self.settings_pages = QStackedWidget()
        self.settings_pages.addWidget(self._build_account_page())
        self.settings_pages.addWidget(self._build_export_page())
        self.settings_pages.addWidget(self._build_ai_page())
        content_layout.addWidget(self.settings_pages, 1)

        divider = QFrame()
        divider.setObjectName("contentDivider")
        content_layout.addWidget(divider)
        content_layout.addSpacing(14)

        actions = QHBoxLayout()
        actions.setSpacing(10)
        actions.addStretch(1)
        btn_cancel = QPushButton("取消")
        btn_cancel.setObjectName("ghostButton")
        btn_cancel.clicked.connect(self.reject)
        btn_save = QPushButton("保存设置")
        btn_save.setObjectName("primaryButton")
        btn_save.clicked.connect(self.save_settings)
        actions.addWidget(btn_cancel)
        actions.addWidget(btn_save)
        content_layout.addLayout(actions)

        root_layout.addWidget(sidebar)
        root_layout.addWidget(content, 1)

        # 默认打开导出页，练习版开关无需额外查找。
        self.nav_buttons[1].setChecked(True)
        self.settings_pages.setCurrentIndex(1)

    def _create_page(self, title_text, subtitle_text):
        page = QWidget()
        layout = QVBoxLayout(page)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(0)

        title = QLabel(title_text)
        title.setObjectName("pageTitle")
        subtitle = QLabel(subtitle_text)
        subtitle.setObjectName("pageSubtitle")
        layout.addWidget(title)
        layout.addSpacing(5)
        layout.addWidget(subtitle)
        layout.addSpacing(20)
        return page, layout

    def _add_form_row(self, layout, label_text, field, action=None):
        row = QFrame()
        row.setObjectName("formRow")
        row.setMinimumHeight(62)
        row_layout = QHBoxLayout(row)
        row_layout.setContentsMargins(0, 10, 0, 10)
        row_layout.setSpacing(10)

        label = QLabel(label_text)
        label.setObjectName("fieldLabel")
        label.setFixedWidth(170)
        row_layout.addWidget(label)
        row_layout.addWidget(field, 1)
        if action is not None:
            row_layout.addWidget(action)
        layout.addWidget(row)
        return row

    def _add_toggle_row(self, layout, label_text, toggle):
        row = QFrame()
        row.setObjectName("formRow")
        row.setMinimumHeight(58)
        row_layout = QHBoxLayout(row)
        row_layout.setContentsMargins(0, 8, 0, 8)
        label = QLabel(label_text)
        label.setObjectName("fieldLabel")
        row_layout.addWidget(label)
        row_layout.addStretch(1)
        row_layout.addWidget(toggle)
        layout.addWidget(row)
        return row

    def _build_account_page(self):
        page, layout = self._create_page(
            "账号设置",
            "登录信息仅保存在本机，用于自动填充云考网页。",
        )

        self.txt_yunkao_user = QLineEdit()
        self.txt_yunkao_user.setText(self.original_yunkao_user)
        self.txt_yunkao_user.setPlaceholderText("请输入学号或账号")
        self._add_form_row(layout, "云考账号", self.txt_yunkao_user)

        self.txt_yunkao_pwd = QLineEdit()
        self.txt_yunkao_pwd.setEchoMode(QLineEdit.Password)
        self.txt_yunkao_pwd.setPlaceholderText("请输入云考密码")
        self.btn_toggle_yunkao_pwd = QToolButton()
        self.btn_toggle_yunkao_pwd.setObjectName("secretButton")
        self.btn_toggle_yunkao_pwd.setText("显示")
        self.btn_toggle_yunkao_pwd.setCheckable(True)
        self.btn_toggle_yunkao_pwd.toggled.connect(
            lambda checked: self._toggle_secret_visibility(
                self.txt_yunkao_pwd,
                self.btn_toggle_yunkao_pwd,
                checked,
            )
        )
        self._add_form_row(
            layout,
            "云考密码",
            self.txt_yunkao_pwd,
            self.btn_toggle_yunkao_pwd,
        )

        if self.original_yunkao_user:
            try:
                saved_pwd = keyring.get_password(
                    SERVICE_NAME,
                    f"{HARDCODED_SCHOOL_CODE}_{self.original_yunkao_user}",
                )
                if saved_pwd:
                    self.txt_yunkao_pwd.setText(saved_pwd)
            except Exception:
                pass

        self.chk_remember_pwd = ToggleSwitch()
        self.chk_remember_pwd.setChecked(
            self.config.get("yunkao_remember_password", True)
        )
        self._add_toggle_row(
            layout,
            "在本地记住密码并自动填充",
            self.chk_remember_pwd,
        )
        layout.addStretch(1)
        return page

    def _build_export_page(self):
        page, layout = self._create_page(
            "导出设置",
            "配置导出目录、文件选项与 PDF 生成规则。",
        )

        self.txt_dir = QLineEdit()
        self.txt_dir.setReadOnly(True)
        self.txt_dir.setText(
            self.config.get("default_export_dir", os.path.expanduser("~"))
        )
        btn_browse = QPushButton("浏览…")
        btn_browse.clicked.connect(self.browse_directory)
        self._add_form_row(layout, "默认导出目录", self.txt_dir, btn_browse)

        self.txt_prefix = QLineEdit()
        self.txt_prefix.setText(
            self.config.get("default_filename_prefix", "融智云考题库")
        )
        self.txt_prefix.setPlaceholderText("例如：融智云考题库")
        self._add_form_row(layout, "默认文件名前缀", self.txt_prefix)

        self.cmb_engine = NoWheelComboBox()
        self.cmb_engine.addItem("极速内核 (Chromium) - 推荐", "chromium")
        self.cmb_engine.addItem("经典内核 (依赖本地 WPS/Office)", "wps")
        engine_index = self.cmb_engine.findData(
            self.config.get("pdf_export_engine", "chromium")
        )
        if engine_index >= 0:
            self.cmb_engine.setCurrentIndex(engine_index)
        self._add_form_row(layout, "PDF 引擎", self.cmb_engine)

        self.chk_auto_open = ToggleSwitch()
        self.chk_auto_open.setChecked(
            self.config.get("auto_open_after_export", True)
        )
        self._add_toggle_row(layout, "导出后自动打开文件", self.chk_auto_open)

        self.chk_export_without_answers = ToggleSwitch()
        self.chk_export_without_answers.setChecked(
            self.config.get("export_without_answers", False)
        )
        self.chk_export_without_answers.setToolTip(
            "选择、判断和填空题保留一行；主观题保留三行"
        )
        self._add_toggle_row(
            layout,
            "练习版（不打印答案与解析）",
            self.chk_export_without_answers,
        )

        self.lbl_practice_hint = QLabel(
            "开启练习版后会隐藏答案和解析，并自动加入适量手写留白。"
        )
        self.lbl_practice_hint.setObjectName("infoText")
        self.lbl_practice_hint.setWordWrap(True)
        layout.addWidget(self.lbl_practice_hint)
        layout.addStretch(1)
        return page

    def _build_ai_page(self):
        page, layout = self._create_page(
            "AI 补全",
            "为缺失答案的题目配置兼容 OpenAI 协议的模型服务。",
        )

        self.chk_ai_fill = ToggleSwitch()
        self.chk_ai_fill.setChecked(
            self.config.get("ai_auto_fill_missing_answers", False)
        )
        self._add_toggle_row(
            layout,
            "导出时自动补全缺失答案",
            self.chk_ai_fill,
        )

        self.lbl_ai_warning = QLabel("AI 生成内容可能不准确，请注意甄别。")
        self.lbl_ai_warning.setObjectName("warningText")
        layout.addWidget(self.lbl_ai_warning)
        layout.addSpacing(4)

        self.cmb_provider = NoWheelComboBox()
        for label, provider in (
            ("OpenAI / GPT", "openai"),
            ("DeepSeek", "deepseek"),
            ("Kimi / Moonshot", "kimi"),
            ("千问 / Qwen", "qwen"),
            ("智谱 / GLM", "glm"),
            ("小米 MiMo", "mimo"),
            ("自定义兼容接口", "custom"),
        ):
            self.cmb_provider.addItem(label, provider)
        provider_index = self.cmb_provider.findData(
            self.config.get("ai_provider", "openai")
        )
        if provider_index >= 0:
            self.cmb_provider.setCurrentIndex(provider_index)
        self.cmb_provider.currentIndexChanged.connect(self._apply_provider_preset)
        self._add_form_row(layout, "接口类型", self.cmb_provider)

        self.txt_ai_url = QLineEdit()
        self.txt_ai_url.setText(
            self.config.get("ai_base_url", "https://api.openai.com/v1")
        )
        self.txt_ai_url.setPlaceholderText("例如：https://api.openai.com/v1")
        self._add_form_row(layout, "API 地址", self.txt_ai_url)

        self.txt_ai_key = QLineEdit()
        self.txt_ai_key.setEchoMode(QLineEdit.Password)
        self.txt_ai_key.setText(self.config.get("ai_api_key", ""))
        self.txt_ai_key.setPlaceholderText("留空则不会触发 AI 补全")
        self.btn_toggle_ai_key = QToolButton()
        self.btn_toggle_ai_key.setObjectName("secretButton")
        self.btn_toggle_ai_key.setText("显示")
        self.btn_toggle_ai_key.setCheckable(True)
        self.btn_toggle_ai_key.toggled.connect(
            lambda checked: self._toggle_secret_visibility(
                self.txt_ai_key,
                self.btn_toggle_ai_key,
                checked,
            )
        )
        self._add_form_row(
            layout,
            "API Key",
            self.txt_ai_key,
            self.btn_toggle_ai_key,
        )

        self.cmb_ai_model = NoWheelComboBox()
        self.cmb_ai_model.setEditable(True)
        self.cmb_ai_model.setCurrentText(
            self.config.get("ai_model", "gpt-4o-mini")
        )
        self.txt_ai_model = self.cmb_ai_model.lineEdit()
        self.txt_ai_model.setPlaceholderText("例如：gpt-4o-mini")
        self.btn_fetch_models = QToolButton()
        self.btn_fetch_models.setText("查询模型")
        self.btn_fetch_models.setToolTip("从当前 API 地址查询可用模型")
        self.btn_fetch_models.clicked.connect(self.fetch_models)
        self._add_form_row(
            layout,
            "模型名称",
            self.cmb_ai_model,
            self.btn_fetch_models,
        )

        self.lbl_model_status = QLabel("")
        self.lbl_model_status.setObjectName("statusText")
        layout.addWidget(self.lbl_model_status)

        self.chk_custom_images = ToggleSwitch()
        self.chk_custom_images.setChecked(
            self.config.get("ai_supports_images", True)
        )
        self._add_toggle_row(
            layout,
            "支持图片题与图片选项识别",
            self.chk_custom_images,
        )

        layout.addStretch(1)
        return page

    @staticmethod
    def _toggle_secret_visibility(line_edit, button, visible):
        """切换敏感字段的显示状态，并同步按钮文案。"""
        line_edit.setEchoMode(QLineEdit.Normal if visible else QLineEdit.Password)
        button.setText("隐藏" if visible else "显示")

    def browse_directory(self):
        dir_path = QFileDialog.getExistingDirectory(self, "选择默认导出目录", self.txt_dir.text())
        if dir_path:
            self.txt_dir.setText(dir_path)

    def _apply_provider_preset(self):
        from modules.ai_answer import PROVIDER_PRESETS
        provider_key = self.cmb_provider.currentData()
        preset = PROVIDER_PRESETS.get(provider_key, PROVIDER_PRESETS["custom"])
        if preset.get("base_url"):
            self.txt_ai_url.setText(preset["base_url"])
        if preset.get("model"):
            self.txt_ai_model.setText(preset["model"])
        self.chk_custom_images.setChecked(bool(preset.get("supports_images", False)))

    def fetch_models(self, *_args):
        base_url = self.txt_ai_url.text().strip().rstrip("/")
        api_key = self.txt_ai_key.text().strip()
        if not base_url or not api_key:
            self.lbl_model_status.setText("API 配置不完整")
            self.lbl_model_status.setStyleSheet("color: #B8860B; font-size: 11px;")
            return

        parsed_url = QUrl(base_url)
        if not parsed_url.isValid() or parsed_url.scheme().lower() not in {"http", "https"}:
            self.lbl_model_status.setText("API 地址无效")
            self.lbl_model_status.setStyleSheet("color: #B00020; font-size: 11px;")
            return

        if self.model_reply is not None and self.model_reply.isRunning():
            self.model_reply.abort()

        from modules.ai_answer import PROVIDER_PRESETS

        provider = self.cmb_provider.currentData()
        preset = PROVIDER_PRESETS.get(provider, PROVIDER_PRESETS["custom"])
        auth_header = preset.get("auth_header", "Authorization").encode("utf-8")
        auth_value = f"{preset.get('auth_prefix', 'Bearer ')}{api_key}".encode("utf-8")
        models_url = base_url if base_url.lower().endswith("/models") else f"{base_url}/models"

        request = QNetworkRequest(QUrl(models_url))
        request.setRawHeader(auth_header, auth_value)
        request.setRawHeader(b"Accept", b"application/json")
        request.setTransferTimeout(15000)

        self.btn_fetch_models.setEnabled(False)
        self.lbl_model_status.setText("正在读取模型...")
        self.lbl_model_status.setStyleSheet("color: #666666; font-size: 11px;")
        reply = self.network_manager.get(request)
        self.model_reply = reply
        reply.finished.connect(lambda reply=reply: self._finish_model_fetch(reply))

    def _finish_model_fetch(self, reply):
        if reply is not self.model_reply:
            reply.deleteLater()
            return

        self.model_reply = None
        self.btn_fetch_models.setEnabled(True)
        try:
            if reply.error() != QNetworkReply.NoError:
                raise RuntimeError(reply.errorString())
            payload = json.loads(bytes(reply.readAll()).decode("utf-8"))
            model_ids = extract_model_ids(payload)
            if not model_ids:
                raise ValueError("接口未返回可识别的模型列表")
            self._populate_models(model_ids)
            self.lbl_model_status.setText(f"已读取 {len(model_ids)} 个模型")
            self.lbl_model_status.setStyleSheet("color: #107C10; font-size: 11px;")
        except (RuntimeError, ValueError, UnicodeDecodeError, json.JSONDecodeError) as exc:
            self.lbl_model_status.setText(f"读取失败：{exc}")
            self.lbl_model_status.setStyleSheet("color: #B00020; font-size: 11px;")
        finally:
            reply.deleteLater()

    def _populate_models(self, model_ids):
        current_model = self.cmb_ai_model.currentText().strip()
        self.cmb_ai_model.blockSignals(True)
        self.cmb_ai_model.clear()
        self.cmb_ai_model.addItems(model_ids)
        if current_model:
            self.cmb_ai_model.setCurrentText(current_model)
        elif model_ids:
            self.cmb_ai_model.setCurrentIndex(0)
        self.cmb_ai_model.blockSignals(False)

    def save_settings(self):
        new_dir = self.txt_dir.text().strip()
        new_prefix = self.txt_prefix.text().strip()

        if not new_dir or not os.path.isdir(new_dir):
            QMessageBox.warning(self, "错误", "请选择一个有效的导出目录！")
            return
        if not new_prefix:
            QMessageBox.warning(self, "错误", "文件名前缀不能为空！")
            return
            
        yunkao_user = self.txt_yunkao_user.text().strip()
        yunkao_pwd = self.txt_yunkao_pwd.text().strip()
        remember_pwd = self.chk_remember_pwd.isChecked()

        if remember_pwd and yunkao_user and not yunkao_pwd:
            QMessageBox.warning(self, "错误", "勾选记住密码时，云考密码不能为空！")
            return

        self.config["yunkao_user"] = yunkao_user
        self.config["yunkao_remember_password"] = remember_pwd

        # 更换学号后清理旧凭据，避免本机凭据库长期残留无效密码。
        if self.original_yunkao_user and self.original_yunkao_user != yunkao_user:
            try:
                keyring.delete_password(
                    SERVICE_NAME,
                    f"{HARDCODED_SCHOOL_CODE}_{self.original_yunkao_user}",
                )
            except Exception:
                pass

        if yunkao_user and remember_pwd:
            try:
                keyring.set_password(
                    SERVICE_NAME,
                    f"{HARDCODED_SCHOOL_CODE}_{yunkao_user}",
                    yunkao_pwd,
                )
            except Exception as e:
                QMessageBox.warning(self, "警告", f"保存密码失败: {e}")
        elif yunkao_user and not remember_pwd:
            try:
                keyring.delete_password(SERVICE_NAME, f"{HARDCODED_SCHOOL_CODE}_{yunkao_user}")
            except Exception:
                pass

        self.config["default_export_dir"] = new_dir
        self.config["default_filename_prefix"] = new_prefix
        self.config["auto_open_after_export"] = self.chk_auto_open.isChecked()
        self.config["pdf_export_engine"] = self.cmb_engine.currentData()
        self.config["export_without_answers"] = (
            self.chk_export_without_answers.isChecked()
        )

        self.config["ai_auto_fill_missing_answers"] = self.chk_ai_fill.isChecked()
        self.config["ai_mode"] = "custom"  # 强制走 custom
        self.config["ai_provider"] = self.cmb_provider.currentData()
        self.config["ai_base_url"] = self.txt_ai_url.text().strip() or "https://api.openai.com/v1"
        self.config["ai_model"] = self.txt_ai_model.text().strip() or "gpt-4o-mini"
        provider = self.cmb_provider.currentData() or "custom"
        api_key = self.txt_ai_key.text().strip()
        try:
            if api_key:
                keyring.set_password(AI_KEYRING_SERVICE, str(provider), api_key)
            else:
                try:
                    keyring.delete_password(AI_KEYRING_SERVICE, str(provider))
                except Exception:
                    pass
        except Exception as exc:
            QMessageBox.warning(self, "凭据保存失败", f"AI Key 无法保存到系统凭据库：{exc}")
            return
        self.config["ai_api_key"] = api_key
        self.config["ai_key_saved"] = bool(api_key)
        self.config["ai_supports_images"] = self.chk_custom_images.isChecked()

        persisted_config = dict(self.config)
        persisted_config.pop("ai_api_key", None)
        save_config(persisted_config)
        self.config_updated.emit(self.config)
        self.accept()
