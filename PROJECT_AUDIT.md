# 项目检查与修复记录

检查日期：2026-11-15。范围：`main` 分支的 WinUI 应用、C# Core、Python Parser/Exporter Worker、网页桥接、测试和发布流程。

## Git 同步

先将原有本地改动保存为 `a0cb9b5`，再合并远程 `main` 的已有提交，以 `7dc0a7b` 推送至 `origin/main`。没有强制推送或丢弃原有改动。本轮修复另行提交。

## 修复清单

| 范围 | 问题与修复 |
| --- | --- |
| 登录桥接 | 截图中的 `/admin/index.php?act=login&op=login` 被 API 路由分类器排除。新增独立桥接规则，支持实际 PHP 登录入口，并保留域名与 HTTPS 限制。 |
| 消息来源 | 接收普通桥接消息前补充可信域名与当前文档完整请求 URL 校验，拒绝其他页面和不同查询参数的消息。 |
| 提取线程 | 消费队列的 `ConfigureAwait(false)` 会丢失 UI 上下文，后续却仍调用 WebView2。改为保留上下文，并补齐销毁时的导航事件解绑。 |
| 活动题选择 | 合并 CSS 选择器可能按 DOM 顺序选到前一题。先查活动 Swiper，再回退到兼容容器；同时支持内部容器的题目标识。 |
| 登录填写 | 无已保存账号时也填学校代码；跳过空账号和密码，避免清空手动输入；只允许在登录表单填写，扩充字段选择器。 |
| Parser | 支持与 JS 一致的四种兼容题目根容器；判断题优先按实际选项映射 A/B，修复 A=错误、B=正确时的答案反转。 |
| AI 图片 | 标记长度写错导致 URL 丢失首字符。改用标记实际长度，HTTPS 与 data URI 请求正文测试先失败、修复后通过。 |
| 图片下载 | 保留多张图片之间的 UI 上下文，按块限制单图为 10 MiB，传播主动取消；Referer 不再附带页面查询参数。 |
| Worker 取消 | 控制请求从线程池分离，在读取线程执行；排队前登记取消标识，繁忙时仍能取消运行中和未开始的请求。 |
| Worker 协议 | Exporter 图片容错信息改到 stderr，避免污染 stdout 的 Content-Length 协议。 |
| 导出路径 | 文件名前缀清理路径分隔符与非法字符，并限制长度，防止越出指定目录或保存失败。 |
| 诊断脱敏 | 增加 JSON 引号、带空格的密钥、access/refresh token 和 Cookie 头覆盖；复制和导出的诊断摘要也脱敏。 |
| 诊断布局 | 内容不足 720 DIP 时改为可滚动单列，缩短标签列；诊断刷新、复制和打开目录失败时显示状态。 |
| 设置保存 | 保存失败时不再清空输入或显示成功；保存密码前检查账号及“记住密码”开关。 |
| CI 与发布 | CI 增加 Python 依赖和全量回归；打包增加桥接测试、自动 restore 和冻结 Worker 运行验证门禁。 |

活动题选择、空凭据、练习页填写、反向判断题、兼容容器、AI 图片地址和路径边界均有新增回归测试。WebView2 线程及响应式布局已通过编译，但未做窗口实测。

## 实际验证

| 检查 | 结果 |
| --- | --- |
| C# 全量测试 | 130 passed（检查前 110） |
| Python 全量测试 | 37 passed（检查前 34） |
| JavaScript 桥接测试 | 5 passed（检查前 2） |
| JS 语法 | 通过 |
| WinUI Release 构建 | 通过，0 警告、0 错误 |
| 源码构建 Worker、自包含发布与压缩 | 通过 |
| 必需文件、版本、Worker / Bridge SHA-256 | 通过 |
| 冻结 Worker 脱离项目目录启动 | 通过，清除 PYTHONPATH / PYTHONHOME |
| 冻结 Worker 六类题目解析 | 通过 |
| 冻结 Worker 中文含空格路径 MD / TXT / DOCX | 答案版与练习版共 6 次导出通过，逐个检查正文及答案/解析隐藏 |
| 冻结 Worker 损坏图片容错与正常退出 | 通过 |

执行了完整 `build_unified_bundle.ps1`；加入冻结 Worker 门禁后，又执行 `build_unified_bundle.ps1 -SkipWorker -SkipTests` 验证新门禁及最终压缩，复用前一次构建的 Worker。可独立执行：

```powershell
python tools/smoke-worker.py dist/yunkao-desktop-v2.0.0-windows-x64/worker/YunKao.Worker.exe
```

产物：`dist/yunkao-desktop-v2.0.0-windows-x64.zip`。应用入口：`dist/yunkao-desktop-v2.0.0-windows-x64/YunKao.App.exe`。运行时需解压并保留整个目录。

## 验证边界

- 窗口自动检查被用户按 Esc 中断，随后停止窗口操作。诊断布局、在线桥接连通和真实提取效果未标记为实测通过。
- 未使用真实账号验收登录、连续翻题、登录态图片和恢复任务的完整链路，夹具和代码检查不能替代这些验收。
- 未请求真实 AI 供应商；本轮验证本地请求正文及已有响应、并发测试。
- 未实际调用 Word/WPS 生成 PDF；PDF 仍依赖可用的本机 Office 组件，三种导出通过不代表 PDF 已验收。
- 未验收干净 Windows、125%/150% DPI、多屏和 MSIX 安装。产物为 x64 自包含目录压缩包。
- 本机 pytest-asyncio 有既有默认事件循环作用域弃用提示，不影响通过。新增 CI 已写入，远程运行结果尚未检查。
