using System.Text.Json;
using YunKao.Core.Services;

namespace YunKao.Tests;

public sealed class WorkerFramingTests
{
    [Fact]
    public async Task Writes_and_reads_one_content_length_framed_message()
    {
        await using var stream = new MemoryStream();

        await WorkerProtocol.WriteAsync(
            stream,
            new { protocol = 1, id = "test-1", method = "health" },
            CancellationToken.None);

        stream.Position = 0;
        using JsonDocument message = await WorkerProtocol.ReadAsync(stream, CancellationToken.None);

        Assert.Equal("test-1", message.RootElement.GetProperty("id").GetString());
        Assert.Equal("health", message.RootElement.GetProperty("method").GetString());
    }
}
