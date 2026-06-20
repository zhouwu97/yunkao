import json
import os
import signal
import subprocess
import sys
from pathlib import Path

from PySide6.QtCore import QObject, QThread, QTimer, Signal
from PySide6.QtWidgets import (
    QFrame,
    QHBoxLayout,
    QLabel,
    QMessageBox,
    QMenu,
    QProgressBar,
    QPushButton,
    QSizePolicy,
    QVBoxLayout,
    QWidget,
)

from config.settings import load_config, save_config
from modules.wallet_api import get_wallet
from ui.main_window import YunKaoExtractorApp


ONECLASS_TIER_LABELS = {
    "one_time": "一次性购买",
    "lifetime_updates": "长期更新",
    "upgrade_updates": "补差升级长期更新",
}


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


def resolve_oneclass_python() -> Path:
    if sys.platform == "win32":
        candidate = ONECLASS_ROOT / ".venv" / "Scripts" / "python.exe"
    else:
        candidate = ONECLASS_ROOT / ".venv" / "bin" / "python"
    if candidate.exists():
        return candidate
    return Path(sys.executable)


class StatusRefreshWorker(QObject):
    finished = Signal(dict)

    def __init__(
        self,
        jwt_token: str,
        oneclass_base_command: list[str],
        oneclass_workdir: str,
        oneclass_available: bool,
    ):
        super().__init__()
        self.jwt_token = jwt_token
        self.oneclass_base_command = oneclass_base_command
        self.oneclass_workdir = oneclass_workdir
        self.oneclass_available = oneclass_available

    def run(self) -> None:
        result: dict[str, object] = {
            "wallet": None,
            "wallet_error": "",
            "oneclass": None,
            "oneclass_error": "",
            "sync_warning": "",
            "oneclass_available": self.oneclass_available,
        }

        try:
            result["wallet"] = get_wallet(self.jwt_token)
        except Exception as exc:
            result["wallet_error"] = str(exc)

        if not self.oneclass_available:
            self.finished.emit(result)
            return

        try:
            if self.jwt_token:
                sync = subprocess.run(
                    [*self.oneclass_base_command, "sync-license", "--jwt-token", self.jwt_token],
                    cwd=self.oneclass_workdir,
                    capture_output=True,
                    text=True,
                    timeout=18,
                )
                if sync.returncode != 0:
                    result["sync_warning"] = (
                        (sync.stderr or sync.stdout or "").strip() or "服务端授权同步失败"
                    )

            status = subprocess.run(
                [*self.oneclass_base_command, "status-json"],
                cwd=self.oneclass_workdir,
                capture_output=True,
                text=True,
                timeout=12,
            )
            if status.returncode != 0:
                detail = (status.stderr or status.stdout or "").strip()
                raise RuntimeError(detail or "OneClass 状态读取失败")
            result["oneclass"] = json.loads((status.stdout or "{}").strip() or "{}")
        except Exception as exc:
            result["oneclass_error"] = str(exc)

        self.finished.emit(result)


