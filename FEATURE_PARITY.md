# 旧功能能力矩阵与 WinUI v2 回归基线

## 目的与边界

本矩阵以当前工作区的 `feature/winui3-migration` 为验收对象。正式应用是 WinUI 3；Python 代码仅作为 Parser / Exporter Worker。`ui/` 中的 PySide6 实现和它的回归测试不能作为 WinUI 发布验收的唯一依据。

`PASS` 表示已有 WinUI / Worker 实现且已被自动化测试覆盖；`PARTIAL` 表示实现存在，但仍缺少真实云考站点的端到端验证；`REMOVED_BY_DESIGN` 表示不属于当前独立本地工具的产品范围。

## 独立本地工具能力

| 能力 | 状态 | 当前证据 / 回归入口 | 发布前仍需验证 |
|---|---|---|---|
| WebView2 内嵌云考、返回、刷新、外部浏览器打开 | PARTIAL | `WebViewService`、`BrowserShell` | 真实登录页、首页、练习页、断网与外部浏览器 |
| 本地账号/密码保存 | PASS | `SettingsService`、`SettingsServiceTests` | Windows Credential Manager 可用性 |
| 登录信息自动填写 | PARTIAL | `yunkao-bridge.js`、`ExtractionCoordinator.FillCredentialsOnBridgeReadyAsync` | 登录页 DOM 是否仍匹配 |
| 旧配置与凭据迁移 | PASS | `SettingsService`、`SettingsServiceTests` | 真实旧安装目录升级 |
| 自动提取、自动翻题 | PARTIAL | `ExtractionCoordinator` 单消费者队列、会话 ID 守卫、Worker Fixture 协议回归 | 真实 1/2/100 题、慢动画和延迟加载 |
| 题目去重 | PASS | `ExtractionSession.TryAddQuestion`、`ExtractionSessionTests` | 真实跨页重复题 |
| 单选、多选、判断、图片、MathJax、长题解析 | PASS | `tests/test_parser_fixtures.py` | 真实页面 DOM 变更 |
| 仅传输当前题 DOM | PASS | `WebViewService.GetActiveQuestionHtmlAsync` 使用 `outerHTML` | 复杂真实题目的传输耗时 |
| 开始、暂停、继续、结束 | PASS | `ExtractionStatus`、`ExtractionSessionTests`、`ExtractionPanel` | 真实页面暂停后的继续行为 |
| 清空当前题目 | PASS | `更多 → 清空本次题目`、`ExtractionCoordinator.ClearCurrentSession` | 提取中清空与立即重开 |
| AI 缺失答案补全 | PARTIAL | `AiService`、`AiServiceTests` | 实际供应商、模型、图片输入 |
| AI 并发限制与 429/5xx/超时重试 | PASS | `AiTaskQueue` 固定并发 3、1/2/4/8 秒重试、`AiServiceTests` | 真实 429、超时、连接重置 |
| AI 旧回调不污染新会话 | PASS | 会话 ID 守卫、`Stale_ai_callback_cannot_mutate_a_restarted_session` | 提取中清空/重开压力场景 |
| PDF / DOCX / Markdown / TXT 导出 | PASS | `ExportService`、Worker 协议回归 | Windows Word/WPS、中文/空格路径、大文件 |
| 练习版隐藏答案和解析 | PASS | Worker 导出器、Python 回归 | PDF 的实际视觉输出 |
| 导出快照隔离 | PASS | `ExtractionSession.Questions` 返回深克隆、`ExportAsync` 先获取快照 | 导出时清空、AI 回调与重开 |
| 导出后自动打开 | PARTIAL | `AutoOpenAfterExport`、`ExtractionCoordinator.ExportAsync` | 默认应用关联与被占用文件 |
| 会话历史与断点恢复 | PASS | SQLite 会话快照保存来源 URL、重复/异常/AI 指标，`HistoryStoreTests`、`HistoryPage` | 崩溃后恢复与跨版本升级 |
| 导出记录 | PASS | `ExportPage` 可打开文件/文件夹、复制路径、重新导出、仅删除记录；`HistoryStoreTests` | Windows 文件关联、已占用文件 |
| 运行诊断 | PASS | `DiagnosticsPage` 汇总版本、WebView2、凭据库、任务、导出、DPI、内存；支持复制/导出/打开日志 | 真实 WebView2 与系统权限组合 |
| 原生 Windows 窗口行为 | PARTIAL | WinUI `AppWindow` 与 `SetTitleBar` | Snap Layout、多屏 DPI、贴靠和边缘缩放 |

## 已按设计移除的历史能力

| 历史能力 | 状态 | 说明 |
|---|---|---|
| SYLUlive 登录与 JWT | REMOVED_BY_DESIGN | 不再引入远程用户体系 |
| VIP 权益校验、钱包与充值 | REMOVED_BY_DESIGN | 独立本地工具不包含付费账户边界 |
| 管理后台 API、管理员窗口 | REMOVED_BY_DESIGN | 不属于桌面端离线提取/导出流程 |
| OneClass 统一入口与服务端强绑定 | REMOVED_BY_DESIGN | 保持对云考站点的最小桥接 |

## 当前自动化基线

运行时间：2026-08-26。

| 命令 | 结果 | 覆盖重点 |
|---|---:|---|
| `dotnet test src/YunKao.Tests/YunKao.Tests.csproj --no-restore --configuration Debug` | 16 passed | 配置/凭据、会话状态、AI 并发限制、历史 SQLite、协议 framing |
| `python -m pytest tests -q` | 34 passed | Worker 协议、六类解析 Fixture、旧 UI 兼容回归、导出格式 |
| `build_unified_bundle.ps1 -SkipWorker` | passed | 发布包包含 Worker/Bridge、版本和 SHA-256 完整性清单 |

## 发布门禁：真实站点 E2E

以下用例必须在发布候选包和真实云考测试账号上记录结果；未执行前，矩阵中所有 `PARTIAL` 均不能升级为 `PASS`。

1. 首次安装、旧配置升级、无密码、记住密码、Credential Manager 不可用。
2. 登录页、章节页、单题页、返回、刷新、外部浏览器、断网与 401/403。
3. 单选、多选、判断、填空、简答、图片、公式和 100 题自动翻页；覆盖首题、末题、重复题、慢加载。
4. 开始、暂停、继续、结束、清空、立即重开、关闭应用与恢复未完成任务。
5. AI 正常、401、429、5xx、超时、无效 JSON、低置信度及并发三任务上限。
6. PDF/DOCX/MD/TXT 的答案版与练习版；覆盖中文、图片、公式、长题、中文和空格路径、同名与已占用文件。
7. 干净 Windows 用户环境安装 MSIX，验证 WebView2 Runtime、Worker、Bridge 和最终导出文件均存在。
