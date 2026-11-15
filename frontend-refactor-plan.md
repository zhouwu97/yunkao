# 前端重构方案（WinUI 3 外壳）

日期：2026-11-14。分支 `feature/winui3-migration`，基于当前工作区（含未提交改动）检查。范围是 `src/YunKao.App` 的界面层：主题、控件、页面、工作台和界面状态。云考网页内容、Python Worker 和导出逻辑不在范围内。

## 一、检查结论

实际运行 Debug 版（1300×850）：标题栏、导航栏、浏览器工具栏和状态行正常显示，登录页自动缩放到 72%，Debug 构建 0 警告 0 错误。下面的问题来自代码，不是视觉缺陷。目前界面能用，但继续迭代的成本越来越高。

### 1. 业务逻辑和控件绑死（最严重）

- `Services/ExtractionCoordinator.cs`（1022 行）的构造函数直接接收 6 个控件实例：`BrowserShell`、`ExtractionPanel`、`ProgressCard`、`ExportCard`、`AiStatusCard`、`EventList`。它一边跑提取队列、AI 补全和 Worker 重试，一边直接调用控件的 `SetState`。
- 协调器在 `WorkspacePage.OnLoaded` 中创建。为了不丢任务，页面只能用 `NavigationCacheMode.Required`。业务的生命周期由页面决定。
- 按钮文案、主操作和提示的状态机写在 `ExtractionPanel.SetState()` 里（UI 层），没法单元测试。Core 里的 `WorkspaceDrawerPolicy`、`AppearancePolicy` 已经证明"纯策略类加测试"在这个项目里可行，可以照这个办法做。
- 全局用 `App.Services` 静态访问服务，没有依赖注入。

### 2. 没有数据绑定，列表在代码里拼

- `HistoryPage.xaml.cs` 在循环里 `new Grid / StackPanel / TextBlock / Button`，再用 `Application.Current.Resources["..."]` 取样式，然后塞进 `ListView.Items`。`ExportPage` 和 `DiagnosticsPage` 也一样。
- 后果：没有 DataTemplate，样式改动要去改 C#；直接塞 UIElement，ListView 的虚拟化也用不上。
- 全项目没有 ViewModel、`x:Bind` 和 `INotifyPropertyChanged`。

### 3. 两套按钮和卡片体系，残留大量"液态玻璃"代码

- 按钮有两套：`Themes/Controls.xaml` 里的 Primary/Secondary/Ghost/Quiet/Danger 样式，以及 `LiquidButton` UserControl 的 Primary/Soft/Ghost/Violet/Coral 变体。同一页面里两种混用，比如历史页的空状态用 `Button`，标题区用 `LiquidButton`。
- `LiquidButton` 是 UserControl 包了一个透明 `Button`，再加指针跟随高光（244 行）。它不支持 `Command`，禁用态和焦点框靠手工模拟，不是真正的 Button。
- `LiquidCard` 里带着 `SystemBackdropElement`、Composition 模糊层、OuterRim/InnerRim，默认都是 Collapsed。`MainWindow.xaml` 的环境光网格也是 Collapsed。`design.md` 已经定为"白色内容区、细边框、不用玻璃"，这些代码都是死代码。
- `EventList.xaml` 里写死了假数据（"刚刚 · 练习页面已打开"、"LIVE"），和 design.md 的"不显示虚构数据"冲突。它目前处于隐藏状态。

### 4. 主题令牌命名混乱，外观切换靠改共享画刷

- `Colors.xaml` 同时有 `BluePrimary`、`IceBlueColor`（别名）、`IceBlueBrush`、`LiquidSurface*`、`SurfaceBrush`（"legacy"），一种颜色有好几个名字。紫、珊瑚、琥珀、青、粉五种强调色只零星在用。
- `MainWindow.ApplyAppearance()` 在运行时用硬编码 ARGB 改写 `TitleBarBrush`、`LiquidSurfaceBrush` 等共享 `SolidColorBrush` 的 `Color`。改的是全局可变状态，每加一个外观选项就要在这里补分支。
- `App.xaml` 强制设了 `RequestedTheme="Light"`，没有 ThemeDictionaries，所以不支持深色模式和高对比度。
- XAML 里有 12 处硬编码颜色，C# 里有 38 处取资源或构造颜色。

### 5. 外壳自己实现了系统已有的控件

- `NavigationRail`（111 行 XAML、176 行 C#）自己实现了选中滑块动画和 1060 断点的紧凑模式，`MainWindow` 里还有两段重复的页面映射 `switch`。这些都是 `NavigationView`（`PaneDisplayMode=Auto`）自带的，还附带无障碍和键盘导航。
- 标题栏右侧 `Padding="20,0,144,0"` 写死给系统按钮留的空间，在 125% / 150% 缩放和 RTL 下可能对不齐。
- 页面头部各写各的：历史和导出页是 `Padding 14,12` 加 `Spacing 3`，设置和诊断页是 `12,16` 加 `8`。

