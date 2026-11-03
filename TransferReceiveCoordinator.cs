using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;

namespace KuaiJian.WinUI;

public sealed class TransferDownloadedChunk : IAsyncDisposable
{
    public Guid TransferId { get; }
    public long Index { get; }
    public Stream Content { get; }

    public TransferDownloadedChunk(Guid transferId, long index, Stream content)
    {
        TransferId = transferId;
        Index = index;
        Content = content ?? throw new ArgumentNullException(nameof(content));
    }

    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

public sealed record TransferReceiveAcknowledgement(
    Guid TransferId,
    string Sha256,
    bool Acknowledged);

/// <summary>
/// Adapter boundary for inbox downloads. DownloadChunkAsync should return a
/// streaming response (not buffer the whole HTTP body). AcknowledgeSavedAsync
/// must be idempotent and only acknowledge after the destination file exists.
/// </summary>
public interface ITransferReceiveTransport
{
    Task<TransferDownloadedChunk> DownloadChunkAsync(
        TransferFileDescriptor descriptor,
        long index,
        CancellationToken cancellationToken);

    Task<TransferReceiveAcknowledgement> AcknowledgeSavedAsync(
        TransferFileDescriptor descriptor,
        string savedPath,
        CancellationToken cancellationToken);
}

public sealed record TransferReceiveProgress(
    int ReceivedChunks,
    long TotalChunks,
    long ReceivedBytes,
    long TotalBytes);

/// <summary>
/// Coordinates durable chunk reception, safe local commit and post-save remote
/// acknowledgement. A small account-local ack outbox makes acknowledgement
/// retries avoid downloading or saving a second copy of an already committed file.
/// </summary>
public static class TransferReceiveCoordinator
{
    private const long MaximumReceiptBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);

