"""
融智云考助手 - 管理员 API 客户端
"""
import requests
from config.settings import API_BASE_URL


class AdminAPI:
    def __init__(self, jwt_token):
        self.jwt_token = jwt_token
        self.base = f"{API_BASE_URL}/api/yunkao/admin"
        self.headers = {"Authorization": f"Bearer {jwt_token}"}

    def _get(self, path, params=None):
        resp = requests.get(f"{self.base}{path}", headers=self.headers, params=params, timeout=10)
        resp.raise_for_status()
        return resp.json()

    def _post(self, path, json_data=None):
        resp = requests.post(f"{self.base}{path}", headers=self.headers, json=json_data, timeout=10)
        resp.raise_for_status()
        return resp.json()

    def _put(self, path, json_data=None):
        resp = requests.put(f"{self.base}{path}", headers=self.headers, json=json_data, timeout=10)
        resp.raise_for_status()
        return resp.json()

    def _delete(self, path):
        resp = requests.delete(f"{self.base}{path}", headers=self.headers, timeout=10)
        resp.raise_for_status()
        return resp.json()

    # ============ 提供商管理 ============
    def get_providers(self):
        return self._get("/providers")

    def create_provider(self, data):
        return self._post("/providers", data)

    def update_provider(self, provider_id, data):
        return self._put(f"/providers/{provider_id}", data)

    def delete_provider(self, provider_id):
        return self._delete(f"/providers/{provider_id}")

    # ============ 模型管理 ============
    def get_models(self, provider_key=None):
        params = {}
        if provider_key:
            params["provider_key"] = provider_key
        return self._get("/models", params)

    def create_model(self, data):
        return self._post("/models", data)

    def update_model(self, model_id, data):
        return self._put(f"/models/{model_id}", data)

    def delete_model(self, model_id):
        return self._delete(f"/models/{model_id}")

    # ============ 钱包管理 ============
    def get_user_wallets(self, search="", page=1, page_size=20):
        return self._get("/wallets", {"search": search, "page": page, "page_size": page_size})

    def recharge_wallet(self, user_id, amount_cents, remark=""):
        return self._post("/wallet/recharge", {
            "user_id": user_id,
            "amount_cents": amount_cents,
            "remark": remark
        })

    def deduct_wallet(self, user_id, amount_cents, remark=""):
        return self._post("/wallet/deduct", {
            "user_id": user_id,
            "amount_cents": amount_cents,
            "remark": remark
        })

    # ============ 错题审核 ============
    def get_wrong_reports(self, status="pending", page=1, page_size=20):
        return self._get("/reports", {"status": status, "page": page, "page_size": page_size})

    def review_report(self, report_id, action, final_answer=""):
        return self._post(f"/reports/{report_id}/review", {
            "action": action,
            "final_answer": final_answer
        })

    # ============ 使用日志 ============
    def get_usage_logs(self, search="", page=1, page_size=20):
        return self._get("/usage-logs", {"search": search, "page": page, "page_size": page_size})

    # ============ 统计 ============
    def get_stats(self):
        return self._get("/stats")
