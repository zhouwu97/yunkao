from PySide6.QtCore import Property, QEasingCurve, QPropertyAnimation, Qt
from PySide6.QtGui import QColor, QPainter, QPen
from PySide6.QtWidgets import QCheckBox, QComboBox


class NoWheelComboBox(QComboBox):
    """Prevents accidental selection changes while scrolling a form."""

    def wheelEvent(self, event):
        if self.view().isVisible():
            super().wheelEvent(event)
            return
        event.ignore()


class ToggleSwitch(QCheckBox):
    """适用于桌面表单的键盘可访问开关。"""

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setCursor(Qt.PointingHandCursor)
        self.setFixedSize(46, 26)
        self._handle_position = 23.0 if self.isChecked() else 3.0

        self._animation = QPropertyAnimation(self, b"handlePosition", self)
        self._animation.setDuration(140)
        self._animation.setEasingCurve(QEasingCurve.OutCubic)
        self.toggled.connect(self._animate_handle)

    def sizeHint(self):
        return self.minimumSizeHint()

    def minimumSizeHint(self):
        return self.size()

    def _animate_handle(self, checked):
        self._animation.stop()
        self._animation.setStartValue(self._handle_position)
        self._animation.setEndValue(23.0 if checked else 3.0)
        self._animation.start()

    def get_handle_position(self):
        return self._handle_position

    def set_handle_position(self, value):
        self._handle_position = float(value)
        self.update()

    handlePosition = Property(
        float,
        get_handle_position,
        set_handle_position,
    )

    def paintEvent(self, event):
        painter = QPainter(self)
        painter.setRenderHint(QPainter.Antialiasing)

        if self.isEnabled():
            track_color = QColor("#2563EB" if self.isChecked() else "#CBD5E1")
            handle_color = QColor("#FFFFFF")
        else:
            track_color = QColor("#E2E8F0")
            handle_color = QColor("#F8FAFC")

        painter.setPen(Qt.NoPen)
        painter.setBrush(track_color)
        painter.drawRoundedRect(0, 2, 46, 22, 11, 11)

        painter.setBrush(handle_color)
        painter.drawEllipse(int(self._handle_position), 4, 18, 18)

        if self.hasFocus():
            painter.setBrush(Qt.NoBrush)
            painter.setPen(QPen(QColor("#93C5FD"), 2))
            painter.drawRoundedRect(1, 1, 44, 24, 12, 12)
