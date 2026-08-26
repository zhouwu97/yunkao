using YunKao.Core.Services;

namespace YunKao.Services;

/// <summary>
/// 工作台业务状态的应用级容器。页面只是绑定层，离开页面不会销毁会话和题目快照。
/// </summary>
public sealed class WorkspaceRuntime
{
    public ExtractionSession Session { get; } = new();
    public Uri? CurrentUrl { get; set; }
    public string CurrentCourse { get; set; } = "";
    public bool AuthExpired { get; set; }
    public bool PermissionDenied { get; set; }
    public bool WebViewInitialized { get; set; }
    public bool BridgeInstalled { get; set; }
    public string BrowserVersion { get; set; } = "未初始化";
    public string BrowserStatus { get; set; } = "等待 WebView2";
}
