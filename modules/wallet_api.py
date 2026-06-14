"""
融智云考助手 - 钱包 API 客户端
"""
import requests
from config.settings import API_BASE_URL


def get_wallet(jwt_token):
    """获取余额和可用模型列表"""
    resp = requests.get(
        f"{API_BASE_URL}/api/yunkao/wallet",
        headers={"Authorization": f"Bearer {jwt_token}"},
        timeout=10
    )
    resp.raise_for_status()
    return resp.json()


def get_wallet_logs(jwt_token, page=1, page_size=20):
    """获取钱包消费记录"""
    resp = requests.get(
        f"{API_BASE_URL}/api/yunkao/wallet/logs",
        headers={"Authorization": f"Bearer {jwt_token}"},
        params={"page": page, "page_size": page_size},
        timeout=10
    )
    resp.raise_for_status()
    return resp.json()


def get_models(jwt_token):
    """获取可用的官方模型列表"""
    resp = requests.get(
        f"{API_BASE_URL}/api/yunkao/models",
        headers={"Authorization": f"Bearer {jwt_token}"},
        timeout=10
    )
    resp.raise_for_status()
    return resp.json()
