import os
import json

APP_NAME = "YunKaoDesktop"
_app_data_root = os.environ.get("APPDATA") or os.path.join(
    os.path.expanduser("~"),
    "AppData",
    "Roaming",
)
CONFIG_FILE = os.environ.get("YUNKAO_CONFIG_FILE") or os.path.join(
    _app_data_root,
    APP_NAME,
    "config.json",
)
LEGACY_CONFIG_FILES = tuple(
    dict.fromkeys(
        [
            os.path.abspath("config.json"),
            os.path.abspath(os.path.join(os.path.dirname(__file__), os.pardir, "config.json")),
        ]
    )
)
SERVICE_NAME = "YunKaoDesktop"
HARDCODED_SCHOOL_CODE = "u101441"
CONFIG_VERSION = 2

DEFAULT_CONFIG = {
    "extract_answer": False,
    "export_prefix": "基础题库导出",
    "pdf_export_engine": "chromium",
    "auto_open_after_export": True,
    "export_without_answers": False,
    "ai_auto_fill_missing_answers": False,
    "ai_mode": "custom",
    "ai_provider": "openai",
    "ai_base_url": "https://api.openai.com/v1",
    "ai_api_key": "",
    "ai_model": "gpt-4o-mini",
    "ai_supports_images": True,
    # 本地云考账号配置
    "yunkao_user": "",
    "yunkao_remember_password": True,
    "config_version": CONFIG_VERSION,
}

def load_config():
    """加载用户级配置，并兼容迁移旧版工作目录配置。"""
    cfg = DEFAULT_CONFIG.copy()
    file_existed = os.path.exists(CONFIG_FILE)
    migrated = False
    loaded_version = 0
    config_valid = False

    if file_existed:
        try:
            with open(CONFIG_FILE, 'r', encoding='utf-8') as f:
                data = json.load(f)
                if isinstance(data, dict):
                    loaded_version = data.get("config_version", 0)
                    cfg.update(data)
                    config_valid = True
        except Exception:
            pass  # 解析失败或损坏，走下方兜底重新写入
    else:
        target_path = os.path.normcase(os.path.abspath(CONFIG_FILE))
        for legacy_file in LEGACY_CONFIG_FILES:
            legacy_path = os.path.normcase(os.path.abspath(legacy_file))
            if legacy_path == target_path or not os.path.isfile(legacy_file):
                continue
            try:
                with open(legacy_file, 'r', encoding='utf-8') as f:
                    data = json.load(f)
                if not isinstance(data, dict):
                    continue
                loaded_version = data.get("config_version", 0)
                cfg.update(data)
                config_valid = True
                migrated = True
                break
            except Exception:
                continue

    # 独立版早期把学号写在 user 字段中，这里统一迁移到 yunkao_user。
    configured_user = str(cfg.get("yunkao_user", "") or "").strip()
    legacy_user = str(cfg.get("user", "") or "").strip()
    if not configured_user and legacy_user and legacy_user != "local_user":
        cfg["yunkao_user"] = legacy_user
        migrated = True

    # 版本迁移：旧配置无 config_version 字段，首次升级强制切换为极速内核
    if loaded_version < CONFIG_VERSION:
        if file_existed and loaded_version < 1:
            # 旧配置首次升级：强制使用极速内核 (Chromium)
            # 之后用户手动选择经典内核时保留选择，不重复覆盖
            cfg["pdf_export_engine"] = "chromium"
        cfg["config_version"] = CONFIG_VERSION
        migrated = True

    # 文件不存在或发生迁移/损坏，写入配置
    if not file_existed or not config_valid or migrated:
        save_config(cfg)

    return cfg

def save_config(data):
    """以原子替换方式保存用户级配置。"""
    config_dir = os.path.dirname(os.path.abspath(CONFIG_FILE))
    os.makedirs(config_dir, exist_ok=True)
    temp_file = f"{CONFIG_FILE}.tmp"
    with open(temp_file, 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
        f.flush()
        os.fsync(f.fileno())
    os.replace(temp_file, CONFIG_FILE)
