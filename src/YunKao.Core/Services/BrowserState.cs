namespace YunKao.Core.Services;

/// <summary>
/// WebView 页面状态。PracticeReady 必须同时满足已登录和当前题目根节点存在。
/// </summary>
public sealed record BrowserPageState(
    bool IsLogin,
    bool IsPractice,
    string Url = "",
    string Title = "")
{
    public bool IsPracticeReady => IsPractice && !IsLogin;
}

public enum BrowserResourceKind
{
    Other,
    MainDocument,
    AuthenticationApi,
    QuestionApi,
}

/// <summary>
/// 云考资源边界。只有主文档、认证 API 和题目 API 的错误才允许影响提取任务。
/// </summary>
public static class CloudResourceClassifier
{
    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "cctrcloud.net",
        "www.cctrcloud.net",
    };

    private static readonly string[] AuthenticationSegments =
    ["login", "logout", "auth", "oauth", "token", "sso", "signin", "sign-in", "session", "account", "credential"];

    private static readonly string[] QuestionSegments =
    ["practice", "exam", "question", "questions", "subject", "subjects", "paper", "test", "quiz", "exercise", "assessment"];

    public static IReadOnlyCollection<string> AllowedHostNames => AllowedHosts;

    public static bool IsAllowedHost(Uri? uri)
    {
        return uri is not null
            && uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && AllowedHosts.Contains(uri.Host);
    }

    public static BrowserResourceKind Classify(Uri? uri, string? resourceContext)
    {
        if (!IsAllowedHost(uri)) return BrowserResourceKind.Other;

        string context = resourceContext?.Trim() ?? "";
        if (IsMainDocumentContext(context)) return BrowserResourceKind.MainDocument;
        if (IsStaticAsset(uri!.AbsolutePath)) return BrowserResourceKind.Other;

        string path = Uri.UnescapeDataString(uri.AbsolutePath).Trim('/');
        if (string.IsNullOrWhiteSpace(path)) return BrowserResourceKind.MainDocument;
        string[] segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (ContainsRouteSegment(segments, AuthenticationSegments)) return BrowserResourceKind.AuthenticationApi;
        if (ContainsRouteSegment(segments, QuestionSegments)) return BrowserResourceKind.QuestionApi;

        return BrowserResourceKind.Other;
    }

    public static bool ShouldReportStatus(int statusCode)
    {
        return statusCode is 401 or 403 or 408 or 429 or 500 or 502 or 503 or 504;
    }

    private static bool IsMainDocumentContext(string context)
    {
        return context.Equals("Document", StringComparison.OrdinalIgnoreCase)
            || context.Equals("NavigationPreloadMainFrame", StringComparison.OrdinalIgnoreCase)
            || context.Equals("NavigationPreloadSubFrame", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsRouteSegment(IReadOnlyList<string> segments, IReadOnlyList<string> candidates)
    {
        foreach (string segment in segments)
        {
            foreach (string candidate in candidates)
            {
                if (segment.Equals(candidate, StringComparison.OrdinalIgnoreCase)
                    || segment.StartsWith(candidate + "-", StringComparison.OrdinalIgnoreCase)
                    || segment.StartsWith(candidate + "_", StringComparison.OrdinalIgnoreCase)
                    || segment.StartsWith(candidate + ".", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsStaticAsset(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".gif", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".svg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".css", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".js", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".woff", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".woff2", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".ico", StringComparison.OrdinalIgnoreCase);
    }
}
