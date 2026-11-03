namespace KuaiJian.WinUI;

public static class BoundedHttpContentReader
{
    public static async Task<byte[]> ReadBytesAsync(
        HttpContent content, int maxBytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        if (content.Headers.ContentLength is long length && length > maxBytes)
            throw new InvalidDataException("服务器响应超过允许大小。");

        await using var source = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream(Math.Min(maxBytes, 64 * 1024));
        var chunk = new byte[32 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(chunk.AsMemory(), cancellationToken);
            if (read == 0) break;
            if (buffer.Length + read > maxBytes)
                throw new InvalidDataException("服务器响应超过允许大小。");
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }
        return buffer.ToArray();
    }
}
