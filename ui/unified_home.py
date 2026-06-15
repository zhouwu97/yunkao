import json
import os
import subprocess
import sys
from pathlib import Path

from PySide6.QtCore import QTimer
from PySide6.QtWidgets import (
    QFrame,
    QHBoxLayout,
    QLabel,
    QMessageBox,
    QPushButton,
    QVBoxLayout,
    QWidget,
)

from config.settings import load_config, save_config
from modules.wallet_api import get_wallet
from ui.main_window import YunKaoExtractorApp


def resolve_oneclass_root() -> Path:
    configured = os.environ.get("ONECLASS_ROOT", "").strip()
    if configured:
        return Path(configured)
    sibling = Path(__file__).resolve().parents[2] / "oneclass" / "wechat_word_bot_v2" / "wechat_word_bot"
    if sibling.exists():
        return sibling
    return Path(r"E:\AI\oneclass\wechat_word_bot_v2\wechat_word_bot")


ONECLASS_ROOT = resolve_oneclass_root()


def resolve_bundled_oneclass_exe() -> Path | None:
    if not getattr(sys, "frozen", False):
        return None
    exe_dir = Path(sys.executable).resolve().parent
    candidates = [
        exe_dir / "_internal" / "oneclass" / "oneclass.exe",
        exe_dir / "oneclass" / "oneclass.exe",
        exe_dir / "oneclass.exe",
    ]
    for candidate in candidates:
        if candidate.exists():
            return candidate
    return None


