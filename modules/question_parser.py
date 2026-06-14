import re

from bs4 import BeautifulSoup


PLACEHOLDER_ANSWERS = {"", "略", "暂无", "未知", "未提供", "无"}


def is_placeholder_answer(answer_text):
    text = (answer_text or "").strip()
    if not text:
        return True
    normalized = re.sub(r"\s+", "", text)
    if normalized in PLACEHOLDER_ANSWERS:
        return True
    for token in ("略", "待补充", "未填写", "暂无答案"):
        if token in normalized:
            return True
    return False


def extract_rich_text(element):
    if not element:
        return ""

    text = ""
    block_tags = {"p", "div", "li", "h1", "h2", "h3", "h4", "h5", "h6"}

    for child in element.contents:
        if isinstance(child, str):
            text += child
        elif child.name == "br":
            text += "\n"
        elif child.name == "img":
            src = child.get("src", "")
            if not src:
                continue

            style = child.get("style", "")
            v_align_match = re.search(r"vertical-align:\s*([-0-9.]+)(px|ex|em|pt)", style)
            w_match = re.search(r"width:\s*([-0-9.]+)(px|ex|em|pt|%)", style)
            h_match = re.search(r"height:\s*([-0-9.]+)(px|ex|em|pt|%)", style)

            w_val, w_unit, h_val, h_unit = None, None, None, None
            if w_match:
                w_val, w_unit = w_match.groups()
            else:
                w_attr = child.get("width")
                if w_attr and re.match(r"^[-0-9.]+$", str(w_attr)):
                    w_val, w_unit = w_attr, "px"
                elif w_attr:
                    match = re.match(r"^([-0-9.]+)(px|ex|em|pt|%)$", str(w_attr))
                    if match:
                        w_val, w_unit = match.groups()

            if h_match:
                h_val, h_unit = h_match.groups()
            else:
                h_attr = child.get("height")
                if h_attr and re.match(r"^[-0-9.]+$", str(h_attr)):
                    h_val, h_unit = h_attr, "px"
                elif h_attr:
                    match = re.match(r"^([-0-9.]+)(px|ex|em|pt|%)$", str(h_attr))
                    if match:
                        h_val, h_unit = match.groups()

            align_str = f"|align:{v_align_match.group(1)}{v_align_match.group(2)}" if v_align_match else ""
            w_str = f"|w:{w_val}{w_unit}" if w_val else ""
            h_str = f"|h:{h_val}{h_unit}" if h_val else ""
            text += f"![img]<{src}{align_str}{w_str}{h_str}>"
        elif child.name == "table":
            if text and not text.endswith("\n"):
                text += "\n"
            for row in child.find_all("tr"):
                row_data = []
                for cell in row.find_all(["td", "th"]):
                    cell_text = extract_rich_text(cell).strip().replace("\n", " ")
                    row_data.append(cell_text)
                if row_data:
                    text += " \t".join(row_data) + "\n"
            text += "\n"
        elif hasattr(child, "contents"):
            if child.name in block_tags and text and not text.endswith("\n"):
                text += "\n"
            text += extract_rich_text(child)
            if child.name in block_tags and not text.endswith("\n"):
                text += "\n"

    return text


def _cleanup_target_math(target, soup):
    for preview in target.select(".MathJax_Preview"):
        preview.decompose()

    for mjx in target.select(".MathJax, mjx-container, .katex"):
        tex_script = mjx.find_previous_sibling("script", type=lambda t: t and t.startswith("math/tex"))
        if not tex_script and mjx.parent:
            tex_script = mjx.parent.select_one('script[type^="math/tex"]')
        if tex_script:
            tex_text = tex_script.get_text()
            mjx.insert_before(soup.new_string(f" {tex_text} "))
            tex_script.decompose()
        mjx.decompose()


