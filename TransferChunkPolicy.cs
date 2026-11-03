namespace KuaiJian.WinUI;

public static class TransferChunkPolicy
{
    public const int ChunkSize = 8 * 1024 * 1024;

    public static long GetChunkCount(long totalBytes)
    {
        if (totalBytes < 0) throw new ArgumentOutOfRangeException(nameof(totalBytes));
        var count = totalBytes == 0 ? 0 : 1 + ((totalBytes - 1) / ChunkSize);
        if (count > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(totalBytes), "文件分块数量超过客户端可持久化范围。");
        return count;
    }

    public static long GetChunkOffset(long index)
    {
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
        return checked(index * ChunkSize);
    }

    public static int GetChunkLength(long totalBytes, long index)
    {
        var count = GetChunkCount(totalBytes);
        if (index < 0 || index >= count) throw new ArgumentOutOfRangeException(nameof(index));
        return (int)Math.Min(ChunkSize, totalBytes - GetChunkOffset(index));
    }

    public static bool IsValidSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);
}
