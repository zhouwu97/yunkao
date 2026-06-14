import sys

with open('ui/main_window.py', 'r', encoding='utf-8') as f:
    content = f.read()

# Replace ExportThread
old_export_thread = '''class ExportThread(QThread):
    finished = Signal(bool, str)
    progress = Signal(int, int, str)
    
    def __init__(self, questions, file_path, filter_type):
        super().__init__()
        self.questions = questions
        self.file_path = file_path
        self.filter_type = filter_type
        
    def run(self):
        def report_progress(current, total, message):
            self.progress.emit(current, total, message)
            
        try:
            if self.filter_type.startswith("Word"):
                from modules.exporter import export_to_docx
                export_to_docx(self.questions, self.file_path, progress_callback=report_progress)
            elif self.filter_type.startswith("PDF"):
                from modules.exporter import export_to_pdf
                export_to_pdf(self.questions, self.file_path, progress_callback=report_progress)
            elif self.filter_type.startswith("Markdown"):
                from modules.exporter import export_to_markdown
                export_to_markdown(self.questions, self.file_path)
            else:
                from modules.exporter import export_to_txt
                export_to_txt(self.questions, self.file_path)
            self.finished.emit(True, self.file_path)
        except Exception as e:
            self.finished.emit(False, str(e))'''

new_export_thread = '''class ExportThread(QThread):
    finished = Signal(bool, str)
    progress = Signal(int, int, str)
    html_ready = Signal(str, str) # html_str, file_path
    
    def __init__(self, questions, file_path, filter_type):
        super().__init__()
        self.questions = questions
        self.file_path = file_path
        self.filter_type = filter_type
        
    def run(self):
        def report_progress(current, total, message):
            self.progress.emit(current, total, message)
            
        try:
            if self.filter_type.startswith("Word"):
                from modules.exporter import export_to_docx, preload_images_concurrently
                image_cache = preload_images_concurrently(self.questions, progress_callback=report_progress)
                export_to_docx(self.questions, self.file_path, progress_callback=report_progress, image_cache=image_cache)
                self.finished.emit(True, self.file_path)
            elif self.filter_type.startswith("PDF"):
                from modules.exporter import generate_html, preload_images_concurrently
                image_cache = preload_images_concurrently(self.questions, progress_callback=report_progress)
                report_progress(90, 100, "正在组装排版文档...")
                html_str = generate_html(self.questions, image_cache)
                self.html_ready.emit(html_str, self.file_path)
                # 不发射 finished 信号，退出线程，转交主线程 QWebEnginePage 渲染
            elif self.filter_type.startswith("Markdown"):
                from modules.exporter import export_to_markdown
                export_to_markdown(self.questions, self.file_path)
                self.finished.emit(True, self.file_path)
            else:
                from modules.exporter import export_to_txt
                export_to_txt(self.questions, self.file_path)
                self.finished.emit(True, self.file_path)
        except Exception as e:
            self.finished.emit(False, str(e))

class WatermarkThread(QThread):
    pdf_done = Signal(bool, str)
    
    def __init__(self, file_path):
        super().__init__()
        self.file_path = file_path
        
    def run(self):
        try:
            from modules.exporter import apply_pdf_watermark
            apply_pdf_watermark(self.file_path)
            self.pdf_done.emit(True, self.file_path)
        except Exception as e:
            self.pdf_done.emit(False, str(e))
'''

content = content.replace(old_export_thread, new_export_thread)

# Replace the setup in export_questions
old_export_setup = '''        self.export_thread = ExportThread(self.extracted_questions, file_path, filter)
        self.export_thread.progress.connect(self._on_export_progress)
        self.export_thread.finished.connect(self._on_export_finished)
        self.export_thread.start()'''

new_export_setup = '''        self.export_thread = ExportThread(self.extracted_questions, file_path, filter)
        self.export_thread.progress.connect(self._on_export_progress)
        self.export_thread.finished.connect(self._on_export_finished)
        self.export_thread.html_ready.connect(self._on_html_ready)
        self.export_thread.start()
        
    def _on_html_ready(self, html_str, file_path):
        if hasattr(self, 'progress_dialog'):
            self.progress_dialog.setLabelText("正在渲染 PDF (约 5-15 秒)...")
            self.progress_dialog.setMaximum(0) # 不确定模式
        
        from PySide6.QtWebEngineCore import QWebEnginePage
        self._pdf_page = QWebEnginePage(self.browser.page().profile())
        
        def cleanup_temp_html():
            if hasattr(self, '_temp_html_path') and os.path.exists(self._temp_html_path):
                try:
                    os.remove(self._temp_html_path)
                except:
                    pass

        def on_load_finished(ok):
            if ok:
                self._pdf_page.printToPdf(file_path)
            else:
                self._pdf_page.deleteLater()
                cleanup_temp_html()
                self._on_export_finished(False, "HTML加载失败，无法渲染PDF。")
                
        def on_pdf_finished(path, success):
            cleanup_temp_html()
            if success:
                # 启动水印线程
                if hasattr(self, 'progress_dialog'):
                    self.progress_dialog.setLabelText("正在添加防盗版水印并加密保护...")
                self._watermark_thread = WatermarkThread(file_path)
                self._watermark_thread.pdf_done.connect(self._on_pdf_watermark_done)
                self._watermark_thread.start()
            else:
                self._pdf_page.deleteLater()
                self._on_export_finished(False, "PDF打印失败。")
                
        self._pdf_page.loadFinished.connect(on_load_finished)
        self._pdf_page.pdfPrintingFinished.connect(on_pdf_finished)
        
        # 将大型 HTML 写入临时文件，避免 setHtml 的 2MB IPC 限制
        import tempfile
        import os
        from PySide6.QtCore import QUrl
        temp_fd, temp_path = tempfile.mkstemp(suffix=".html")
        with os.fdopen(temp_fd, 'w', encoding='utf-8') as f:
            f.write(html_str)
        self._temp_html_path = temp_path
        self._pdf_page.load(QUrl.fromLocalFile(temp_path))

    def _on_pdf_watermark_done(self, success, result):
        if hasattr(self, '_pdf_page'):
            self._pdf_page.deleteLater()
            del self._pdf_page
        self._on_export_finished(success, result)'''

content = content.replace(old_export_setup, new_export_setup)

with open('ui/main_window.py', 'w', encoding='utf-8') as f:
    f.write(content)

print("Main window patched")