    public static async Task<string> ReceiveAsync(
        TransferFileDescriptor descriptor,
        string receiveDirectory,
        string stagingRoot,
        ITransferReceiveTransport transport,
        IProgress<TransferReceiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(transport);
        ValidateDescriptor(descriptor, receiveDirectory, stagingRoot);

        var fullStagingRoot = Path.GetFullPath(stagingRoot);
        var gateKey = Path.Combine(fullStagingRoot, descriptor.TransferId.ToString("N"));
        var gate = Gates.GetOrAdd(gateKey, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(fullStagingRoot);
            var receiptPath = GetReceiptPath(fullStagingRoot, descriptor.TransferId);
            var receipt = await ReadReceiptAsync(receiptPath, descriptor, cancellationToken);
            if (receipt is null)
            {
                var alreadyCommittedPath = await FindCommittedFileAsync(
                    descriptor, receiveDirectory, cancellationToken);
                if (alreadyCommittedPath is not null)
                {
                    receipt = new CompletionReceipt
                    {
                        TransferId = descriptor.TransferId,
                        FileName = descriptor.FileName,
                        TotalBytes = descriptor.TotalBytes,
                        Sha256 = descriptor.Sha256,
                        SavedPath = alreadyCommittedPath,
                        UpdatedAt = DateTimeOffset.UtcNow
                    };
                    await SaveReceiptAsync(receiptPath, receipt, cancellationToken);
                }
            }

            if (receipt is null)
            {
                var stageDirectory = Path.Combine(fullStagingRoot, descriptor.TransferId.ToString("N"));
                var receiver = Directory.Exists(stageDirectory)
                    ? await ResumableTransferReceiver.ResumeAsync(
                        descriptor.TransferId.ToString("D"), receiveDirectory, fullStagingRoot, cancellationToken)
                    : await ResumableTransferReceiver.BeginAsync(
                        descriptor.TransferId.ToString("D"), descriptor.FileName, descriptor.TotalBytes,
                        descriptor.Sha256, receiveDirectory, fullStagingRoot, cancellationToken);

                long nextIndex = 0;
                while (nextIndex < descriptor.ChunkCount)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var pending = await receiver.GetPendingChunkIndicesAsync(nextIndex, 1, cancellationToken);
                    if (pending.Count == 0) break;

                    var index = pending[0];
                    await using var downloaded = await transport.DownloadChunkAsync(descriptor, index, cancellationToken);
                    if (downloaded.TransferId != descriptor.TransferId || downloaded.Index != index)
                        throw new InvalidDataException("互传服务返回了不匹配的接收分块；本地检查点已保留。");

                    var expectedLength = TransferChunkPolicy.GetChunkLength(descriptor.TotalBytes, index);
                    var chunkData = await ReadExactlyBoundedAsync(downloaded.Content, expectedLength, cancellationToken);
                    await receiver.ReceiveChunkAsync(index, chunkData, cancellationToken);
                    progress?.Report(new TransferReceiveProgress(
                        receiver.ReceivedChunkCount,
                        descriptor.ChunkCount,
                        receiver.ReceivedByteCount,
                        descriptor.TotalBytes));
                    nextIndex = index + 1;
                }

                var savedPath = await receiver.CompleteAsync(cancellationToken);
                receipt = new CompletionReceipt
                {
                    TransferId = descriptor.TransferId,
                    FileName = descriptor.FileName,
                    TotalBytes = descriptor.TotalBytes,
                    Sha256 = descriptor.Sha256,
                    SavedPath = Path.GetFullPath(savedPath),
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                await SaveReceiptAsync(receiptPath, receipt, cancellationToken);
            }

            await VerifySavedFileAsync(receipt, receiveDirectory, cancellationToken);
            var acknowledgement = await transport.AcknowledgeSavedAsync(
                descriptor, receipt.SavedPath, cancellationToken);
            if (acknowledgement.TransferId != descriptor.TransferId
                || !acknowledgement.Acknowledged
                || !string.Equals(acknowledgement.Sha256, descriptor.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("互传服务未确认已安全保存的文件；本地接收确认记录已保留供重试。");

            File.Delete(receiptPath);
            return receipt.SavedPath;
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task<byte[]> ReadExactlyBoundedAsync(Stream content, int expectedLength,
        CancellationToken cancellationToken)
    {
        if (!content.CanRead) throw new InvalidDataException("互传分块响应流不可读取。");
        var data = new byte[expectedLength];
        await content.ReadExactlyAsync(data, cancellationToken);
        var extra = new byte[1];
        if (await content.ReadAsync(extra, cancellationToken) != 0)
            throw new InvalidDataException("互传分块响应超过该序号允许的长度。");
        return data;
    }

    private static void ValidateDescriptor(TransferFileDescriptor descriptor,
        string receiveDirectory, string stagingRoot)
    {
        if (descriptor.TransferId == Guid.Empty
            || string.IsNullOrWhiteSpace(descriptor.FileName)
            || descriptor.FileName is "." or ".."
            || descriptor.FileName.IndexOfAny(new[] { '/', '\\' }) >= 0
            || !string.Equals(Path.GetFileName(descriptor.FileName), descriptor.FileName, StringComparison.Ordinal))
            throw new InvalidDataException("互传接收任务元信息无效。");
        if (TransferChunkPolicy.GetChunkCount(descriptor.TotalBytes) != descriptor.ChunkCount
            || !TransferChunkPolicy.IsValidSha256(descriptor.Sha256))
            throw new InvalidDataException("互传接收任务长度、分块数或 SHA-256 无效。");
        if (string.IsNullOrWhiteSpace(receiveDirectory) || string.IsNullOrWhiteSpace(stagingRoot))
            throw new InvalidDataException("互传接收目录配置无效。");
    }

    private static async Task<CompletionReceipt?> ReadReceiptAsync(
        string path,
        TransferFileDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (!info.Exists) return null;
        if (info.Length <= 0 || info.Length > MaximumReceiptBytes)
            throw new InvalidDataException("互传接收确认记录不存在或超出大小限制。");
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var receipt = await JsonSerializer.DeserializeAsync<CompletionReceipt>(input, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("互传接收确认记录格式无效。");
        if (receipt.TransferId != descriptor.TransferId
            || !string.Equals(receipt.FileName, descriptor.FileName, StringComparison.Ordinal)
            || receipt.TotalBytes != descriptor.TotalBytes
            || !string.Equals(receipt.Sha256, descriptor.Sha256, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(receipt.SavedPath))
            throw new InvalidDataException("互传接收确认记录与服务端任务不匹配。");
        return receipt;
    }

    private static async Task SaveReceiptAsync(string path, CompletionReceipt receipt,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(receipt, JsonOptions);
        if (bytes.LongLength > MaximumReceiptBytes)
            throw new InvalidDataException("互传接收确认记录超过大小限制。");
        var temporary = path + ".tmp";
        try
        {
            await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await output.WriteAsync(bytes, cancellationToken);
                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
            throw;
        }
    }

    private static async Task<string?> FindCommittedFileAsync(
        TransferFileDescriptor descriptor,
        string receiveDirectory,
        CancellationToken cancellationToken)
    {
        var fullReceiveDirectory = Path.GetFullPath(receiveDirectory);
        if (!Directory.Exists(fullReceiveDirectory)) return null;
        foreach (var path in Directory.EnumerateFiles(fullReceiveDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsSameNameFamily(Path.GetFileName(path), descriptor.FileName)) continue;
            var info = new FileInfo(path);
            if (info.Length != descriptor.TotalBytes) continue;
            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken));
            if (string.Equals(hash, descriptor.Sha256, StringComparison.OrdinalIgnoreCase))
                return Path.GetFullPath(path);
        }
        return null;
    }

    private static bool IsSameNameFamily(string candidate, string requested)
    {
        var extension = Path.GetExtension(requested);
        var stem = Path.GetFileNameWithoutExtension(requested);
        if (string.Equals(candidate, requested, StringComparison.OrdinalIgnoreCase)) return true;
        if (candidate.Length <= stem.Length + extension.Length + 3
            || !candidate.StartsWith(stem + " (", StringComparison.OrdinalIgnoreCase)
            || !candidate.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) return false;
        var numberLength = candidate.Length - stem.Length - extension.Length - 3;
        return numberLength > 0
            && candidate.AsSpan(stem.Length + 2, numberLength).IndexOfAnyExceptInRange('0', '9') < 0
            && candidate[stem.Length + numberLength + 2] == ')';
    }

    private static async Task VerifySavedFileAsync(CompletionReceipt receipt, string receiveDirectory,
        CancellationToken cancellationToken)
    {
        var fullDirectory = Path.GetFullPath(receiveDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(receipt.SavedPath);
        if (!fullPath.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("互传接收确认记录指向接收目录以外的路径。");
        var info = new FileInfo(fullPath);
        if (!info.Exists || info.Length != receipt.TotalBytes)
            throw new IOException("已保存的互传文件不存在或大小发生变化；未向服务端确认接收。");
        await using var input = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken));
        if (!string.Equals(hash, receipt.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("已保存的互传文件 SHA-256 已变化；未向服务端确认接收。");
    }

    private static string GetReceiptPath(string root, Guid id) =>
        Path.Combine(root, "receive-ack-outbox", id.ToString("N") + ".json");

    private sealed class CompletionReceipt
    {
        public Guid TransferId { get; set; }
        public string FileName { get; set; } = "";
        public long TotalBytes { get; set; }
        public string Sha256 { get; set; } = "";
        public string SavedPath { get; set; } = "";
        public DateTimeOffset UpdatedAt { get; set; }
    }
}
