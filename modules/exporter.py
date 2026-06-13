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

def export_to_docx(questions, file_path, progress_callback=None, watermark=True):
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
        for match in re.finditer(r'!\[[^\]]*\]<([^>]+)>', text):
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
    
    # 强制全局使用宋体，避免 WPS 在包含图片的段落中因为渲染路径不同导致字体发粗（假黑体）的 Bug
    from docx.oxml.ns import qn
    style = doc.styles['Normal']
    style.font.name = '宋体'
    style._element.rPr.rFonts.set(qn('w:eastAsia'), '宋体')
    
    # 设置标题
    heading = doc.add_heading('融智云考题库导出', 0)
    heading.alignment = 1 # 居中
    
    # 开头添加防诈骗提示
    anti_scam_text = "此文件免费导出，若是购买来的，请退款。作者qq2170194804（不是卖家的QQ）"
    p_scam_start = doc.add_paragraph()
    p_scam_start.alignment = 1
    run_scam_start = p_scam_start.add_run(anti_scam_text)
    run_scam_start.font.color.rgb = RGBColor(255, 0, 0)
    
    total = len(questions)
    mid_point = total // 2
    for i, q in enumerate(questions, 1):
        if progress_callback:
            progress_callback(i, total, f"正在处理第 {i}/{total} 题，渲染图片公式中...")
        # 题目
        p = doc.add_paragraph()
        p.add_run(f"{i}. ")
        add_rich_text_to_paragraph(p, q['title'])
        # 选项
        for opt in q['options']:
            p_opt = doc.add_paragraph(style='List Bullet')
            add_rich_text_to_paragraph(p_opt, opt)
            
        # 答案
        if q.get('answer'):
            p_ans = doc.add_paragraph()
            run = p_ans.add_run("[答案]: ")
            run.font.color.rgb = RGBColor(0, 112, 192) # 蓝色
            add_rich_text_to_paragraph(p_ans, q['answer'])
            
        # 解析
        if q.get('analysis'):
            p_ana = doc.add_paragraph()
            run = p_ana.add_run("[解析]: ")
            run.font.color.rgb = RGBColor(237, 125, 49) # 橙色
            add_rich_text_to_paragraph(p_ana, q['analysis'])
            
        doc.add_paragraph("-" * 40)
        
        # 题库中间添加防诈骗提示
        if i == mid_point and total > 1:
            p_scam_mid = doc.add_paragraph()
            p_scam_mid.alignment = 1
            run_scam_mid = p_scam_mid.add_run(anti_scam_text)
            run_scam_mid.font.color.rgb = RGBColor(255, 0, 0)
            doc.add_paragraph("-" * 40)

    # 结尾添加防诈骗提示
    p_scam_end = doc.add_paragraph()
    p_scam_end.alignment = 1
    run_scam_end = p_scam_end.add_run(anti_scam_text)
    run_scam_end.font.color.rgb = RGBColor(255, 0, 0)
        
    # 添加全页水印
    from docx.oxml import parse_xml
    from docx.oxml.ns import nsdecls
    if watermark:
        for section in doc.sections:
            header = section.header
            p_watermark = header.paragraphs[0] if header.paragraphs else header.add_paragraph()
            run_watermark = p_watermark.add_run()
            xml = f'''<w:pict xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:v="urn:schemas-microsoft-com:vml" xmlns:o="urn:schemas-microsoft-com:office:office">
                <v:shapetype id="_x0000_t136" coordsize="21600,21600" o:spt="136" adj="10800" path="m@7,l@8,m@5,21600l@6,21600e">
                    <v:path textpathok="t"/>
                    <v:textpath on="t" fitshape="t"/>
                </v:shapetype>
                <v:shape id="WordWaterMark1" type="#_x0000_t136" style="position:absolute;left:0;margin-left:0;margin-top:-100pt;width:600pt;height:150pt;rotation:315;z-index:-251658240;mso-position-horizontal:center;mso-position-horizontal-relative:margin;mso-position-vertical:center;mso-position-vertical-relative:margin" fillcolor="#E0E0E0" stroked="f">
                    <v:textpath on="t" style="font-family:'SimHei';font-size:50pt" string="此题库免费，注意防诈。"/>
                </v:shape>
                <v:shape id="WordWaterMark2" type="#_x0000_t136" style="position:absolute;left:0;margin-left:0;margin-top:100pt;width:600pt;height:100pt;rotation:315;z-index:-251658240;mso-position-horizontal:center;mso-position-horizontal-relative:margin;mso-position-vertical:center;mso-position-vertical-relative:margin" fillcolor="#E0E0E0" stroked="f">
                    <v:textpath on="t" style="font-family:'Arial';font-size:40pt" string="GitHub SYLUlive"/>
                </v:shape>
            </w:pict>'''
            run_watermark._element.append(parse_xml(xml))
        
    # 添加防篡改编辑锁：设置文档为“只读”并混淆加密盐值，永久锁定水印防止被删
    try:
        settings = doc.settings._element
        # 使用随机乱码的 hash 和 salt 导致没有任何密码可以解锁这个文档
        prot_xml = f'<w:documentProtection {nsdecls("w")} w:edit="readOnly" w:enforcement="1" w:cryptProviderType="rsaFull" w:cryptAlgorithmClass="hash" w:cryptAlgorithmType="typeAny" w:cryptAlgorithmSid="4" w:cryptSpinCount="100000" w:hash="A1B2C3D4E5F6G7H8I9J0K==" w:salt="xYzA=="/>'
        settings.append(parse_xml(prot_xml))
    except Exception as lock_err:
        print("Failed to lock document:", lock_err)
        
    doc.save(file_path)

