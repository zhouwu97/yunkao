# 融智云考桌面版

当前版本：**v2.0.0**。正式主线是 `feature/winui3-migration` 上的 WinUI 3 桌面版；Python 只作为本地 Parser/Exporter Worker，不再作为独立 UI 发布入口。

## 能力

- WebView2 内置云考页面，登录凭据只保存到 Windows Credential Manager。
- Bridge 题目 marker 单消费者队列，支持暂停、停止、恢复页面后继续。
- 题目会话、AI 补全和导出使用独立取消生命周期；停止提取不会影响已保存题目的导出。
- PDF、DOCX、Markdown、TXT 离线导出；登录态图片会先缓存为本地 data URI。
- 历史记录使用 SQLite UPSERT、串行写入和 keyset 分页。
- HTTP 401/403、WebView2 崩溃、空白页和常见弱网错误会分类提示。

## 开发环境

- Windows 10 1809 或更高版本，Windows 11 推荐。
- .NET SDK 10.0.400。
- WebView2 Evergreen Runtime。
- Python 3.11+；Worker 构建依赖见 `requirements.txt`。

## 运行

```powershell
dotnet run --project src/YunKao.App/YunKao.App.csproj -c Debug -p:Platform=x64
```

本地没有 Worker 可执行文件时，应用会回退到 `python worker/worker_main.py`。可先运行：

```powershell
powershell -ExecutionPolicy Bypass -File tools/build-worker.ps1 -PackageForApp -OneFile
```

## 发布

发布入口只有根目录的 WinUI bundle 脚本：

```powershell
powershell -ExecutionPolicy Bypass -File build_unified_bundle.ps1
```

脚本从根目录 `VERSION` 读取版本，构建 Worker、WinUI 应用并检查 Bridge/Worker 是否都进入最终包。不要再使用旧的 `yunkao_dev.spec` 作为正式发布入口。

## 安全边界

桌面版不包含钱包、充值、官方代理、管理后台或 admin API。Bridge 只注入 `cctrcloud.net` 的登录/练习路径；外部页面只作为普通 WebView 浏览，不接收题目提取消息。
