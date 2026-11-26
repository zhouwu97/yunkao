# 融智云考桌面版

把云考练习页中的题目保存到本机，再导出成 PDF、Word、Markdown 或 TXT。当前版本为 **2.1.3**，界面使用 WinUI 3，题目解析和文档导出由随程序附带的 Python Worker 完成。

本版修复设置页开关连续点击、重新按下和越界反拖的动画衔接，液态玻璃材质沿用 v2.1.2。完整更新见 [v2.1.3 发布说明](docs/releases/v2.1.3.md)。

## 怎么用

使用打包版本时，解压整个文件夹，运行 `YunKao.App.exe`。不要只复制这个 EXE，程序还需要同目录的运行库、`worker` 和 `Scripts`。

1. 在工作台打开的云考学生练习页完成登录，再进入具体的练习题。学校代码目前固定为 `u101441`。
2. 在左侧任务卡点击 **开始提取**。窄窗口中点击带题目数量的任务按钮展开任务卡。程序会保存当前题目并切换到下一题。
3. 需要中途离开时，可以暂停；点击停止后，已经保存的题目仍然可以导出。重新进入练习页点击“继续提取”会保留原有题目。任务菜单中的“新建提取任务”和“移出工作台”会先保存历史，原题目可从历史页恢复。切换到设置或历史页也会保留当前任务。
4. 在任务卡选择 PDF、DOCX，或在“更多”中选择 Markdown、TXT。“导出题库”主按钮使用设置中的默认格式。勾选“练习版”后，导出的文件隐藏答案和解析。导出成功后，提示条会显示文件位置，并提供“打开文件夹”入口。
5. 在历史记录按课程名称或题库链接搜索并恢复任务。在导出记录按文件名、路径或格式搜索，打开文件或根据题库快照重新导出。两个页面都支持 Ctrl+F 定位搜索框。

登录页未加载、登录失效或翻页异常时，先看工作台提示。窄窗口的任务弹出层可以按 `Esc` 关闭；浏览器支持 `Ctrl+L` 编辑地址、`Ctrl+R` / `F5` 刷新、`Alt+←` / `Alt+→` 后退和前进。

## 运行需要什么

- Windows x64，最低目标版本为 Windows 10 1809，推荐 Windows 11。
- Microsoft Edge WebView2 Evergreen Runtime，用于内置网页。
- **导出 PDF 需要可用的桌面版 Word 或 WPS。** 当前导出方式先生成 DOCX，再调用本机 Office 组件转成 PDF。没有这些组件时，可以使用 DOCX、Markdown 或 TXT。

打包版本自带 .NET 运行库和 Worker，使用时无需另装 Python。题目中的图片会随导出处理，但复杂公式、图片和长表格的分页仍建议检查一下。

## 账号、数据和 AI

云考账号可以在设置中保存。勾选记住密码后，密码写入 Windows Credential Manager，应用只在允许的云考页面自动填写；登录时仍会提交给云考网站。API Key 也存放在系统凭据库，普通配置文件不保存这两类密钥。

| 内容 | 默认位置 |
| --- | --- |
| 账号名称、导出与外观设置 | `%APPDATA%\YunKaoDesktop\settings.json` |
| 提取历史与题目快照 | `%LOCALAPPDATA%\YunKaoDesktop\yunkao.db` |
| 导出文件 | 系统文档目录，或设置中指定的目录 |

AI 补全默认关闭。开启并配置服务商后，缺少答案的题目会发送到你填写的 API 地址；允许图片识别时，也会处理题目图片。AI 结果会标注来源和置信度，答案需要自行核对。

程序会访问云考网站、题目图片地址，以及你启用的 AI 接口。题库与配置主要保存在本地，这不等于程序完全离线。导出文件带有免费获取的提示，请勿倒卖。

## 从源码运行

需要 .NET SDK **10.0.400**（见 `global.json`）、Python **3.11+** 和 WebView2 Runtime。在仓库根目录执行：

```powershell
python -m pip install -r requirements.txt

dotnet restore YunKao.sln
dotnet run --project src/YunKao.App/YunKao.App.csproj -c Debug -p:Platform=x64
```

没有打包 Worker 时，应用会使用 `python worker/worker_main.py`。也可以先构建 Worker：

```powershell
python -m pip install pyinstaller
powershell -ExecutionPolicy Bypass -File tools/build-worker.ps1 -PackageForApp -OneFile
```

正式桌面入口是 `src/YunKao.App`；根目录的 `main.py` 和 `ui/` 是保留的旧版 PySide6 界面。

液态玻璃独立演示：运行 `.\tools\run-glass-demo.ps1`，左侧观察移动透镜，右侧按住真实应用卡片或按钮拉扯，位置固定、松手回弹；加 `-Capture` 保存移动透镜七阶段及卡片、按钮各六阶段的 GPU 帧和状态数据。实现与验证记录见 [liquid-glass-study.md](liquid-glass-study.md)。

## 测试与打包

```powershell
python -m pip install pytest

dotnet test src/YunKao.Tests/YunKao.Tests.csproj --configuration Debug
python -m pytest tests -q
node --test tests/bridge.test.js
```

最后一项需要 Node.js。测试包含任务状态、配置、历史、Worker 协议、题目解析和网页桥接。测试夹具不能替代登录真实网站后的操作检查。

生成 Windows 发布包：

```powershell
powershell -ExecutionPolicy Bypass -File build_unified_bundle.ps1
```

脚本读取根目录 `VERSION`，运行回归测试，构建 Worker 和 WinUI 应用，检查必需文件与校验值，再生成 `dist/yunkao-desktop-v<版本号>-windows-x64.zip`。打包前需安装上面的 Python 依赖、pytest 和 PyInstaller。

项目目录：

| 目录 | 用途 |
| --- | --- |
| `src/YunKao.App` | WinUI 界面、WebView2 与任务协调 |
| `src/YunKao.Core` | 配置、题库历史、任务状态和服务 |
| `worker` / `modules` | 本地协议、题目解析与文档导出 |
| `tests` / `src/YunKao.Tests` | Python、JavaScript 与 C# 回归测试 |
| `tools` | Worker 构建、发布检查与性能测量 |

此前的性能与窗口验收记录见 [ui-validation-report.md](ui-validation-report.md)，体验复测见 [experience-review.md](experience-review.md)，最新界面调整见 [frontend-review.md](frontend-review.md)。

2026-11-15 的项目检查、修复清单、发布包验证与在线验收边界见 [PROJECT_AUDIT.md](PROJECT_AUDIT.md)。打包流程也会运行桥接测试，并在独立临时目录验证冻结 Worker 的解析和 MD/TXT/DOCX 导出。
