# WinUI 迁移骨架

当前分支 `feature/winui3-migration` 从 `pyside-baseline-v1` 开始，第一阶段只建立 C# / WinUI 3 桌面壳。现有 PySide6、`modules/`、Python 测试和打包脚本均保持原位置，不会被新项目启动流程调用。

## 工具链

- .NET SDK 10.0.400
- Windows App SDK 2.3.1（Stable）
- Windows SDK 10.0.26100
- x64 / Packaged MSIX

本机开发工具按约定放在 `D:\kaifa`，当前 SDK 路径为 `D:\kaifa\dotnet10`。如果 SDK 已加入 PATH，可直接使用 `dotnet`；否则使用下面的显式命令。

## 构建

```powershell
& 'D:\kaifa\dotnet10\dotnet.exe' restore YunKao.sln --configfile NuGet.config
& 'D:\kaifa\dotnet10\dotnet.exe' build YunKao.sln --configuration Debug --no-restore -p:Platform=x64
```

## 当前范围

- `YunKao.App`：WinUI 3 单项目 MSIX、Light Theme、Desktop Acrylic 降级链、标题栏、左侧导航、工作台、设置页和右侧控制台占位。
- `Themes/`：颜色、排版、控件和玻璃卡片资源，冰蓝为主色，AI 使用淡紫，导出预留暖橙色。
- `Scripts/yunkao-bridge.js`：只保留独立脚本边界，第一阶段不会向网页注入。
- `Services/BackdropService.cs`：Desktop Acrylic → Mica → Solid，系统高对比度时主动关闭透明材质。

WebView2、Credential Manager、Python Worker、Parser/Exporter、AI 和配置迁移将在后续 PR 接入；第一阶段启动不会拉起 Python。
