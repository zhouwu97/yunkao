from PySide6.QtWidgets import (QDialog, QVBoxLayout, QHBoxLayout, QLabel,
                                QLineEdit, QPushButton, QCheckBox, QFileDialog, QMessageBox,
                                QGroupBox, QFrame, QScrollArea, QWidget, QSpacerItem, QSizePolicy)
from PySide6.QtCore import Signal, Qt
import json
import os
import keyring
from config.settings import load_config, save_config, SERVICE_NAME, HARDCODED_SCHOOL_CODE
from ui.widgets import NoWheelComboBox

class SettingsDialog(QDialog):
    config_updated = Signal(dict)

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setWindowTitle("系统设置 - 融智云考助手 (免费版 · 禁止倒卖)")
        self.setMinimumSize(520, 600)

        self.config = load_config()
        self.init_ui()

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

        # ---- 分区 A：本地账号设置 ----
        account_group = QGroupBox("🔑 融智云考本地账号")
        account_group.setStyleSheet("QGroupBox { font-weight: bold; padding-top: 16px; }")
        account_layout = QVBoxLayout(account_group)
        account_layout.setSpacing(8)

        # 账号
        user_layout = QHBoxLayout()
        lbl_user = QLabel("云考账号:")
        lbl_user.setFixedWidth(110)
        self.txt_yunkao_user = QLineEdit()
        self.txt_yunkao_user.setText(self.config.get("yunkao_user", ""))
        self.txt_yunkao_user.setPlaceholderText("请输入学号或账号")
        user_layout.addWidget(lbl_user)
        user_layout.addWidget(self.txt_yunkao_user)
        account_layout.addLayout(user_layout)

        # 密码
        pwd_layout = QHBoxLayout()
        lbl_pwd = QLabel("云考密码:")
        lbl_pwd.setFixedWidth(110)
        self.txt_yunkao_pwd = QLineEdit()
        self.txt_yunkao_pwd.setEchoMode(QLineEdit.Password)
        self.txt_yunkao_pwd.setPlaceholderText("请输入云考密码")
        
        # 尝试读取本地密码
        saved_user = self.config.get("yunkao_user", "")
        if saved_user:
            try:
                saved_pwd = keyring.get_password(SERVICE_NAME, f"{HARDCODED_SCHOOL_CODE}_{saved_user}")
                if saved_pwd:
                    self.txt_yunkao_pwd.setText(saved_pwd)
            except Exception:
                pass
                
        pwd_layout.addWidget(lbl_pwd)
        pwd_layout.addWidget(self.txt_yunkao_pwd)
        account_layout.addLayout(pwd_layout)

        # 记住密码
        self.chk_remember_pwd = QCheckBox("在本地记住密码（自动填充网页登录）")
        self.chk_remember_pwd.setChecked(self.config.get("yunkao_remember_password", True))
        account_layout.addWidget(self.chk_remember_pwd)

        layout.addWidget(account_group)

        # ---- 分区 B：导出设置 ----
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

        # ---- 分区 C：AI 补全设置 ----
        ai_group = QGroupBox("🤖 AI 补全设置 (自定义API)")
        ai_group.setStyleSheet("QGroupBox { font-weight: bold; padding-top: 16px; }")
        ai_layout = QVBoxLayout(ai_group)
        ai_layout.setSpacing(8)

        # 主开关
        self.chk_ai_fill = QCheckBox("导出时自动补全答案（支持空答案 / 略题目）")
        self.chk_ai_fill.setChecked(self.config.get("ai_auto_fill_missing_answers", False))
        ai_layout.addWidget(self.chk_ai_fill)

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
        ai_layout.addLayout(provider_layout)

        # API Base URL
        url_layout = QHBoxLayout()
        lbl_url = QLabel("API 地址:")
        lbl_url.setFixedWidth(110)
        self.txt_ai_url = QLineEdit()
        self.txt_ai_url.setText(self.config.get("ai_base_url", "https://api.openai.com/v1"))
        self.txt_ai_url.setPlaceholderText("例如: https://api.openai.com/v1")
        url_layout.addWidget(lbl_url)
        url_layout.addWidget(self.txt_ai_url)
        ai_layout.addLayout(url_layout)

        # 模型名
        model_layout = QHBoxLayout()
        lbl_model = QLabel("模型名:")
        lbl_model.setFixedWidth(110)
        self.txt_ai_model = QLineEdit()
        self.txt_ai_model.setText(self.config.get("ai_model", "gpt-4o-mini"))
        self.txt_ai_model.setPlaceholderText("例如: gpt-4o-mini")
        model_layout.addWidget(lbl_model)
        model_layout.addWidget(self.txt_ai_model)
        ai_layout.addLayout(model_layout)

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
        ai_layout.addLayout(key_layout)

        self.chk_custom_images = QCheckBox("该接口支持图片题/图片选项识别")
        self.chk_custom_images.setChecked(self.config.get("ai_supports_images", True))
        ai_layout.addWidget(self.chk_custom_images)

        layout.addWidget(ai_group)

        layout.addStretch()

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

        self.config["yunkao_user"] = yunkao_user
        self.config["yunkao_remember_password"] = remember_pwd

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

        self.config["ai_auto_fill_missing_answers"] = self.chk_ai_fill.isChecked()
        self.config["ai_mode"] = "custom"  # 强制走 custom
        self.config["ai_provider"] = self.cmb_provider.currentData()
        self.config["ai_base_url"] = self.txt_ai_url.text().strip() or "https://api.openai.com/v1"
        self.config["ai_model"] = self.txt_ai_model.text().strip() or "gpt-4o-mini"
        self.config["ai_api_key"] = self.txt_ai_key.text().strip()
        self.config["ai_supports_images"] = self.chk_custom_images.isChecked()

        save_config(self.config)
        self.config_updated.emit(self.config)
        self.accept()
