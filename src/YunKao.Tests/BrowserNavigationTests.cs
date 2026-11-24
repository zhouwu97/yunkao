using YunKao.ViewModels;

namespace YunKao.Tests;

public sealed class BrowserNavigationTests
{
    [Theory]
    [InlineData("back")]
    [InlineData("forward")]
    [InlineData("home")]
    [InlineData("refresh")]
    [InlineData("fit")]
    public void Sidebar_navigation_returns_to_workspace_before_operating_the_browser(string operation)
    {
        var calls = new List<string>();
        var viewModel = new BrowserViewModel();
        viewModel.Attach(new BrowserActions(calls));
        viewModel.SetNavigationState(true, true);
        viewModel.WorkspaceRequested += (_, _) => calls.Add("workspace");
        var command = operation switch
        {
            "back" => viewModel.BackCommand,
            "forward" => viewModel.ForwardCommand,
            "home" => viewModel.HomeCommand,
            "refresh" => viewModel.RefreshCommand,
            _ => viewModel.FitWidthCommand,
        };
        command.Execute(null);
        Assert.Equal(new[] { "workspace", operation }, calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("请输入网址")]
    public void Address_submission_preserves_the_browsers_result(string? result)
    {
        var calls = new List<string>();
        var viewModel = new BrowserViewModel();
        viewModel.Attach(new BrowserActions(calls, result));

        Assert.Equal(result, viewModel.Navigate("https://example.com/"));
        Assert.Equal(new[] { "https://example.com/" }, calls);
    }

    [Fact]
    public void Address_submission_reports_when_no_browser_is_attached()
    {
        Assert.Equal("浏览器尚未就绪", new BrowserViewModel().Navigate("https://example.com/"));
    }

    private sealed class BrowserActions(List<string> calls, string? navigationResult = null) : IBrowserActions
    {
        public void Back() => calls.Add("back");
        public void Forward() => calls.Add("forward");
        public void Refresh() => calls.Add("refresh");
        public void Home() => calls.Add("home");
        public void OpenExternal() => calls.Add("external");
        public void ToggleFitWidth() => calls.Add("fit");
        public string? Navigate(string input)
        {
            calls.Add(input);
            return navigationResult;
        }
    }
}
