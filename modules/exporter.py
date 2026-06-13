import re

def export_to_markdown(questions, file_path):
    with open(file_path, 'w', encoding='utf-8') as f:
        f.write("# 题库导出\n\n")
        for i, q in enumerate(questions, 1):
            f.write(f"### {i}. {q['title']}\n\n")
            for opt in q['options']:
                f.write(f"- {opt}\n")
            
            if q.get('answer'):
                f.write(f"\n**[答案]**\n{q['answer']}\n")
            if q.get('analysis'):
                f.write(f"\n**[解析]**\n{q['analysis']}\n")
                
            f.write("\n---\n\n")

def export_to_txt(questions, file_path):
    with open(file_path, 'w', encoding='utf-8') as f:
        f.write("题库导出\n")
        f.write("="*30 + "\n\n")
        for i, q in enumerate(questions, 1):
            # TXT 去除图片 markdown 标记
            title = re.sub(r'!\[[^\]]*\]\((?:[^)(]+|\([^)(]*\))*\)', '[图片]', q['title'])
            f.write(f"{i}. {title}\n")
            for opt in q['options']:
                opt_txt = re.sub(r'!\[[^\]]*\]\((?:[^)(]+|\([^)(]*\))*\)', '[图片]', opt)
                f.write(f"  {opt_txt}\n")
                
            if q.get('answer'):
                ans_txt = re.sub(r'!\[[^\]]*\]\((?:[^)(]+|\([^)(]*\))*\)', '[图片]', q['answer'])
                f.write(f"\n  [答案]: {ans_txt}\n")
            if q.get('analysis'):
                ana_txt = re.sub(r'!\[[^\]]*\]\((?:[^)(]+|\([^)(]*\))*\)', '[图片]', q['analysis'])
                f.write(f"  [解析]: {ana_txt}\n")
                
            f.write("\n" + "-"*30 + "\n\n")

