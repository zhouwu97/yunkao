import json
import re

import requests
from config.settings import API_BASE_URL


PLACEHOLDER_ANSWERS = {"", "略", "暂无", "未知", "未提供", "无"}

PROVIDER_PRESETS = {
    "openai": {
        "label": "OpenAI / GPT",
        "base_url": "https://api.openai.com/v1",
        "model": "gpt-4o-mini",
        "supports_images": True,
        "auth_header": "Authorization",
        "auth_prefix": "Bearer ",
    },
    "deepseek": {
        "label": "DeepSeek",
        "base_url": "https://api.deepseek.com",
        "model": "deepseek-v4-flash",
        "supports_images": False,
        "auth_header": "Authorization",
        "auth_prefix": "Bearer ",
    },
    "kimi": {
        "label": "Kimi / Moonshot",
        "base_url": "https://api.moonshot.cn/v1",
        "model": "kimi-k2.6",
        "supports_images": True,
        "auth_header": "Authorization",
        "auth_prefix": "Bearer ",
    },
    "qwen": {
        "label": "千问 / Qwen",
        "base_url": "https://dashscope.aliyuncs.com/compatible-mode/v1",
        "model": "qwen-vl-plus",
        "supports_images": True,
        "auth_header": "Authorization",
        "auth_prefix": "Bearer ",
    },
    "glm": {
        "label": "智谱 / GLM",
        "base_url": "https://open.bigmodel.cn/api/paas/v4",
        "model": "glm-5.1",
        "supports_images": True,
        "auth_header": "Authorization",
        "auth_prefix": "Bearer ",
    },
    "mimo": {
        "label": "小米 MiMo",
        "base_url": "https://api.xiaomimimo.com/v1",
        "model": "mimo-v2.5-pro",
        "supports_images": True,
        "auth_header": "api-key",
        "auth_prefix": "",
    },
    "custom": {
        "label": "自定义兼容接口",
        "base_url": "",
        "model": "",
        "supports_images": False,
        "auth_header": "Authorization",
        "auth_prefix": "Bearer ",
    },
}


def is_placeholder_answer(answer_text):
    text = (answer_text or "").strip()
    if not text:
        return True

    normalized = re.sub(r"\s+", "", text)
    if normalized in PLACEHOLDER_ANSWERS:
        return True

    placeholder_tokens = ("略", "待补充", "未填写", "暂无答案")
    return any(token in normalized for token in placeholder_tokens)


def should_use_ai(question, config):
    if not config.get("ai_auto_fill_missing_answers", False):
        return False
    if not is_placeholder_answer(question.get("answer")):
        return False
    ai_mode = config.get("ai_mode", "custom")
    if ai_mode == "official":
        # 官方接口不需要检查本地 api_key，直接可调
        return True
    if not config.get("ai_api_key", "").strip():
        return False
    if not config.get("ai_model", "").strip():
        return False
    return True


def get_provider_preset(provider_name):
    return PROVIDER_PRESETS.get(provider_name, PROVIDER_PRESETS["custom"]).copy()


def _build_prompt(question):
    question_type = question.get("question_type", "")
    options = question.get("options", [])
    options_text = "\n".join(options) if options else "无选项"
    analysis = question.get("analysis", "")

    return f"""你是一个严格的考试题答案补全助手。请根据题目内容给出尽量准确的答案。

要求：
1. 只返回 JSON，不要返回 Markdown。
2. 如果把握不足，confidence 降低，不要编造已知事实来源。
3. 单选题 answer 只返回 A/B/C/D 这类选项。
4. 多选题 answer 返回如 ABD。
5. 判断题 answer 返回 对 或 错。
6. 填空题 answer 返回字符串，多个空用中文分号分隔。
7. 简答/计算/名词解释题 answer 返回简洁标准答案。

题型：{question_type}
题干：{question.get("title", "")}
选项：
{options_text}
现有解析：
{analysis or "无"}

返回格式：
{{
  "answer": "答案文本",
  "analysis": "简短解析",
  "confidence": 0.0
}}"""


