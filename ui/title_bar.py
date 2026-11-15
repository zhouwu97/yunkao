"""融智云考无边框主窗口标题栏。"""

from PySide6.QtCore import QPoint, Qt, Signal
from PySide6.QtWidgets import QFrame, QLabel, QHBoxLayout, QPushButton

from config.version import APP_RELEASE
from ui.theme import STATUS_INFO


class TitleBar(QFrame):
    """提供品牌、运行状态和标准窗口操作的轻量标题栏。"""

    minimize_requested = Signal()
    maximize_requested = Signal()
    close_requested = Signal()

    def __init__(self, parent=None):
        super().__init__(parent)
        self._drag_position = QPoint()
        self._dragging = False
        self._was_maximized = False
        self._restore_ratio = 0.5
        self.setObjectName("titleBar")
        self.setFixedHeight(54)

        layout = QHBoxLayout(self)
        layout.setContentsMargins(16, 0, 8, 0)
        layout.setSpacing(10)

        mark = QLabel("Y")
        mark.setObjectName("brandMark")
        mark.setAlignment(Qt.AlignCenter)
        mark.setFixedSize(28, 28)

        brand = QLabel("融智云考")
        brand.setObjectName("brandTitle")

        version = QLabel(f"本地桌面端 · {APP_RELEASE}")
        version.setObjectName("brandVersion")

        self.status = QLabel("●  本地运行 · 凭据受保护")
        self.status.setObjectName("safeStatus")

        self.btn_minimize = self._make_window_button("—", "最小化")
        self.btn_maximize = self._make_window_button("□", "最大化")
        self.btn_close = self._make_window_button("×", "关闭")
        self.btn_close.setObjectName("closeWindowButton")

        layout.addWidget(mark)
        layout.addWidget(brand)
        layout.addWidget(version)
        layout.addStretch(1)
        layout.addWidget(self.status)
        layout.addSpacing(8)
        layout.addWidget(self.btn_minimize)
        layout.addWidget(self.btn_maximize)
        layout.addWidget(self.btn_close)

        self.btn_minimize.clicked.connect(self.minimize_requested)
        self.btn_maximize.clicked.connect(self.maximize_requested)
        self.btn_close.clicked.connect(self.close_requested)

    @staticmethod
    def _make_window_button(text, tooltip):
        button = QPushButton(text)
        button.setObjectName("windowButton")
        button.setToolTip(tooltip)
        button.setCursor(Qt.PointingHandCursor)
        button.setFixedSize(38, 32)
        return button

    def set_status(self, text, color=STATUS_INFO):
        """更新标题栏状态胶囊，颜色由运行状态决定。"""
        self.status.setText(f"●  {text}")
        self.status.setStyleSheet(f"color: {color};")

    def mousePressEvent(self, event):
        if event.button() == Qt.LeftButton:
            window = self.window()
            global_position = event.globalPosition().toPoint()
            self._dragging = True
            self._was_maximized = window.isMaximized()
            if self._was_maximized:
                frame = window.frameGeometry()
                self._restore_ratio = max(
                    0.0,
                    min(
                        1.0,
                        (global_position.x() - frame.left()) / max(frame.width(), 1),
                    ),
                )
                self._drag_position = QPoint()
            else:
                self._drag_position = global_position - window.frameGeometry().topLeft()
            event.accept()
            return
        super().mousePressEvent(event)

    def mouseMoveEvent(self, event):
        if self._dragging and event.buttons() & Qt.LeftButton:
            window = self.window()
            if window.isMaximized():
                window.showNormal()
                self._drag_position = QPoint(
                    int(window.width() * self._restore_ratio),
                    min(20, max(window.height() - 1, 0)),
                )
            window.move(event.globalPosition().toPoint() - self._drag_position)
            self._was_maximized = False
            event.accept()
            return
        super().mouseMoveEvent(event)

    def mouseReleaseEvent(self, event):
        if event.button() == Qt.LeftButton:
            self._dragging = False
        super().mouseReleaseEvent(event)

    def mouseDoubleClickEvent(self, event):
        if event.button() == Qt.LeftButton:
            self._dragging = False
            self.maximize_requested.emit()
            event.accept()
            return
        super().mouseDoubleClickEvent(event)
