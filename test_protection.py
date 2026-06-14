from docx import Document
from docx.oxml import parse_xml

doc = Document()
doc.add_paragraph("This is a protected document.")

settings = doc.settings._element
# A dummy hash and salt. It will lock the document and no password will ever match it (probably).
xml = '<w:documentProtection xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" w:edit="readOnly" w:enforcement="1" w:cryptProviderType="rsaFull" w:cryptAlgorithmClass="hash" w:cryptAlgorithmType="typeAny" w:cryptAlgorithmSid="4" w:cryptSpinCount="100000" w:hash="x/y/z==" w:salt="a/b/c="/>'
settings.append(parse_xml(xml))

doc.save("test_protection.docx")
print("Saved test_protection.docx")
