# WinUI 3 工作台设计 QA 与验收规范

- framework: WinUI 3 / Windows App SDK (C# + XAML)
- design system: Windows 11 Fluent Design System
- primary material: Mica (Window background) + Content Surface (Navigation Rail & Cards)
- responsive model: Browser-first (Overlay Drawer < 1396px content width; Docked Sidebar >= 1396px)

## 核心设计验收基线

### 1. 视区与布局响应式矩阵

| 窗口分辨率 | 预期布局模式 | 抽屉行为 | WebView 宽度保护 |
| :--- | :--- | :--- | :--- |
| **1024 × 768** | Narrow | Overlay Drawer | 占满全宽（~950px），不因抽屉展开而变化 |
| **1280 × 800** | **Narrow (核心基线)** | **Overlay Drawer** | **ActualWidth 恒定保持约 1170px，网页无横向滚动条** |
| **1366 × 768** | Narrow | Overlay Drawer | 占满全宽（~1250px），抽屉平滑浮层覆盖 |
| **1440 × 900** | Narrow | Overlay Drawer | 占满全宽（~1320px），无网页排版抖动 |
| **1600 × 900** | Wide (Docked) | 344px 常驻右栏 | 浏览器宽度仍稳定保留 >= 1150px |
| **1920 × 1080** | ExtraWide (Docked) | 344px 常驻右栏 | 浏览器宽度保留 >= 1470px 宽广视区 |

### 2. 1280×800 核心基准测试要求 (P0)

1. **WebView 几何尺寸恒定**：
   - 打开/关闭任务面板时，`Browser` 的 `ActualWidth` 不得发生任何改变。
   - 打开任务抽屉不得触发云考网页（登录页、宣传横幅、练习题库）产生横向滚动条或重排抖动。
2. **动效隔离**：
   - 抽屉展开仅执行 `TranslateX: 24 → 0` 与 `Opacity: 0 → 1`（200ms），关闭执行 `TranslateX: 0 → 24`（180ms）。
   - 网页容器绝不参与 Scale/Blur/Opacity 动效。

### 3. Fluent 材质与对比度规范 (P0)

1. **主窗背景**：默认优先使用 `Mica` 材质；系统不支持时优雅降级为 `Solid` (`#EEF2F6`)。
2. **左侧 Rail**：纯净 `RailSurfaceBrush`（~95% 不透明度），不嵌套独立 DesktopAcrylicBackdrop。
3. **任务抽屉**：
   - Overlay 态：使用 `DrawerSurfaceBrush`（高 Tint 不透明度），背底网页文字只能轻微透出轮廓，不得干扰按钮与状态标签阅读。
   - Docked 态：使用透明/实体卡片 Surface，不使用 Acrylic 穿透。
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
