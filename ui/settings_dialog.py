from PySide6.QtWidgets import (QDialog, QVBoxLayout, QHBoxLayout, QLabel,
                                QLineEdit, QPushButton, QCheckBox, QFileDialog, QMessageBox,
                                QGroupBox, QFrame, QScrollArea, QWidget, QSpacerItem, QSizePolicy)
from PySide6.QtCore import Signal, Qt, QUrl, QTimer
from PySide6.QtNetwork import QNetworkAccessManager, QNetworkReply, QNetworkRequest
import json
import os
import webbrowser
from urllib.parse import quote
from config.settings import load_config, save_config, API_BASE_URL
from ui.widgets import NoWheelComboBox


class SettingsDialog(QDialog):
    config_updated = Signal(dict)
    logout_requested = Signal()
    open_admin_panel = Signal()
    wallet_balance_updated = Signal(int)

    def __init__(self, parent=None, jwt_token=""):
        super().__init__(parent)
        self.setWindowTitle("系统设置 - 融智云考助手")
        self.setMinimumSize(520, 600)

        self.config = load_config()
        self.jwt_token = jwt_token

        # 服务端模型列表
        self.server_models = []
        # 钱包余额
        self.wallet_balance_cents = 0
        self._server_data_loading = False
        self._server_data_loaded = False
        self._recharge_balance_before = None
        self._recharge_poll_count = 0
        self.network_manager = QNetworkAccessManager(self)
        self.recharge_poll_timer = QTimer(self)
        self.recharge_poll_timer.setInterval(3000)
        self.recharge_poll_timer.timeout.connect(self._poll_recharge_balance)

        self.init_ui()
        self._load_server_data()

    def init_ui(self):
        main_layout = QVBoxLayout(self)
        main_layout.setSpacing(12)
        main_layout.setContentsMargins(20, 16, 20, 16)

        # 滚动区域包裹
        scroll = QScrollArea()
        scroll.setWidgetResizable(True)
        scroll.setFrameShape(QFrame.NoFrame)
        scroll_widget = QWidget()
        layout = QVBoxLayout(scroll_widget)
        layout.setSpacing(12)

        # ---- 分区 A：导出设置 ----
        export_group = QGroupBox("📁 导出设置")
        export_group.setStyleSheet("QGroupBox { font-weight: bold; padding-top: 16px; }")
        export_layout = QVBoxLayout(export_group)
        export_layout.setSpacing(8)

        # 默认导出目录
        dir_layout = QHBoxLayout()
        lbl_dir = QLabel("默认导出目录:")
        lbl_dir.setFixedWidth(110)
        self.txt_dir = QLineEdit()
        self.txt_dir.setReadOnly(True)
        self.txt_dir.setText(self.config.get("default_export_dir", os.path.expanduser("~")))
        btn_browse = QPushButton("浏览...")
        btn_browse.clicked.connect(self.browse_directory)
        dir_layout.addWidget(lbl_dir)
        dir_layout.addWidget(self.txt_dir)
        dir_layout.addWidget(btn_browse)
        export_layout.addLayout(dir_layout)

        # 默认文件名前缀
        prefix_layout = QHBoxLayout()
        lbl_prefix = QLabel("默认文件名前缀:")
        lbl_prefix.setFixedWidth(110)
        self.txt_prefix = QLineEdit()
        self.txt_prefix.setText(self.config.get("default_filename_prefix", "融智云考题库"))
        self.txt_prefix.setPlaceholderText("例如: 融智云考题库")
        prefix_layout.addWidget(lbl_prefix)
        prefix_layout.addWidget(self.txt_prefix)
        export_layout.addLayout(prefix_layout)

        # PDF 导出内核
        engine_layout = QHBoxLayout()
        lbl_engine = QLabel("PDF 导出内核:")
        lbl_engine.setFixedWidth(110)
        self.cmb_engine = NoWheelComboBox()
        self.cmb_engine.addItem("极速内核 (Chromium) - 推荐", "chromium")
        self.cmb_engine.addItem("经典内核 (依赖本地 WPS/Office)", "wps")
        current_engine = self.config.get("pdf_export_engine", "chromium")
        index = self.cmb_engine.findData(current_engine)
        if index >= 0:
            self.cmb_engine.setCurrentIndex(index)
        engine_layout.addWidget(lbl_engine)
        engine_layout.addWidget(self.cmb_engine)
        export_layout.addLayout(engine_layout)

        # 导出后自动打开
        self.chk_auto_open = QCheckBox("导出后自动打开文件")
        self.chk_auto_open.setChecked(self.config.get("auto_open_after_export", True))
        export_layout.addWidget(self.chk_auto_open)

        layout.addWidget(export_group)

        # ---- 分区 B：AI 补全设置 ----
        ai_group = QGroupBox("🤖 AI 补全设置")
        ai_group.setStyleSheet("QGroupBox { font-weight: bold; padding-top: 16px; }")
        ai_layout = QVBoxLayout(ai_group)
        ai_layout.setSpacing(8)

        # 主开关
        self.chk_ai_fill = QCheckBox("导出时自动补全答案（支持空答案 / 略题目）")
        self.chk_ai_fill.setChecked(self.config.get("ai_auto_fill_missing_answers", False))
        self.chk_ai_fill.toggled.connect(self._refresh_ai_state)
        ai_layout.addWidget(self.chk_ai_fill)

        # 调用方式
        mode_layout = QHBoxLayout()
        lbl_mode = QLabel("调用方式:")
        lbl_mode.setFixedWidth(110)
        self.cmb_ai_mode = NoWheelComboBox()
        self.cmb_ai_mode.addItem("官方接口（支持缓存，价格更低）", "official")
        self.cmb_ai_mode.addItem("自定义 API（直连你的模型，不走官方计费）", "custom")
        mode_index = self.cmb_ai_mode.findData(self.config.get("ai_mode", "custom"))
        if mode_index >= 0:
            self.cmb_ai_mode.setCurrentIndex(mode_index)
        self.cmb_ai_mode.currentIndexChanged.connect(self._refresh_ai_form_state)
        mode_layout.addWidget(lbl_mode)
        mode_layout.addWidget(self.cmb_ai_mode)
        ai_layout.addLayout(mode_layout)

        # === 官方接口子区域 ===
        self.official_widget = QWidget()
        official_layout = QVBoxLayout(self.official_widget)
        official_layout.setContentsMargins(0, 0, 0, 0)
        official_layout.setSpacing(6)

        # 模型选择
        off_model_layout = QHBoxLayout()
        lbl_off_model = QLabel("官方模型:")
        lbl_off_model.setFixedWidth(110)
        self.cmb_official_model = NoWheelComboBox()
        self.cmb_official_model.setMinimumWidth(200)
        self.cmb_official_model.currentIndexChanged.connect(self._update_pricing_hint)
        off_model_layout.addWidget(lbl_off_model)
        off_model_layout.addWidget(self.cmb_official_model)
        off_model_layout.addStretch()
        official_layout.addLayout(off_model_layout)

        # 余额显示
        balance_row = QHBoxLayout()
        self.lbl_balance = QLabel("当前余额: 加载中...")
        self.lbl_balance.setStyleSheet("color: #DAA520; font-size: 13px; font-weight: bold;")
        balance_row.addWidget(self.lbl_balance)
        balance_row.addStretch()

        self.btn_recharge = QPushButton("💰 在线充值")
        self.btn_recharge.setStyleSheet("""
            QPushButton {
                background-color: #DAA520;
                color: white;
                padding: 4px 12px;
                font-weight: bold;
                border-radius: 4px;
                font-size: 12px;
            }
            QPushButton:hover { background-color: #B8860B; }
        """)
        self.btn_recharge.clicked.connect(self.open_recharge)
        balance_row.addWidget(self.btn_recharge)

        self.btn_refresh_balance = QPushButton("🔄")
        self.btn_refresh_balance.setToolTip("刷新余额")
        self.btn_refresh_balance.setFixedWidth(32)
        self.btn_refresh_balance.setStyleSheet("""
            QPushButton { background-color: transparent; font-size: 14px; padding: 2px; }
            QPushButton:hover { background-color: rgba(255,255,255,0.1); }
        """)
        self.btn_refresh_balance.clicked.connect(lambda: self._load_server_data(force=True))
        balance_row.addWidget(self.btn_refresh_balance)
        official_layout.addLayout(balance_row)

        # 计费说明
        self.lbl_pricing = QLabel("")
        self.lbl_pricing.setWordWrap(True)
        self.lbl_pricing.setStyleSheet("color: #888; font-size: 11px;")
        official_layout.addWidget(self.lbl_pricing)

        # 图片题识别
        self.chk_official_images = QCheckBox("启用图片题识别（部分模型支持）")
        self.chk_official_images.setChecked(self.config.get("official_supports_images", True))
        official_layout.addWidget(self.chk_official_images)

        ai_layout.addWidget(self.official_widget)

        # === 自定义 API 子区域 ===
        self.custom_widget = QWidget()
        custom_layout = QVBoxLayout(self.custom_widget)
        custom_layout.setContentsMargins(0, 0, 0, 0)
        custom_layout.setSpacing(6)

        # 接口类型
        provider_layout = QHBoxLayout()
        lbl_provider = QLabel("接口类型:")
        lbl_provider.setFixedWidth(110)
        self.cmb_provider = NoWheelComboBox()
        self.cmb_provider.addItem("OpenAI / GPT", "openai")
        self.cmb_provider.addItem("DeepSeek", "deepseek")
        self.cmb_provider.addItem("Kimi / Moonshot", "kimi")
        self.cmb_provider.addItem("千问 / Qwen", "qwen")
        self.cmb_provider.addItem("智谱 / GLM", "glm")
        self.cmb_provider.addItem("小米 MiMo", "mimo")
        self.cmb_provider.addItem("自定义兼容接口", "custom")
        provider_index = self.cmb_provider.findData(self.config.get("ai_provider", "openai"))
        if provider_index >= 0:
            self.cmb_provider.setCurrentIndex(provider_index)
        self.cmb_provider.currentIndexChanged.connect(self._apply_provider_preset)
        provider_layout.addWidget(lbl_provider)
        provider_layout.addWidget(self.cmb_provider)
        custom_layout.addLayout(provider_layout)

        # API Base URL
        url_layout = QHBoxLayout()
        lbl_url = QLabel("API 地址:")
        lbl_url.setFixedWidth(110)
        self.txt_ai_url = QLineEdit()
        self.txt_ai_url.setText(self.config.get("ai_base_url", "https://api.openai.com/v1"))
        self.txt_ai_url.setPlaceholderText("例如: https://api.openai.com/v1")
        url_layout.addWidget(lbl_url)
        url_layout.addWidget(self.txt_ai_url)
        custom_layout.addLayout(url_layout)

        # 模型名
        model_layout = QHBoxLayout()
        lbl_model = QLabel("模型名:")
        lbl_model.setFixedWidth(110)
        self.txt_ai_model = QLineEdit()
        self.txt_ai_model.setText(self.config.get("ai_model", "gpt-4o-mini"))
        self.txt_ai_model.setPlaceholderText("例如: gpt-4o-mini")
        model_layout.addWidget(lbl_model)
        model_layout.addWidget(self.txt_ai_model)
        custom_layout.addLayout(model_layout)

        # API Key
        key_layout = QHBoxLayout()
        lbl_key = QLabel("API Key:")
        lbl_key.setFixedWidth(110)
        self.txt_ai_key = QLineEdit()
        self.txt_ai_key.setEchoMode(QLineEdit.Password)
        self.txt_ai_key.setText(self.config.get("ai_api_key", ""))
        self.txt_ai_key.setPlaceholderText("留空则不会触发 AI 补全")
        key_layout.addWidget(lbl_key)
        key_layout.addWidget(self.txt_ai_key)
        custom_layout.addLayout(key_layout)

        self.chk_custom_images = QCheckBox("该接口支持图片题/图片选项识别")
        self.chk_custom_images.setChecked(self.config.get("ai_supports_images", True))
        custom_layout.addWidget(self.chk_custom_images)

        # 自定义 API 不参与官方缓存和计费的说明
        tip_custom = QLabel("⚠ 自定义 API 直接调用你的模型，不参与官方缓存与计费。")
        tip_custom.setWordWrap(True)
        tip_custom.setStyleSheet("color: #B8860B; font-size: 11px;")
        custom_layout.addWidget(tip_custom)

        ai_layout.addWidget(self.custom_widget)

        layout.addWidget(ai_group)

        # ---- 分区 C：管理员设置（仅管理员可见）----
        self.admin_group = QGroupBox("🛡️ 管理员设置")
        self.admin_group.setStyleSheet("QGroupBox { font-weight: bold; padding-top: 16px; color: #DAA520; }")
        admin_layout = QVBoxLayout(self.admin_group)
        admin_layout.setSpacing(8)

        btn_open_admin = QPushButton("🔧 打开管理员面板")
        btn_open_admin.setStyleSheet("""
            QPushButton {
                background-color: #DAA520;
                color: white;
                padding: 8px 16px;
                font-weight: bold;
                border-radius: 6px;
            }
            QPushButton:hover { background-color: #B8860B; }
        """)
        btn_open_admin.clicked.connect(lambda: self.open_admin_panel.emit())
        admin_layout.addWidget(btn_open_admin)

        admin_desc = QLabel("管理官方 AI 提供商、模型价格、用户余额、错题审核等。")
        admin_desc.setWordWrap(True)
        admin_desc.setStyleSheet("color: #888; font-size: 11px;")
        admin_layout.addWidget(admin_desc)

        self.admin_group.setVisible(False)  # 默认隐藏
        layout.addWidget(self.admin_group)

        layout.addStretch()

        # 退出登录按钮
        btn_logout = QPushButton("🚪 退出登录")
        btn_logout.setFixedHeight(36)
        btn_logout.setStyleSheet("""
            QPushButton {
                background-color: transparent;
                color: #D83B01;
                border: 1px solid #D83B01;
                border-radius: 6px;
                font-size: 13px;
                font-weight: bold;
            }
            QPushButton:hover { background-color: rgba(216, 59, 1, 0.08); }
        """)
        btn_logout.clicked.connect(self.confirm_logout)
        layout.addWidget(btn_logout)

        layout.addSpacing(10)

        # 底部按钮
        btn_layout = QHBoxLayout()
        btn_layout.addStretch()
        btn_save = QPushButton("保存设置")
        btn_save.setStyleSheet("background-color: #0078D4; color: white; padding: 6px 15px; font-weight: bold; border-radius: 4px;")
        btn_save.clicked.connect(self.save_settings)
        btn_cancel = QPushButton("取消")
        btn_cancel.setStyleSheet("padding: 6px 15px; border-radius: 4px;")
        btn_cancel.clicked.connect(self.reject)
        btn_layout.addWidget(btn_cancel)
        btn_layout.addWidget(btn_save)
        layout.addLayout(btn_layout)

        scroll.setWidget(scroll_widget)
        main_layout.addWidget(scroll)

        self._refresh_ai_form_state()

    def set_admin_visible(self, visible):
        self.admin_group.setVisible(visible)

    def browse_directory(self):
        dir_path = QFileDialog.getExistingDirectory(self, "选择默认导出目录", self.txt_dir.text())
        if dir_path:
            self.txt_dir.setText(dir_path)

    def _load_server_data(self, force=False):
        """异步加载模型列表和余额，避免阻塞 Qt 主线程。"""
        if not self.jwt_token or self._server_data_loading:
            return
        if self._server_data_loaded and not force:
            return

        self._server_data_loading = True
        self.btn_refresh_balance.setEnabled(False)
        self.lbl_balance.setText("当前余额: 加载中...")

        request = QNetworkRequest(QUrl(f"{API_BASE_URL}/api/yunkao/wallet"))
        request.setRawHeader(b"Authorization", f"Bearer {self.jwt_token}".encode("utf-8"))
        request.setTransferTimeout(5000)
        reply = self.network_manager.get(request)
        reply.finished.connect(lambda: self._finish_server_data_load(reply))

    def _finish_server_data_load(self, reply):
        self._server_data_loading = False
        self.btn_refresh_balance.setEnabled(True)
        try:
            if reply.error() != QNetworkReply.NoError:
                raise RuntimeError(reply.errorString())
            data = json.loads(bytes(reply.readAll()).decode("utf-8"))
            wallet = data.get("wallet", {})
            self.wallet_balance_cents = wallet.get("balance_cents", 0)
            balance_yuan = self.wallet_balance_cents / 100.0
            self.lbl_balance.setText(f"当前余额: ¥{balance_yuan:.2f}")

            models = data.get("models", [])
            self.server_models = models
            selected_model_id = (
                self.cmb_official_model.currentData()
                or self.config.get("official_model_id", 0)
            )
            self.cmb_official_model.blockSignals(True)
            self.cmb_official_model.clear()
            if models:
                for model in models:
                    label = model.get("label", model.get("model_name", ""))
                    prefix = "⭐ " if model.get("is_default", False) else ""
                    self.cmb_official_model.addItem(
                        f"{prefix}{label}", model.get("id", 0)
                    )
            else:
                self.cmb_official_model.addItem("暂无可用模型", 0)
            selected_index = self.cmb_official_model.findData(selected_model_id)
            if selected_index >= 0:
                self.cmb_official_model.setCurrentIndex(selected_index)
            self.cmb_official_model.blockSignals(False)
            self._server_data_loaded = True
            self._update_pricing_hint()
        except Exception:
            self.lbl_balance.setText("当前余额: 网络不可达")
        finally:
            reply.deleteLater()

    def _update_pricing_hint(self):
        model_id = self.cmb_official_model.currentData()
        model = next(
            (item for item in self.server_models if item.get("id") == model_id),
            None,
        )
        if model:
            m = model
            cache_hit = m.get("cache_hit_input_price_1m_cents", 0) / 100.0
            live_in = m.get("live_input_price_1m_cents", 0) / 100.0
            live_out = m.get("output_price_1m_cents", 0) / 100.0
            self.lbl_pricing.setText(
                f"计费：缓存命中 ¥{cache_hit:.2f}/百万tokens | "
                f"实时输入 ¥{live_in:.2f}/百万tokens | "
                f"输出 ¥{live_out:.2f}/百万tokens"
            )
        else:
            self.lbl_pricing.setText("")

    def _apply_provider_preset(self):
        from modules.ai_answer import PROVIDER_PRESETS
        provider_key = self.cmb_provider.currentData()
        preset = PROVIDER_PRESETS.get(provider_key, PROVIDER_PRESETS["custom"])
        if preset.get("base_url"):
            self.txt_ai_url.setText(preset["base_url"])
        if preset.get("model"):
            self.txt_ai_model.setText(preset["model"])
        self.chk_custom_images.setChecked(bool(preset.get("supports_images", False)))

    def _refresh_ai_state(self):
        enabled = self.chk_ai_fill.isChecked()
        self.cmb_ai_mode.setEnabled(enabled)
        self._refresh_ai_form_state()

    def _refresh_ai_form_state(self):
        is_official = self.cmb_ai_mode.currentData() == "official"
        self.official_widget.setVisible(is_official)
        self.custom_widget.setVisible(not is_official)

        if is_official and self.jwt_token and not self._server_data_loaded:
            self._load_server_data()

    def open_recharge(self):
        """打开在线充值"""
        if not self.jwt_token:
            QMessageBox.warning(self, "错误", "请先登录")
            return

        self._recharge_balance_before = (
            self.wallet_balance_cents if self._server_data_loaded else None
        )
        self._recharge_poll_count = 0
        self.recharge_poll_timer.start()
        self.btn_recharge.setText("等待充值到账...")
        token = quote(self.jwt_token, safe="")
        webbrowser.open(f"{API_BASE_URL}/api/yunkao/pay/recharge-page#token={token}")
        QTimer.singleShot(1000, self._poll_recharge_balance)

    def _poll_recharge_balance(self):
        if not self.jwt_token:
            self._stop_recharge_poll()
            return

        self._recharge_poll_count += 1
        if self._recharge_poll_count > 200:
            self._stop_recharge_poll()
            return

        request = QNetworkRequest(QUrl(f"{API_BASE_URL}/api/yunkao/wallet"))
        request.setRawHeader(b"Authorization", f"Bearer {self.jwt_token}".encode("utf-8"))
        request.setTransferTimeout(5000)
        reply = self.network_manager.get(request)
        reply.finished.connect(lambda: self._finish_recharge_balance_poll(reply))

    def _finish_recharge_balance_poll(self, reply):
        try:
            if reply.error() != QNetworkReply.NoError:
                return
            data = json.loads(bytes(reply.readAll()).decode("utf-8"))
            balance_cents = int((data.get("wallet") or {}).get("balance_cents", 0) or 0)
            previous_balance = self._recharge_balance_before
            self.wallet_balance_cents = balance_cents
            self.lbl_balance.setText(f"当前余额: ¥{balance_cents / 100:.2f}")

            if previous_balance is None:
                self._recharge_balance_before = balance_cents
                return
            if balance_cents < previous_balance:
                self._recharge_balance_before = balance_cents
                return
            if balance_cents > previous_balance:
                self._stop_recharge_poll()
                self.wallet_balance_updated.emit(balance_cents)
                QMessageBox.information(
                    self,
                    "充值到账",
                    f"余额已自动更新为 ¥{balance_cents / 100:.2f}",
                )
        except (TypeError, ValueError, json.JSONDecodeError):
            pass
        finally:
            reply.deleteLater()

    def _stop_recharge_poll(self):
        self.recharge_poll_timer.stop()
        self.btn_recharge.setText("💰 在线充值")

    def save_settings(self):
        new_dir = self.txt_dir.text().strip()
        new_prefix = self.txt_prefix.text().strip()

        if not new_dir or not os.path.isdir(new_dir):
            QMessageBox.warning(self, "错误", "请选择一个有效的导出目录！")
            return
        if not new_prefix:
            QMessageBox.warning(self, "错误", "文件名前缀不能为空！")
            return

        self.config["default_export_dir"] = new_dir
        self.config["default_filename_prefix"] = new_prefix
        self.config["auto_open_after_export"] = self.chk_auto_open.isChecked()
        self.config["ai_auto_fill_missing_answers"] = self.chk_ai_fill.isChecked()
        self.config["ai_mode"] = self.cmb_ai_mode.currentData()

        # 官方接口配置
        if self.cmb_official_model.currentData():
            self.config["official_model_id"] = self.cmb_official_model.currentData()
        self.config["official_supports_images"] = self.chk_official_images.isChecked()

        # 自定义 API 配置
        self.config["ai_provider"] = self.cmb_provider.currentData()
        self.config["ai_base_url"] = self.txt_ai_url.text().strip() or "https://api.openai.com/v1"
        self.config["ai_model"] = self.txt_ai_model.text().strip() or "gpt-4o-mini"
        self.config["ai_api_key"] = self.txt_ai_key.text().strip()
        self.config["ai_supports_images"] = self.chk_custom_images.isChecked()
        self.config["pdf_export_engine"] = self.cmb_engine.currentData()

        save_config(self.config)
        self.config_updated.emit(self.config)
        self.accept()

    def confirm_logout(self):
        reply = QMessageBox.question(
            self, "确认退出",
            "退出登录将清除本地保存的登录状态和云考密码。\n\n确定要退出吗？",
            QMessageBox.Yes | QMessageBox.No,
            QMessageBox.No
        )
        if reply == QMessageBox.Yes:
            self.logout_requested.emit()
            self.accept()
