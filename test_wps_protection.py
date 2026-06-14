import win32com.client
import os
pdf_path = os.path.abspath("test_protection.pdf")
app = win32com.client.DispatchEx("Word.Application")
app.Visible = False
doc = app.Documents.Open(os.path.abspath("test_protection.docx"))
try:
    doc.SaveAs(pdf_path, 17)
    print("Protected PDF Export Success!")
except Exception as e:
    print("Protected PDF Export Failed:", e)
finally:
    doc.Close(0)
    app.Quit()
