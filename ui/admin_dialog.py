"""
融智云考助手 - 管理员面板
"""
from PySide6.QtWidgets import (QDialog, QVBoxLayout, QHBoxLayout, QLabel,
                                QLineEdit, QPushButton, QTabWidget, QTableWidget,
                                QTableWidgetItem, QHeaderView, QMessageBox, QComboBox,
                                QGroupBox, QFormLayout, QSpinBox, QDoubleSpinBox,
                                QTextEdit, QWidget, QCheckBox, QInputDialog)
from PySide6.QtCore import Qt
from modules.admin_api import AdminAPI


class AdminDialog(QDialog):
    def __init__(self, parent=None, jwt_token=""):
        super().__init__(parent)
        self.setWindowTitle("管理员面板 - 融智云考助手")
        self.setMinimumSize(800, 600)
        self.resize(900, 650)

        self.api = AdminAPI(jwt_token)

        self.init_ui()
        self._load_providers()

    def init_ui(self):
        layout = QVBoxLayout(self)

        tabs = QTabWidget()

        # Tab 1: 提供商管理
        self.tab_providers = QWidget()
        tabs.addTab(self.tab_providers, "🔌 提供商")
        self._init_provider_tab()

        # Tab 2: 模型管理
        self.tab_models = QWidget()
        tabs.addTab(self.tab_models, "🧠 模型管理")
        self._init_model_tab()

        # Tab 3: 余额管理
        self.tab_wallets = QWidget()
        tabs.addTab(self.tab_wallets, "💰 余额管理")
        self._init_wallet_tab()

        # Tab 4: 错题审核
        self.tab_reports = QWidget()
        tabs.addTab(self.tab_reports, "📋 错题审核")
        self._init_report_tab()

        # Tab 5: 使用日志
        self.tab_logs = QWidget()
        tabs.addTab(self.tab_logs, "📊 使用日志")
        self._init_log_tab()

        layout.addWidget(tabs)

        close_btn = QPushButton("关闭")
        close_btn.clicked.connect(self.accept)
        layout.addWidget(close_btn)

    # ============ Tab 1: 提供商管理 ============
    def _init_provider_tab(self):
        layout = QVBoxLayout(self.tab_providers)

        # 操作按钮
        btn_layout = QHBoxLayout()
        btn_add = QPushButton("➕ 新增提供商")
        btn_add.clicked.connect(self._add_provider)
        btn_refresh = QPushButton("🔄 刷新")
        btn_refresh.clicked.connect(self._load_providers)
        btn_layout.addWidget(btn_add)
        btn_layout.addWidget(btn_refresh)
        btn_layout.addStretch()
        layout.addLayout(btn_layout)

        # 表格
        self.provider_table = QTableWidget()
        self.provider_table.setColumnCount(6)
        self.provider_table.setHorizontalHeaderLabels(["ID", "标识", "名称", "Base URL", "启用", "优先级"])
        self.provider_table.horizontalHeader().setSectionResizeMode(QHeaderView.Stretch)
        self.provider_table.setEditTriggers(QTableWidget.NoEditTriggers)
        self.provider_table.doubleClicked.connect(self._edit_provider)
        layout.addWidget(self.provider_table)

    def _load_providers(self):
        try:
            data = self.api.get_providers()
            providers = data.get("providers", [])
            self.provider_table.setRowCount(len(providers))
            for row, p in enumerate(providers):
                self.provider_table.setItem(row, 0, QTableWidgetItem(str(p.get("id", ""))))
                self.provider_table.setItem(row, 1, QTableWidgetItem(p.get("provider_key", "")))
                self.provider_table.setItem(row, 2, QTableWidgetItem(p.get("label", "")))
                self.provider_table.setItem(row, 3, QTableWidgetItem(p.get("base_url", "")))
                self.provider_table.setItem(row, 4, QTableWidgetItem("✅" if p.get("enabled") else "❌"))
                self.provider_table.setItem(row, 5, QTableWidgetItem(str(p.get("priority", 0))))
        except Exception as e:
            QMessageBox.warning(self, "错误", f"加载提供商失败: {e}")

    def _add_provider(self):
        dialog = ProviderEditDialog(self)
        if dialog.exec() == QDialog.Accepted:
            try:
                self.api.create_provider(dialog.get_data())
                self._load_providers()
            except Exception as e:
                QMessageBox.warning(self, "错误", f"创建失败: {e}")

    def _edit_provider(self, index):
        row = index.row()
        provider_id = int(self.provider_table.item(row, 0).text())
        # 简单编辑：切换启用状态和修改 API Key
        action, ok = QInputDialog.getItem(
            self, "操作", "选择操作:",
            ["启用/禁用切换", "设置 API Key", "删除"], 0, False
        )
        if not ok:
            return
        try:
            if action == "启用/禁用切换":
                current = self.provider_table.item(row, 4).text()
                new_enabled = current != "✅"
                self.api.update_provider(provider_id, {"enabled": new_enabled})
                self._load_providers()
            elif action == "设置 API Key":
                key, ok = QInputDialog.getText(self, "API Key", "请输入 API Key:")
                if ok:
                    self.api.update_provider(provider_id, {"api_key": key})
                    QMessageBox.information(self, "成功", "API Key 已更新")
            elif action == "删除":
                reply = QMessageBox.question(self, "确认", "确定删除此提供商？关联模型也会被删除。",
                                             QMessageBox.Yes | QMessageBox.No, QMessageBox.No)
                if reply == QMessageBox.Yes:
                    self.api.delete_provider(provider_id)
                    self._load_providers()
        except Exception as e:
            QMessageBox.warning(self, "错误", f"操作失败: {e}")

    # ============ Tab 2: 模型管理 ============
    def _init_model_tab(self):
        layout = QVBoxLayout(self.tab_models)

        btn_layout = QHBoxLayout()
        btn_add = QPushButton("➕ 新增模型")
        btn_add.clicked.connect(self._add_model)
        btn_refresh = QPushButton("🔄 刷新")
        btn_refresh.clicked.connect(self._load_models)
        btn_layout.addWidget(btn_add)
        btn_layout.addWidget(btn_refresh)
        btn_layout.addStretch()
        layout.addLayout(btn_layout)

        self.model_table = QTableWidget()
        self.model_table.setColumnCount(8)
        self.model_table.setHorizontalHeaderLabels([
            "ID", "提供商", "模型名", "标签", "视觉", "输入(命中)", "输入(未命中)", "输出"
        ])
        self.model_table.horizontalHeader().setSectionResizeMode(QHeaderView.Stretch)
        self.model_table.setEditTriggers(QTableWidget.NoEditTriggers)
        self.model_table.doubleClicked.connect(self._edit_model)
        layout.addWidget(self.model_table)

    def _load_models(self):
        try:
            data = self.api.get_models()
            models = data.get("models", [])
            self.model_table.setRowCount(len(models))
            for row, m in enumerate(models):
                self.model_table.setItem(row, 0, QTableWidgetItem(str(m.get("id", ""))))
                self.model_table.setItem(row, 1, QTableWidgetItem(m.get("provider_key", "")))
                self.model_table.setItem(row, 2, QTableWidgetItem(m.get("model_name", "")))
                self.model_table.setItem(row, 3, QTableWidgetItem(m.get("label", "")))
                self.model_table.setItem(row, 4, QTableWidgetItem("✅" if m.get("supports_vision") else "❌"))
                self.model_table.setItem(row, 5, QTableWidgetItem(
                    f"¥{m.get('cache_hit_input_price_1m_cents', 0) / 100:.2f}"))
                self.model_table.setItem(row, 6, QTableWidgetItem(
                    f"¥{m.get('live_input_price_1m_cents', 0) / 100:.2f}"))
                self.model_table.setItem(row, 7, QTableWidgetItem(
                    f"¥{m.get('output_price_1m_cents', 0) / 100:.2f}"))
        except Exception as e:
            QMessageBox.warning(self, "错误", f"加载模型失败: {e}")

    def _add_model(self):
        dialog = ModelEditDialog(self)
        if dialog.exec() == QDialog.Accepted:
            try:
                self.api.create_model(dialog.get_data())
                self._load_models()
            except Exception as e:
                QMessageBox.warning(self, "错误", f"创建失败: {e}")

    def _edit_model(self, index):
        row = index.row()
        model_id = int(self.model_table.item(row, 0).text())
        action, ok = QInputDialog.getItem(
            self, "操作", "选择操作:",
            ["修改价格", "设为默认", "启用/禁用", "删除"], 0, False
        )
        if not ok:
            return
        try:
            if action == "修改价格":
                cache_hit, ok1 = QInputDialog.getDouble(self, "缓存命中输入价", "¥/百万tokens:", 0.10, 0, 1000, 2)
                if not ok1: return
                live_in, ok2 = QInputDialog.getDouble(self, "实时输入价", "¥/百万tokens:", 2.00, 0, 1000, 2)
                if not ok2: return
                live_out, ok3 = QInputDialog.getDouble(self, "输出价", "¥/百万tokens:", 6.00, 0, 1000, 2)
                if not ok3: return
                self.api.update_model(model_id, {
                    "cache_hit_input_price_1m_cents": int(cache_hit * 100),
                    "live_input_price_1m_cents": int(live_in * 100),
                    "output_price_1m_cents": int(live_out * 100),
                })
                self._load_models()
            elif action == "设为默认":
                self.api.update_model(model_id, {"is_default": True})
                self._load_models()
            elif action == "启用/禁用":
                current = self.model_table.item(row, 1).text()
                # 这里简化：toggle enabled
                self.api.update_model(model_id, {"enabled": True})  # 可扩展
                self._load_models()
            elif action == "删除":
                reply = QMessageBox.question(self, "确认", "确定删除此模型？",
                                             QMessageBox.Yes | QMessageBox.No, QMessageBox.No)
                if reply == QMessageBox.Yes:
                    self.api.delete_model(model_id)
                    self._load_models()
        except Exception as e:
            QMessageBox.warning(self, "错误", f"操作失败: {e}")

    # ============ Tab 3: 余额管理 ============
    def _init_wallet_tab(self):
        layout = QVBoxLayout(self.tab_wallets)

        search_layout = QHBoxLayout()
        self.wallet_search = QLineEdit()
        self.wallet_search.setPlaceholderText("搜索学号或昵称...")
        btn_search = QPushButton("搜索")
        btn_search.clicked.connect(self._load_wallets)
        search_layout.addWidget(self.wallet_search)
        search_layout.addWidget(btn_search)
        layout.addLayout(search_layout)

        self.wallet_table = QTableWidget()
        self.wallet_table.setColumnCount(5)
        self.wallet_table.setHorizontalHeaderLabels(["用户ID", "学号", "昵称", "余额(元)", "操作"])
        self.wallet_table.horizontalHeader().setSectionResizeMode(QHeaderView.Stretch)
        layout.addWidget(self.wallet_table)

        self._load_wallets()

    def _load_wallets(self):
        try:
            search = self.wallet_search.text().strip()
            data = self.api.get_user_wallets(search=search)
            wallets = data.get("wallets", [])
            self.wallet_table.setRowCount(len(wallets))
            for row, w in enumerate(wallets):
                self.wallet_table.setItem(row, 0, QTableWidgetItem(str(w.get("user_id", ""))))
                self.wallet_table.setItem(row, 1, QTableWidgetItem(w.get("student_id", "")))
                self.wallet_table.setItem(row, 2, QTableWidgetItem(w.get("nickname", "")))
                self.wallet_table.setItem(row, 3, QTableWidgetItem(
                    f"¥{w.get('balance_cents', 0) / 100:.2f}"))

                op_widget = QWidget()
                op_layout = QHBoxLayout(op_widget)
                op_layout.setContentsMargins(0, 0, 0, 0)
                btn_recharge = QPushButton("充值")
                btn_recharge.clicked.connect(lambda checked, r=row: self._recharge_user(r))
                btn_deduct = QPushButton("扣减")
                btn_deduct.clicked.connect(lambda checked, r=row: self._deduct_user(r))
                op_layout.addWidget(btn_recharge)
                op_layout.addWidget(btn_deduct)
                self.wallet_table.setCellWidget(row, 4, op_widget)
        except Exception as e:
            QMessageBox.warning(self, "错误", f"加载钱包失败: {e}")

    def _recharge_user(self, row):
        user_id = int(self.wallet_table.item(row, 0).text())
        amount, ok = QInputDialog.getDouble(self, "充值", "金额（元）:", 10.0, 0.01, 10000, 2)
        if ok:
            try:
                self.api.recharge_wallet(user_id, int(amount * 100), "管理员手工充值")
                self._load_wallets()
                QMessageBox.information(self, "成功", f"已为用 {user_id} 充值 ¥{amount:.2f}")
            except Exception as e:
                QMessageBox.warning(self, "错误", f"充值失败: {e}")

    def _deduct_user(self, row):
        user_id = int(self.wallet_table.item(row, 0).text())
        amount, ok = QInputDialog.getDouble(self, "扣减", "金额（元）:", 1.0, 0.01, 10000, 2)
        if ok:
            try:
                self.api.deduct_wallet(user_id, int(amount * 100), "管理员手工扣减")
                self._load_wallets()
            except Exception as e:
                QMessageBox.warning(self, "错误", f"扣减失败: {e}")

    # ============ Tab 4: 错题审核 ============
    def _init_report_tab(self):
        layout = QVBoxLayout(self.tab_reports)

        filter_layout = QHBoxLayout()
        self.report_filter = QComboBox()
        self.report_filter.addItem("待审核", "pending")
        self.report_filter.addItem("全部", "all")
        self.report_filter.addItem("已通过", "approved")
        self.report_filter.addItem("已拒绝", "rejected")
        btn_refresh = QPushButton("刷新")
        btn_refresh.clicked.connect(self._load_reports)
        filter_layout.addWidget(QLabel("状态:"))
        filter_layout.addWidget(self.report_filter)
        filter_layout.addWidget(btn_refresh)
        filter_layout.addStretch()
        layout.addLayout(filter_layout)

        self.report_table = QTableWidget()
        self.report_table.setColumnCount(6)
        self.report_table.setHorizontalHeaderLabels(["ID", "用户ID", "题目Hash", "原因", "状态", "操作"])
        self.report_table.horizontalHeader().setSectionResizeMode(QHeaderView.Stretch)
        layout.addWidget(self.report_table)

        self._load_reports()

    def _load_reports(self):
        try:
            status = self.report_filter.currentData()
            data = self.api.get_wrong_reports(status=status)
            reports = data.get("reports", [])
            self.report_table.setRowCount(len(reports))
            for row, r in enumerate(reports):
                self.report_table.setItem(row, 0, QTableWidgetItem(str(r.get("id", ""))))
                self.report_table.setItem(row, 1, QTableWidgetItem(str(r.get("user_id", ""))))
                self.report_table.setItem(row, 2, QTableWidgetItem(
                    (r.get("question_hash", "") or "")[:16]))
                self.report_table.setItem(row, 3, QTableWidgetItem(
                    (r.get("report_reason", "") or "")[:30]))
                status_text = r.get("status", "pending")
                self.report_table.setItem(row, 4, QTableWidgetItem(status_text))

                if status_text == "pending":
                    op_widget = QWidget()
                    op_layout = QHBoxLayout(op_widget)
                    op_layout.setContentsMargins(0, 0, 0, 0)
                    btn_approve = QPushButton("通过")
                    btn_approve.setStyleSheet("background-color: #4CAF50; color: white;")
                    btn_approve.clicked.connect(lambda checked, r=row: self._approve_report(r))
                    btn_reject = QPushButton("拒绝")
                    btn_reject.setStyleSheet("background-color: #D83B01; color: white;")
                    btn_reject.clicked.connect(lambda checked, r=row: self._reject_report(r))
                    op_layout.addWidget(btn_approve)
                    op_layout.addWidget(btn_reject)
                    self.report_table.setCellWidget(row, 5, op_widget)
        except Exception as e:
            QMessageBox.warning(self, "错误", f"加载错题报告失败: {e}")

    def _approve_report(self, row):
        report_id = int(self.report_table.item(row, 0).text())
        answer, ok = QInputDialog.getText(self, "确认答案", "最终答案（留空则使用缓存答案）:")
        if ok:
            try:
                self.api.review_report(report_id, "approve", answer)
                self._load_reports()
                QMessageBox.information(self, "成功", "错题已审核通过并写入缓存")
            except Exception as e:
                QMessageBox.warning(self, "错误", f"操作失败: {e}")

    def _reject_report(self, row):
        report_id = int(self.report_table.item(row, 0).text())
        try:
            self.api.review_report(report_id, "reject")
            self._load_reports()
        except Exception as e:
            QMessageBox.warning(self, "错误", f"操作失败: {e}")

    # ============ Tab 5: 使用日志 ============
    def _init_log_tab(self):
        layout = QVBoxLayout(self.tab_logs)

        search_layout = QHBoxLayout()
        self.log_search = QLineEdit()
        self.log_search.setPlaceholderText("搜索学号或题目Hash...")
        btn_search = QPushButton("搜索")
        btn_search.clicked.connect(self._load_logs)
        search_layout.addWidget(self.log_search)
        search_layout.addWidget(btn_search)
        layout.addLayout(search_layout)

        self.log_table = QTableWidget()
        self.log_table.setColumnCount(8)
        self.log_table.setHorizontalHeaderLabels([
            "ID", "用户", "模型", "输入tokens", "输出tokens", "金额(元)", "缓存命中", "时间"
        ])
        self.log_table.horizontalHeader().setSectionResizeMode(QHeaderView.Stretch)
        layout.addWidget(self.log_table)

        self._load_logs()

    def _load_logs(self):
        try:
            search = self.log_search.text().strip()
            data = self.api.get_usage_logs(search=search)
            logs = data.get("logs", [])
            self.log_table.setRowCount(len(logs))
            for row, l in enumerate(logs):
                self.log_table.setItem(row, 0, QTableWidgetItem(str(l.get("id", ""))))
                self.log_table.setItem(row, 1, QTableWidgetItem(
                    l.get("student_id", str(l.get("user_id", "")))))
                self.log_table.setItem(row, 2, QTableWidgetItem(l.get("model_name", "")))
                self.log_table.setItem(row, 3, QTableWidgetItem(str(l.get("prompt_tokens", 0))))
                self.log_table.setItem(row, 4, QTableWidgetItem(str(l.get("completion_tokens", 0))))
                self.log_table.setItem(row, 5, QTableWidgetItem(
                    f"¥{l.get('billed_amount_cents', 0) / 100:.4f}"))
                self.log_table.setItem(row, 6, QTableWidgetItem(
                    "✅" if l.get("cache_hit") else "❌"))
                self.log_table.setItem(row, 7, QTableWidgetItem(
                    str(l.get("created_at", ""))[:19]))
        except Exception as e:
            QMessageBox.warning(self, "错误", f"加载日志失败: {e}")


