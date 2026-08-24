using Microsoft.UI.Xaml.Controls;

namespace YunKao.Controls;

/// <summary>
/// 浏览器承载区的阶段一占位控件。尚未创建 WebView2，避免提前触发网络或凭据流程。
/// </summary>
public sealed partial class BrowserShell : UserControl
{
    public BrowserShell()
    {
        InitializeComponent();
    }
}