def export_to_pdf(questions, file_path, progress_callback=None):
    import os
    
    # 强制将临时 Word 放在最终导出的同级目录下，防止存放在 Temp 目录触发 WPS/Office 的“受保护的视图”而导致导出失败
    file_dir = os.path.dirname(os.path.abspath(file_path))
    temp_docx = os.path.join(file_dir, f"~yunkao_temp_{os.getpid()}.docx")
    temp_pdf = os.path.join(file_dir, f"~yunkao_temp_{os.getpid()}.pdf")
    
    try:
        export_to_docx(questions, temp_docx, progress_callback, watermark=False)
        if progress_callback:
            progress_callback(len(questions), len(questions), "正在启动 Office 引擎生成 PDF (兼容 WPS)...")
            
        import win32com.client
        import pythoncom
        pythoncom.CoInitialize()
        
        try:
            # 优先尝试 WPS，防止用户电脑上有过期/损坏的微软 Office 导致命令拒绝
            word = None
            for app_name in ["KWPS.Application", "WPS.Application", "Word.Application"]:
                try:
                    word = win32com.client.DispatchEx(app_name)
                    break
                except:
                    continue
            
            if not word:
                raise Exception("未找到可用的 Word 或 WPS 组件")
                
            word.Visible = False
            word.DisplayAlerts = 0
            doc = word.Documents.Open(os.path.abspath(temp_docx))
            try:
                # 导出到临时 PDF，避免 WPS 云同步功能干扰或增量保存导致文件丢失
                if os.path.exists(temp_pdf):
                    os.remove(temp_pdf)
                        
                try:
                    doc.SaveAs(os.path.abspath(temp_pdf), 17) # 17 = wdFormatPDF
                except Exception as save_err:
                    try:
                        # 兼容部分极其特殊的 WPS 版本
                        doc.ExportAsFixedFormat(os.path.abspath(temp_pdf), 17)
                    except:
                        raise save_err
            finally:
                doc.Close(0)
                word.Quit()
        except Exception as e_com:
            raise RuntimeError(f"调用 Office 组件失败，请确保没有打开同名 PDF 文件，且 WPS 处于正常状态: {str(e_com)}")
        finally:
            pythoncom.CoUninitialize()

        # 4. 成功生成 PDF 后，使用 PyMuPDF 添加防盗版水印 (绕过 WPS 渲染 VML 崩溃的 BUG)
        if progress_callback:
            progress_callback(len(questions), len(questions), "正在添加防盗版水印...")
        import fitz
        from PIL import Image, ImageDraw, ImageFont
        import io
        
        img = Image.new('RGBA', (800, 800), (255, 255, 255, 0))
        d = ImageDraw.Draw(img)
        try:
            font = ImageFont.truetype("C:\\Windows\\Fonts\\msyh.ttc", 50)
            font_small = ImageFont.truetype("C:\\Windows\\Fonts\\msyh.ttc", 40)
        except:
            font = ImageFont.load_default()
            font_small = ImageFont.load_default()
            
        text1 = "此题库免费，注意防诈。"
        text2 = "GitHub SYLUlive"
        
        def draw_centered_text(draw, txt, y_offset, fnt):
            try:
                bbox = draw.textbbox((0, 0), txt, font=fnt)
                w = bbox[2] - bbox[0]
                h = bbox[3] - bbox[1]
            except AttributeError:
                w, h = draw.textsize(txt, font=fnt)
            x = (800 - w) / 2
            y = (800 - h) / 2 + y_offset
            draw.text((x, y), txt, fill=(200, 200, 200, 80), font=fnt)
            
        draw_centered_text(d, text1, -40, font)
        draw_centered_text(d, text2, 40, font_small)
        img = img.rotate(45, resample=Image.BICUBIC)
        img_byte_arr = io.BytesIO()
        img.save(img_byte_arr, format='PNG')
        img_bytes = img_byte_arr.getvalue()
        
        # 确保目标文件未被占用
        target_pdf = os.path.abspath(file_path)
        if os.path.exists(target_pdf):
            try:
                os.remove(target_pdf)
            except Exception as rm_err:
                raise RuntimeError(f"无法覆盖已存在的文件，请确保该 PDF 未在其他软件中打开！({str(rm_err)})")

        pdf_doc = fitz.open(temp_pdf)
        for page in pdf_doc:
            rect = page.rect
            watermark_rect = fitz.Rect(
                (rect.width - 500) / 2,
                (rect.height - 500) / 2,
                (rect.width + 500) / 2,
                (rect.height + 500) / 2
            )
            page.insert_image(watermark_rect, stream=img_bytes)
            
        # 保存并覆盖（不使用 incremental=True，以防产生不可预知的文件锁冲突）
        pdf_doc.save(target_pdf, encryption=fitz.PDF_ENCRYPT_KEEP)
        pdf_doc.close()
            
    except Exception as e:
        raise RuntimeError(f"PDF 转换失败: {str(e)}")
    finally:
        if os.path.exists(temp_docx):
            try:
                os.remove(temp_docx)
            except:
                pass
        if os.path.exists(temp_pdf):
            try:
                os.remove(temp_pdf)
            except:
                pass
