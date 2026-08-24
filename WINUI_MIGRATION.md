# WinUI 迁移骨架

当前分支 `feature/winui3-migration` 已完成 WinUI 3 主流程迁移。正式桌面入口是 C# / WinUI 3；现有 PySide6 文件保留为兼容与历史回归用途，`modules/` 仅作为 Worker 的解析、导出和 AI 实现，正式发布不再启动 Python UI。

## 工具链

- .NET SDK 10.0.400
- Windows App SDK 2.3.1（Stable）
- Windows SDK 10.0.26100
- x64 / Packaged MSIX

本机开发工具按约定放在 `D:\kaifa`，当前 SDK 路径为 `D:\kaifa\dotnet10`。如果 SDK 已加入 PATH，可直接使用 `dotnet`；否则使用下面的显式命令。

## 构建与发布

```powershell
& 'D:\kaifa\dotnet10\dotnet.exe' restore YunKao.sln --configfile NuGet.config
& 'D:\kaifa\dotnet10\dotnet.exe' build YunKao.sln --configuration Debug --no-restore -p:Platform=x64

# 生成统一 WinUI 发布包（会先构建 Worker，再发布 WinUI 应用）
.\build_unified_bundle.ps1
```

## 当前范围

- `YunKao.App`：WinUI 3 单项目 MSIX、Light Theme、Desktop Acrylic 降级链、标题栏、左侧导航、工作台、设置页、历史、导出和诊断页面。
- `Themes/`：颜色、排版、控件和玻璃卡片资源，冰蓝为主色，AI 使用淡紫，导出预留暖橙色。
- `Scripts/yunkao-bridge.js`：受页面来源限制注入，负责题目标记、下一题点击和凭据桥接。
- `Services/BackdropService.cs`：Desktop Acrylic → Mica → Solid，系统高对比度时主动关闭透明材质。
- `Services/WebViewService.cs`：WebView2 导航、认证状态、进程故障恢复、空白页探测和题目标记读取。
- `Services/ExtractionCoordinator.cs`：Worker 解析、题目队列、AI、会话持久化、断点恢复和导出生命周期协调。
- `worker/`：由 `tools/build-worker.ps1` 生成的独立 Worker，可被 WinUI 应用直接拉起。

正式版本号统一读取根目录 `VERSION`，当前为 `2.0.0`。发布脚本只清理自身的 `dist/<artifact>` 与 `build/<artifact>` 目录；旧 Python UI 打包脚本不属于正式发布入口。
