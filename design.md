# YunKao 桌面界面

面向整理练习题的学生和教师。核心场景是"在网页里找到练习页 → 连续提取 → 导出"，因此网页要大、任务状态要一直看得见、其余页面要安静好读。

## 布局与插画

工作台将地址栏放在常驻 WebView 上方。导航和任务卡共用左栏。窗口宽度小于 980 DIP 时，左栏自动缩到 86 DIP，展开宽度为 250 DIP。用户也可以手动切换，紧凑模式下点任务按钮即可看到完整的任务卡。

樱小路露娜的银发、象牙色制服和黑色缎带决定了整窗配色。浅色使用暖象牙与灰紫纸面，深色使用墨紫。斜线与圆弧延伸到整个窗口。工作台和窄窗在左栏的空位显示人物，宽屏的记录、诊断与设置页在内容旁预留人物栏。高对比度模式使用系统色。

导航透镜、地址工具栏、任务卡、内容分组和辅助按钮共用 Win2D / Direct2D 像素着色器。变形后的距离字段同时决定轮廓、法线、圆弧截面折射、光谱分离和方向性高光。背景按原生像素比例取样并共享缓存，不把越界透明区域缩放进玻璃。默认使用轻微染色和低模糊，保留背景纹理；导航只绘制移动透镜，不叠加白色悬停条。导航拖动时独立的图标与文字纹理参与折射，六个弹簧连续处理位置、压力、拉伸和弯曲；松手后选页，取消手势回到原页。卡片和玻璃按钮只在抓取边缘局部形变，按压鼓起、松手产生可见回弹，主体与远侧边缘固定，文字不缩放，内部按钮、输入框与滚动保留原生行为。纯色、高对比度与关闭系统透明效果时使用实体表面，减少动画时重置形变并直接定位选中项。人物留在侧栏，按可用空间放大，半身素材底部由任务卡自然遮挡。

浏览器成功载入时独占网页视口。导航失败时保留浏览器实例，收起网页画面，在整窗插画上显示玻璃错误卡和恢复操作。

角色来自 Navel《近月少女的礼仪》。本项目内的立绘为 AI 生成的非官方同人图，参考了发行方的角色设定。[角色介绍](https://cloud.moeapp.net/special/tsuriotsu.html)

## 令牌（Themes/Tokens.xaml）

`Frame*` 外框、`Sidebar*` 侧边栏文字与条目、`Glass*` 外框上的卡片、`Surface*` 内容卡片、`Text*` 内容文字、`Control*` 按钮输入、`Segment*` 分段控件、`Accent*` 品牌蓝、`Status*` 状态点、`*Soft / *Text` 状态底色与文字。Light / Dark / HighContrast 三套，随主题变化的颜色一律 `{ThemeResource}` 引用。

## 组件

| 组件 | 用途 |
| --- | --- |
| `BrowserShell` | 常驻 WebView 宿主，实现 `IBrowserActions`，圆角裁切与宽度适配 |
| `BrowserBar` | 工作台顶部的网页导航、地址栏与状态行 |
| `TaskCard` | 常驻任务卡（展开 / 紧凑） |
| `NavigationRail` | 页面导航，选中标记平移过渡 |
| `PageHeader` / `EmptyState` | 页面标题区、空状态 |
| `GlassPanel` | 根据窗口材质显示磨砂玻璃或实体任务卡 |
| 样式 | `PrimaryButtonStyle` `SecondaryButtonStyle` `DangerButtonStyle` `SubtleButtonStyle` `IconButtonStyle` `SidebarButtonStyle` `SidebarIconButtonStyle` `SegmentRadioStyle` `GroupStyle` `RowItemStyle` |

## 状态与数据

- `TaskPresentation`（Core，纯函数，单元测试覆盖）：任务状态 → 文案、主操作、状态类别。
- `TaskViewModel` / `BrowserViewModel`：应用级单例，业务层与浏览器宿主只更新它们，界面只绑定。
- `ExtractionCoordinator` 不引用任何控件；横幅经 `NotificationService` 由外壳 InfoBar 显示。
- 列表页：`HistoryViewModel` / `ExportsViewModel` / `DiagnosticsViewModel` + DataTemplate。

## 交互

导航支持拖选、方向键和 Home / End，页面淡入。"减少动画"或系统关闭动画时直接切换。快捷键：Ctrl+L 地址栏、F5 / Ctrl+R 刷新、Alt+← → 前进后退。数据与提示使用真实状态。

## 检查

浅色 / 深色、展开 / 收起侧栏、工作台 / 历史 / 设置已用真实窗口截图核对（截图工具会遮挡 WebView2 进程，需用窗口截图）。登录后的连续提取、125% / 150% 缩放、键盘全流程尚未在新布局下验证。
