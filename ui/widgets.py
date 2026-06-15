from PySide6.QtWidgets import QComboBox


class NoWheelComboBox(QComboBox):
    """Prevents accidental selection changes while scrolling a form."""

    def wheelEvent(self, event):
        if self.view().isVisible():
            super().wheelEvent(event)
            return
        event.ignore()
