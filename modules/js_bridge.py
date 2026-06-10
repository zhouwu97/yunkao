from PySide6.QtCore import QObject, Slot

class ExtractorBridge(QObject):
    def __init__(self, main_window):
        super().__init__()
        self.main_window = main_window

    @Slot(str)
    def receiveRawHtml(self, html_content):
        self.main_window.log_msg("📥 成功接收网页数据，开始进行极速清洗...")
        self.main_window.process_html_with_bs4(html_content)