### 6. 交互层面

- 提取的主操作（开始、暂停、继续）全在抽屉里。工具栏的"提取任务"按钮只负责开关抽屉，任务状态只出现在标题栏的小标签上。每次开始或暂停都要多点一次，网页也会被遮住。
- 设置页的保存方式不统一：外观即时预览，其余要点"保存设置"，离开页面后外观回滚。表头靠 `Width="{Binding ActualWidth, ElementName=SettingsScroll}"` 对齐。
- WebView 错误层的"安装 WebView2"和"重新检测"都用 Primary 样式，两个主按钮并排。
- 诊断页 5 个按钮横排，窄窗口下会挤。

## 二、目标

1. 界面状态和业务分离：提取流程不依赖任何控件，能在 Core 里测试；页面可以随时销毁重建。
2. 一套组件、一套令牌：每种控件只有一种写法，颜色只用语义名称。
3. 尽量用 WinUI 原生控件：NavigationView、TitleBar、InfoBar、CommandBar 以及社区工具包的 SettingsCard。
4. 支持浅色、深色和高对比度，保留"减少动画"。
5. 外观和行为不倒退：沿用 design.md 的视觉规范，三档窗口尺寸的验收照旧。

## 三、目标架构

```
YunKao.Core
  ├─ Services/ExtractionController      ← 从 ExtractionCoordinator 抽出：队列、AI、Worker 重试、持久化
  │     依赖 IBrowserBridge（接口），对外发出 TaskSnapshot 状态
  ├─ Presentation/TaskPresentation      ← 纯函数：TaskSnapshot → 主操作 / 文案 / 图标 / 可用性（替代 ExtractionPanel.SetState）
  └─ （已有）WorkspaceDrawerPolicy / AppearancePolicy / BrowserZoomCalculator …

YunKao.App
  ├─ App.xaml.cs                         ← Microsoft.Extensions.DependencyInjection 注册服务和 ViewModel
  ├─ ViewModels/                         ← CommunityToolkit.Mvvm（ObservableObject、RelayCommand）
  │    ShellViewModel  WorkspaceViewModel  TaskViewModel
  │    HistoryViewModel  ExportsViewModel  DiagnosticsViewModel  SettingsViewModel
  ├─ Views/                              ← 只有 x:Bind 和 DataTemplate，code-behind 只处理视图细节（焦点、动画）
  ├─ Controls/
  │    PageHeader  EmptyState  OverlayDrawer  TaskStatusStrip  BrowserShell（保留，瘦身）
  ├─ Services/WebViewBridge : IBrowserBridge   ← WebView2 适配层
  └─ Themes/
       Tokens.xaml（ThemeDictionaries: Light / Dark / HighContrast）
       Typography.xaml  Controls.xaml（按钮 / 卡片 / 输入的唯一样式）
```

关键约定：

- `ExtractionController` 是单例，生命周期跟着应用走，不跟页面。`WorkspacePage` 可以去掉 `NavigationCacheMode.Required`；WebView2 实例由 `BrowserShell` 宿主管理（见第四节阶段 4 的风险说明）。
- 后台线程的状态变化由 ViewModel 统一通过 `DispatcherQueue` 发到 UI 线程，Controller 不碰 UI。
- 横幅（`WorkspaceBannerInfo`）改成 `IUserNotificationService`，由 Shell 层的 `InfoBar` 统一显示。

## 四、分阶段实施

每个阶段单独提交，都能编译、能运行、测试全绿。按风险从低到高排列。

### 阶段 0：先建安全网（约 0.5 天）

- 记录基线截图：1040×700、1300×850、最大化三种尺寸，各截工作台、抽屉、历史、导出、诊断、设置。
- 补测试：给 `ExtractionPanel.SetState` 现有的每个分支各写一个用例（先写在 Tests 项目里，期望值抄当前行为），阶段 3 迁移后用来对照。
- 确认现有 88 项 C# 测试、34 项 Python 测试和 2 项 JS 测试全部通过。

### 阶段 1：主题令牌（约 1 天，低风险）

- 新建 `Themes/Tokens.xaml`，只保留语义令牌，用 `ThemeDictionaries` 分别给出 Light / Dark / HighContrast 的值：
  - `Accent` / `AccentHover` / `OnAccent`（品牌深蓝 #2854B0）
  - `Surface.Window` / `Surface.Layer` / `Surface.Card` / `Surface.Subtle`
  - `Stroke.Default` / `Stroke.Subtle`
  - `Text.Primary` / `Text.Secondary` / `Text.Tertiary`
  - `Status.Success|Warning|Danger|Info` 的 Fill 和 Text
