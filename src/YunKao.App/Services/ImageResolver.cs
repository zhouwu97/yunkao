using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using YunKao.Core.Models;

namespace YunKao.Services;

/// <summary>
/// 将 WebView 登录态图片转换为本地缓存的 data URI，供 Worker 导出和 AI 视觉请求共同使用。
/// </summary>
public sealed class ImageResolver : IDisposable
{
    private static readonly Regex ImagePattern = new(@"!\[img\]<([^>|]+)([^>]*)>", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "image/gif", "image/webp", "image/svg+xml",
    };
    private const int MaxImageBytes = 10 * 1024 * 1024;
    private readonly WebViewService _webView;
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly string _cacheDirectory;

    public ImageResolver(WebViewService webView, string? cacheDirectory = null)
    {
        _webView = webView;
        _cacheDirectory = cacheDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "YunKaoDesktop", "cache", "images");
        Directory.CreateDirectory(_cacheDirectory);
    }

    public async Task<Question> ResolveAsync(Question question, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(question);
        Question resolved = question.Clone();
        resolved.Title = await ReplaceImagesAsync(resolved.Title, cancellationToken).ConfigureAwait(false);
        resolved.Answer = await ReplaceImagesAsync(resolved.Answer, cancellationToken).ConfigureAwait(false);
        resolved.Analysis = await ReplaceImagesAsync(resolved.Analysis, cancellationToken).ConfigureAwait(false);
        for (int index = 0; index < resolved.Options.Count; index++)
        {
            resolved.Options[index] = await ReplaceImagesAsync(resolved.Options[index], cancellationToken).ConfigureAwait(false);
        }
        return resolved;
    }

    public void Dispose() => _httpClient.Dispose();

    private async Task<string> ReplaceImagesAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        MatchCollection matches = ImagePattern.Matches(text);
        if (matches.Count == 0) return text;

        var builder = new StringBuilder(text.Length);
        int lastIndex = 0;
        foreach (Match match in matches)
        {
            builder.Append(text, lastIndex, match.Index - lastIndex);
            string source = match.Groups[1].Value.Trim();
            string resolved = await ResolveSourceAsync(source, cancellationToken).ConfigureAwait(false);
            builder.Append("![img]<").Append(resolved).Append(match.Groups[2].Value).Append('>');
            lastIndex = match.Index + match.Length;
        }
        builder.Append(text, lastIndex, text.Length - lastIndex);
        return builder.ToString();
    }

    private async Task<string> ResolveSourceAsync(string source, CancellationToken cancellationToken)
    {
        if (source.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)) return source;
        if (!Uri.TryCreate(source, UriKind.Absolute, out Uri? uri)
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return source;
        }

        try
        {
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
            string dataPath = Path.Combine(_cacheDirectory, hash + ".bin");
            string mimePath = Path.Combine(_cacheDirectory, hash + ".mime");
            if (File.Exists(dataPath) && File.Exists(mimePath) && new FileInfo(dataPath).Length <= MaxImageBytes)
            {
                return ToDataUri(await File.ReadAllBytesAsync(dataPath, cancellationToken).ConfigureAwait(false),
                    (await File.ReadAllTextAsync(mimePath, cancellationToken).ConfigureAwait(false)).Trim());
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Referrer = _webView.CurrentUri;
            string cookies = await _webView.GetCookieHeaderAsync(uri, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(cookies)) request.Headers.TryAddWithoutValidation("Cookie", cookies);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("YunKaoDesktop", "2.0"));
            using HttpResponseMessage response = await _httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            string mime = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? InferMime(uri);
            if (!AllowedMimeTypes.Contains(mime)) return source;
            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (buffer.Length == 0 || buffer.Length > MaxImageBytes) return source;
            byte[] bytes = buffer.ToArray();
            string tempData = dataPath + ".tmp";
            await File.WriteAllBytesAsync(tempData, bytes, cancellationToken).ConfigureAwait(false);
            File.Move(tempData, dataPath, true);
            await File.WriteAllTextAsync(mimePath, mime, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            return ToDataUri(bytes, mime);
        }
        catch
        {
            // 单张图片失败不应阻断整题提取或导出，Worker 会保留原始标记并显示失败占位符。
            return source;
        }
    }

    private static string ToDataUri(byte[] bytes, string mime)
        => $"data:{mime};base64,{Convert.ToBase64String(bytes)}";

    private static string InferMime(Uri uri)
    {
        string path = uri.AbsolutePath.ToLowerInvariant();
        return path.EndsWith(".png", StringComparison.Ordinal) ? "image/png"
            : path.EndsWith(".jpg", StringComparison.Ordinal) || path.EndsWith(".jpeg", StringComparison.Ordinal) ? "image/jpeg"
            : path.EndsWith(".gif", StringComparison.Ordinal) ? "image/gif"
            : path.EndsWith(".webp", StringComparison.Ordinal) ? "image/webp"
            : path.EndsWith(".svg", StringComparison.Ordinal) ? "image/svg+xml"
            : "application/octet-stream";
    }
}