def _extract_page_info(soup):
    current_node = soup.select_one(".swiper-pagination-current")
    total_node = soup.select_one("#swiper-total")
    if current_node and total_node:
        return f"{current_node.get_text(strip=True)}/{total_node.get_text(strip=True)}"

    matches = re.findall(r"(\d+)\s*/\s*(\d+)", soup.get_text())
    if matches:
        best_match = max(matches, key=lambda item: int(item[1]))
        return f"{best_match[0]}/{best_match[1]}"

    pagination = soup.select_one(".swiper-pagination-fraction, .swiper-pagination, .page_num")
    if pagination:
        return pagination.get_text(strip=True)
    return ""


def _build_option_records(target):
    option_records = []
    for index, li in enumerate(target.select(".option_content > li, .options > li")):
        auto_label = chr(65 + index)
        label_node = li.select_one(".letterArr, .option-letter, .letter")
        label = auto_label
        if label_node:
            extracted = re.sub(r"[^A-Z]", "", label_node.get_text().upper())
            if extracted:
                label = extracted

        text_node = li.select_one(".txt, .option-text, .text")
        option_text = extract_rich_text(text_node or li).strip("\r\n")
        option_records.append(
            {
                "label": label,
                "text": option_text,
                "is_correct": bool(
                    li.select_one('input[data-isright="1"]')
                    or "is-right" in li.get("class", [])
                    or "correct" in li.get("class", [])
                ),
            }
        )
    return option_records


def _is_judgment_type(question_type):
    return "判断" in (question_type or "")


def _is_fill_type(question_type):
    return "填空" in (question_type or "")


def _is_choice_type(question_type):
    q_type = question_type or ""
    return "单选" in q_type or "多选" in q_type


def _is_subjective_type(question_type):
    q_type = question_type or ""
    return any(token in q_type for token in ("简答", "计算", "名词解释", "论述", "综合"))


def _extract_raw_answer(target, content_div):
    answer_text = ""
    answer_node = target.select_one(".answer-text")
    if answer_node is not None:
        answer_text = extract_rich_text(answer_node).strip("\r\n")

    if not answer_text:
        answer_container = target.select_one(".practice_analysis .answer, .answer")
        if answer_container is not None:
            answer_text = extract_rich_text(answer_container)
            answer_text = re.sub(r"^正确答案[：:]?\s*", "", answer_text).strip("\r\n")

    if content_div is not None and content_div.has_attr("data-answer"):
        data_answer = str(content_div["data-answer"]).strip()
        if data_answer:
            answer_text = data_answer

    return answer_text


def _normalize_judgment_answer(answer_text, option_records):
    normalized = re.sub(r"\s+", "", answer_text or "").upper()
    if not normalized:
        return ""

    positive_label = "A"
    negative_label = "B"
    for record in option_records[:2]:
        option_text = re.sub(r"\s+", "", record.get("text", ""))
        if any(token in option_text for token in ("对", "正确", "是", "√")):
            positive_label = record["label"]
        if any(token in option_text for token in ("错", "错误", "否", "×")):
            negative_label = record["label"]

    if normalized in {"A", "对", "正确", "TRUE", "YES", "√"}:
        return positive_label
    if normalized in {"B", "错", "错误", "FALSE", "NO", "×"}:
        return negative_label
    if normalized.startswith("正确答案"):
        stripped = re.sub(r"^正确答案[：:]?", "", normalized)
        return _normalize_judgment_answer(stripped, option_records)
    return answer_text.strip()


def _normalize_choice_answer(answer_text):
    letters = re.findall(r"[A-F]", (answer_text or "").upper())
    if not letters:
        return answer_text.strip()
    unique_letters = []
    for letter in letters:
        if letter not in unique_letters:
            unique_letters.append(letter)
    return "".join(unique_letters)


def _looks_like_choice_letter(answer_text):
    normalized = re.sub(r"\s+", "", answer_text or "").upper()
    return bool(re.fullmatch(r"[A-F]+", normalized))


