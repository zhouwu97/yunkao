from PySide6.QtWidgets import (QDialog, QVBoxLayout, QHBoxLayout, QLabel, 
                               QLineEdit, QPushButton, QCheckBox, QFileDialog, QMessageBox, QComboBox)
from PySide6.QtCore import Signal, Qt
import os
from config.settings import load_config, save_config
from modules.ai_answer import PROVIDER_PRESETS

class SettingsDialog(QDialog):
    # 自定义信号
    config_updated = Signal(dict)
    logout_requested = Signal()

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setWindowTitle("系统设置 - 融智云考助手")
        self.setFixedSize(500, 650)

        # 加载当前配置
        self.config = load_config()

        self.init_ui()

    def init_ui(self):
        layout = QVBoxLayout(self)
        layout.setSpacing(15)
        layout.setContentsMargins(20, 20, 20, 20)

        # 1. 默认下载目录
        dir_layout = QHBoxLayout()
        lbl_dir = QLabel("默认导出目录:")
        lbl_dir.setFixedWidth(100)
        
        self.txt_dir = QLineEdit()
        self.txt_dir.setReadOnly(True)
        self.txt_dir.setText(self.config.get("default_export_dir", os.path.expanduser("~")))
        
        btn_browse = QPushButton("浏览...")
        btn_browse.clicked.connect(self.browse_directory)
        
        dir_layout.addWidget(lbl_dir)
        dir_layout.addWidget(self.txt_dir)
        dir_layout.addWidget(btn_browse)
        layout.addLayout(dir_layout)

        # 2. 默认文件名前缀
        prefix_layout = QHBoxLayout()
        lbl_prefix = QLabel("默认文件名前缀:")
        lbl_prefix.setFixedWidth(100)
        
        self.txt_prefix = QLineEdit()
        self.txt_prefix.setText(self.config.get("default_filename_prefix", "融智云考题库"))
        self.txt_prefix.setPlaceholderText("例如: 融智云考题库")
        
        prefix_layout.addWidget(lbl_prefix)
        prefix_layout.addWidget(self.txt_prefix)
        layout.addLayout(prefix_layout)

        # 3. 高级功能开关
        self.chk_answers = QCheckBox("导出时自动提取并附带正确答案与解析 (高级功能)")
        self.chk_answers.setChecked(self.config.get("export_with_answers", False))
        layout.addWidget(self.chk_answers)

        self.chk_ai_fill = QCheckBox("对“空答案/略”题目启用 AI 自动补全")
        self.chk_ai_fill.setChecked(self.config.get("ai_auto_fill_missing_answers", False))
        layout.addWidget(self.chk_ai_fill)

        mode_layout = QHBoxLayout()
        lbl_ai_mode = QLabel("补全方式:")
        lbl_ai_mode.setFixedWidth(100)
        self.cmb_ai_mode = QComboBox()
        self.cmb_ai_mode.addItem("官方接口 (推荐，价格更低)", "official")
        self.cmb_ai_mode.addItem("自定义 API", "custom")
        mode_index = self.cmb_ai_mode.findData(self.config.get("ai_mode", "custom"))
        if mode_index >= 0:
            self.cmb_ai_mode.setCurrentIndex(mode_index)
        self.cmb_ai_mode.currentIndexChanged.connect(self._refresh_ai_form_state)
        mode_layout.addWidget(lbl_ai_mode)
        mode_layout.addWidget(self.cmb_ai_mode)
        layout.addLayout(mode_layout)

        provider_layout = QHBoxLayout()
        lbl_provider = QLabel("接口类型:")
        lbl_provider.setFixedWidth(100)
        self.cmb_provider = QComboBox()
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
        layout.addLayout(provider_layout)

        self.lbl_official_tip = QLabel("使用官方接口时，会优先命中服务端题库缓存；未命中再调用官方模型。计费可设置为低于市场直连价，建议优先购买。")
        self.lbl_official_tip.setWordWrap(True)
        self.lbl_official_tip.setStyleSheet("color: #B8860B; font-size: 12px;")
        layout.addWidget(self.lbl_official_tip)

        ai_url_layout = QHBoxLayout()
        lbl_ai_url = QLabel("AI 接口地址:")
        lbl_ai_url.setFixedWidth(100)
        self.txt_ai_url = QLineEdit()
        self.txt_ai_url.setText(self.config.get("ai_base_url", "https://api.openai.com/v1"))
        self.txt_ai_url.setPlaceholderText("例如: https://api.openai.com/v1")
        ai_url_layout.addWidget(lbl_ai_url)
        ai_url_layout.addWidget(self.txt_ai_url)
        layout.addLayout(ai_url_layout)

        ai_model_layout = QHBoxLayout()
        lbl_ai_model = QLabel("AI 模型名:")
        lbl_ai_model.setFixedWidth(100)
        self.txt_ai_model = QLineEdit()
        self.txt_ai_model.setText(self.config.get("ai_model", "gpt-4o-mini"))
        self.txt_ai_model.setPlaceholderText("例如: gpt-4o-mini")
        ai_model_layout.addWidget(lbl_ai_model)
        ai_model_layout.addWidget(self.txt_ai_model)
        layout.addLayout(ai_model_layout)

        ai_key_layout = QHBoxLayout()
        lbl_ai_key = QLabel("AI API Key:")
        lbl_ai_key.setFixedWidth(100)
        self.txt_ai_key = QLineEdit()
        self.txt_ai_key.setEchoMode(QLineEdit.Password)
        self.txt_ai_key.setText(self.config.get("ai_api_key", ""))
        self.txt_ai_key.setPlaceholderText("留空则不会触发 AI 补全")
        ai_key_layout.addWidget(lbl_ai_key)
        ai_key_layout.addWidget(self.txt_ai_key)
        layout.addLayout(ai_key_layout)

        self.chk_ai_images = QCheckBox("该接口支持图片题/图片选项识别")
        self.chk_ai_images.setChecked(self.config.get("ai_supports_images", True))
        layout.addWidget(self.chk_ai_images)

        # 4. PDF 导出内核
        engine_layout = QHBoxLayout()
        lbl_engine = QLabel("PDF 导出内核:")
        lbl_engine.setFixedWidth(100)
        
        self.cmb_engine = QComboBox()
        self.cmb_engine.addItem("极速内核 (Chromium) - 推荐", "chromium")
        self.cmb_engine.addItem("经典内核 (依赖本地 WPS/Office)", "wps")
        
        # 根据配置设置默认项
        current_engine = self.config.get("pdf_export_engine", "chromium")
        index = self.cmb_engine.findData(current_engine)
        if index >= 0:
            self.cmb_engine.setCurrentIndex(index)
            
        engine_layout.addWidget(lbl_engine)
        engine_layout.addWidget(self.cmb_engine)
        layout.addLayout(engine_layout)

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
            QPushButton:hover {
                background-color: rgba(216, 59, 1, 0.08);
            }
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

        self._refresh_ai_form_state()

    def browse_directory(self):
        dir_path = QFileDialog.getExistingDirectory(self, "选择默认导出目录", self.txt_dir.text())
        if dir_path:
            self.txt_dir.setText(dir_path)

    def _apply_provider_preset(self):
        provider_key = self.cmb_provider.currentData()
        preset = PROVIDER_PRESETS.get(provider_key, PROVIDER_PRESETS["custom"])
        if preset.get("base_url"):
            self.txt_ai_url.setText(preset["base_url"])
        if preset.get("model"):
            self.txt_ai_model.setText(preset["model"])
        self.chk_ai_images.setChecked(bool(preset.get("supports_images", False)))
        self._refresh_ai_form_state()

    def _refresh_ai_form_state(self):
        is_official = self.cmb_ai_mode.currentData() == "official"
        self.cmb_provider.setEnabled(not is_official)
        self.txt_ai_url.setEnabled(not is_official)
        self.txt_ai_model.setEnabled(not is_official)
        self.txt_ai_key.setEnabled(not is_official)
        self.chk_ai_images.setEnabled(not is_official)
        self.lbl_official_tip.setVisible(is_official)

    def save_settings(self):
        # 更新配置字典
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
        self.config["export_with_answers"] = self.chk_answers.isChecked()
        self.config["ai_auto_fill_missing_answers"] = self.chk_ai_fill.isChecked()
        self.config["ai_mode"] = self.cmb_ai_mode.currentData()
        self.config["ai_provider"] = self.cmb_provider.currentData()
        self.config["ai_base_url"] = self.txt_ai_url.text().strip() or "https://api.openai.com/v1"
        self.config["ai_model"] = self.txt_ai_model.text().strip() or "gpt-4o-mini"
        self.config["ai_api_key"] = self.txt_ai_key.text().strip()
        self.config["ai_supports_images"] = self.chk_ai_images.isChecked()
        self.config["pdf_export_engine"] = self.cmb_engine.currentData()

        # 保存到本地文件
        save_config(self.config)

        # 触发信号实时通知主窗口
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
