import os
import json

CONFIG_FILE = "config.json"
SERVICE_NAME = "YunKaoDesktop"
HARDCODED_SCHOOL_CODE = "u101441"
CONFIG_VERSION = 1

# 后端 API 基础地址，可用环境变量覆盖，避免在多环境构建里改源码。
API_BASE_URL = os.environ.get("YUNKAO_API_BASE_URL", "https://sylu.zhouwu.ccwu.cc").rstrip("/")

DEFAULT_CONFIG = {
    "extract_answer": False,
    "export_prefix": "基础题库导出",
    "pdf_export_engine": "chromium",
    "auto_open_after_export": True,
    "ai_auto_fill_missing_answers": False,
    "ai_mode": "custom",
    "ai_provider": "openai",
    "ai_base_url": "https://api.openai.com/v1",
    "ai_api_key": "",
    "ai_model": "gpt-4o-mini",
    "ai_supports_images": True,
    # 融智云考助手独立配置
    "official_model_id": 0,
    "official_supports_images": True,
    "config_version": CONFIG_VERSION,
}

def load_config():
    """加载本地配置文件，带 Fallback 容灾机制与版本迁移"""
    cfg = DEFAULT_CONFIG.copy()
    file_existed = os.path.exists(CONFIG_FILE)
    migrated = False
    loaded_version = 0

    if file_existed:
        try:
            with open(CONFIG_FILE, 'r', encoding='utf-8') as f:
                data = json.load(f)
                if isinstance(data, dict):
                    loaded_version = data.get("config_version", 0)
                    cfg.update(data)
        except Exception:
            pass  # 解析失败或损坏，走下方兜底重新写入

    # 版本迁移：旧配置无 config_version 字段，首次升级强制切换为极速内核
    if loaded_version < CONFIG_VERSION:
        if file_existed and loaded_version < 1:
            # 旧配置首次升级：强制使用极速内核 (Chromium)
            # 之后用户手动选择经典内核时保留选择，不重复覆盖
            cfg["pdf_export_engine"] = "chromium"
        cfg["config_version"] = CONFIG_VERSION
        migrated = True

    # 文件不存在或发生迁移/损坏，写入配置
    if not file_existed or migrated:
        save_config(cfg)

    return cfg

def save_config(data):
    """保存配置到本地文件"""
    with open(CONFIG_FILE, 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
