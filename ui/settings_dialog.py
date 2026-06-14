from PySide6.QtWidgets import (QDialog, QVBoxLayout, QHBoxLayout, QLabel, 
                               QLineEdit, QPushButton, QCheckBox, QFileDialog, QMessageBox, QComboBox)
from PySide6.QtCore import Signal, Qt
import os
from config.settings import load_config, save_config

class SettingsDialog(QDialog):
    # 自定义信号
    config_updated = Signal(dict)
    logout_requested = Signal()

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setWindowTitle("系统设置 - 融智云考助手")
        self.setFixedSize(420, 400)

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

    def browse_directory(self):
        dir_path = QFileDialog.getExistingDirectory(self, "选择默认导出目录", self.txt_dir.text())
        if dir_path:
            self.txt_dir.setText(dir_path)

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
