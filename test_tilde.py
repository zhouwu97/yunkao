import win32com.client
import os
import shutil

# Copy the protected docx to a name with ~
shutil.copy("test_protection.docx", "~test_protection.docx")

pdf_path = os.path.abspath("test_tilde.pdf")
app = win32com.client.DispatchEx("Word.Application")
app.Visible = False
app.DisplayAlerts = 0

doc = app.Documents.Open(os.path.abspath("~test_protection.docx"))
try:
    doc.SaveAs(pdf_path, 17)
    print("Export with Tilde Success!")
except Exception as e:
    print("Export with Tilde Failed:", e)
finally:
    doc.Close(0)
    app.Quit()
