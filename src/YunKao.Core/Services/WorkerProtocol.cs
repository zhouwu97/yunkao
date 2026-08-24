using System.Buffers;
using System.Text;
using System.Text.Json;

namespace YunKao.Core.Services;

/// <summary>
/// Python Worker 使用的 LSP 风格 Content-Length framing。
/// stdout 只承载此协议，日志必须走 stderr。
/// </summary>
public static class WorkerProtocol
{
    private const int MaxHeaderBytes = 16 * 1024;
    private const int MaxPayloadBytes = 64 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    public static async Task WriteAsync(Stream stream, object payload, CancellationToken cancellationToken)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        if (body.Length > MaxPayloadBytes)
        {
            throw new WorkerProtocolException("Worker 消息超过 64 MiB 限制。");
        }

        byte[] header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<JsonDocument> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        byte[] headerBytes = await ReadHeaderAsync(stream, cancellationToken).ConfigureAwait(false);
        int contentLength = ParseContentLength(headerBytes);
        if (contentLength < 0 || contentLength > MaxPayloadBytes)
        {
            throw new WorkerProtocolException($"非法 Content-Length: {contentLength}。");
        }

        byte[] payload = ArrayPool<byte>.Shared.Rent(contentLength);
        try
        {
            await ReadExactlyAsync(stream, payload.AsMemory(0, contentLength), cancellationToken)
                .ConfigureAwait(false);
            return JsonDocument.Parse(payload.AsMemory(0, contentLength));
        }
        catch (JsonException exception)
        {
            throw new WorkerProtocolException("Worker 返回了无效 JSON。", exception);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(payload);
        }
    }

    private static async Task<byte[]> ReadHeaderAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var header = new MemoryStream();
        var lastFour = new Queue<byte>(4);

        while (header.Length <= MaxHeaderBytes)
        {
            byte[] one = new byte[1];
            int read = await stream.ReadAsync(one, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("Worker 在消息头结束前关闭了 stdout。");
            }

            header.WriteByte(one[0]);
            lastFour.Enqueue(one[0]);
            if (lastFour.Count > 4)
            {
                lastFour.Dequeue();
            }

            if (lastFour.Count == 4 && lastFour.SequenceEqual(new byte[] { 13, 10, 13, 10 }))
            {
                return header.ToArray();
            }
        }

        throw new WorkerProtocolException("Worker 消息头超过 16 KiB 限制。");
    }

    private static int ParseContentLength(byte[] headerBytes)
    {
        string header = Encoding.ASCII.GetString(headerBytes);
        foreach (string line in header.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = line.IndexOf(':');
            if (separator <= 0 || !line[..separator].Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return int.TryParse(line[(separator + 1)..].Trim(), out int length) ? length : -1;
        }

        return -1;
    }

    private static async Task ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer[offset..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("Worker 在消息体结束前关闭了 stdout。");
            }

            offset += read;
        }
    }
}

public sealed class WorkerProtocolException(string message, Exception? innerException = null)
    : Exception(message, innerException);
