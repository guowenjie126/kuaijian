using System.Security.Cryptography;

namespace KuaiJian.WinUI;

public sealed record TransferFileDescriptor(
    Guid TransferId,
    string FileName,
    long TotalBytes,
    string Sha256,
    long ChunkCount);

/// <summary>
/// Stable, retryable source of transfer chunks. The open handle permits readers
/// but prevents the file from changing while its hash and chunks are in use.
/// </summary>
public sealed class TransferFileSender : IAsyncDisposable
{
    private readonly FileStream _stream;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public TransferFileDescriptor Descriptor { get; }

    private TransferFileSender(FileStream stream, TransferFileDescriptor descriptor)
    {
        _stream = stream;
        Descriptor = descriptor;
    }

    public static async Task<TransferFileSender> OpenAsync(
        string path,
        string? transferId = null,
        CancellationToken cancellationToken = default)
    {
        var id = string.IsNullOrWhiteSpace(transferId)
            ? Guid.NewGuid()
            : Guid.TryParse(transferId, out var parsed) && parsed != Guid.Empty
                ? parsed
                : throw new InvalidDataException("互传标识格式无效。");
        var fullPath = Path.GetFullPath(path);
        var fileName = Path.GetFileName(fullPath);
        if (string.IsNullOrWhiteSpace(fileName))
            throw new InvalidDataException("发送文件名无效。");

        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.Asynchronous | FileOptions.RandomAccess);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var length = stream.Length;
            var chunkCount = TransferChunkPolicy.GetChunkCount(length);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
            stream.Seek(0, SeekOrigin.Begin);
            return new TransferFileSender(stream,
                new TransferFileDescriptor(id, fileName, length, hash, chunkCount));
        }
        catch
        {
            await stream.DisposeAsync();
            throw;
        }
    }

    public async Task<byte[]> ReadChunkAsync(long index, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var length = TransferChunkPolicy.GetChunkLength(Descriptor.TotalBytes, index);
            var buffer = new byte[length];
            _stream.Seek(TransferChunkPolicy.GetChunkOffset(index), SeekOrigin.Begin);
            await _stream.ReadExactlyAsync(buffer, cancellationToken);
            return buffer;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_disposed) return;
            _disposed = true;
            await _stream.DisposeAsync();
        }
        finally
        {
            _gate.Release();
        }
    }
}
