using YunKao.Core.Presentation;

namespace YunKao.ViewModels;

/// <summary>浏览器宿主对侧边栏暴露的操作。</summary>
public interface IBrowserActions
{
    void Back();
    void Forward();
    void Refresh();
    void Home();
    void OpenExternal();
    void ToggleFitWidth();

    /// <summary>导航到用户输入的地址；返回 null 表示成功，否则为错误提示。</summary>
    string? Navigate(string input);
}

/// <summary>
/// 浏览器状态（应用级）。WebView 常驻在窗口内容区，地址栏与导航按钮位于侧边栏，二者通过此对象同步。
/// </summary>
public sealed class BrowserViewModel : ObservableObject
{
    private IBrowserActions? _actions;
    private bool _canGoBack;
    private bool _canGoForward;
    private string _host = "cctrcloud.net";
    private string _path = "";
    private string _fullAddress = "https://www.cctrcloud.net/";
    private string _statusText = "正在准备 WebView2…";
    private string _zoomLabel = "适应宽度";

    public BrowserViewModel()
    {
        BackCommand = new RelayCommand(() => _actions?.Back(), () => _canGoBack);
        ForwardCommand = new RelayCommand(() => _actions?.Forward(), () => _canGoForward);
        RefreshCommand = new RelayCommand(() => _actions?.Refresh());
        HomeCommand = new RelayCommand(() => _actions?.Home());
        ExternalCommand = new RelayCommand(() => _actions?.OpenExternal());
        FitWidthCommand = new RelayCommand(() => _actions?.ToggleFitWidth());
    }

    /// <summary>网页内按下 Ctrl+L 等需要侧边栏进入地址编辑时触发。</summary>
    public event EventHandler? AddressEditRequested;

    public RelayCommand BackCommand { get; }
    public RelayCommand ForwardCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand HomeCommand { get; }
    public RelayCommand ExternalCommand { get; }
    public RelayCommand FitWidthCommand { get; }

    public string Host { get => _host; private set => SetProperty(ref _host, value); }
    public string Path { get => _path; private set => SetProperty(ref _path, value); }
    public string FullAddress { get => _fullAddress; private set => SetProperty(ref _fullAddress, value); }
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }
    public string ZoomLabel { get => _zoomLabel; set => SetProperty(ref _zoomLabel, value); }

    public void Attach(IBrowserActions actions) => _actions = actions;

    public string? Navigate(string input) => _actions?.Navigate(input) ?? "浏览器尚未就绪";

    public void RequestAddressEdit() => AddressEditRequested?.Invoke(this, EventArgs.Empty);

    public void SetNavigationState(bool canGoBack, bool canGoForward)
    {
        _canGoBack = canGoBack;
        _canGoForward = canGoForward;
        BackCommand.NotifyCanExecuteChanged();
        ForwardCommand.NotifyCanExecuteChanged();
    }

    public void SetAddress(Uri? uri)
    {
        if (uri is null) return;
        FullAddress = uri.AbsoluteUri;
        Host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        string path = uri.AbsolutePath.TrimEnd('/');
        Path = string.IsNullOrEmpty(path) ? "" : path;
    }
}
