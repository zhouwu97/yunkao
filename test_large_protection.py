from docx import Document
from docx.oxml import parse_xml
from docx.oxml.ns import nsdecls

doc = Document()
for i in range(100):
    doc.add_paragraph(f"This is a protected document, paragraph {i}.")

settings = doc.settings._element
prot_xml = f'<w:documentProtection {nsdecls("w")} w:edit="readOnly" w:enforcement="1" w:cryptProviderType="rsaFull" w:cryptAlgorithmClass="hash" w:cryptAlgorithmType="typeAny" w:cryptAlgorithmSid="4" w:cryptSpinCount="100000" w:hash="A1B2C3D4E5F6G7H8I9J0K==" w:salt="xYzA=="/>'
settings.append(parse_xml(prot_xml))

doc.save("test_large_protection.docx")

import win32com.client
import os

pdf_path = os.path.abspath("test_large.pdf")
app = win32com.client.DispatchEx("Word.Application")
app.Visible = False
app.DisplayAlerts = 0

doc = app.Documents.Open(os.path.abspath("test_large_protection.docx"))
try:
    doc.SaveAs(pdf_path, 17)
    print("Large Protected PDF Export Success!")
except Exception as e:
    print("Large Protected PDF Export Failed:", e)
finally:
    doc.Close(0)
    app.Quit()
