import win32com.client
import os

docx_path = os.path.abspath("test_watermark2.docx")
pdf_path = os.path.abspath("test_wps.pdf")

try:
    word = win32com.client.Dispatch("Word.Application")
    word.Visible = False
    doc = word.Documents.Open(docx_path)
    
    try:
        # 尝试 WPS 特有的 ExportPdf
        doc.ExportPdf(pdf_path)
        print("ExportPdf success")
    except Exception as e1:
        print("ExportPdf failed:", e1)
        try:
            # 尝试 SaveAs
            doc.SaveAs(pdf_path, 17)
            print("SaveAs 17 success")
        except Exception as e2:
            print("SaveAs failed:", e2)
            try:
                # 尝试 docx2pdf 用到的 ExportAsFixedFormat
                doc.ExportAsFixedFormat(pdf_path, 17)
                print("ExportAsFixedFormat success")
            except Exception as e3:
                print("ExportAsFixedFormat failed:", e3)

    doc.Close(0)
    word.Quit()
except Exception as e:
    print("General error:", e)
