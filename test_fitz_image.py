import fitz
import os
from PIL import Image, ImageDraw, ImageFont
import io

# 1. Create watermark image using PIL
img = Image.new('RGBA', (800, 800), (255, 255, 255, 0))
d = ImageDraw.Draw(img)
# Load font
try:
    font = ImageFont.truetype("C:\\Windows\\Fonts\\msyh.ttc", 60) # Microsoft YaHei
except:
    font = ImageFont.load_default()

# Get text bounding box to center it
text = "融智云考题库"
bbox = d.textbbox((0, 0), text, font=font)
w = bbox[2] - bbox[0]
h = bbox[3] - bbox[1]
x = (800 - w) / 2
y = (800 - h) / 2

# Draw text
d.text((x, y), text, fill=(200, 200, 200, 80), font=font) # 80 alpha is about 30% opacity
# Rotate 45 degrees
img = img.rotate(45, resample=Image.BICUBIC)

# Save to bytes
img_byte_arr = io.BytesIO()
img.save(img_byte_arr, format='PNG')
img_bytes = img_byte_arr.getvalue()

# 2. Add watermark to PDF
pdf_path = "test_no_vml.pdf"
if os.path.exists(pdf_path):
    pdf_doc = fitz.open(pdf_path)
    for page in pdf_doc:
        rect = page.rect
        
        # Calculate multiple positions to tile the watermark or just center it
        watermark_rect = fitz.Rect(
            (rect.width - 400) / 2,
            (rect.height - 400) / 2,
            (rect.width + 400) / 2,
            (rect.height + 400) / 2
        )
        
        # Insert image
        page.insert_image(watermark_rect, stream=img_bytes)
        
    pdf_doc.save("test_fitz_image_watermark.pdf")
    print("PyMuPDF Image Watermark Success!")
else:
    print("test_no_vml.pdf not found!")
