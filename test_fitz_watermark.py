import fitz
import os
import win32com.client
from docx import Document

# 1. Create a blank docx and convert it to PDF (without VML)
doc = Document()
doc.add_paragraph("This is a test document without VML.")
doc.save("test_no_vml.docx")

pdf_path = os.path.abspath("test_no_vml.pdf")
app = win32com.client.DispatchEx("Word.Application")
app.Visible = False
app.DisplayAlerts = 0

word_doc = app.Documents.Open(os.path.abspath("test_no_vml.docx"))
try:
    word_doc.SaveAs(pdf_path, 17)
    print("Normal PDF Export Success!")
finally:
    word_doc.Close(0)
    app.Quit()

# 2. Add watermark using PyMuPDF
pdf_doc = fitz.open("test_no_vml.pdf")
for page in pdf_doc:
    rect = page.rect
    # Insert watermark text diagonally
    page.insert_text(
        fitz.Point(50, rect.height - 50),
        "融智云考题库",
        fontname="helv", # We need a CJK font for Chinese
        fontsize=60,
        rotate=45,
        color=(0.8, 0.8, 0.8),
        fill_opacity=0.5
    )
pdf_doc.save("test_watermarked.pdf")
print("PDF Watermark Success!")