- 通过覆盖 `SystemAccentColor*`，让 WinUI 原生控件（ToggleSwitch、ProgressBar、CheckBox 等）自动用品牌蓝，省去逐个设置 `Foreground`。
- 删除别名和遗留键（`IceBlue*`、`Lavender*`、`PinkMist*`、`SurfaceBrush`、`Ambient*Glow`、`GlassSheenBrush`），用全局替换把引用改成新键。
- `ApplyAppearance` 不再改画刷颜色，只切换材质（Mica、Acrylic、纯色），再把 `Surface.Window` 指向对应的透明度档位，三档 alpha 也放进令牌里。
- 去掉 `RequestedTheme="Light"`，设置里加"跟随系统 / 浅色 / 深色"。

### 阶段 2：统一组件（约 2 天，中低风险）

| 现状 | 替换为 |
| --- | --- |
| `LiquidButton`（5 种变体）+ 5 个 Button 样式 | 只用 `Button`：`AccentButtonStyle`（主）、默认样式（次）、`SubtleButtonStyle`（文字按钮）、`DangerButtonStyle`。删除 `LiquidButton` |
| `LiquidCard` + `GlassCardStyle` / `SubtleCardStyle` / `ToolbarCardStyle` | 一个 `CardStyle`（Border 样式：Card 背景、细边框、圆角 8、内边距 16）。删除 `LiquidCard`、`Glass.xaml`、`CompositionBackdropService` 中只供卡片使用的部分 |
| `StatusPill`、`StatusPillStyle` | `InfoBadge` 或一个 Border 样式 |
| `EventList`（含假数据） | 删除；诊断页直接用 ListView 加 DataTemplate |
| `AiStatusCard`（工作台中隐藏） | 内容并入诊断页；从工作台移除 |
| 各页手写的标题区 | `PageHeader` 控件（Title、Description、Status、右侧 Actions 插槽，内边距统一 24,16） |
| 历史和导出页两份几乎一样的空状态 | `EmptyState` 控件（Icon、Title、Text、Action） |
| `HoverMotionBehavior` + 列表回弹 | 删除，按 design.md "列表与导航不使用弹性变形" |

- `MotionService` 只保留抽屉进出、页面淡入和减少动画开关，其余动画先盘点调用点再决定去留。

### 阶段 3：MVVM 和协调器拆分（约 3–4 天，风险最高）

1. 引入 `CommunityToolkit.Mvvm` 和 `Microsoft.Extensions.DependencyInjection`，`App.Services` 改为容器，过渡期保留静态访问器。
2. 定义 `IBrowserBridge`（导航事件、Bridge 消息、HTTP 状态、进程失败和恢复事件、`ExecuteScriptAsync`、`FillCredentials`）。`WebViewService` 实现它。
3. 把 `ExtractionCoordinator` 中不碰 UI 的部分搬进 Core 的 `ExtractionController`：标记队列、`ParseAndSave`、`FillAi`、Worker 重试、会话代次、持久化和历史恢复。对外只暴露命令方法（Start、Pause、Stop、Clear、Restart、Restore、Export）和一个 `TaskSnapshot` 状态流（状态、计数、页面就绪、恢复中、登录页、导出进度、AI 状态）。
4. 把 `ExtractionPanel.SetState` 的分支搬成 `TaskPresentation.From(TaskSnapshot)`（纯函数），阶段 0 的用例改成测这个函数。
5. `TaskViewModel` 订阅快照，把 `TaskPresentation` 的结果映射成绑定属性和 `IRelayCommand`。`ExtractionPanel`、`ProgressCard`、`ExportCard` 改成只靠 `x:Bind` 的视图，去掉 `SetState` 和各种 `XxxRequested` 事件。
6. 标题栏状态（`MainWindow.SetTaskState`）改为绑定 `ShellViewModel.TaskStatus`。

完成标准：`ExtractionController` 和 `TaskPresentation` 不引用 `Microsoft.UI.*`，并有单元测试；`ExtractionCoordinator.cs` 删除或缩到 150 行以内的适配层。

### 阶段 4：外壳和页面重写（约 3 天，中风险）

**外壳**

