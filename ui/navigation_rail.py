"""主窗口左侧导航栏。"""

from PySide6.QtCore import Qt, Signal
from PySide6.QtWidgets import QButtonGroup, QFrame, QVBoxLayout, QPushButton


class NavigationRail(QFrame):
    """提供工作台、记录、导出、诊断和设置入口。"""

    page_requested = Signal(str)

    _ITEMS = (
        ("workspace", "▦", "练习工作台"),
        ("history", "◷", "提取记录"),
        ("exports", "⇩", "导出中心"),
        ("diagnostics", "▥", "运行诊断"),
    )

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setObjectName("navigationRail")
        self.setFixedWidth(70)

        layout = QVBoxLayout(self)
        layout.setContentsMargins(8, 10, 8, 10)
        layout.setSpacing(6)

        self.button_group = QButtonGroup(self)
        self.button_group.setExclusive(True)
        self.buttons = {}

        for index, (page_id, glyph, label) in enumerate(self._ITEMS):
            button = self._make_button(page_id, glyph, label)
            self.button_group.addButton(button, index)
            self.buttons[page_id] = button
            layout.addWidget(button)

        layout.addStretch(1)

        self.btn_dock = self._make_button("collapse", "⌃", "折叠控制台")
        self.btn_settings = self._make_button("settings", "⚙", "设置")
        layout.addWidget(self.btn_dock)
        layout.addWidget(self.btn_settings)

        self.buttons["workspace"].setChecked(True)
        self.button_group.idClicked.connect(self._on_page_clicked)
        self.btn_settings.clicked.connect(lambda: self.page_requested.emit("settings"))

    def _make_button(self, page_id, glyph, label):
        button = QPushButton(glyph)
        button.setObjectName("railButton")
        button.setProperty("pageId", page_id)
        button.setToolTip(label)
        button.setAccessibleName(label)
        button.setCheckable(page_id not in {"dock", "settings"})
        button.setCursor(Qt.PointingHandCursor)
        button.setFixedHeight(44)
        return button

    def _on_page_clicked(self, button_id):
        button = self.button_group.button(button_id)
        if button is not None:
            self.page_requested.emit(button.property("pageId"))
