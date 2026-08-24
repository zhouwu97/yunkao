namespace YunKao.Controls;

/// <summary>
/// 控制液态组件是否创建自己的系统背景采样。
/// </summary>
public enum LiquidBackdropMode
{
    /// <summary>组件独立采样桌面或窗口背景。</summary>
    System,

    /// <summary>复用父级玻璃岛，只绘制 tint、高光和边缘。</summary>
    Inherited,

    /// <summary>不创建系统背景，只绘制组件自身的表面层。</summary>
    None,
}