# ============ 辅助对话框 ============

class ProviderEditDialog(QDialog):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setWindowTitle("新增提供商")
        layout = QFormLayout(self)

        self.txt_key = QLineEdit()
        self.txt_key.setPlaceholderText("例如: deepseek")
        layout.addRow("标识 (provider_key):", self.txt_key)

        self.txt_label = QLineEdit()
        self.txt_label.setPlaceholderText("例如: DeepSeek")
        layout.addRow("名称:", self.txt_label)

        self.txt_url = QLineEdit()
        self.txt_url.setPlaceholderText("例如: https://api.deepseek.com")
        layout.addRow("Base URL:", self.txt_url)

        self.txt_api_key = QLineEdit()
        self.txt_api_key.setPlaceholderText("提供商默认 API Key")
        layout.addRow("API Key:", self.txt_api_key)

        btn_save = QPushButton("保存")
        btn_save.clicked.connect(self.accept)
        layout.addRow(btn_save)

    def get_data(self):
        return {
            "provider_key": self.txt_key.text().strip(),
            "label": self.txt_label.text().strip(),
            "base_url": self.txt_url.text().strip(),
            "api_key": self.txt_api_key.text().strip(),
        }


class ModelEditDialog(QDialog):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setWindowTitle("新增模型")
        self.setMinimumWidth(400)
        layout = QFormLayout(self)

        self.txt_provider = QLineEdit()
        self.txt_provider.setPlaceholderText("例如: deepseek")
        layout.addRow("提供商标识:", self.txt_provider)

        self.txt_model = QLineEdit()
        self.txt_model.setPlaceholderText("例如: deepseek-v4-flash")
        layout.addRow("模型名:", self.txt_model)

        self.txt_label = QLineEdit()
        self.txt_label.setPlaceholderText("展示标签")
        layout.addRow("标签:", self.txt_label)

        self.chk_vision = QCheckBox()
        layout.addRow("支持图片:", self.chk_vision)

        self.spin_cache_hit = QDoubleSpinBox()
        self.spin_cache_hit.setRange(0, 1000)
        self.spin_cache_hit.setValue(0.10)
        self.spin_cache_hit.setSuffix(" 元/百万tokens")
        layout.addRow("输入价(缓存命中):", self.spin_cache_hit)

        self.spin_live_in = QDoubleSpinBox()
        self.spin_live_in.setRange(0, 1000)
        self.spin_live_in.setValue(2.00)
        self.spin_live_in.setSuffix(" 元/百万tokens")
        layout.addRow("输入价(未命中):", self.spin_live_in)

        self.spin_live_out = QDoubleSpinBox()
        self.spin_live_out.setRange(0, 1000)
        self.spin_live_out.setValue(6.00)
        self.spin_live_out.setSuffix(" 元/百万tokens")
        layout.addRow("输出价:", self.spin_live_out)

        self.chk_default = QCheckBox("设为默认推荐")
        layout.addRow(self.chk_default)

        btn_save = QPushButton("保存")
        btn_save.clicked.connect(self.accept)
        layout.addRow(btn_save)

    def get_data(self):
        return {
            "provider_key": self.txt_provider.text().strip(),
            "model_name": self.txt_model.text().strip(),
            "label": self.txt_label.text().strip(),
            "supports_vision": self.chk_vision.isChecked(),
            "cache_hit_input_price_1m_cents": int(self.spin_cache_hit.value() * 100),
            "live_input_price_1m_cents": int(self.spin_live_in.value() * 100),
            "output_price_1m_cents": int(self.spin_live_out.value() * 100),
            "is_default": self.chk_default.isChecked(),
        }