def _extract_fill_answer(target, raw_answer):
    answers = []

    for elem in target.select(".fill_option li .txt, .answer-input-result"):
        text = extract_rich_text(elem).strip("\r\n")
        clean_text = re.sub(r"^空\d+[：:]\s*", "", text).strip()
        if clean_text and not is_placeholder_answer(clean_text):
            answers.append(clean_text)

    if answers:
        return "；".join(answers)

    if raw_answer and not is_placeholder_answer(raw_answer) and not _looks_like_choice_letter(raw_answer):
        return raw_answer.strip()

    subjective_node = target.select_one(".subjective-answer, .answer-content, .answer-detail")
    if subjective_node is not None:
        text = extract_rich_text(subjective_node).strip("\r\n")
        if text and not is_placeholder_answer(text) and not _looks_like_choice_letter(text):
            return text

    return ""


def _extract_subjective_answer(target, raw_answer):
    if raw_answer and not is_placeholder_answer(raw_answer) and not _looks_like_choice_letter(raw_answer):
        return raw_answer.strip()

    subjective_node = target.select_one(".subjective-answer, .answer-content, .answer-detail")
    if subjective_node is not None:
        return extract_rich_text(subjective_node).strip("\r\n")
    return ""


def parse_active_question(html_content):
    soup = BeautifulSoup(html_content, "html.parser")

    target = soup.select_one(".swiper-slide-active")
    if not target:
        target = soup.select_one(".practice_slide_content")
    if not target:
        return None

    _cleanup_target_math(target, soup)
    page_info = _extract_page_info(soup)

    content_div = target
    if "practice_slide_content" not in target.get("class", []):
        content_div = target.select_one(".practice_slide_content")

    type_tag = target.select_one(".practice_slide_title .type")
    question_type = type_tag.get_text(strip=True) if type_tag else ""

    title_tag = (
        target.select_one(".practice_slide_title .title")
        or target.select_one(".practice_slide_title .txt")
        or target.select_one(".practice_slide_title")
    )
    title_text = extract_rich_text(title_tag).strip("\r\n") if title_tag else "未知题目"

    question_id = ""
    for node in (content_div, target):
        if not node:
            continue
        question_id = (
            node.get("data-questionid")
            or node.get("data-id")
            or node.get("data-question-id")
            or ""
        )
        if question_id:
            break

    option_records = []
    if _is_choice_type(question_type) or _is_judgment_type(question_type):
        option_records = _build_option_records(target)

    raw_answer = _extract_raw_answer(target, content_div)
    correct_labels = [record["label"] for record in option_records if record["is_correct"]]
    answer_text = ""

    if _is_choice_type(question_type):
        answer_text = "".join(correct_labels) if correct_labels else _normalize_choice_answer(raw_answer)
    elif _is_judgment_type(question_type):
        seed_answer = "".join(correct_labels) if correct_labels else raw_answer
        answer_text = _normalize_judgment_answer(seed_answer, option_records)
    elif _is_fill_type(question_type):
        answer_text = _extract_fill_answer(target, raw_answer)
    elif _is_subjective_type(question_type):
        answer_text = _extract_subjective_answer(target, raw_answer)
    else:
        answer_text = raw_answer.strip()

    analysis_text = ""
    analysis_node = target.select_one(".practice_analysis .analysis-content .desc")
    if not analysis_node:
        analysis_node = target.select_one(".analysis-content .desc")
    if analysis_node is not None:
        analysis_text = extract_rich_text(analysis_node).strip("\r\n")

    options = [f"{record['label']}. {record['text']}" for record in option_records if record["text"]]
    marker = question_id or f"{question_type}|{title_text}|{'|'.join(options[:2])}"

    question = {
        "title": title_text,
        "options": options,
        "question_type": question_type,
        "question_id": question_id,
        "page_info": page_info,
        "marker": marker,
    }
    if answer_text:
        question["answer"] = answer_text
        question["answer_source"] = "dom"
    if analysis_text:
        question["analysis"] = analysis_text

    return question