def _extract_json(content):
    text = (content or "").strip()
    if not text:
        raise ValueError("AI 返回为空")

    fenced = re.search(r"```(?:json)?\s*(\{.*\})\s*```", text, re.S)
    if fenced:
        text = fenced.group(1).strip()
    else:
        start = text.find("{")
        end = text.rfind("}")
        if start != -1 and end != -1 and end > start:
            text = text[start:end + 1]

    return json.loads(text)


def _normalize_usage(data):
    usage = data or {}
    return {
        "prompt_tokens": int(usage.get("prompt_tokens", 0) or 0),
        "completion_tokens": int(usage.get("completion_tokens", 0) or 0),
        "total_tokens": int(usage.get("total_tokens", 0) or 0),
        "cache_reference_tokens": int(usage.get("cache_reference_tokens", 0) or 0),
    }


def _normalize_billing(data):
    billing = data or {}
    return {
        "billed_amount_cents": int(billing.get("billed_amount_cents", 0) or 0),
        "reserved_amount_cents": int(billing.get("reserved_amount_cents", 0) or 0),
        "balance_after_cents": int(billing.get("balance_after_cents", 0) or 0),
    }


def _extract_image_urls(question):
    image_urls = []
    seen = set()
    texts = [
        question.get("title", ""),
        *(question.get("options", []) or []),
        question.get("analysis", ""),
    ]
    for text in texts:
        for match in re.finditer(r'!\[[^\]]*\]<([^>|]+)', text or ""):
            url = match.group(1).strip()
            if url and url not in seen:
                seen.add(url)
                image_urls.append(url)
    return image_urls


def _build_direct_request_payload(question, config):
    prompt = _build_prompt(question)
    supports_images = bool(config.get("ai_supports_images", False))
    image_urls = _extract_image_urls(question)

    if supports_images and image_urls:
        content = [{"type": "text", "text": prompt}]
        for url in image_urls:
            content.append({"type": "image_url", "image_url": {"url": url}})
        return {
            "model": config.get("ai_model", "").strip(),
            "temperature": 0.2,
            "messages": [{"role": "user", "content": content}],
        }

    return {
        "model": config.get("ai_model", "").strip(),
        "temperature": 0.2,
        "messages": [
            {
                "role": "system",
                "content": "你只输出 JSON。你的目标是补全题目答案，并给出谨慎的置信度。"
            },
            {
                "role": "user",
                "content": prompt
            }
        ]
    }


def _call_official_service(question, config, jwt_token=None):
    if not jwt_token:
        raise ValueError("当前登录态缺失，无法调用官方接口")

    model_id = config.get("official_model_id", 0)
    supports_images = bool(config.get("official_supports_images", True))
    image_urls = _extract_image_urls(question)

    # 构建 export_job_id（基于时间戳）
    import time
    export_job_id = f"export_{int(time.time() * 1000)}"

    response = requests.post(
        f"{API_BASE_URL}/api/yunkao/solve",
        headers={
            "Authorization": f"Bearer {jwt_token}",
            "Content-Type": "application/json",
        },
        json={
            "question_type": question.get("question_type", ""),
            "raw_content": question,
            "content_text": "\n".join(
                [question.get("title", "")] + list(question.get("options", []) or [])
            ),
            "model_id": model_id,
            "export_job_id": export_job_id,
            "has_image": supports_images and bool(image_urls),
        },
        timeout=45,
    )
    response.raise_for_status()
    data = response.json()
    answer = str(data.get("answer", "")).strip()
    source = str(data.get("source", "ai")).strip() or "ai"
    confidence = 1.0 if source == "cache" else 0.85
    usage = _normalize_usage(data.get("usage"))
    billing = _normalize_billing(data.get("billing"))
    model_info = data.get("model", {})
    return {
        "answer": answer,
        "analysis": "",
        "confidence": confidence,
        "source": source,
        "usage": usage,
        "billing": billing,
        "model": model_info,
        "raw": data,
    }


