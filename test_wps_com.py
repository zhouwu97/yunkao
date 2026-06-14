import win32com.client
import os

pdf_path = os.path.abspath("test_wps2.pdf")

apps = ["KWPS.Application", "WPS.Application", "Word.Application"]

for app_name in apps:
    print(f"\n--- Testing {app_name} ---")
    try:
        app = win32com.client.DispatchEx(app_name)
        print(f"Successfully dispatched {app_name}")
        app.Visible = False
        doc = app.Documents.Open(os.path.abspath("test_watermark2.docx"))
        try:
            doc.SaveAs(pdf_path, 17)
            print("SaveAs(17) succeeded!")
        except Exception as e:
            print("SaveAs(17) failed:", e)
        finally:
            doc.Close(0)
            app.Quit()
    except Exception as e:
        print(f"Failed to dispatch {app_name}:", type(e).__name__)
