# 悬浮窗展示完整性设计 QA

- source visual truth path: `C:\Users\haha\AppData\Local\Temp\codex-clipboard-3ab43bc5-19a3-429a-a6e4-24c55fb3f567.png`
- implementation screenshot path: `C:\Users\haha\AppData\Local\Temp\yunkao-overlay-after.png`
- comparison image path: `C:\Users\haha\AppData\Local\Temp\yunkao-overlay-comparison.png`
- viewport: 悬浮窗展开态，逻辑宽度 320 px
- source pixels: 548 × 364；其中悬浮窗裁切区域约 401 × 281
- implementation pixels: 320 × 248
- CSS size: 不适用，界面为 PySide6 原生桌面组件
- density normalization: 源截图约为 1.25 倍系统缩放；比较图按面板宽度对齐，保留各自纵横比
- state: 登录页、练习版关闭、导出按钮禁用、底部操作栏可见

## Findings

- [P1] 展开态固定高度导致底部操作栏被压缩
  - Location: `TampermonkeyFloatingWindow` / `ui/main_window.py`
  - Evidence: 修复前面板写死为 320 × 226；源截图中按钮底边贴近窗口边缘，底部留白和圆角区域被裁切。修复后高度按布局计算为 248 px，三个按钮和底部边距完整显示。
  - Impact: 高 DPI、字体度量或状态文字变长时，主要操作区可能显示不全。
  - Fix: 取消展开态固定高度，保留固定宽度；根据布局 `sizeHint()` 与最小高度动态调整，并在首次显示、进度更新和状态更新后重新计算。

## Required Fidelity Surfaces

- Fonts and typography: 字体族、字号、字重及层级保持不变；状态和进度文字增加自动换行，不再被高度裁切。
- Spacing and layout rhythm: 底部按钮下方恢复完整边距，圆角轮廓闭合；展开态最小高度为 248 px，并允许内容继续增长。
- Colors and visual tokens: 白色面板、灰色边框、钴蓝主按钮、绿色成功状态均保持原主题。
- Image quality and asset fidelity: 此组件没有图片资产；截图中边框、阴影和文字均清晰。
- Copy and content: 标题、返回、设置、进度、练习版及三个操作按钮文案均完整保留。

## Full-view comparison evidence

对照图同时包含修复前和修复后完整悬浮窗。修复后底部操作栏不再触碰窗口下边缘，所有按钮轮廓、圆角和底部留白均可见。

## Focused region comparison evidence

无需单独裁切：悬浮窗仅 320 逻辑像素宽，对照图中的底部操作区以可读尺寸完整呈现，按钮边界和留白可直接判断。

## Comparison history

1. 初次对照发现 P1：固定 226 px 高度无法容纳当前按钮最小高度、行间距和 DPI 字体度量。
2. 修复：展开态改为内容自适应，新增首次显示及文字变化后的高度刷新；进度和状态标签允许换行。
3. 修复后证据：实现截图为 320 × 248，底部三个按钮与面板下边缘之间留有完整空间；回归测试验证按钮底部始终位于悬浮窗内容区域内。

## Implementation Checklist

- [x] 展开态按内容和 DPI 计算高度
- [x] 折叠与展开尺寸约束正确恢复
- [x] 长进度和状态文字可换行
- [x] 底部操作栏几何边界回归测试
- [x] 12 项自动化测试通过

final result: passed