def _call_legacy_official_service(question, config, jwt_token=None):
    """旧版 /api/v1/question/solve 接口（保留兼容）"""
    if not jwt_token:
        raise ValueError("当前登录态缺失，无法调用官方接口")

    response = requests.post(
        f"{API_BASE_URL}/api/v1/question/solve",
        headers={
            "Authorization": f"Bearer {jwt_token}",
            "Content-Type": "application/json",
        },
        json={
            "question_type": question.get("question_type", ""),
            "raw_content": question,
            "content_text": "\n".join(
                [question.get("title", "")] + list(question.get("options", []) or [])
            ),
        },
        timeout=45,
    )
    response.raise_for_status()
    data = response.json()
    answer = str(data.get("answer", "")).strip()
    source = str(data.get("source", "ai")).strip() or "ai"
    confidence = 1.0 if source == "cache" else 0.85
    usage = _normalize_usage(data.get("usage"))
    billing = _normalize_billing(data.get("billing"))
    return {
        "answer": answer,
        "analysis": "",
        "confidence": confidence,
        "source": source,
        "usage": usage,
        "billing": billing,
        "model": {},
        "raw": data,
    }


def infer_answer_with_ai(question, config, jwt_token=None):
    ai_mode = config.get("ai_mode", "custom")
    if ai_mode == "official":
        # 使用新的融智云考助手独立接口（支持多模型三段式计费）
        return _call_official_service(question, config, jwt_token=jwt_token)

    base_url = config.get("ai_base_url", "https://api.openai.com/v1").rstrip("/")
    api_key = config.get("ai_api_key", "").strip()
    model = config.get("ai_model", "").strip()
    provider = config.get("ai_provider", "openai")
    preset = get_provider_preset(provider)

    if not api_key or not model or not base_url:
        raise ValueError("AI 配置不完整")

    payload = _build_direct_request_payload(question, config)
    auth_header = preset.get("auth_header", "Authorization")
    auth_prefix = preset.get("auth_prefix", "Bearer ")
    headers = {
        "Content-Type": "application/json",
        auth_header: f"{auth_prefix}{api_key}",
    }

    response = requests.post(
        f"{base_url}/chat/completions",
        headers=headers,
        json=payload,
        timeout=45,
    )
    response.raise_for_status()
    data = response.json()
    content = data["choices"][0]["message"]["content"]
    result = _extract_json(content)
    usage = _normalize_usage(data.get("usage"))

    answer = str(result.get("answer", "")).strip()
    analysis = str(result.get("analysis", "")).strip()

    try:
        confidence = float(result.get("confidence", 0))
    except Exception:
        confidence = 0.0
    confidence = max(0.0, min(1.0, confidence))

    return {
        "answer": answer,
        "analysis": analysis,
        "confidence": confidence,
        "source": "direct",
        "usage": usage,
        "billing": {
            "billed_amount_cents": 0,
            "reserved_amount_cents": 0,
            "balance_after_cents": 0,
        },
        "raw": result,
    }


def report_wrong_answer(question_hash, usage_log_id, export_job_id, question_snapshot,
                        current_answer, report_reason, jwt_token):
    """向服务端报告错题"""
    response = requests.post(
        f"{API_BASE_URL}/api/yunkao/report-wrong",
        headers={
            "Authorization": f"Bearer {jwt_token}",
            "Content-Type": "application/json",
        },
        json={
            "question_hash": question_hash,
            "usage_log_id": usage_log_id,
            "export_job_id": export_job_id,
            "question_snapshot": question_snapshot,
            "current_answer": current_answer,
            "report_reason": report_reason,
        },
        timeout=10,
    )
    response.raise_for_status()
    return response.json()
