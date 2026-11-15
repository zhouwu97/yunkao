"""QWebEngineView 外层浏览器工具栏。"""

from PySide6.QtCore import QUrl, Qt, Signal
from PySide6.QtWidgets import QFrame, QHBoxLayout, QLineEdit, QPushButton, QVBoxLayout
from PySide6.QtWebEngineWidgets import QWebEngineView


class BrowserShell(QFrame):
    """把网页浏览区和轻量工具栏组合成主窗口中间区域。"""

    external_open_requested = Signal()

    def __init__(self, parent=None):
        super().__init__(parent)
        self.setObjectName("browserShell")
        self.setMinimumWidth(420)

        root_layout = QVBoxLayout(self)
        root_layout.setContentsMargins(0, 0, 0, 0)
        root_layout.setSpacing(0)

        toolbar = QFrame()
        toolbar.setObjectName("browserToolbar")
        toolbar_layout = QHBoxLayout(toolbar)
        toolbar_layout.setContentsMargins(10, 6, 10, 6)
        toolbar_layout.setSpacing(6)

        self.btn_back = self._make_button("‹", "返回")
        self.btn_refresh = self._make_button("↻", "刷新")
        self.btn_back.setEnabled(False)
        self.address = QLineEdit()
        self.address.setObjectName("addressBar")
        self.address.setReadOnly(True)
        self.address.setText("www.cctrcloud.net/practice/login.html")
        self.btn_external = self._make_button("↗", "在系统浏览器打开")

        toolbar_layout.addWidget(self.btn_back)
        toolbar_layout.addWidget(self.btn_refresh)
        toolbar_layout.addWidget(self.address, 1)
        toolbar_layout.addWidget(self.btn_external)

        self.browser = QWebEngineView(self)
        self.browser.setObjectName("webView")
        self.browser.setContextMenuPolicy(Qt.DefaultContextMenu)

        self.btn_back.clicked.connect(self.browser.back)
        self.btn_refresh.clicked.connect(self.browser.reload)
        self.btn_external.clicked.connect(self.external_open_requested)
        self.browser.urlChanged.connect(self._sync_address)

        root_layout.addWidget(toolbar)
        root_layout.addWidget(self.browser, 1)

    @staticmethod
    def _make_button(text, tooltip):
        button = QPushButton(text)
        button.setObjectName("browserToolButton")
        button.setToolTip(tooltip)
        button.setAccessibleName(tooltip)
        button.setCursor(Qt.PointingHandCursor)
        button.setFixedSize(30, 30)
        return button

    def _sync_address(self, url):
        self.address.setText(url.toString() or "about:blank")