def export_to_docx(questions, file_path, progress_callback=None):
    try:
        from docx import Document
        from docx.shared import Pt, RGBColor, Inches
    except ImportError:
        raise ImportError("未安装 python-docx 库，无法导出 Word 文档。请运行 pip install python-docx")

    import re
    import requests
    from io import BytesIO

    def add_rich_text_to_paragraph(p, text):
        """将包含 ![img](url) 的富文本添加到段落中，自动下载并嵌入图片"""
        last_end = 0
        for match in re.finditer(r'!\[[^\]]*\]\(((?:[^)(]+|\([^)(]*\))*)\)', text):
            # 添加图片前面的文本
            text_before = text[last_end:match.start()]
            if text_before:
                p.add_run(text_before)
            
            # 尝试下载并嵌入图片
            raw_url = match.group(1)
            align_val = None
            align_unit = None
            url = raw_url
            if "|align:" in raw_url:
                parts = raw_url.split("|align:")
                url = parts[0]
                align_str = parts[1]
                v_match = re.match(r'([-0-9.]+)(px|ex|em|pt)', align_str)
                if v_match:
                    align_val = float(v_match.group(1))
                    align_unit = v_match.group(2)
                    
            def apply_w_position(run_obj, a_val, a_unit):
                if a_val is None: return
                offset_half_pt = 0
                if a_unit == 'px': offset_half_pt = a_val * 1.5
                elif a_unit == 'pt': offset_half_pt = a_val * 2
                elif a_unit == 'ex': offset_half_pt = a_val * 12
                elif a_unit == 'em': offset_half_pt = a_val * 24
                
                if offset_half_pt != 0:
                    from docx.oxml import OxmlElement
                    from docx.oxml.ns import qn
                    rPr = run_obj._element.get_or_add_rPr()
                    pos = OxmlElement('w:position')
                    pos.set(qn('w:val'), str(int(offset_half_pt)))
                    rPr.append(pos)
            try:
                # 补全相对路径和协议头
                if url.startswith("//"):
                    url = "https:" + url
                elif url.startswith("/"):
                    url = "https://www.cctrcloud.net" + url
                elif not url.startswith("http") and not url.startswith("data:"):
                    # 可能是未知格式，暂时忽略
                    pass
                
                if url.startswith("data:image/svg+xml"):
                    import urllib.parse
                    import fitz  # PyMuPDF
                    
                    prefix, data = url.split(',', 1)
                    if ';base64' in prefix:
                        import base64
                        data += '=' * (-len(data) % 4)
                        svg_content = base64.b64decode(data).decode('utf-8')
                    else:
                        svg_content = urllib.parse.unquote(data)
                        
                    try:
                        # 使用 PyMuPDF 将 SVG 渲染为 PNG
                        doc = fitz.open("svg", svg_content.encode('utf-8'))
                        # 获取 SVG 原始定义的物理宽高
                        svg_width_pt = doc[0].rect.width
                        svg_height_pt = doc[0].rect.height
                        
                        # 高清渲染 (300dpi)
                        pix = doc[0].get_pixmap(alpha=True, dpi=300)
                        image_stream = BytesIO(pix.tobytes("png"))
                        run = p.add_run()
                        # 在 Word 中以原始物理宽度插入，确保排版完美且高清
                        run.add_picture(image_stream, width=Pt(svg_width_pt))
                        
                        # 优先使用 HTML style 提取到的对齐信息
                        if align_val is not None:
                            apply_w_position(run, align_val, align_unit)
                        else:
                            # 尝试解析 SVG 的 vertical-align 以精准对齐 baseline
                            try:
                                v_align_match = re.search(r'vertical-align:\s*([-0-9.]+)ex', svg_content)
                                height_match = re.search(r'height="([0-9.]+)ex"', svg_content)
                                if v_align_match and height_match:
                                    v_align_ex = float(v_align_match.group(1))
                                    height_ex = float(height_match.group(1))
                                    if height_ex > 0:
                                        offset_pt = (v_align_ex / height_ex) * svg_height_pt
                                        from docx.oxml import OxmlElement
                                        from docx.oxml.ns import qn
                                        rPr = run._element.get_or_add_rPr()
                                        position = OxmlElement('w:position')
                                        position.set(qn('w:val'), str(int(offset_pt * 2)))
                                        rPr.append(position)
                            except Exception as e_offset:
                                print("Failed to apply vertical-align offset:", e_offset)
                            
                    except Exception as e:
                        print(f"PyMuPDF failed to render SVG: {e}")
                        p.add_run("[图片加载失败]")
                    
                    last_end = match.end()
                    continue

                # 处理普通的 base64 图片
                elif url.startswith("data:image"):
                    import base64
                    header, data = url.split(',', 1)
                    data += '=' * (-len(data) % 4)  # 修复 Incorrect padding
                    image_data = base64.b64decode(data)
                    image_stream = BytesIO(image_data)
                    run = p.add_run()
                    run.add_picture(image_stream)
                    if align_val is not None: apply_w_position(run, align_val, align_unit)
                    last_end = match.end()
                    continue

                response = requests.get(url, timeout=5)
                if response.status_code == 200:
                    from PIL import Image
                    image_data = response.content
                    image_stream = BytesIO(image_data)
                    
                    # 检查图片宽度，如果超过文档宽度则进行限制
                    try:
                        img = Image.open(BytesIO(image_data))
                        width, height = img.size
                        # 假设 96 dpi，如果宽度大于 500 像素，则限制为 5 英寸
                        run = p.add_run()
                        if width > 500:
                            run.add_picture(image_stream, width=Inches(5.0))
                        else:
                            run.add_picture(image_stream)
                        if align_val is not None: apply_w_position(run, align_val, align_unit)
                    except Exception:
                        run = p.add_run()
                        run.add_picture(image_stream)
                        if align_val is not None: apply_w_position(run, align_val, align_unit)
                else:
                    p.add_run("[图片加载失败]")
            except Exception as e:
                print(f"Failed to load image: {url}, Error: {e}")
                p.add_run("[图片加载失败]")
                
            last_end = match.end()
            
        # 添加最后剩余的文本
        text_after = text[last_end:]
        if text_after:
            p.add_run(text_after)

    doc = Document()
    
    # 设置标题
    heading = doc.add_heading('融智云考题库导出', 0)
    heading.alignment = 1 # 居中
    
    total = len(questions)
    for i, q in enumerate(questions, 1):
        if progress_callback:
            progress_callback(i, total, f"正在处理第 {i}/{total} 题，渲染图片公式中...")
        # 题目
        p = doc.add_paragraph()
        p.add_run(f"{i}. ").bold = True
        add_rich_text_to_paragraph(p, q['title'])
        # 让整段题目加粗
        for run in p.runs:
            run.bold = True
        
        # 选项
        for opt in q['options']:
            p_opt = doc.add_paragraph(style='List Bullet')
            add_rich_text_to_paragraph(p_opt, opt)
            
        # 答案
        if q.get('answer'):
            p_ans = doc.add_paragraph()
            run = p_ans.add_run("[答案]: ")
            run.bold = True
            run.font.color.rgb = RGBColor(0, 112, 192) # 蓝色
            add_rich_text_to_paragraph(p_ans, q['answer'])
            
        # 解析
        if q.get('analysis'):
            p_ana = doc.add_paragraph()
            run = p_ana.add_run("[解析]: ")
            run.bold = True
            run.font.color.rgb = RGBColor(237, 125, 49) # 橙色
            add_rich_text_to_paragraph(p_ana, q['analysis'])
            
        doc.add_paragraph("-" * 40)
        
    # 添加全页水印
    from docx.oxml import parse_xml
    for section in doc.sections:
        header = section.header
        p_watermark = header.paragraphs[0] if header.paragraphs else header.add_paragraph()
        run_watermark = p_watermark.add_run()
        xml = f'''<w:pict xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:v="urn:schemas-microsoft-com:vml">
            <v:shape id="WordWaterMark" style="position:absolute;left:0;margin-left:0;margin-top:0;width:500pt;height:200pt;rotation:315;z-index:-251658240;mso-position-horizontal:center;mso-position-horizontal-relative:margin;mso-position-vertical:center;mso-position-vertical-relative:margin" fillcolor="#D0D0D0" stroked="f">
                <v:fill opacity="0.2"/>
                <v:textpath style="font-family:'SimHei';font-size:60pt" string="融智云考题库"/>
            </v:shape>
        </w:pict>'''
        run_watermark._element.append(parse_xml(xml))
        
    doc.save(file_path)