- `NavigationRail` 换成 `NavigationView`（`PaneDisplayMode=Auto`，`CompactModeThresholdWidth=1060`，设置放在 `FooterMenuItems`）。删除 `MainWindow` 中重复的页面映射，改用一张 `Dictionary<string, Type>`。
- 标题栏改用 WinAppSDK 的 `TitleBar` 控件；如果当前版本的定制能力不够，就保留自绘，但用 `AppWindow.TitleBar.RightInset` 计算右侧留白。
- 横幅统一由 Shell 层的 `InfoBar` 显示，各页面不再各放一个。

**工作台**

- 在 `BrowserShell` 工具栏加 `TaskStatusStrip`：状态点、"已保存 N 题"和当前主操作（开始 / 暂停 / 继续）。常用操作不必打开抽屉；抽屉只放详细进度、导出和更多操作。
- 抽屉抽成 `OverlayDrawer` 控件（右侧覆盖、遮罩、Esc 关闭、焦点归还、减少动画）。`WorkspacePage.xaml.cs` 目标是 50 行以内。
- 风险：去掉页面缓存后 WebView2 会随页面重建。方案 A：WebView2 放在 Shell 层常驻，切到其他页时隐藏（推荐，网页登录状态不会丢）；方案 B：保留 `NavigationCacheMode.Required`，只完成其余拆分。阶段 4 开始前先做 A 的小型验证。
- WebView 错误层：主按钮"安装 WebView2"，次按钮"重新检测"。

**列表页**

- 历史、导出、诊断页改为 `ListView ItemsSource="{x:Bind VM.Items}"` 加 `DataTemplate`，用 `IncrementalLoadingCollection` 或保留"加载更多"按钮，但由命令驱动。
- 诊断页操作放进 `CommandBar`（主操作：复制、刷新；溢出菜单：导出日志、打开日志），窄窗口自动收起。

**设置页**

- 改用社区工具包的 `SettingsCard` / `SettingsExpander`，分组为账号、导出、AI、外观、数据与诊断。
- 保存方式统一为即时保存：每项变更防抖 500 ms 写入，标题区显示"已保存"。API Key 和密码这类敏感项仍要点按钮确认。去掉表头的 `ActualWidth` 绑定，改用 `MaxWidth=880` 的居左容器。

### 阶段 5：验收和打磨（约 1–1.5 天）

- 三种窗口尺寸对照阶段 0 的基线截图逐页检查，再加 125% 和 150% 系统缩放（以前没测过）。
- 浅色、深色、高对比度三种主题都检查一遍；文字对比度达到 WCAG AA（正文 4.5:1）。
- 只用键盘走完全流程：Tab 顺序、Esc 关闭抽屉、Ctrl+L / F5 / Alt+← →；用讲述人读导航、任务状态和抽屉。
- 开启减少动画后，所有位移动画都应关闭。
- 更新 design.md（令牌表、组件清单），从 README 删除过时的"液态玻璃"描述。

## 五、工作量和顺序

| 阶段 | 内容 | 预估 | 风险 | 依赖 |
| --- | --- | --- | --- | --- |
| 0 | 基线和对照测试 | 0.5 天 | 低 | — |
| 1 | 主题令牌、深色模式 | 1 天 | 低 | 0 |
| 2 | 统一组件 | 2 天 | 中低 | 1 |
| 3 | MVVM 和协调器拆分 | 3–4 天 | 高 | 0 |
| 4 | 外壳和页面重写 | 3 天 | 中 | 2、3 |
| 5 | 验收打磨 | 1–1.5 天 | 低 | 4 |

阶段 1–2（样式）和阶段 3（逻辑）改动的文件基本不重叠，可以并行，在阶段 4 汇合。共约 10–12 个工作日。

## 六、预期删除和新增

- 删除：`LiquidButton`、`LiquidCard`、`StatusPill`、`EventList`、`AiStatusCard`、`NavigationRail`、`HoverMotionBehavior`、`Glass.xaml`、`LiquidBackdropMode`，以及 `Colors.xaml` 中的别名，粗估减少 1,200–1,500 行。
- 新增：`Tokens.xaml`、6 个 ViewModel、`TaskPresentation`、`ExtractionController`、`IBrowserBridge`、`PageHeader`、`EmptyState`、`OverlayDrawer`、`TaskStatusStrip`。
- 新依赖：`CommunityToolkit.Mvvm`、`CommunityToolkit.WinUI.Controls.SettingsControls`、`Microsoft.Extensions.DependencyInjection`。

## 七、暂不做

- 文案迁移到 `.resw` 多语言资源：目前只有中文用户，等有需求再做，但新写的 ViewModel 不要拼接可见文案，便于以后迁移。
- 改动云考网页本身的样式（仍只调整外壳和缩放）。
- 更换技术栈（如改成 Web 前端或 Avalonia）：WinUI 3 加 WebView2 已经满足需求，问题出在分层，不在框架。
