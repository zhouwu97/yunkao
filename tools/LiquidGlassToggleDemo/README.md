# 放大玻璃开关 Demo

独立实验，不读取账号、保存项目设置或改动原版玻璃 Demo。按住时普通的 30×24 DIP 滑块弹性放大为约 52×32 DIP 的玻璃透镜，玻璃可以越过 56×30 DIP 轨道；拖动越过中点后才开始切换轨道颜色，松手才提交开关状态，并缩回普通滑块。窗口分别提供浅色、深色及放大查看。

Demo 直接链接项目的开关、模板、运动模型和着色器，两处使用同一份实现。轨道通过 GPU 合成进入折射源；没有在动画中反复截取、编码或上传轨道。键盘和 UIA TogglePattern 保留。自动演示使用模型重放，手动拖动用于查看实际指针交互。

```powershell
dotnet publish tools/LiquidGlassToggleDemo/LiquidGlassToggleDemo.csproj -c Release -p:Platform=x64 --self-contained true -o build/large-switch-demo
dotnet test tools/LiquidGlassToggleDemo.Tests/LiquidGlassToggleDemo.Tests.csproj -c Release
```

完整保留发布目录后，通过资源管理器运行 `LiquidGlassToggleDemo.exe`。