class UnifiedHomePage(QWidget):
    def __init__(self, current_user, jwt_token, user_data):
        super().__init__()
        self.current_user = current_user
        self.jwt_token = jwt_token
        self.user_data = user_data or {}
        self.yunkao_window = None
        self.oneclass_process = None
        self.oneclass_sync_timer = QTimer(self)
        self.oneclass_sync_timer.setInterval(2500)
        self.oneclass_sync_timer.timeout.connect(self._poll_oneclass_activation)
        self.oneclass_sync_deadline = 0
        self.oneclass_sync_notified = False
        self.oneclass_runtime_timer = QTimer(self)
        self.oneclass_runtime_timer.setInterval(1200)
        self.oneclass_runtime_timer.timeout.connect(self._update_oneclass_runtime_state)
        self.oneclass_runtime_timer.start()
        self.refresh_button = None
        self.locate_oneclass_button = None
        self.stop_oneclass_button = None
        self.refresh_thread = None
        self.refresh_worker = None
        self.needs_relogin = False

        nickname = self.user_data.get("nickname") or current_user
        self.setWindowTitle(f"学习工具中心 - {nickname}")
        self.setObjectName("UnifiedHomePage")
        self.resize(820, 520)
        self.setStyleSheet(
            "QWidget#UnifiedHomePage { background:#f4f7fb; color:#1f2937; font-family:'Microsoft YaHei UI'; }"
            "QLabel { background:transparent; }"
            "QFrame#Card { background:white; border-radius:16px; }"
            "QPushButton { border:0; border-radius:10px; padding:12px 16px; font-weight:bold; min-height:20px; }"
            "QPushButton#Primary { background:#1677ff; color:white; }"
            "QPushButton#Primary:hover { background:#3b8cff; }"
            "QPushButton#Primary:pressed { background:#0f5ed7; }"
            "QPushButton#Secondary { background:#eef4ff; color:#1268d3; }"
            "QPushButton#Secondary:hover { background:#dbeafe; }"
            "QPushButton#Secondary:pressed { background:#bfdbfe; color:#0f5ed7; }"
            "QPushButton#Danger { background:#fff1f2; color:#be123c; }"
            "QPushButton#Danger:hover { background:#ffe4e6; }"
            "QPushButton#Danger:pressed { background:#fecdd3; }"
            "QPushButton:disabled { background:#cbd5e1; color:white; }"
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
                None,
                None,
                "打开云考",
                self.open_yunkao,
            )
        )

        self.oneclass_label = QLabel("OneClass 授权：检查中...")
        self.oneclass_progress_label = QLabel("")
        self.oneclass_progress_label.setWordWrap(True)
        self.oneclass_progress_label.setStyleSheet("color:#64748b;font-size:12px;")
        self.oneclass_progress_label.hide()
        self.oneclass_progress = QProgressBar()
        self.oneclass_progress.setTextVisible(False)
        self.oneclass_progress.setFixedHeight(10)
        self.oneclass_progress.hide()
        cards.addWidget(
            self._build_card(
                "OneClass 授权",
                "OneClass 单独购买，付款后授权绑定当前机器，不会增加云考余额。",
                self.oneclass_label,
                self.oneclass_progress_label,
                self.oneclass_progress,
                "启动 OneClass",
                self.open_oneclass,
                secondary_text="购买 OneClass / 同步授权",
                secondary_callback=self.purchase_oneclass,
            )
        )

        bottom = QHBoxLayout()
        bottom.addStretch()
        self.refresh_button = QPushButton("刷新状态")
        self.refresh_button.setObjectName("Secondary")
        self.refresh_button.setMinimumHeight(44)
        self.refresh_button.setSizePolicy(QSizePolicy.Fixed, QSizePolicy.Fixed)
        self.refresh_button.clicked.connect(self.handle_refresh_click)
        bottom.addWidget(self.refresh_button)
        self.locate_oneclass_button = QPushButton("定位 OneClass")
        self.locate_oneclass_button.setObjectName("Secondary")
        self.locate_oneclass_button.setMinimumHeight(44)
        self.locate_oneclass_button.setSizePolicy(QSizePolicy.Fixed, QSizePolicy.Fixed)
        locate_menu = QMenu(self.locate_oneclass_button)
        locate_menu.addAction("内容区定位", lambda: self.launch_oneclass_locator("calibrate-region"))
        locate_menu.addAction("听写点位定位", lambda: self.launch_oneclass_locator("calibrate-dictation"))
        locate_menu.addAction("再来一组按钮定位", lambda: self.launch_oneclass_locator("calibrate-next-group"))
        self.locate_oneclass_button.setMenu(locate_menu)
        bottom.addWidget(self.locate_oneclass_button)
        self.stop_oneclass_button = QPushButton("停止 OneClass")
        self.stop_oneclass_button.setObjectName("Secondary")
        self.stop_oneclass_button.setMinimumHeight(44)
        self.stop_oneclass_button.setSizePolicy(QSizePolicy.Fixed, QSizePolicy.Fixed)
        self.stop_oneclass_button.clicked.connect(self.stop_oneclass)
        self.stop_oneclass_button.setEnabled(False)
        bottom.addWidget(self.stop_oneclass_button)
        logout = QPushButton("退出登录")
        logout.setObjectName("Danger")
        logout.setMinimumHeight(44)
        logout.setSizePolicy(QSizePolicy.Fixed, QSizePolicy.Fixed)
        logout.clicked.connect(self.logout)
        bottom.addWidget(logout)
        root.addLayout(bottom)

        QTimer.singleShot(0, self.refresh_status)

    def _build_card(
        self,
        title,
        desc,
        status_label,
        progress_label,
        progress_bar,
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

        if progress_label is not None:
            layout.addWidget(progress_label)
        if progress_bar is not None:
            layout.addWidget(progress_bar)
        layout.addStretch()

        primary = QPushButton(primary_text)
        primary.setObjectName("Primary")
        primary.setMinimumHeight(44)
        primary.setSizePolicy(QSizePolicy.Expanding, QSizePolicy.Fixed)
        primary.clicked.connect(primary_callback)
        layout.addWidget(primary)

        if secondary_text and secondary_callback:
            secondary = QPushButton(secondary_text)
            secondary.setObjectName("Secondary")
            secondary.setMinimumHeight(44)
            secondary.setSizePolicy(QSizePolicy.Expanding, QSizePolicy.Fixed)
            secondary.clicked.connect(secondary_callback)
            layout.addWidget(secondary)
        return card

    def refresh_status(self):
        if self.refresh_thread is not None and self.refresh_thread.isRunning():
            return
        self._set_oneclass_progress("正在同步 OneClass 授权状态...", active=True)
        self._start_refresh_worker()

    def handle_refresh_click(self):
        if self.refresh_thread is not None and self.refresh_thread.isRunning():
            return
        if self.refresh_button is not None:
            self.refresh_button.setEnabled(False)
            self.refresh_button.setText("正在同步...")
        self._set_oneclass_progress("正在向服务端同步 OneClass 授权状态...", active=True)
        QTimer.singleShot(0, self._start_refresh_worker)

    def _restore_refresh_button(self):
        if self.refresh_button is not None:
            self.refresh_button.setText("刷新状态")
            self.refresh_button.setEnabled(True)

    def _start_refresh_worker(self):
        if self.refresh_thread is not None and self.refresh_thread.isRunning():
            return
        bundled_exe = resolve_bundled_oneclass_exe()
        oneclass_available = not (bundled_exe is None and not ONECLASS_ROOT.exists())
        self.refresh_thread = QThread(self)
        self.refresh_worker = StatusRefreshWorker(
            self.jwt_token,
            self._oneclass_command(),
            str(self._oneclass_workdir()),
            oneclass_available,
        )
        self.refresh_worker.moveToThread(self.refresh_thread)
        self.refresh_thread.started.connect(self.refresh_worker.run)
        self.refresh_worker.finished.connect(self._apply_refresh_result)
        self.refresh_worker.finished.connect(self.refresh_thread.quit)
        self.refresh_worker.finished.connect(self.refresh_worker.deleteLater)
        self.refresh_thread.finished.connect(self.refresh_thread.deleteLater)
        self.refresh_thread.finished.connect(self._clear_refresh_worker)
        self.refresh_thread.start()

    def _clear_refresh_worker(self):
        self.refresh_thread = None
        self.refresh_worker = None

    def _apply_refresh_result(self, result: dict):
        wallet_error = str(result.get("wallet_error") or "")
        wallet_data = result.get("wallet")
        if wallet_error:
            self.wallet_label.setText(f"云考余额：获取失败（{wallet_error}）")
        elif isinstance(wallet_data, dict):
            wallet = wallet_data.get("wallet") or {}
            self.wallet_label.setText(f"云考余额：¥{float(wallet.get('balance_yuan', 0) or 0):.2f}")

        if not result.get("oneclass_available"):
            self.oneclass_label.setText("OneClass 授权：未找到运行时（源码目录或打包 oneclass.exe）")
        else:
            oneclass_error = str(result.get("oneclass_error") or "")
            oneclass_data = result.get("oneclass")
            sync_warning = str(result.get("sync_warning") or "")
            if oneclass_error:
                self.oneclass_label.setText("OneClass 授权：检查失败")
                self.oneclass_label.setToolTip(oneclass_error)
                if not self.oneclass_sync_timer.isActive():
                    self._set_oneclass_progress("点击“刷新状态”可重试读取 OneClass 授权。", active=False)
            elif isinstance(oneclass_data, dict):
                self._apply_oneclass_status(oneclass_data, sync_warning)

        self._update_oneclass_runtime_state()
        self._restore_refresh_button()

    def _update_oneclass_runtime_state(self):
        running = bool(self.oneclass_process and self.oneclass_process.poll() is None)
        if self.stop_oneclass_button is not None:
            self.stop_oneclass_button.setEnabled(running)
        if running and not self.oneclass_sync_timer.isActive():
            self._set_oneclass_progress("OneClass 正在运行。可点击下方“停止 OneClass”。", active=False)

    def refresh_wallet(self):
        try:
            data = get_wallet(self.jwt_token)
            wallet = data.get("wallet") or {}
            self.wallet_label.setText(f"云考余额：¥{float(wallet.get('balance_yuan', 0) or 0):.2f}")
        except Exception as exc:
            self.wallet_label.setText(f"云考余额：获取失败（{exc}）")

    def _set_oneclass_progress(self, message: str = "", active: bool = False) -> None:
        self.oneclass_progress_label.setText(message)
        self.oneclass_progress_label.setVisible(bool(message))
        if active:
            self.oneclass_progress.show()
            self.oneclass_progress.setRange(0, 0)
        else:
            self.oneclass_progress.hide()
            self.oneclass_progress.setRange(0, 100)
            self.oneclass_progress.setValue(0)

    def _oneclass_command(self, *args):
        bundled_exe = resolve_bundled_oneclass_exe()
        if bundled_exe is not None:
            return [str(bundled_exe), *args]
        python = resolve_oneclass_python()
        main_py = ONECLASS_ROOT / "main.py"
        return [str(python), str(main_py), *args]

    def _oneclass_workdir(self) -> Path:
        bundled_exe = resolve_bundled_oneclass_exe()
        if bundled_exe is not None:
            return bundled_exe.parent
        if ONECLASS_ROOT.exists():
            return ONECLASS_ROOT
        return Path(sys.executable).resolve().parent

    def _read_oneclass_status(self) -> dict[str, object]:
        result = subprocess.run(
            self._oneclass_command("status-json"),
            cwd=str(self._oneclass_workdir()),
            capture_output=True,
            text=True,
            timeout=12,
        )
        if result.returncode != 0:
            detail = (result.stderr or result.stdout or "").strip()
            raise RuntimeError(detail or "OneClass 状态读取失败")
        return json.loads((result.stdout or "{}").strip() or "{}")

    def _sync_oneclass_license(self) -> str:
        if not self.jwt_token:
            return ""
        result = subprocess.run(
            self._oneclass_command("sync-license", "--jwt-token", self.jwt_token),
            cwd=str(self._oneclass_workdir()),
            capture_output=True,
            text=True,
            timeout=18,
        )
        if result.returncode != 0:
            detail = (result.stderr or result.stdout or "").strip()
            if detail:
                return detail
            return "服务端授权同步失败"
        return ""

    def _apply_oneclass_status(self, data: dict, sync_warning: str = "") -> None:
        if data.get("active"):
            tier = str(data.get("tier") or "unknown")
            tier_label = ONECLASS_TIER_LABELS.get(tier, tier)
            order_no = data.get("order_no") or "-"
            updates_until = data.get("updates_until") or "长期更新"
            self.oneclass_label.setText(f"OneClass 授权：已激活（{tier_label}）")
            self.oneclass_label.setToolTip(f"订单：{order_no}\n更新截止：{updates_until}")
            if not self.oneclass_sync_timer.isActive():
                self._set_oneclass_progress("", active=False)
            return

        reason = data.get("reason") or "未激活"
        self.oneclass_label.setText(f"OneClass 授权：{reason}")
        self.oneclass_label.setToolTip(sync_warning)
        if not self.oneclass_sync_timer.isActive():
            message = sync_warning or "未购买时会提示购买；付款后会自动同步授权。"
            self._set_oneclass_progress(message, active=False)

    def refresh_oneclass(self):
        bundled_exe = resolve_bundled_oneclass_exe()
        if bundled_exe is None and not ONECLASS_ROOT.exists():
            self.oneclass_label.setText("OneClass 授权：未找到运行时（源码目录或打包 oneclass.exe）")
            return
        try:
            sync_warning = self._sync_oneclass_license()
            data = self._read_oneclass_status()
            self._apply_oneclass_status(data, sync_warning)
        except Exception as exc:
            self.oneclass_label.setText("OneClass 授权：检查失败")
            self.oneclass_label.setToolTip(str(exc))
            if not self.oneclass_sync_timer.isActive():
                self._set_oneclass_progress("点击“刷新状态”可重试读取 OneClass 授权。", active=False)

    def _begin_oneclass_sync_monitor(self, message: str) -> None:
        self.oneclass_sync_deadline = 72
        self.oneclass_sync_notified = False
        self._set_oneclass_progress(message, active=True)
        self.oneclass_sync_timer.start()

    def _poll_oneclass_activation(self) -> None:
        try:
            data = self._read_oneclass_status()
        except Exception as exc:
            self.oneclass_sync_deadline -= 1
            self._set_oneclass_progress(f"正在等待授权写入，稍后自动重试…（读取失败：{exc}）", active=True)
            if self.oneclass_sync_deadline <= 0:
                self.oneclass_sync_timer.stop()
                self._set_oneclass_progress("等待超时。你可以点击“刷新状态”或再次进入“同步授权”。", active=False)
            return

        if data.get("active"):
            self.oneclass_sync_timer.stop()
            self._apply_oneclass_status(data)
            self._set_oneclass_progress("付款已同步完成，首页授权状态已自动刷新。", active=False)
            if not self.oneclass_sync_notified:
                self.oneclass_sync_notified = True
                QMessageBox.information(self, "OneClass", "付款已同步完成，OneClass 授权状态已刷新。")
            return

        self.oneclass_sync_deadline -= 1
        reason = str(data.get("reason") or "未激活").strip()
        self.oneclass_label.setText(f"OneClass 授权：{reason}")
        self._set_oneclass_progress("已打开支付窗口，正在等待付款确认并自动刷新授权状态…", active=True)
        if self.oneclass_sync_deadline <= 0:
            self.oneclass_sync_timer.stop()
            self._set_oneclass_progress("暂时还没有同步成功。付款后可点击“刷新状态”或再次进入“同步授权”。", active=False)

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
        try:
            status = self._read_oneclass_status()
        except Exception as exc:
            QMessageBox.warning(self, "启动失败", f"启动前检查 OneClass 授权失败：{exc}")
            return
        if not status.get("active"):
            reason = str(status.get("reason") or "未激活")
            self.refresh_oneclass()
            QMessageBox.information(
                self,
                "需要授权",
                f"当前还不能启动 OneClass：{reason}\n\n请先点击“购买 OneClass / 同步授权”。",
            )
            return
        env = os.environ.copy()
        env["ONECLASS_LOGIN_JWT"] = self.jwt_token
        try:
            creationflags = 0
            if sys.platform == "win32":
                creationflags = (
                    getattr(subprocess, "CREATE_NEW_CONSOLE", 0)
                    | getattr(subprocess, "CREATE_NEW_PROCESS_GROUP", 0)
                )
            self.oneclass_process = subprocess.Popen(
                self._oneclass_command(),
                cwd=str(self._oneclass_workdir()),
                env=env,
                creationflags=creationflags,
            )
            self._update_oneclass_runtime_state()
        except Exception as exc:
            QMessageBox.warning(self, "启动失败", f"无法启动 OneClass：{exc}")

    def launch_oneclass_locator(self, command: str):
        if self.oneclass_process and self.oneclass_process.poll() is None:
            QMessageBox.information(self, "OneClass", "请先停止 OneClass，再重新定位。")
            return
        commands = {
            "calibrate-region": "内容区定位",
            "calibrate-dictation": "听写点位定位",
            "calibrate-next-group": "再来一组按钮定位",
        }
        if command not in commands:
            QMessageBox.warning(self, "定位失败", f"未知定位方式：{command}")
            return
        try:
            creationflags = 0
            if sys.platform == "win32":
                creationflags = getattr(subprocess, "CREATE_NEW_CONSOLE", 0)
            subprocess.Popen(
                self._oneclass_command(command),
                cwd=str(self._oneclass_workdir()),
                creationflags=creationflags,
            )
            self._set_oneclass_progress(f"已打开{commands[command]}窗口，请按控制台提示完成定位。", active=False)
        except Exception as exc:
            QMessageBox.warning(self, "定位失败", f"无法启动 OneClass 定位：{exc}")

    def stop_oneclass(self):
        if not self.oneclass_process or self.oneclass_process.poll() is not None:
            self._update_oneclass_runtime_state()
            QMessageBox.information(self, "OneClass", "OneClass 当前没有在运行。")
            return
        try:
            if sys.platform == "win32" and hasattr(signal, "CTRL_BREAK_EVENT"):
                self.oneclass_process.send_signal(signal.CTRL_BREAK_EVENT)
                self.oneclass_process.wait(timeout=4)
            else:
                self.oneclass_process.terminate()
                self.oneclass_process.wait(timeout=4)
        except Exception:
            try:
                self.oneclass_process.kill()
            except Exception:
                pass
        finally:
            self._update_oneclass_runtime_state()
            if not self.oneclass_sync_timer.isActive():
                self._set_oneclass_progress("OneClass 已停止。重新启动时会弹出独立控制台。", active=False)

    def purchase_oneclass(self):
        env = os.environ.copy()
        env["ONECLASS_LOGIN_JWT"] = self.jwt_token
        try:
            subprocess.Popen(
                self._oneclass_command("purchase", "--jwt-token", self.jwt_token),
                cwd=str(self._oneclass_workdir()),
                env=env,
            )
            self._begin_oneclass_sync_monitor("已打开支付窗口，等待你完成付款。授权成功后这里会自动刷新。")
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
