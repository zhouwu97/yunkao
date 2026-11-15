# WinUI 3 工作台设计 QA 与验收规范

- framework: WinUI 3 / Windows App SDK (C# + XAML)
- design system: Windows 11 Fluent Design System
- primary material: Desktop Acrylic (fallback Mica → Solid) + Content Surface (Navigation Rail & Cards)
- responsive model: Browser-first (任务抽屉始终 Overlay，浏览器列不因抽屉改变宽度)

## 核心设计验收基线

### 1. 视区与布局响应式矩阵

窗口尺寸按物理像素记录；`ActualWidth` 是 DIP，需结合实际 DPI 换算。2026-10-07 实测结果和未覆盖范围见 [ui-validation-report.md](ui-validation-report.md)。

| 窗口分辨率 | 预期布局模式 | 抽屉行为 | WebView 宽度保护 |
| :--- | :--- | :--- | :--- |
| **1024 × 768** | Narrow | Overlay Drawer | 占满浏览器可用区，不因抽屉展开而变化 |
| **1280 × 800** | **Narrow (核心基线)** | **Overlay Drawer** | **打开/关闭抽屉时 ActualWidth 恒定，网页无横向滚动条** |
| **1366 × 768** | Narrow | Overlay Drawer | 占满浏览器可用区，抽屉平滑浮层覆盖 |
| **1440 × 900** | Narrow | Overlay Drawer | 占满浏览器可用区，无网页排版抖动 |
| **1600 × 900** | Overlay Drawer | 最大 344px 浮层 | 浏览器宽度不因抽屉改变 |
| **1920 × 1080** | Overlay Drawer | 最大 344px 浮层 | 浏览器宽度不因抽屉改变 |

### 2. 1280×800 核心基准测试要求 (P0)

1. **WebView 几何尺寸恒定**：
   - 打开/关闭任务面板时，`Browser` 的 `ActualWidth` 不得发生任何改变。
   - 打开任务抽屉不得触发云考网页（登录页、宣传横幅、练习题库）产生横向滚动条或重排抖动。
2. **动效隔离**：
   - 抽屉展开仅执行 `TranslateX: 24 → 0` 与 `Opacity: 0 → 1`（200ms），关闭执行 `TranslateX: 0 → 24`（180ms）。
   - 网页容器绝不参与 Scale/Blur/Opacity 动效。

### 3. Fluent 材质与对比度规范 (P0)

1. **主窗背景**：默认请求 `Desktop Acrylic`；不可用时依次降级为 `Mica` 和 `Solid` (`#EEF2F6`)。
2. **左侧 Rail**：纯净 `RailSurfaceBrush`（~95% 不透明度），不嵌套独立 DesktopAcrylicBackdrop。
3. **任务抽屉**：
    - Overlay 态：使用唯一的 Composition backdrop host + `DrawerSurfaceBrush` Tint；Composition 不可用时保留高 Tint fallback，背底网页文字不得干扰按钮与状态标签阅读。
4. **网页容器**：保持 `#FFFFFF` / 实色白底，确保 WebView2 网页对比度与清晰度。

### 4. 状态机与操作焦点规范 (P1)

- **Idle（待机）**：Primary 按钮禁用显示“等待进入练习页”，辅助操作全部收进 `···` 菜单。
- **Ready（就绪）**：唯一的蓝色 Primary 按钮“开始提取”。
- **Running（提取中）**：Primary 按钮动态变为“暂停提取”，显示已保存/AI待补/平均速度三项关键指标。
- **Paused（暂停）**：Primary 按钮变为“继续提取”，右侧提供“停止”动作。
- **Completed（已完成）**：Primary 按钮变为“导出题库”，导出卡片自动激活可用。
- **Error（异常）**：Primary 按钮提示“重新开始”。
- **ExportCard 联动**：题目数 = 0 时显示安静空状态“提取到题目后可在此快速导出”，不展示多余禁用按钮；题目数 > 0 时激活 PDF/DOCX/更多及练习版开关。

### 5. 无障碍与降级

- **DPI 适配**：125% 与 150% 系统缩放下，按钮文字、图标与圆角完整闭合，无裁剪截断。
- **Reduced Motion**：当系统开启“关闭动画”时，抽屉和页面立即切换，不执行位移动画。
- **High Contrast**：高对比度模式下自动停用所有半透明材质并降级为实体 Solid 背景。