class UnifiedHomePage(QWidget):
    def __init__(self, current_user, jwt_token, user_data):
        super().__init__()
        self.current_user = current_user
        self.jwt_token = jwt_token
        self.user_data = user_data or {}
        self.yunkao_window = None
        self.oneclass_process = None
        self.needs_relogin = False

        nickname = self.user_data.get("nickname") or current_user
        self.setWindowTitle(f"学习工具中心 - {nickname}")
        self.resize(760, 520)
        self.setStyleSheet(
            "QWidget { background:#f4f7fb; color:#1f2937; font-family:'Microsoft YaHei UI'; }"
            "QFrame#Card { background:white; border-radius:16px; }"
            "QPushButton { border:0; border-radius:10px; padding:12px 16px; font-weight:bold; }"
            "QPushButton#Primary { background:#1677ff; color:white; }"
            "QPushButton#Secondary { background:#eef4ff; color:#1268d3; }"
            "QPushButton#Danger { background:#fff1f2; color:#be123c; }"
        )

        root = QVBoxLayout(self)
        root.setContentsMargins(28, 28, 28, 28)
        root.setSpacing(18)

        title = QLabel("学习工具中心")
        title.setStyleSheet("font-size:26px;font-weight:800;color:#1268d3;")
        root.addWidget(title)

        self.account_label = QLabel(
            f"当前账号：{nickname}（{self.user_data.get('role', 'user')}）"
        )
        self.account_label.setStyleSheet("color:#64748b;font-size:13px;")
        root.addWidget(self.account_label)

        notice = QLabel(
            "统一入口只保留当前这个首页：左侧是融智云考余额与云考功能，右侧是 OneClass 授权购买与同步。两边权益、支付入口和使用范围完全分开，不互通。"
        )
        notice.setWordWrap(True)
        notice.setStyleSheet("background:#fff7ed;color:#9a3412;border-radius:10px;padding:10px;")
        root.addWidget(notice)

        cards = QHBoxLayout()
        cards.setSpacing(16)
        root.addLayout(cards, stretch=1)

        self.wallet_label = QLabel("云考余额：加载中...")
        cards.addWidget(
            self._build_card(
                "融智云考",
                "题库导出、AI 补全，以及云考余额充值与消耗。",
                self.wallet_label,
                "打开云考",
                self.open_yunkao,
            )
        )

        self.oneclass_label = QLabel("OneClass 授权：检查中...")
        cards.addWidget(
            self._build_card(
                "OneClass 授权",
                "OneClass 单独购买，付款后授权绑定当前机器，不会增加云考余额。",
                self.oneclass_label,
                "启动 OneClass",
                self.open_oneclass,
                secondary_text="购买 OneClass / 同步授权",
                secondary_callback=self.purchase_oneclass,
            )
        )

        bottom = QHBoxLayout()
        bottom.addStretch()
        refresh = QPushButton("刷新状态")
        refresh.setObjectName("Secondary")
        refresh.clicked.connect(self.refresh_status)
        bottom.addWidget(refresh)
        logout = QPushButton("退出登录")
        logout.setObjectName("Danger")
        logout.clicked.connect(self.logout)
        bottom.addWidget(logout)
        root.addLayout(bottom)

        QTimer.singleShot(0, self.refresh_status)

    def _build_card(
        self,
        title,
        desc,
        status_label,
        primary_text,
        primary_callback,
        secondary_text=None,
        secondary_callback=None,
    ):
        card = QFrame()
        card.setObjectName("Card")
        layout = QVBoxLayout(card)
        layout.setContentsMargins(22, 22, 22, 22)
        layout.setSpacing(12)

        title_label = QLabel(title)
        title_label.setStyleSheet("font-size:20px;font-weight:800;")
        layout.addWidget(title_label)

        desc_label = QLabel(desc)
        desc_label.setWordWrap(True)
        desc_label.setStyleSheet("color:#64748b;")
        layout.addWidget(desc_label)

        status_label.setWordWrap(True)
        status_label.setStyleSheet("color:#0f766e;font-weight:bold;")
        layout.addWidget(status_label)
        layout.addStretch()

        primary = QPushButton(primary_text)
        primary.setObjectName("Primary")
        primary.clicked.connect(primary_callback)
        layout.addWidget(primary)

        if secondary_text and secondary_callback:
            secondary = QPushButton(secondary_text)
            secondary.setObjectName("Secondary")
            secondary.clicked.connect(secondary_callback)
            layout.addWidget(secondary)
        return card

    def refresh_status(self):
        self.refresh_wallet()
        self.refresh_oneclass()

    def refresh_wallet(self):
        try:
            data = get_wallet(self.jwt_token)
            wallet = data.get("wallet") or {}
            self.wallet_label.setText(f"云考余额：¥{float(wallet.get('balance_yuan', 0) or 0):.2f}")
        except Exception as exc:
            self.wallet_label.setText(f"云考余额：获取失败（{exc}）")

    def _oneclass_command(self, *args):
        bundled_exe = resolve_bundled_oneclass_exe()
        if bundled_exe is not None:
            return [str(bundled_exe), *args]
        python = sys.executable
        main_py = ONECLASS_ROOT / "main.py"
        return [python, str(main_py), *args]

    def refresh_oneclass(self):
        bundled_exe = resolve_bundled_oneclass_exe()
        if bundled_exe is None and not ONECLASS_ROOT.exists():
            self.oneclass_label.setText("OneClass 授权：未找到运行时（源码目录或打包 oneclass.exe）")
            return
        try:
            result = subprocess.run(
                self._oneclass_command("status-json"),
                cwd=str(ONECLASS_ROOT if ONECLASS_ROOT.exists() else Path(sys.executable).resolve().parent),
                capture_output=True,
                text=True,
                timeout=12,
            )
            data = json.loads((result.stdout or "{}").strip() or "{}")
            if data.get("active"):
                tier = data.get("tier") or "unknown"
                order_no = data.get("order_no") or "-"
                updates_until = data.get("updates_until") or "长期更新"
                self.oneclass_label.setText(
                    f"OneClass 授权：已激活（{tier}）\n订单：{order_no}\n更新截止：{updates_until}"
                )
            else:
                reason = data.get("reason") or "未激活"
                self.oneclass_label.setText(f"OneClass 授权：{reason}")
        except Exception as exc:
            self.oneclass_label.setText(f"OneClass 授权：检查失败（{exc}）")

    def open_yunkao(self):
        if self.yunkao_window is None:
            self.yunkao_window = YunKaoExtractorApp(
                current_user=self.current_user,
                jwt_token=self.jwt_token,
                user_data=self.user_data,
            )
            self.yunkao_window.destroyed.connect(lambda: setattr(self, "yunkao_window", None))
        self.yunkao_window.show()
        self.yunkao_window.raise_()

    def open_oneclass(self):
        if self.oneclass_process and self.oneclass_process.poll() is None:
            QMessageBox.information(self, "OneClass", "OneClass 已在运行中。")
            return
        env = os.environ.copy()
        env["ONECLASS_LOGIN_JWT"] = self.jwt_token
        try:
            self.oneclass_process = subprocess.Popen(
                self._oneclass_command(),
                cwd=str(ONECLASS_ROOT if ONECLASS_ROOT.exists() else Path(sys.executable).resolve().parent),
                env=env,
            )
        except Exception as exc:
            QMessageBox.warning(self, "启动失败", f"无法启动 OneClass：{exc}")

    def purchase_oneclass(self):
        env = os.environ.copy()
        env["ONECLASS_LOGIN_JWT"] = self.jwt_token
        try:
            subprocess.Popen(
                self._oneclass_command("purchase", "--jwt-token", self.jwt_token),
                cwd=str(ONECLASS_ROOT if ONECLASS_ROOT.exists() else Path(sys.executable).resolve().parent),
                env=env,
            )
        except Exception as exc:
            QMessageBox.warning(self, "购买失败", f"无法打开 OneClass 购买窗口：{exc}")

    def logout(self):
        cfg = load_config()
        cfg["jwt_token"] = ""
        cfg["user"] = ""
        cfg["user_data"] = {}
        save_config(cfg)
        self.needs_relogin = True
        self.close()
