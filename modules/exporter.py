def export_to_markdown(questions, file_path):
    with open(file_path, 'w', encoding='utf-8') as f:
        f.write("# 基础题库导出\n\n")
        for i, q in enumerate(questions, 1):
            f.write(f"### {i}. {q['title']}\n\n")
            for opt in q['options']:
                f.write(f"- {opt}\n")
            f.write("\n")

def export_to_txt(questions, file_path):
    with open(file_path, 'w', encoding='utf-8') as f:
        f.write("基础题库导出\n")
        f.write("="*30 + "\n\n")
        for i, q in enumerate(questions, 1):
            f.write(f"{i}. {q['title']}\n")
            for opt in q['options']:
                f.write(f"  {opt}\n")
            f.write("\n")
