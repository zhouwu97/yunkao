using System.Text;
using System.Text.Json;
using YunKao.Core.Models;

namespace YunKao.Core.Services;

public sealed record AiProviderPreset(
    string Label,
    string BaseUrl,
    string Model,
    bool SupportsImages,
    string AuthHeader,
    string AuthPrefix);

public static class AiProviderRegistry
{
    private static readonly IReadOnlyDictionary<string, AiProviderPreset> Presets =
        new Dictionary<string, AiProviderPreset>(StringComparer.OrdinalIgnoreCase)
        {
            ["openai"] = new("OpenAI / GPT", "https://api.openai.com/v1", "gpt-4o-mini", true, "Authorization", "Bearer "),
            ["deepseek"] = new("DeepSeek", "https://api.deepseek.com", "deepseek-v4-flash", false, "Authorization", "Bearer "),
            ["kimi"] = new("Kimi / Moonshot", "https://api.moonshot.cn/v1", "kimi-k2.6", true, "Authorization", "Bearer "),
            ["qwen"] = new("千问 / Qwen", "https://dashscope.aliyuncs.com/compatible-mode/v1", "qwen-vl-plus", true, "Authorization", "Bearer "),
            ["glm"] = new("智谱 / GLM", "https://open.bigmodel.cn/api/paas/v4", "glm-5.1", true, "Authorization", "Bearer "),
            ["mimo"] = new("小米 MiMo", "https://api.xiaomimimo.com/v1", "mimo-v2.5-pro", true, "api-key", ""),
            ["custom"] = new("自定义兼容接口", "", "", false, "Authorization", "Bearer "),
        };

    public static AiProviderPreset Get(string? provider)
    {
        return provider is not null && Presets.TryGetValue(provider, out AiProviderPreset? preset)
            ? preset
            : Presets["custom"];
    }

    public static IReadOnlyDictionary<string, AiProviderPreset> All => Presets;
}

