# 液态玻璃参考与实现记录

研究与验证日期：2026-11-24。先用独立 Demo 验证光学与连续形变，再接入 WinUI 组件。

## 研究的实现

- [FlutterLiquidglass / 2c281279](https://github.com/zhouwu97/FlutterLiquidglass/tree/2c281279bdde506d65a035442638a90c031bd593)：读过 shader、renderer、scene、dynamics、controller、手势、像素和运动测试，并操作在线演示的网格、清透与拖动。
- [LiquidGlassWinUI / 647fd60c](https://github.com/luckyelysia/LiquidGlassWinUI/tree/647fd60c3ded87dc2a7472f813ec86a265529509)：研究 HLSL 的采样留白、曲面和光谱重建。其内部 DWM hook 绑定 Windows App SDK 2.2.0，本项目不引入该路径。
- [ComputeSharp / v3.2.0](https://github.com/Sergio0694/ComputeSharp/tree/v3.2.0)：核对复杂采样、场景坐标、变换映射与 Win2D 互操作，使用 NuGet 的 Direct2D 像素效果。

## 参考决定的实现

| Before | After | Why |
| --- | --- | --- |
| 固定法线贴图，外层缩放和斜切 | 在 shader 内逆变换和弯曲 SDF，再由该字段求法线 | 轮廓、折射和光照随形变一起更新 |
| 圆周亮描边 | 方向性 Fresnel、两侧不同强度的高光与内侧遮蔽 | 用曲面表达厚度 |
| 每个面板上传整窗背景 | 同一场景按设备和版本共享纹理 | 控制 GPU 背景复制 |
| 文字独立叠在玻璃之上 | 移动导航透镜将独立前景纹理纳入采样 | 图标与背景一起折射 |
| 快慢拖动只有位移不同 | 六弹簧保留速度，速度驱动拉伸，滞后驱动双轴弯曲 | 反向与释放保持运动连续 |
| 卡片只有局部鼓起 | 按当前参考整块卡片及正文小幅跟手，松手回原位 | 最新交互选择覆盖之前的固定局部拉扯要求 |
| 只看静态截图 | 真实像素检查及自动输入轨迹 | 能区别真实折射与仅模糊效果 |

`LiquidGlassShader` 以同一个距离字段生成覆盖率、法线、向内的圆弧截面折射、七通道光谱重建和方向光。拉伸矩阵的行列式为 1，避免速度变大就无故膨胀。大面板保留正文清晰；移动透镜中的前景参与采样。

效果在 96 DPI 的物理像素纹理中执行，逻辑参数统一乘同一缩放因子，最后一次呈现才转换为 XAML DIP。这解决复杂采样器在隐式 DPI 补偿图中的失败。纹理尺寸有上限。

WinUI 的 `RenderTargetBitmap` 还会包含超出布局范围的子元素。背景捕获保留原生像素比例，GPU 缩小纹理后按相同坐标换算，不能直接把整个截图宽度适配到布局宽度，否则右侧的透明留白会变成黑条。共享纹理和表面纹理均限制在最长 2048 像素、总计 1,048,576 像素以内。

按实际界面反馈将标准染色降至 6%，导航进一步减半，面板默认模糊为 0.25 DIP，导航为 0 DIP。人物保持在侧栏、贴近任务卡，半身素材的底部遮挡到卡片边缘，避免浅色衣服干扰任务文字。窄窗口任务弹层采用它所在内容区域的主题，保持透明后的文字对比度。

### 当前交互与材质

参考 [design-systems / 7a424c93](https://github.com/ruiqichenbiec/design-systems/tree/7a424c93b9be13cf835b91ea67495fec2ec0584e)，读取并实际执行 `JellyController.tick`、`Spring` 与 `jellyMatrix`。按用户后续选择，卡片/正文随组件暂时位移，速度驱动拉伸、滞后驱动局部曲面弯曲，松手弹回原位。按最新幅度反馈，宿主位移增益为 0.18，上限卡片 12 DIP、按钮 4 DIP；保留参考弹簧系数。210 帧上游轨迹作为独立 fixture 对照，覆盖换向、释放与再次抓取。

卡片默认 28 DIP 圆角、0.25 DIP 模糊、28 DIP 折射、曲面带按短边 22% 扩展到最多 40 DIP，避免呈现平面的磨砂片。字体清晰叠加，但和卡片共同施加 native affine pose。shader 不重复施加拉伸，只处理 lag bend；逆矩阵用于世界法线与采样偏移。

导航保留原本页面选择行为与独立移动透镜。静止及拖动均采样文字，折射 14 DIP、色散 0.5、方向边缘高光 0.85。深色导航中去除 blur、tint、按压椭圆泛光，以免中心出现灰色雾面；保留曲面边缘折射。Demo 的普通长透镜仍有自己的轻微 tint/frost，二者共享 shader，外观参数按场景选择。

原生材质曾出现法线矩阵为全零的问题：ComputeSharp `Float4` CPU 运算使用了 shader intrinsic，占位运算返回默认值。改为先做标量运算、最后构造 uniform；现在原生捕获检查实际 uniform，并与独立像素探针分层验收。

应用面板采样其实际下方的独立装饰层，不把玻璃和正文捕获进去。背景在尺寸、主题、插画或显式失效后更新，不宣称实时捕获 WebView2 网页。独立 Demo 则直接在 GPU 上绘制网格、文字和插画。

任务卡、诊断摘要、事件、任务动态、AI 状态卡及按钮共用 `LiquidGlassInteraction`。原生文本/按钮/开关各自拥有输入，父卡片不抢手势。真拖动临时抑制按钮的 Release 激活路径，轻点、键盘和自动化保持原语义；仅转移 capture 在本 WinUI 模板中不足以防止点击。减少动画、取消与卸载恢复状态。

设置页六个开关使用 `LiquidGlassToggleSwitch`：按住时滑块变为清透玻璃，轨道颜色随滑块位置连续混合，松手才提交 IsOn 并弹向端点，玻璃随压力弹簧淡回普通白色滑块。向右再拖回左侧不提前保存；取消回到原状态。保留 UIA TogglePattern、键盘空格/方向键、禁用态与系统高对比回退。WinUI ToggleSwitch 为 sealed，采用独立 Control 与显式可访问性 peer，而非伪造派生类。

## 验证入口

```powershell
.\tools\run-glass-demo.ps1
.\tools\run-glass-demo.ps1 -Capture
dotnet run --project tools/GlassProbe/GlassProbe.csproj -c Release -- build/glass-probe
dotnet test src/YunKao.Tests/YunKao.Tests.csproj -c Debug
```

当前验证：191 项 C# 测试、39 项独立 Demo 测试、21 项 Direct2D 像素检查通过。像素检查包括透明边界、曲面折射、DPI、背景坐标、外部 affine 不重复应用、实际法线矩阵，以及深色导航中心无雾面且边缘仍折射。Python/网页业务未修改。

原生 Demo 已完成实际鼠标按住/反向/松手回原位、按钮拖动不误触、轻点、空格键及输入/选择；开关完成左右预览期间状态不提交、松手提交、反向关闭、Esc 取消、键盘与 UIA TogglePattern。模型驱动 GPU 捕获与鼠标证据分别保存，未将模型重放当作真实输入。当前证据在 `build/liquid-glass-publish/examples/winui-liquid-glass/build/` 与 `build/reference-app-qa/`，截图来自可见客户端，PrintWindow 不作为 GPU 材质依据。

高对比度和禁用系统透明效果使用实体背景；减少动画停止形变。鼠标的按住值 1 代表激活，不是测得的真实压力。真实硬件帧率、不同 GPU 与跨显示器 DPI 热切换仍需对应环境验收。

浅色背景按压黑边：已在共享 shader 删除内圈压暗公式；四边亮反射和方向高光统一由曲面法线产生。GPU 增加浅色静态源回归，要求按压不暗于源色、四个方向的边缘均有亮反射。
