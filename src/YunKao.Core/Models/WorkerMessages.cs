using System.Text.Json;
using System.Text.Json.Serialization;

namespace YunKao.Core.Models;

public sealed class WorkerEvent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "event";

    [JsonPropertyName("event")]
    public string Event { get; set; } = "";

    [JsonPropertyName("data")]
    public JsonElement Data { get; set; }
}

public sealed class WorkerError
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = "worker_error";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "Worker 请求失败";
}

public sealed class WorkerResponse<T>
{
    [JsonPropertyName("protocol")]
    public int Protocol { get; set; } = 1;

    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("result")]
    public T? Result { get; set; }

    [JsonPropertyName("error")]
    public WorkerError? Error { get; set; }
}
