import os
import json

CONFIG_FILE = "config.json"
SERVICE_NAME = "YunKaoDesktop"
HARDCODED_SCHOOL_CODE = "u101441"

# 后端 API 基础地址（生产环境部署后替换为真实域名）
API_BASE_URL = "http://101.42.27.44:8080"

def load_config():
    """加载本地配置文件"""
    if os.path.exists(CONFIG_FILE):
        try:
            with open(CONFIG_FILE, 'r', encoding='utf-8') as f:
                return json.load(f)
        except:
            pass
    return {}

def save_config(data):
    """保存配置到本地文件"""
    with open(CONFIG_FILE, 'w', encoding='utf-8') as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
