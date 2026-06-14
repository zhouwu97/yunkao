import sys
import re

with open(r'e:\AI\yunkao\modules\exporter.py', 'r', encoding='utf-8') as f:
    content = f.read()

# 1. User's edit: export_to_markdown AI answer
old_md_ans = '''            if q.get('answer'):
                f.write(f"\\n**[答案]**\\n{q['answer']}\\n")'''
new_md_ans = '''            if q.get('answer'):
                answer_title = "[答案]"
                if q.get("answer_source") == "ai":
                    confidence = q.get("answer_confidence")
                    if isinstance(confidence, (int, float)):
                        answer_title = f"[AI推测答案 | 置信度 {confidence:.2f}]"
                    else:
                        answer_title = "[AI推测答案]"
                f.write(f"\\n**{answer_title}**\\n{q['answer']}\\n")'''
content = content.replace(old_md_ans, new_md_ans)

# 2. User's edit: export_to_txt AI answer
old_txt_ans = '''            if q.get('answer'):
                ans_txt = re.sub(r'!\\[[^\\]]*\\]\\((?:[^)(]+|\\([^)(]*\\))*\\)', '[图片]', q['answer'])
                f.write(f"\\n  [答案]: {ans_txt}\\n")'''
new_txt_ans = '''            if q.get('answer'):
                ans_txt = re.sub(r'!\\[[^\\]]*\\]\\((?:[^)(]+|\\([^)(]*\\))*\\)', '[图片]', q['answer'])
                answer_title = "[答案]"
                if q.get("answer_source") == "ai":
                    confidence = q.get("answer_confidence")
                    if isinstance(confidence, (int, float)):
                        answer_title = f"[AI推测答案|置信度{confidence:.2f}]"
                    else:
                        answer_title = "[AI推测答案]"
                f.write(f"\\n  {answer_title}: {ans_txt}\\n")'''
content = content.replace(old_txt_ans, new_txt_ans)

# 3. New Anti-Scam Text
content = content.replace(
    'anti_scam_text = "此文件免费导出，若是购买来的，请退款。作者qq2170194804（不是卖家的QQ）"',
    'anti_scam_text = "此文档禁止倒卖,鼓励免费分享，工具作者邮箱wu22402@gmail.com"'
)

# 4. Random Anti-Scam Insertion in export_to_docx
old_mid = '''        # 题库中间添加防诈骗提示
        if i == mid_point and total > 1:
            p_scam_mid = doc.add_paragraph()
            p_scam_mid.alignment = 1
            run_scam_mid = p_scam_mid.add_run(anti_scam_text)
            run_scam_mid.font.color.rgb = RGBColor(255, 0, 0)
            doc.add_paragraph("-" * 40)'''

new_mid = '''        import random
        # 题库中间添加防诈骗提示
        if i == mid_point and total > 1:
            p_scam_mid = doc.add_paragraph()
            p_scam_mid.alignment = 1
            run_scam_mid = p_scam_mid.add_run(anti_scam_text)
            run_scam_mid.font.color.rgb = RGBColor(255, 0, 0)
            doc.add_paragraph("-" * 40)
        # 随机插入黑色提示防倒卖（约15%概率，避免和首尾以及中间冲突）
        elif total > 5 and i != total and random.random() < 0.15:
            p_scam_rand = doc.add_paragraph()
            p_scam_rand.alignment = 1
            run_scam_rand = p_scam_rand.add_run(anti_scam_text)
            run_scam_rand.font.color.rgb = RGBColor(0, 0, 0)
            doc.add_paragraph("-" * 40)'''
content = content.replace(old_mid, new_mid)

# 5. PDF Encryption
old_pdf_save = '''        # 保存并覆盖（不使用 incremental=True，以防产生不可预知的文件锁冲突）
        pdf_doc.save(target_pdf, encryption=fitz.PDF_ENCRYPT_KEEP)'''

new_pdf_save = '''        # 保存并加密（设定固定的所有者密码并限制权限，防止被直接编辑倒卖）
        owner_pw = "yunkao2170194804"
        # 允许打印、复制、高质量打印和辅助功能。禁止：修改、批注、组装、表单填写
        perm = int(fitz.PDF_PERM_PRINT | fitz.PDF_PERM_COPY | fitz.PDF_PERM_ACCESSIBILITY | fitz.PDF_PERM_PRINT_HQ)
        pdf_doc.save(
            target_pdf, 
            encryption=fitz.PDF_ENCRYPT_AES_256,
            owner_pw=owner_pw,
            permissions=perm
        )'''
content = content.replace(old_pdf_save, new_pdf_save)

with open(r'e:\AI\yunkao\modules\exporter.py', 'w', encoding='utf-8') as f:
    f.write(content)

print('Success')
