"""
融智云考助手 - 管理员面板
"""
from PySide6.QtWidgets import (QDialog, QVBoxLayout, QHBoxLayout, QLabel,
                                QLineEdit, QPushButton, QTabWidget, QTableWidget,
                                QTableWidgetItem, QHeaderView, QMessageBox,
                                QGroupBox, QFormLayout, QSpinBox, QDoubleSpinBox,
                                QTextEdit, QWidget, QCheckBox, QInputDialog,
                                QListWidget, QListWidgetItem, QSplitter, QMenu,
                                QAbstractItemView)
from PySide6.QtCore import Qt, QThread, QTimer, Signal
from modules.admin_api import AdminAPI
from ui.widgets import NoWheelComboBox


class AdminLoadWorker(QThread):
    succeeded = Signal(object)
    failed = Signal(str)

    def __init__(self, loader, parent=None):
        super().__init__(parent)
        self.loader = loader

    def run(self):
        try:
            self.succeeded.emit(self.loader())
        except Exception as exc:
            self.failed.emit(str(exc))


class RemoteModelComboBox(NoWheelComboBox):
    popup_requested = Signal()

    def showPopup(self):
        if self.count() == 0:
            self.popup_requested.emit()
            return
        super().showPopup()


class AdminDialog(QDialog):
    def __init__(self, parent=None, jwt_token=""):
        super().__init__(parent)
        self.setWindowTitle("管理员面板 - 融智云考助手")
        self.setMinimumSize(800, 600)
        self.resize(900, 650)

        self.api = AdminAPI(jwt_token)
        self._load_workers = {}
        self._loaded_tabs = set()

        self.init_ui()
        QTimer.singleShot(0, self._load_current_tab)

    def init_ui(self):
        layout = QVBoxLayout(self)

        self.tabs = QTabWidget()
        self.tabs.currentChanged.connect(self._load_current_tab)

        # Tab 1: 提供商管理
        self.tab_providers = QWidget()
        self.tabs.addTab(self.tab_providers, "🔌 提供商")
        self._init_provider_tab()

        # Tab 2: 模型管理
        self.tab_models = QWidget()
        self.tabs.addTab(self.tab_models, "🧠 模型管理")
        self._init_model_tab()

        # Tab 3: 余额管理
        self.tab_wallets = QWidget()
        self.tabs.addTab(self.tab_wallets, "💰 余额管理")
        self._init_wallet_tab()

        # Tab 4: 错题审核
        self.tab_reports = QWidget()
        self.tabs.addTab(self.tab_reports, "📋 错题审核")
        self._init_report_tab()

        # Tab 5: 使用日志
        self.tab_logs = QWidget()
        self.tabs.addTab(self.tab_logs, "📊 使用日志")
        self._init_log_tab()

        # Tab 6: 支付配置
        self.tab_pay = QWidget()
        self.tabs.addTab(self.tab_pay, "💳 支付配置")
        self._init_pay_tab()

        layout.addWidget(self.tabs)

        close_btn = QPushButton("关闭")
        close_btn.clicked.connect(self.accept)
        layout.addWidget(close_btn)

    def _load_current_tab(self, index=None):
        if index is None:
            index = self.tabs.currentIndex()
        if index in self._loaded_tabs:
            return
        loaders = {
            0: self._load_providers,
            1: self._load_model_providers,
            2: self._load_wallets,
            3: self._load_reports,
            4: self._load_logs,
            5: self._load_pay_config,
        }
        loader = loaders.get(index)
        if loader:
            self._loaded_tabs.add(index)
            loader()

    def _start_load(self, key, loader, apply_result, error_title, on_error=None):
        current = self._load_workers.get(key)
        if current and current.isRunning():
            return

        worker = AdminLoadWorker(loader, self)
        self._load_workers[key] = worker
        worker.succeeded.connect(apply_result)
        def handle_error(error):
            QMessageBox.warning(self, "错误", f"{error_title}: {error}")
            if on_error:
                on_error()

        worker.failed.connect(handle_error)
        worker.finished.connect(lambda: self._load_workers.pop(key, None))
        worker.finished.connect(worker.deleteLater)
        worker.start()

    # ============ Tab 1: 提供商管理 ============
    def _init_provider_tab(self):
        layout = QVBoxLayout(self.tab_providers)

        provider_tip = QLabel(
            "提供商只管理接口地址、密钥和启用状态。不同模型的价格请在“模型管理”中分别设置。"
        )
        provider_tip.setWordWrap(True)
        provider_tip.setStyleSheet("color: #666; padding: 4px 2px;")
        layout.addWidget(provider_tip)

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
        self.provider_table.setColumnCount(5)
        self.provider_table.setHorizontalHeaderLabels(["标识", "名称", "Base URL", "启用", "API Key"])
        self.provider_table.horizontalHeader().setSectionResizeMode(QHeaderView.Stretch)
        self.provider_table.setEditTriggers(QTableWidget.NoEditTriggers)
        self.provider_table.doubleClicked.connect(self._edit_provider)
        layout.addWidget(self.provider_table)

    def _load_providers(self):
        self._start_load(
            "providers", self.api.get_providers,
            self._apply_providers, "加载提供商失败"
        )

    def _apply_providers(self, data):
        providers = data.get("providers") or []
        self.provider_table.setRowCount(len(providers))
        for row, p in enumerate(providers):
            # 标识（存储 provider_id 为隐藏数据）
            key_item = QTableWidgetItem(p.get("provider_key", ""))
            key_item.setData(Qt.UserRole, p.get("id", 0))
            self.provider_table.setItem(row, 0, key_item)
            self.provider_table.setItem(row, 1, QTableWidgetItem(p.get("label", "")))
            self.provider_table.setItem(row, 2, QTableWidgetItem(p.get("base_url", "")))
            self.provider_table.setItem(row, 3, QTableWidgetItem("✅" if p.get("enabled") else "❌"))
            # API Key 脱敏显示
            api_key = p.get("api_key", "") or ""
            masked = api_key[:4] + "****" + api_key[-4:] if len(api_key) > 8 else ("****" if api_key else "")
            self.provider_table.setItem(row, 4, QTableWidgetItem(masked))

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
        col = index.column()
        key_item = self.provider_table.item(row, 0)
        provider_id = key_item.data(Qt.UserRole)

        # 双击 API Key 列：直接编辑
        if col == 4:
            key, ok = QInputDialog.getText(self, "API Key", "请输入 API Key:")
            if ok:
                try:
                    self.api.update_provider(provider_id, {"api_key": key})
                    self._load_providers()
                except Exception as e:
                    QMessageBox.warning(self, "错误", f"操作失败: {e}")
            return

        # 其他列：弹出操作菜单
        action, ok = QInputDialog.getItem(
            self, "操作", "选择操作:",
            ["启用/禁用切换", "设置 API Key", "删除"], 0, False
        )
        if not ok:
            return
        try:
            if action == "启用/禁用切换":
                current = self.provider_table.item(row, 3).text()
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

        model_tip = QLabel(
            "左侧选择提供商，右侧显示它的模型。双击单元格直接编辑；"
            "右键提供商可新增模型或从接口读取模型列表。"
        )
        model_tip.setWordWrap(True)
        model_tip.setFixedHeight(40)
        model_tip.setAlignment(Qt.AlignTop)
        model_tip.setStyleSheet("color: #666; padding: 4px 2px;")
        layout.addWidget(model_tip)

        btn_layout = QHBoxLayout()
        btn_layout.setSpacing(6)
        btn_add = QPushButton("➕ 新增模型")
        btn_add.clicked.connect(self._add_model)
        btn_fetch = QPushButton("🔎 获取模型")
        btn_fetch.clicked.connect(self._fetch_remote_models)
        btn_refresh = QPushButton("🔄 刷新")
        btn_refresh.clicked.connect(self._load_models)
        btn_layout.addWidget(btn_add)
        btn_layout.addWidget(btn_fetch)
        btn_layout.addWidget(btn_refresh)
        btn_layout.addStretch()
        layout.addLayout(btn_layout)

        splitter = QSplitter(Qt.Horizontal)
        provider_panel = QWidget()
        provider_layout = QVBoxLayout(provider_panel)
        provider_layout.setContentsMargins(0, 0, 0, 0)
        provider_layout.addWidget(QLabel("提供商"))
        self.model_provider_list = QListWidget()
        self.model_provider_list.setFixedWidth(180)
        self.model_provider_list.setContextMenuPolicy(Qt.CustomContextMenu)
        self.model_provider_list.customContextMenuRequested.connect(
            self._show_model_provider_menu
        )
        self.model_provider_list.currentItemChanged.connect(
            self._on_model_provider_selected
        )
        provider_layout.addWidget(self.model_provider_list)
        splitter.addWidget(provider_panel)

        right_panel = QWidget()
        right_layout = QVBoxLayout(right_panel)
        right_layout.setContentsMargins(0, 0, 0, 0)
        right_layout.setSpacing(4)

        self.model_table = QTableWidget()
        self.model_table.setColumnCount(9)
        self.model_table.setHorizontalHeaderLabels([
            "ID", "模型名", "标签", "视觉", "默认", "启用",
            "缓存输入(元/百万t)", "实时输入(元/百万t)", "输出(元/百万t)"
        ])
        header = self.model_table.horizontalHeader()
        header.setSectionResizeMode(QHeaderView.Stretch)
        header.setSectionResizeMode(0, QHeaderView.ResizeToContents)
        # 模型名和标签列加宽（Interactive 允许手动调宽，初始占较多空间）
        header.setSectionResizeMode(1, QHeaderView.Interactive)
        header.setSectionResizeMode(2, QHeaderView.Interactive)
        header.resizeSection(1, 140)
        header.resizeSection(2, 100)
        for column in (3, 4, 5):
            header.setSectionResizeMode(column, QHeaderView.ResizeToContents)
        self.model_table.setEditTriggers(
            QAbstractItemView.DoubleClicked | QAbstractItemView.EditKeyPressed
        )
        self.model_table.setSelectionBehavior(QAbstractItemView.SelectItems)
        self.model_table.itemChanged.connect(self._on_model_item_changed)
        self.model_table.setContextMenuPolicy(Qt.CustomContextMenu)
        self.model_table.customContextMenuRequested.connect(self._show_model_menu)
        right_layout.addWidget(self.model_table)

        # 空状态 / 加载状态提示标签
        self.model_status_label = QLabel("")
        self.model_status_label.setAlignment(Qt.AlignCenter)
        self.model_status_label.setStyleSheet(
            "color: #999; font-size: 13px; padding: 20px;"
        )
        self.model_status_label.hide()
        right_layout.addWidget(self.model_status_label)

        splitter.addWidget(right_panel)
        splitter.setStretchFactor(0, 0)
        splitter.setStretchFactor(1, 1)
        layout.addWidget(splitter)

        self.model_providers = []
        self.models = []
        self._updating_model_table = False

    def _load_model_providers(self):
        self._start_load(
            "model_providers",
            self.api.get_providers,
            self._apply_model_providers,
            "加载提供商失败",
        )

    def _apply_model_providers(self, data):
        selected = self._selected_model_provider()
        selected_key = selected.get("provider_key") if selected else ""
        self.model_providers = data.get("providers") or []
        self.model_provider_list.blockSignals(True)
        self.model_provider_list.clear()
        selected_row = 0
        for row, provider in enumerate(self.model_providers):
            label = provider.get("label") or provider.get("provider_key", "")
            item = QListWidgetItem(label)
            item.setData(Qt.UserRole, provider)
            if not provider.get("enabled", True):
                item.setForeground(Qt.gray)
            self.model_provider_list.addItem(item)
            if provider.get("provider_key") == selected_key:
                selected_row = row
        self.model_provider_list.blockSignals(False)
        if self.model_provider_list.count():
            self.model_provider_list.setCurrentRow(selected_row)

    def _selected_model_provider(self):
        item = self.model_provider_list.currentItem()
        return item.data(Qt.UserRole) if item else None

    def _on_model_provider_selected(self, current, previous):
        if current:
            self._load_models()

    def _load_models(self):
        provider = self._selected_model_provider()
        if not provider:
            if not self.model_providers:
                self._load_model_providers()
            return
        provider_key = provider.get("provider_key", "")
        # 显示加载状态
        self.model_status_label.setText("加载中...")
        self.model_status_label.show()
        self.model_table.hide()
        self._start_load(
            f"models_{provider_key}",
            lambda: self.api.get_models(provider_key),
            self._apply_models, "加载模型失败",
            on_error=lambda: self._show_model_empty(),
        )

    def _apply_models(self, data):
        models = data.get("models") or []
        self.models = models
        self._updating_model_table = True
        self.model_table.setRowCount(len(models))
        if models:
            self.model_status_label.hide()
            self.model_table.show()
            for row, m in enumerate(models):
                id_item = QTableWidgetItem(str(m.get("id", "")))
                id_item.setFlags(id_item.flags() & ~Qt.ItemIsEditable)
                self.model_table.setItem(row, 0, id_item)
                self.model_table.setItem(row, 1, self._editable_model_item(
                    m.get("model_name", ""), "model_name"
                ))
                self.model_table.setItem(row, 2, self._editable_model_item(
                    m.get("label", ""), "label"
                ))
                self.model_table.setItem(row, 3, self._checkable_model_item(
                    m.get("supports_vision", False), "supports_vision"
                ))
                self.model_table.setItem(row, 4, self._checkable_model_item(
                    m.get("is_default", False), "is_default"
                ))
                self.model_table.setItem(row, 5, self._checkable_model_item(
                    m.get("enabled", True), "enabled"
                ))
                self.model_table.setItem(row, 6, self._editable_model_item(
                    f"{m.get('cache_hit_input_price_1m_cents', 0) / 100:.2f}",
                    "cache_hit_input_price_1m_cents",
                ))
                self.model_table.setItem(row, 7, self._editable_model_item(
                    f"{m.get('live_input_price_1m_cents', 0) / 100:.2f}",
                    "live_input_price_1m_cents",
                ))
                self.model_table.setItem(row, 8, self._editable_model_item(
                    f"{m.get('output_price_1m_cents', 0) / 100:.2f}",
                    "output_price_1m_cents",
                ))
        else:
            self._show_model_empty()
        self._updating_model_table = False

    def _show_model_empty(self):
        """显示无模型提示"""
        self.model_table.setRowCount(0)
        self.model_table.hide()
        self.model_status_label.setText("当前提供商暂无模型")
        self.model_status_label.show()

    def _editable_model_item(self, value, field):
        item = QTableWidgetItem(str(value))
        item.setData(Qt.UserRole, field)
        return item

    def _checkable_model_item(self, checked, field):
        item = QTableWidgetItem()
        item.setData(Qt.UserRole, field)
        item.setFlags(
            (item.flags() | Qt.ItemIsUserCheckable) & ~Qt.ItemIsEditable
        )
        item.setCheckState(Qt.Checked if checked else Qt.Unchecked)
        item.setTextAlignment(Qt.AlignCenter)
        return item

    def _add_model(self):
        provider = self._selected_model_provider()
        if not provider:
            QMessageBox.information(self, "新增模型", "请先在左侧选择提供商")
            return
        dialog = ModelEditDialog(
            self,
            provider=provider,
            remote_loader=lambda: self.api.get_remote_models(provider["id"]),
        )
        if dialog.exec() == QDialog.Accepted:
            try:
                self.api.create_model(dialog.get_data())
                self._load_models()
            except Exception as e:
                QMessageBox.warning(self, "错误", f"创建失败: {e}")

    def _on_model_item_changed(self, item):
        if self._updating_model_table:
            return
        id_item = self.model_table.item(item.row(), 0)
        if not id_item:
            return
        model_id = int(id_item.text())
        field = item.data(Qt.UserRole)
        if not field:
            return
        try:
            if field in {"supports_vision", "is_default", "enabled"}:
                value = item.checkState() == Qt.Checked
            elif field.endswith("_cents"):
                value = int(round(float(item.text().replace("¥", "").strip()) * 100))
                if value < 0:
                    raise ValueError
            else:
                value = item.text().strip()
                if field == "model_name" and not value:
                    raise ValueError
        except (TypeError, ValueError):
            QMessageBox.warning(
                self, "格式错误", "模型名不能为空，价格必须是大于等于 0 的数字"
            )
            self._load_models()
            return

        self._start_load(
            f"update_model_{model_id}_{field}",
            lambda: self.api.update_model(model_id, {field: value}),
            lambda data: None,
            "保存模型失败",
            on_error=self._load_models,
        )

    def _show_model_provider_menu(self, position):
        item = self.model_provider_list.itemAt(position)
        if item:
            self.model_provider_list.setCurrentItem(item)
        if not self._selected_model_provider():
            return
        menu = QMenu(self)
        add_action = menu.addAction("新增该提供商的模型")
        fetch_action = menu.addAction("从接口获取模型列表")
        chosen = menu.exec(self.model_provider_list.mapToGlobal(position))
        if chosen == add_action:
            self._add_model()
        elif chosen == fetch_action:
            self._fetch_remote_models()

    def _show_model_menu(self, position):
        item = self.model_table.itemAt(position)
        if item:
            self.model_table.setCurrentItem(item)
        menu = QMenu(self)
        fetch_action = menu.addAction("从接口选择模型名")
        delete_action = menu.addAction("删除模型")
        chosen = menu.exec(self.model_table.viewport().mapToGlobal(position))
        if chosen == fetch_action:
            self._fetch_remote_models(update_current=True)
        elif chosen == delete_action:
            self._delete_current_model()

    def _fetch_remote_models(self, update_current=False):
        provider = self._selected_model_provider()
        if not provider:
            QMessageBox.information(self, "获取模型", "请先选择提供商")
            return
        self._start_load(
            f"remote_models_{provider['id']}",
            lambda: self.api.get_remote_models(provider["id"]),
            lambda data: self._choose_remote_model(data, update_current),
            "获取远端模型失败",
        )

    def _choose_remote_model(self, data, update_current):
        names = data.get("models") or []
        if not names:
            QMessageBox.information(self, "获取模型", "接口没有返回可用模型")
            return
        name, ok = QInputDialog.getItem(
            self, "选择模型", "模型名（也可取消后手工填写）:", names, 0, False
        )
        if not ok:
            return
        if update_current and self.model_table.currentRow() >= 0:
            self.model_table.item(self.model_table.currentRow(), 1).setText(name)
            return
        provider = self._selected_model_provider()
        dialog = ModelEditDialog(self, provider=provider, initial_model=name)
        if dialog.exec() == QDialog.Accepted:
            try:
                self.api.create_model(dialog.get_data())
                self._load_models()
            except Exception as exc:
                QMessageBox.warning(self, "错误", f"创建失败: {exc}")

    def _delete_current_model(self):
        row = self.model_table.currentRow()
        if row < 0:
            return
        model_id = int(self.model_table.item(row, 0).text())
        reply = QMessageBox.question(
            self, "确认", "确定删除此模型？",
            QMessageBox.Yes | QMessageBox.No, QMessageBox.No
        )
        if reply == QMessageBox.Yes:
            try:
                self.api.delete_model(model_id)
                self._load_models()
            except Exception as exc:
                QMessageBox.warning(self, "错误", f"删除失败: {exc}")

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

    def _load_wallets(self):
        search = self.wallet_search.text().strip()
        self._start_load(
            "wallets",
            lambda: self.api.get_user_wallets(search=search),
            self._apply_wallets,
            "加载钱包失败",
        )

    def _apply_wallets(self, data):
        wallets = data.get("wallets") or []
        self.wallet_table.setRowCount(len(wallets))
        for row, wallet in enumerate(wallets):
            self.wallet_table.setItem(
                row, 0, QTableWidgetItem(str(wallet.get("user_id", "")))
            )
            self.wallet_table.setItem(
                row, 1, QTableWidgetItem(wallet.get("student_id", ""))
            )
            self.wallet_table.setItem(
                row, 2, QTableWidgetItem(wallet.get("nickname", ""))
            )
            self.wallet_table.setItem(
                row, 3,
                QTableWidgetItem(f"¥{wallet.get('balance_cents', 0) / 100:.2f}"),
            )

            op_widget = QWidget()
            op_layout = QHBoxLayout(op_widget)
            op_layout.setContentsMargins(0, 0, 0, 0)
            btn_recharge = QPushButton("充值")
            btn_recharge.clicked.connect(
                lambda checked, r=row: self._recharge_user(r)
            )
            btn_deduct = QPushButton("扣减")
            btn_deduct.clicked.connect(
                lambda checked, r=row: self._deduct_user(r)
            )
            op_layout.addWidget(btn_recharge)
            op_layout.addWidget(btn_deduct)
            self.wallet_table.setCellWidget(row, 4, op_widget)

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
        self.report_filter = NoWheelComboBox()
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

    def _load_reports(self):
        status = self.report_filter.currentData()
        self._start_load(
            "reports",
            lambda: self.api.get_wrong_reports(status=status),
            self._apply_reports,
            "加载错题报告失败",
        )

    def _apply_reports(self, data):
        reports = data.get("reports") or []
        self.report_table.setRowCount(len(reports))
        for row, report in enumerate(reports):
            self.report_table.setItem(row, 0, QTableWidgetItem(str(report.get("id", ""))))
            self.report_table.setItem(row, 1, QTableWidgetItem(str(report.get("user_id", ""))))
            self.report_table.setItem(row, 2, QTableWidgetItem(
                (report.get("question_hash", "") or "")[:16]))
            self.report_table.setItem(row, 3, QTableWidgetItem(
                (report.get("report_reason", "") or "")[:30]))
            status_text = report.get("status", "pending")
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

    def _load_logs(self):
        search = self.log_search.text().strip()
        self._start_load(
            "logs",
            lambda: self.api.get_usage_logs(search=search),
            self._apply_logs,
            "加载日志失败",
        )

    def _apply_logs(self, data):
        logs = data.get("logs") or []
        self.log_table.setRowCount(len(logs))
        for row, log in enumerate(logs):
            self.log_table.setItem(row, 0, QTableWidgetItem(str(log.get("id", ""))))
            self.log_table.setItem(row, 1, QTableWidgetItem(
                log.get("student_id", str(log.get("user_id", "")))))
            self.log_table.setItem(row, 2, QTableWidgetItem(log.get("model_name", "")))
            self.log_table.setItem(row, 3, QTableWidgetItem(str(log.get("prompt_tokens", 0))))
            self.log_table.setItem(row, 4, QTableWidgetItem(str(log.get("completion_tokens", 0))))
            self.log_table.setItem(row, 5, QTableWidgetItem(
                f"¥{log.get('billed_amount_cents', 0) / 100:.4f}"))
            self.log_table.setItem(row, 6, QTableWidgetItem(
                "✅" if log.get("cache_hit") else "❌"))
            self.log_table.setItem(row, 7, QTableWidgetItem(
                str(log.get("created_at", ""))[:19]))

    # ============ Tab 6: 支付配置 ============
    def _init_pay_tab(self):
        layout = QVBoxLayout(self.tab_pay)

        tip = QLabel(
            "客户端充值会先打开融智云考收银台，再展示付款二维码。"
            "易支付需要填写商户号、商户密钥和网关地址。"
        )
        tip.setWordWrap(True)
        tip.setStyleSheet("color: #666; padding: 6px 2px;")
        layout.addWidget(tip)

        form = QFormLayout()
        form.addRow("支付网关:", QLabel("易支付"))

        self.pay_enabled = QCheckBox("启用在线支付")
        form.addRow("", self.pay_enabled)

        self.pay_app_id = QLineEdit()
        self.pay_app_id.setPlaceholderText("易支付商户号 PID")
        form.addRow("商户号:", self.pay_app_id)

        self.pay_app_secret = QLineEdit()
        self.pay_app_secret.setEchoMode(QLineEdit.Password)
        self.pay_app_secret.setPlaceholderText("留空表示不修改现有密钥")
        form.addRow("商户密钥:", self.pay_app_secret)

        self.pay_api_url = QLineEdit()
        self.pay_api_url.setPlaceholderText("例如 https://pay.example.com")
        form.addRow("网关地址:", self.pay_api_url)

        self.pay_notify_base = QLineEdit()
        self.pay_notify_base.setPlaceholderText("例如 https://sylu.zhouwu.ccwu.cc")
        form.addRow("回调基地址:", self.pay_notify_base)

        self.pay_min_amount = QSpinBox()
        self.pay_min_amount.setRange(1, 1000000)
        self.pay_min_amount.setSuffix(" 分")
        form.addRow("最低充值:", self.pay_min_amount)
        layout.addLayout(form)

        buttons = QHBoxLayout()
        btn_load = QPushButton("刷新配置")
        btn_load.clicked.connect(self._load_pay_config)
        btn_save = QPushButton("保存支付配置")
        btn_save.clicked.connect(self._save_pay_config)
        buttons.addWidget(btn_load)
        buttons.addStretch()
        buttons.addWidget(btn_save)
        layout.addLayout(buttons)
        layout.addStretch()

    def _load_pay_config(self):
        self._start_load(
            "pay_config", self.api.get_pay_config,
            self._apply_pay_config, "加载支付配置失败"
        )

    def _apply_pay_config(self, data):
        self.pay_enabled.setChecked(str(data.get("enabled", "")).lower() == "true")
        self.pay_app_id.setText(data.get("app_id", ""))
        self.pay_api_url.setText(data.get("api_url", ""))
        self.pay_notify_base.setText(data.get("notify_base", ""))
        try:
            self.pay_min_amount.setValue(int(data.get("min_amount") or 100))
        except (TypeError, ValueError):
            self.pay_min_amount.setValue(100)

    def _save_pay_config(self):
        payload = {
            "gateway_type": "epay",
            "enabled": "true" if self.pay_enabled.isChecked() else "false",
            "api_url": self.pay_api_url.text().strip(),
            "notify_base": self.pay_notify_base.text().strip(),
            "min_amount": str(self.pay_min_amount.value()),
        }
        app_id = self.pay_app_id.text().strip()
        if app_id and "*" not in app_id:
            payload["app_id"] = app_id
        if self.pay_app_secret.text().strip():
            payload["app_secret"] = self.pay_app_secret.text().strip()

        try:
            self.api.update_pay_config(payload)
            self.pay_app_secret.clear()
            QMessageBox.information(self, "成功", "支付配置已保存")
            self._load_pay_config()
        except Exception as e:
            QMessageBox.warning(self, "错误", f"保存支付配置失败: {e}")


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
    def __init__(
        self, parent=None, provider=None, remote_loader=None, initial_model=""
    ):
        super().__init__(parent)
        self.setWindowTitle("新增模型")
        self.setMinimumWidth(400)
        self.provider = provider or {}
        self.remote_loader = remote_loader
        self.remote_worker = None
        layout = QFormLayout(self)

        provider_label = self.provider.get("label") or self.provider.get(
            "provider_key", ""
        )
        self.txt_provider = QLineEdit(provider_label)
        self.txt_provider.setReadOnly(True)
        layout.addRow("提供商:", self.txt_provider)

        model_row = QHBoxLayout()
        self.cmb_model = RemoteModelComboBox()
        self.cmb_model.setEditable(True)
        self.cmb_model.setMinimumWidth(220)
        self.cmb_model.setCurrentText(initial_model)
        self.cmb_model.lineEdit().setPlaceholderText(
            "可手工填写，或点击下拉获取"
        )
        self.cmb_model.popup_requested.connect(self._fetch_remote_models)
        btn_fetch = QPushButton("获取")
        btn_fetch.setEnabled(bool(remote_loader))
        btn_fetch.clicked.connect(self._fetch_remote_models)
        model_row.addWidget(self.cmb_model)
        model_row.addWidget(btn_fetch)
        layout.addRow("模型名:", model_row)

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

    def _fetch_remote_models(self):
        if not self.remote_loader:
            return
        if self.remote_worker and self.remote_worker.isRunning():
            return
        self.remote_worker = AdminLoadWorker(self.remote_loader, self)
        self.remote_worker.succeeded.connect(self._apply_remote_models)
        self.remote_worker.failed.connect(
            lambda error: QMessageBox.warning(
                self, "获取模型失败",
                f"{error}\n\n仍可在模型名输入框中手工填写。"
            )
        )
        self.remote_worker.finished.connect(self.remote_worker.deleteLater)
        self.remote_worker.start()

    def _apply_remote_models(self, data):
        names = data.get("models") or []
        current = self.cmb_model.currentText()
        self.cmb_model.clear()
        self.cmb_model.addItems(names)
        if current:
            self.cmb_model.setCurrentText(current)
        if names:
            self.cmb_model.setToolTip(f"已获取 {len(names)} 个模型，点击下拉选择")
        else:
            QMessageBox.information(
                self, "获取模型", "接口未返回模型，仍可手工填写。"
            )

    def get_data(self):
        return {
            "provider_key": self.provider.get("provider_key", ""),
            "model_name": self.cmb_model.currentText().strip(),
            "label": self.txt_label.text().strip(),
            "supports_vision": self.chk_vision.isChecked(),
            "cache_hit_input_price_1m_cents": int(self.spin_cache_hit.value() * 100),
            "live_input_price_1m_cents": int(self.spin_live_in.value() * 100),
            "output_price_1m_cents": int(self.spin_live_out.value() * 100),
            "is_default": self.chk_default.isChecked(),
        }
