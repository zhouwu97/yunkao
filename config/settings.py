import os
import json

CONFIG_FILE = "config.json"
SERVICE_NAME = "YunKaoDesktop"
HARDCODED_SCHOOL_CODE = "u101441"

# 后端 API 基础地址（生产环境部署后替换为真实域名）
API_BASE_URL = "http://156.233.229.232:8080"

DEFAULT_CONFIG = {
    "extract_answer": False,
    "export_prefix": "基础题库导出",
    "pdf_export_engine": "chromium",
    "ai_auto_fill_missing_answers": False,
    "ai_mode": "custom",
    "ai_provider": "openai",
    "ai_base_url": "https://api.openai.com/v1",
    "ai_api_key": "",
    "ai_model": "gpt-4o-mini",
    "ai_supports_images": True
}

def load_config():
    """加载本地配置文件，带 Fallback 容灾机制"""
    cfg = DEFAULT_CONFIG.copy()
    if os.path.exists(CONFIG_FILE):
        try:
            with open(CONFIG_FILE, 'r', encoding='utf-8') as f:
                data = json.load(f)
                if isinstance(data, dict):
                    cfg.update(data)
                return cfg
        except Exception:
            pass  # 解析失败或损坏，走下方兜底重新写入
            
    # 文件不存在或损坏，写入默认配置
    save_config(cfg)
    return cfg

def save_config(data):
    """保存配置到本地文件"""
    with open(CONFIG_FILE, 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
