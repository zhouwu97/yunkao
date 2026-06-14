import fitz
import os

target_pdf = os.path.abspath("test_slash.pdf")

# Create a blank pdf
doc = fitz.open()
doc.new_page()
doc.save(target_pdf)
doc.close()

# Open it
pdf_doc = fitz.open(target_pdf)
pdf_doc[0].insert_text(fitz.Point(100, 100), "Hello")

# Use a slightly different path string (e.g. forward slashes)
target_pdf_diff = target_pdf.replace('\\', '/')
print(f"Original path: {target_pdf}")
print(f"Different path: {target_pdf_diff}")

try:
    pdf_doc.save(target_pdf_diff, incremental=True, encryption=fitz.PDF_ENCRYPT_KEEP)
    print("Save succeeded!")
except Exception as e:
    print("Save failed:", e)

pdf_doc.close()

print(f"File exists after? {os.path.exists(target_pdf)}")
