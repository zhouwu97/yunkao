# YunKao 桌面界面

面向整理练习题的学生和教师。核心场景是"在网页里找到练习页 → 连续提取 → 导出"，因此网页要大、任务状态要一直看得见、其余页面要安静好读。

## 布局

```
┌──────────────────── 纸色外框（暖白，窗口材质可轻微透出）────────────────────┐
│ 侧边栏（直接放在外框上）    │  内容卡片（白底、细线、圆角 10）      │
│  · 浏览器控制 + 玻璃地址栏  │   工作台：常驻网页                    │
│  · 任务卡（常驻）           │   其他页：覆盖在网页之上的 Frame      │
│  · 页面导航 / 设置 / 收起   │                                       │
└──────────────────────────────────────────────────────────────────┘
```

- **网页常驻**：WebView 永远在内容卡片中，历史、导出、诊断、设置通过 Frame 覆盖其上，切换页面不会重建网页，登录状态始终保留。
- **任务卡常驻**：状态点 + 状态、已保存大号计数与进度、下一步提示、唯一主操作、快捷导出（PDF / DOCX / 更多格式 / 练习版）。不再有抽屉。
- **侧边栏两档**：窗口宽度 ≥ 1100 DIP 时展开（256），更窄时自动收为图标栏（78）；底部按钮可手动切换。收起后地址栏变为弹出框，任务卡变为一颗状态按钮，点开是完整任务卡。

## 视觉：纸感极简

- **暖白纸面**：外框与侧边栏同为 #F6F5F2，内容卡片纯白、1px 细线描边、圆角 10，没有阴影。深色为暖炭色（外框 #191918、内容 #202020），原则相同。
- **没有装饰**：不用渐变、环境光、模糊、高光、亮边。层次只靠底色深浅、细线和留白。
- **黑字为主**：正文 #1F1E1B，次要文字 #5F5D57，说明 #8C8A83。
- **蓝色克制**：墨蓝 #2F5BD8 只用于主操作按钮、开关/进度条和链接；图标底块、状态标签都用中性纸色。
- **控件**：主按钮为墨蓝实色；次按钮白底细描边；分段控件为纸色轨道 + 白色滑块；导航选中项为浅纸色块，切换时平滑滑动、轻微拉伸。
- 字体：中文 Microsoft YaHei UI，数字 Segoe UI Variable。页面标题 24 / 分组 14 半粗 / 正文 14 / 说明 12。
- 外框透明度默认几乎不透明（标准 0.96），选"通透"时桌面材质才轻微透出。`GlassPanel` 保留材质能力，但在本方向的样式中关闭了模糊与高光。

## 令牌（Themes/Tokens.xaml）

`Frame*` 外框、`Sidebar*` 侧边栏文字与条目、`Glass*` 外框上的卡片、`Surface*` 内容卡片、`Text*` 内容文字、`Control*` 按钮输入、`Segment*` 分段控件、`Accent*` 品牌蓝、`Status*` 状态点、`*Soft / *Text` 状态底色与文字。Light / Dark / HighContrast 三套，随主题变化的颜色一律 `{ThemeResource}` 引用。

## 组件

| 组件 | 用途 |
| --- | --- |
| `BrowserShell` | 常驻 WebView 宿主，实现 `IBrowserActions`，圆角裁切与宽度适配 |
| `BrowserBar` | 侧边栏顶部：导航按钮、玻璃地址栏、状态行 |
| `TaskCard` | 常驻任务卡（展开 / 紧凑） |
| `NavigationRail` | 页面导航，选中块液态拉伸过渡 |
| `PageHeader` / `EmptyState` | 页面标题区、空状态 |
| `GlassPanel` | 外框上的卡片容器（本方向为实体白卡） |
| 样式 | `PrimaryButtonStyle` `SecondaryButtonStyle` `DangerButtonStyle` `SubtleButtonStyle` `IconButtonStyle` `SidebarButtonStyle` `SidebarIconButtonStyle` `SegmentRadioStyle` `GroupStyle` `RowItemStyle` |

## 状态与数据

- `TaskPresentation`（Core，纯函数，单元测试覆盖）：任务状态 → 文案、主操作、状态类别。
- `TaskViewModel` / `BrowserViewModel`：应用级单例，业务层与浏览器宿主只更新它们，界面只绑定。
- `ExtractionCoordinator` 不引用任何控件；横幅经 `NotificationService` 由外壳 InfoBar 显示。
- 列表页：`HistoryViewModel` / `ExportsViewModel` / `DiagnosticsViewModel` + DataTemplate。

## 交互

导航选中块平滑滑动；页面淡入；"减少动画"或系统关闭动画时直接切换。快捷键：Ctrl+L 地址栏、F5 / Ctrl+R 刷新、Alt+← → 前进后退。数据与提示使用真实状态。

## 检查

浅色 / 深色、展开 / 收起侧栏、工作台 / 历史 / 设置已用真实窗口截图核对（截图工具会遮挡 WebView2 进程，需用窗口截图）。登录后的连续提取、125% / 150% 缩放、键盘全流程尚未在新布局下验证。