public sealed class AiService(HttpClient httpClient)
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    private static int NewLineIndex(string text)
    {
        return text.IndexOf('\n');
    }

    public async Task<AiResult> InferAsync(
        Question question,
        AiRequestConfiguration configuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);
        if (string.IsNullOrWhiteSpace(configuration.ApiKey)
            || string.IsNullOrWhiteSpace(configuration.BaseUrl)
            || string.IsNullOrWhiteSpace(configuration.Model))
        {
            throw new InvalidOperationException("AI 配置不完整。");
        }

        AiProviderPreset preset = AiProviderRegistry.Get(configuration.Provider);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{configuration.BaseUrl.TrimEnd('/')}/chat/completions");
        request.Headers.TryAddWithoutValidation(
            preset.AuthHeader,
            $"{preset.AuthPrefix}{configuration.ApiKey.Trim()}");
        request.Content = new StringContent(
            JsonSerializer.Serialize(BuildPayload(question, configuration), _jsonOptions),
            Encoding.UTF8,
            "application/json");

        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        string responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new AiHttpException((int)response.StatusCode, response.ReasonPhrase ?? "AI 请求失败");
        }

        using JsonDocument document = JsonDocument.Parse(responseText);
        return ParseResponse(document.RootElement);
    }

    public static AiResult ParseResponse(JsonElement root)
    {
        JsonElement choices = root.GetProperty("choices");
        if (choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
        {
            throw new AiResponseException("AI 返回缺少 choices。");
        }

        JsonElement message = choices[0].GetProperty("message");
        string content = ExtractMessageContent(message);
        using JsonDocument resultDocument = JsonDocument.Parse(ExtractJson(content));
        JsonElement result = resultDocument.RootElement;
        string answer = result.TryGetProperty("answer", out JsonElement answerElement)
            ? answerElement.GetString() ?? answerElement.ToString()
            : "";
        string analysis = result.TryGetProperty("analysis", out JsonElement analysisElement)
            ? analysisElement.GetString() ?? analysisElement.ToString()
            : "";
        double confidence = result.TryGetProperty("confidence", out JsonElement confidenceElement)
            && confidenceElement.TryGetDouble(out double parsedConfidence)
            ? Math.Clamp(parsedConfidence, 0, 1)
            : 0;
        JsonElement usage = root.TryGetProperty("usage", out JsonElement usageElement)
            ? usageElement
            : default;

        return new AiResult
        {
            Answer = answer.Trim(),
            Analysis = analysis.Trim(),
            Confidence = confidence,
            PromptTokens = ReadInt(usage, "prompt_tokens"),
            CompletionTokens = ReadInt(usage, "completion_tokens"),
            TotalTokens = ReadInt(usage, "total_tokens"),
        };
    }

    public static string ExtractJson(string? content)
    {
        string text = (content ?? "").Trim();
        if (text.Length == 0) throw new AiResponseException("AI 返回为空。");

        string fence = new string((char)96, 3);
        int fenceStart = text.IndexOf(fence, StringComparison.Ordinal);
        if (fenceStart >= 0)
        {
            int bodyStart = text.IndexOf(Environment.NewLine, fenceStart, StringComparison.Ordinal);
            int bodyEnd = text.LastIndexOf(fence, StringComparison.Ordinal);
            if (bodyStart >= 0 && bodyEnd > bodyStart)
            {
                text = text[(bodyStart + Environment.NewLine.Length)..bodyEnd].Trim();
            }
        }

        int start = text.IndexOf('{');
        int end = text.LastIndexOf('}');
        if (start >= 0 && end > start) text = text[start..(end + 1)];
        return text;
    }

    private static object BuildPayload(Question question, AiRequestConfiguration configuration)
    {
        string options = question.Options.Count > 0 ? string.Join(Environment.NewLine, question.Options) : "无选项";
        string prompt = "你是一个严格的考试题答案补全助手。只返回 JSON。"
            + Environment.NewLine
            + $"题型：{question.QuestionType}{Environment.NewLine}题干：{question.Title}{Environment.NewLine}"
            + $"选项：{Environment.NewLine}{options}{Environment.NewLine}现有解析：{question.Analysis}";

        if (configuration.SupportsImages)
        {
            List<string> urls = ExtractImageUrls(question);
            if (urls.Count > 0)
            {
                var content = new List<object> { new { type = "text", text = prompt } };
                content.AddRange(urls.Select(url => (object)new { type = "image_url", image_url = new { url } }));
                return new
                {
                    model = configuration.Model,
                    temperature = 0.2,
                    messages = new[] { new { role = "user", content } },
                };
            }
        }

        return new
        {
            model = configuration.Model,
            temperature = 0.2,
            messages = new object[]
            {
                new { role = "system", content = "你只输出 JSON。你的目标是补全题目答案，并给出谨慎的置信度。" },
                new { role = "user", content = prompt },
            },
        };
    }

    private static List<string> ExtractImageUrls(Question question)
    {
        var urls = new List<string>();
        foreach (string text in new[] { question.Title, question.Analysis }.Concat(question.Options))
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            int start = 0;
            while ((start = text.IndexOf("![img]<", start, StringComparison.Ordinal)) >= 0)
            {
                start += 8;
                int end = text.IndexOf('>', start);
                if (end <= start) break;
                string url = text[start..end].Split('|')[0].Trim();
                if (Uri.TryCreate(url, UriKind.Absolute, out _) && !urls.Contains(url)) urls.Add(url);
                start = end + 1;
            }
        }
        return urls;
    }

    private static string ExtractMessageContent(JsonElement message)
    {
        if (!message.TryGetProperty("content", out JsonElement content)) return "";
        if (content.ValueKind == JsonValueKind.String) return content.GetString() ?? "";
        if (content.ValueKind == JsonValueKind.Array)
        {
            return string.Join(
                Environment.NewLine,
                content.EnumerateArray()
                    .Where(part => part.TryGetProperty("text", out _))
                    .Select(part => part.GetProperty("text").GetString() ?? ""));
        }
        return content.ToString();
    }

    private static int ReadInt(JsonElement usage, string property)
    {
        return usage.ValueKind == JsonValueKind.Object
            && usage.TryGetProperty(property, out JsonElement element)
            && element.TryGetInt32(out int value)
            ? value
            : 0;
    }
}

public sealed class AiTaskQueue : IAsyncDisposable
{
    private readonly AiService _service;
    private readonly SemaphoreSlim _slots = new(3, 3);
    private readonly CancellationTokenSource _queueCancellation = new();

    public AiTaskQueue(AiService service) { _service = service; }

    public async Task<AiResult> EnqueueAsync(
        Question question,
        AiRequestConfiguration configuration,
        CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _queueCancellation.Token);
        await _slots.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            return await _service.InferAsync(question, configuration, linked.Token).ConfigureAwait(false);
        }
        finally
        {
            _slots.Release();
        }
    }

    public void CancelAll() => _queueCancellation.Cancel();

    public ValueTask DisposeAsync()
    {
        _queueCancellation.Cancel();
        _queueCancellation.Dispose();
        _slots.Dispose();
        return ValueTask.CompletedTask;
    }
}

public sealed class AiHttpException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public sealed class AiResponseException(string message, Exception? innerException = null)
    : Exception(message, innerException);
