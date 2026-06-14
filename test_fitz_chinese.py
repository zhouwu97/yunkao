import os
import win32com.client
from docx import Document
import fitz

target_pdf = os.path.abspath("融智云考题库测试.pdf")

doc = Document()
doc.add_paragraph("Test chinese missing file")
doc.save("test_chinese.docx")

app = win32com.client.DispatchEx("Word.Application")
app.Visible = False
app.DisplayAlerts = 0
word_doc = app.Documents.Open(os.path.abspath("test_chinese.docx"))
word_doc.SaveAs(target_pdf, 17)
word_doc.Close(0)
app.Quit()

print("File exists before fitz:", os.path.exists(target_pdf))

pdf_doc = fitz.open(target_pdf)
page = pdf_doc[0]
page.insert_text(fitz.Point(100, 100), "Hello")
pdf_doc.save(target_pdf, incremental=True, encryption=fitz.PDF_ENCRYPT_KEEP)
pdf_doc.close()

print("File exists after fitz:", os.path.exists(target_pdf))
