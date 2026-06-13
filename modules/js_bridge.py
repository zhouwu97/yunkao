from PySide6.QtCore import QObject, Slot

class ExtractorBridge(QObject):
    def __init__(self, main_window):
        super().__init__()
        self.main_window = main_window

    @Slot(str)
    def receiveRawHtml(self, html_content):
        self.main_window.process_html_with_bs4(html_content)
