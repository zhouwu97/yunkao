import win32com.client
import os

pdf_path = os.path.abspath("test_protection2.pdf")
app = win32com.client.DispatchEx("Word.Application")
app.Visible = False
app.DisplayAlerts = 0

doc = app.Documents.Open(os.path.abspath("test_protection.docx"))
try:
    doc.SaveAs(pdf_path, 17)
    print("Protected PDF Export Success WITH DisplayAlerts=0!")
except Exception as e:
    print("Protected PDF Export Failed WITH DisplayAlerts=0:", e)
finally:
    doc.Close(0)
    app.Quit()
