# 液态玻璃独立 Demo

验证问题：同一个曲面是否能同时控制轮廓、法线、背景／文字折射和高光，并在慢拖、快甩、反向、松手后连续变化？

运行：

```powershell
.\tools\run-glass-demo.ps1
```

左侧在网格、文字和插画上拖动长透镜；右侧使用主程序的真实卡片、按钮和输入框。按住空白处，整块玻璃和正文小幅跟手，速度产生拉伸和弯曲，松手回到原位。卡片/按钮位移上限 12/4 DIP，增益 0.18。点“自动演示”可重复观察移动透镜的输入轨迹。磨砂、仅模糊、深色与减少动画只改变演示内存状态，不读取或保存主程序设置。

保存按压、拖动和释放阶段的原生渲染图与弹簧状态：

```powershell
.\tools\run-glass-demo.ps1 -Capture
```

输出在 `build/liquid-glass-demo-captures/`，自动演示结束后关闭窗口。`motion.json` 记录移动透镜的位置、按压、拉伸和弯曲；`native-motion.json` 与卡片、按钮各六张图片记录跟手、释放、回弹和减少动画。自动捕获由模型驱动；真实鼠标的输入路由仍需在原生窗口中验证。像素验证使用同一份生产着色器：

```powershell
dotnet run --project tools/GlassProbe/GlassProbe.csproj -c Release -- build/glass-probe
```

参考为 [FlutterLiquidglass](https://github.com/zhouwu97/FlutterLiquidglass/tree/2c281279bdde506d65a035442638a90c031bd593)。这里采用 Win2D / Direct2D / ComputeSharp 的本地像素着色器，不运行 Flutter，也不使用 WebView 画玻璃。许可证随 Demo 的 `Assets/GlassNotices/` 分发。

开关按住时转为液态玻璃，左右拖动连续改变轨道颜色，松手提交状态并恢复普通滑块。真实输入脚本 `capture-toggle.py` 检查预览期间状态不提前提交、左右拖动、取消、键盘和 UIA。
