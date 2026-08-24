namespace YunKao.Core.Models;

public sealed class AiRequestConfiguration
{
    public string Provider { get; init; } = "openai";
    public string BaseUrl { get; init; } = "";
    public string Model { get; init; } = "";
    public string ApiKey { get; init; } = "";
    public bool SupportsImages { get; init; }
}

public sealed class AiResult
{
    public string Answer { get; init; } = "";
    public string Analysis { get; init; } = "";
    public double Confidence { get; init; }
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }
    public int TotalTokens { get; init; }
}
