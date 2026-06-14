import os

target_pdf = os.path.abspath("test_saveas_missing.pdf")

# Create a dummy pdf using WPS
import win32com.client
from docx import Document

doc = Document()
doc.add_paragraph("Test missing file")
doc.save("test_saveas_missing.docx")

app = win32com.client.DispatchEx("Word.Application")
app.Visible = False
app.DisplayAlerts = 0

word_doc = app.Documents.Open(os.path.abspath("test_saveas_missing.docx"))
word_doc.SaveAs(target_pdf, 17)
word_doc.Close(0)
app.Quit()

import fitz
pdf_doc = fitz.open(target_pdf)
page = pdf_doc[0]
page.insert_text(fitz.Point(100, 100), "Hello")
# Save with incremental=True
pdf_doc.save(target_pdf, incremental=True, encryption=fitz.PDF_ENCRYPT_KEEP)
pdf_doc.close()

print(f"File exists after fitz save? {os.path.exists(target_pdf)}")
