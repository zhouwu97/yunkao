# 融智云考桌面端：WinUI 3 工作台与 Fluent UI 优化方案

## 架构结论

本项目已完成从旧版 PySide6 悬浮窗向 **WinUI 3 / Windows App SDK 原生桌面架构** 的全面迁移。当前产品的核心链路为：
「WebView2 浏览云考网页 → 识别练习页面 → 循环提取 → Background Worker 解析与 AI 补全 → 多格式导出」。

在现代化 Fluent 桌面重构中，工作台遵循以下核心原则：
1. **网页即主角**：WebView2 是核心工作区。外壳界面保持平静、克制，不强行压缩网页排版，不引发横向滚动条。
2. **Overlay Drawer 保护网页宽度**：在常用屏幕宽度（< 1520~1560px 窗口 / 可用工作区 < 1396px）下一律采用覆盖式抽屉（Overlay Drawer），保持 `Browser` 100% 宽度不变，打开/收起抽屉绝不触发 WebView 几何 Resize。仅在超宽屏且能保证浏览器舒适宽度（>= 1040px）时才允许 Dock 右栏。
3. **Mica 基础层与严谨材质层级**：主窗口采用 Windows 11 原生 `Mica` 材质，左侧 Rail 采用 ~95% 高不透明度 Content Layer，抽屉采用高 Tint 不透明度的半透明表面防透字，网页内容区采用纯净实色白底保证可读性。
4. **单主操作状态驱动**：ExtractionPanel 严格基于 6 大任务状态（待机/就绪/提取中/已暂停/已完成/异常）派生**唯一 Primary Action** 按钮，辅助操作全部收进 `···` 更多菜单中。
5. **按需激活导出**：ExportCard 在无题时显示安静提示，有题后自动展开格式选择（PDF/DOCX/MD/TXT）与练习版参数控制。

## 优先级规范

### P0：布局稳定性与材料层级
- **Workspace 响应式规则**：断点由 `MinComfortableBrowserWidth (1040) + DockedControlWidth (344) + BrowserColumnSpacing (12) = 1396px` 预算驱动。
  - 内容区 < 1396px（整窗约 < 1520px）：一律使用 Overlay Drawer，Browser 占满全宽。
  - 内容区 >= 1396px（超宽屏）：允许 Dock 344px 右栏，浏览器仍保持 >= 1040px。
- **材质分级（Mica → Solid）**：
  - 窗口基础层：Mica
  - 左侧 NavigationRail：半透明 Content Layer（~95% 不透明度），不叠加 DesktopAcrylic
  - 网页容器：实色纯净白底，杜绝文字泛白与背景透视
  - 任务 Drawer：Overlay 态使用高 Tint 半透明 Surface，Dock 态使用普通卡片 Surface

### P1：状态驱动与动效反馈
- **任务状态流转**：
  - `Idle`（待机）：Primary 按钮禁用显示“等待进入练习页”，提示检测到练习页面后方可开始。
  - `Ready`（练习页就绪）：Primary 按钮蓝色高亮“开始提取”。
  - `Running`（提取中）：Primary 按钮动态变为“暂停提取”，显示进度与速率。
  - `Paused`（暂停）：Primary 按钮变为“继续提取”，可停止或重新开始。
  - `Completed`（已完成）：Primary 按钮变为“导出题库”，导出卡片解除就绪。
  - `Error`（异常）：Primary 按钮提示“重新开始”。
- **动效标准**：
  - Drawer 动画：`TranslateX: 24 → 0`，`Opacity: 0 → 1`，时长 200ms CubicEaseOut；关闭 180ms。
  - 遮罩 Scrim：`Opacity: 0 → 0.08`，时长 160ms。
  - 按钮按下：微缩放 `Scale: 1 → 0.98`（80ms）及恢复 `0.98 → 1`（120ms）。
  - 全局遵守系统 `ReducedMotion` 偏好。

### P2：批量处理与后台协同
- **多进程解析与 AI 队列**：通过 Background Worker 进程解耦 HTML 解析与 OCR，GUI 线程保持 60fps 响应。
- **导出事务与去重**：导出时对题目做内存快照，历史记录落本地 SQLite，支持中断会话恢复。

## 验收测试基准

| 窗口尺寸 / 状态 | 预期行为 |
| :--- | :--- |
| **1024 × 768** | Rail 紧凑模式，任务栏为 Overlay Drawer，WebView 占满全宽 |
| **1280 × 800** | **核心验收点**：打开/关闭任务抽屉，WebView ActualWidth 保持恒定，页面无横向滚动条 |
| **1366 × 768** | Overlay Drawer，不压缩网页 |
| **1440 × 900** | Overlay Drawer，不压缩网页 |
| **1600 × 900** | 满足最低 1040px 浏览器宽度，允许 Dock 344px 右栏 |
| **1920 × 1080** | Dock 模式，右栏 344px，浏览器保持 >= 1400px 宽广视区 |
| **125% / 150% DPI** | 文本与按钮边缘闭合，不出现溢出与截断 |
| **Transparency Off** | 自动平滑降级到 Solid 实体表面 |
| **Reduced Motion** | 抽屉与切页无大位移动画，直接即时切换 |

