using System.Net;
using System.Net.Http.Headers;
using YunKao.Core.Models;
using YunKao.Core.Services;

namespace YunKao.Tests;

public sealed class AiServiceTests
{
    [Fact]
    public async Task Sends_openai_compatible_request_and_parses_json_result()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"```json\\n{\\\"answer\\\":\\\"A\\\",\\\"analysis\\\":\\\"因为 A\\\",\\\"confidence\\\":0.86}\\n```\"}}],\"usage\":{\"total_tokens\":12}}"),
        });
        using var httpClient = new HttpClient(handler);
        var service = new AiService(httpClient);
        var settings = new AiRequestConfiguration
        {
            Provider = "custom",
            BaseUrl = "https://api.example.com/v1",
            Model = "test-model",
            ApiKey = "secret",
        };

        AiResult result = await service.InferAsync(
            new Question { QuestionType = "单选题", Title = "测试题", Options = ["A. 是"] },
            settings,
            CancellationToken.None);

        Assert.Equal("A", result.Answer);
        Assert.Equal(0.86, result.Confidence, 2);
        Assert.Equal("Bearer secret", handler.LastRequest!.Headers.Authorization!.ToString());
        Assert.Equal("test-model", handler.LastBody!.Value.GetProperty("model").GetString());
    }

    [Fact]
    public async Task Queue_limits_parallel_ai_requests_to_three()
    {
        var handler = new DelayedHandler();
        using var httpClient = new HttpClient(handler);
        var queue = new AiTaskQueue(new AiService(httpClient));
        var settings = new AiRequestConfiguration
        {
            Provider = "custom",
            BaseUrl = "https://api.example.com/v1",
            Model = "test-model",
            ApiKey = "secret",
        };
        try
        {
            Task<AiResult>[] tasks = Enumerable.Range(0, 8)
                .Select(index => queue.EnqueueAsync(
                    new Question { Title = $"测试题 {index}" }, settings, CancellationToken.None))
                .ToArray();
            await Task.WhenAll(tasks);
            Assert.Equal(3, handler.MaximumConcurrency);
        }
        finally
        {
            await queue.DisposeAsync();
        }
    }

    [Fact]
    public async Task TestConnectionAsync_returns_success_on_valid_response()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"pong\"}}]}"),
        });
        using var httpClient = new HttpClient(handler);
        var service = new AiService(httpClient);
        var config = new AiRequestConfiguration
        {
            Provider = "openai",
            BaseUrl = "https://api.openai.com/v1",
            Model = "gpt-4o-mini",
            ApiKey = "sk-test",
        };

        AiConnectionTestResult result = await service.TestConnectionAsync(config);
        Assert.True(result.Success);
        Assert.Contains("连接成功", result.Message);
        Assert.Equal("Bearer sk-test", handler.LastRequest!.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task GetModelsAsync_parses_model_list_correctly()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"data\":[{\"id\":\"gpt-4o\"},{\"id\":\"gpt-4o-mini\"},{\"id\":\"o3-mini\"}]}"),
        });
        using var httpClient = new HttpClient(handler);
        var service = new AiService(httpClient);
        var config = new AiRequestConfiguration
        {
            Provider = "openai",
            BaseUrl = "https://api.openai.com/v1",
            Model = "",
            ApiKey = "sk-test",
        };

        AiModelsResult result = await service.GetModelsAsync(config);
        Assert.True(result.Success);
        Assert.Equal(3, result.Models.Count);
        Assert.Equal("gpt-4o", result.Models[0]);
        Assert.Equal("gpt-4o-mini", result.Models[1]);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public System.Text.Json.JsonElement? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content is not null)
            {
                LastBody = System.Text.Json.JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken)).RootElement.Clone();
            }
            return handler(request);
        }
    }

    private sealed class DelayedHandler : HttpMessageHandler
    {
        private int _inFlight;
        private int _maximumConcurrency;

        public int MaximumConcurrency => _maximumConcurrency;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            int current = Interlocked.Increment(ref _inFlight);
            while (true)
            {
                int previous = _maximumConcurrency;
                if (current <= previous || Interlocked.CompareExchange(ref _maximumConcurrency, current, previous) == previous) break;
            }
            try
            {
                await Task.Delay(35, cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"{\\\"answer\\\":\\\"A\\\",\\\"analysis\\\":\\\"解析\\\",\\\"confidence\\\":0.9}\"}}]}"),
                };
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }
    }
}
