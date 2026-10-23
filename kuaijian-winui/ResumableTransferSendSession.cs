using System.Text.Json;

namespace KuaiJian.WinUI;

/// <summary>
/// Persists sender-side progress after the transport confirms each chunk.
/// Call AcknowledgeChunkAsync only after a positive server acknowledgement.
/// </summary>
public sealed class ResumableTransferSendSession : IAsyncDisposable
{
    private const long MaximumCheckpointBytes = 16 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly string _checkpointPath;
    private readonly string _sourcePath;
    private readonly TransferFileSender _sender;
    private readonly SortedSet<long> _acknowledgedChunks;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _acknowledgedChunkCount;
    private long _acknowledgedByteCount;

    public TransferFileDescriptor Descriptor => _sender.Descriptor;
    public int AcknowledgedChunkCount => Volatile.Read(ref _acknowledgedChunkCount);
    public long AcknowledgedByteCount => Interlocked.Read(ref _acknowledgedByteCount);
    public bool IsFullyAcknowledged => AcknowledgedChunkCount == Descriptor.ChunkCount;
    public string CheckpointPath => _checkpointPath;

    private ResumableTransferSendSession(
        string checkpointPath,
        string sourcePath,
        TransferFileSender sender,
        SortedSet<long> acknowledgedChunks)
    {
        _checkpointPath = checkpointPath;
        _sourcePath = sourcePath;
        _sender = sender;
        _acknowledgedChunks = acknowledgedChunks;
        _acknowledgedChunkCount = acknowledgedChunks.Count;
        _acknowledgedByteCount = acknowledgedChunks.Sum(index =>
            (long)TransferChunkPolicy.GetChunkLength(sender.Descriptor.TotalBytes, index));
    }

    public static async Task<ResumableTransferSendSession> BeginAsync(
        string sourcePath,
        string checkpointRoot,
        string? transferId = null,
        CancellationToken cancellationToken = default)
    {
        var sender = await TransferFileSender.OpenAsync(sourcePath, transferId, cancellationToken);
        var fullRoot = Path.GetFullPath(checkpointRoot);
        Directory.CreateDirectory(fullRoot);
        var checkpointPath = GetCheckpointPath(fullRoot, sender.Descriptor.TransferId);
        if (File.Exists(checkpointPath))
        {
            await sender.DisposeAsync();
            throw new IOException("此发送任务已有检查点；请恢复已有任务，而不是覆盖它。");
        }

        var session = new ResumableTransferSendSession(checkpointPath,
            Path.GetFullPath(sourcePath), sender, new SortedSet<long>());
        try
        {
            await session.SaveCheckpointAsync(overwrite: false, cancellationToken);
            return session;
        }
        catch
        {
            await sender.DisposeAsync();
            TryDelete(checkpointPath + ".tmp");
            throw;
        }
    }

    public static async Task<ResumableTransferSendSession> ResumeAsync(
        string transferId,
        string checkpointRoot,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(transferId, out var id) || id == Guid.Empty)
            throw new InvalidDataException("互传标识格式无效。");
        var fullRoot = Path.GetFullPath(checkpointRoot);
        var checkpointPath = GetCheckpointPath(fullRoot, id);
        var info = new FileInfo(checkpointPath);
        if (!info.Exists || info.Length <= 0 || info.Length > MaximumCheckpointBytes)
            throw new InvalidDataException("互传发送检查点不存在或超出大小限制。");

        await using var input = new FileStream(checkpointPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var checkpoint = await JsonSerializer.DeserializeAsync<Checkpoint>(input, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("互传发送检查点格式无效。");
        if (checkpoint.TransferId != id || string.IsNullOrWhiteSpace(checkpoint.SourcePath)
            || checkpoint.AcknowledgedChunks is null)
            throw new InvalidDataException("互传发送检查点内容无效。");

        var sender = await TransferFileSender.OpenAsync(checkpoint.SourcePath, id.ToString("D"), cancellationToken);
        var descriptor = sender.Descriptor;
        var matches = string.Equals(descriptor.FileName, checkpoint.FileName, StringComparison.Ordinal)
            && descriptor.TotalBytes == checkpoint.TotalBytes
            && string.Equals(descriptor.Sha256, checkpoint.Sha256, StringComparison.OrdinalIgnoreCase)
            && descriptor.ChunkCount == checkpoint.ChunkCount;
        if (!matches)
        {
            await sender.DisposeAsync();
            throw new InvalidDataException("发送源文件已变化，不能安全恢复原互传任务。");
        }

        var chunks = checkpoint.AcknowledgedChunks;
        if (chunks.Any(index => index < 0 || index >= descriptor.ChunkCount)
            || chunks.Distinct().Count() != chunks.Count)
        {
            await sender.DisposeAsync();
            throw new InvalidDataException("互传发送检查点的分块确认序号无效。");
        }

        return new ResumableTransferSendSession(checkpointPath, Path.GetFullPath(checkpoint.SourcePath),
            sender, new SortedSet<long>(chunks));
    }

    public Task<IReadOnlyList<long>> GetPendingChunkIndicesAsync(int maximumCount = 1,
        CancellationToken cancellationToken = default) =>
        GetPendingChunkIndicesAsync(0, maximumCount, cancellationToken);

    public async Task<IReadOnlyList<long>> GetPendingChunkIndicesAsync(long startingAt, int maximumCount,
        CancellationToken cancellationToken = default)
    {
        if (maximumCount <= 0) throw new ArgumentOutOfRangeException(nameof(maximumCount));
        if (startingAt < 0 || startingAt > Descriptor.ChunkCount)
            throw new ArgumentOutOfRangeException(nameof(startingAt));
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var pending = new List<long>(maximumCount);
            for (long index = startingAt; index < Descriptor.ChunkCount && pending.Count < maximumCount; index++)
                if (!_acknowledgedChunks.Contains(index)) pending.Add(index);
            return pending;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<byte[]> ReadChunkAsync(long index, CancellationToken cancellationToken = default) =>
        _sender.ReadChunkAsync(index, cancellationToken);

    public async Task AcknowledgeChunkAsync(long index, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (index < 0 || index >= Descriptor.ChunkCount)
                throw new ArgumentOutOfRangeException(nameof(index));
            if (_acknowledgedChunks.Contains(index)) return;

            var acknowledgedLength = TransferChunkPolicy.GetChunkLength(Descriptor.TotalBytes, index);
            _acknowledgedChunks.Add(index);
            try
            {
                await SaveCheckpointAsync(overwrite: true, cancellationToken);
                Interlocked.Increment(ref _acknowledgedChunkCount);
                Interlocked.Add(ref _acknowledgedByteCount, acknowledgedLength);
            }
            catch
            {
                _acknowledgedChunks.Remove(index);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveCheckpointAfterRemoteCompletionAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_acknowledgedChunks.Count != Descriptor.ChunkCount)
                throw new InvalidOperationException("发送分块尚未全部确认，不能清除互传检查点。");
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(_checkpointPath)) File.Delete(_checkpointPath);
            if (File.Exists(_checkpointPath))
                throw new IOException("互传发送检查点无法清除。");
            TryDelete(_checkpointPath + ".tmp");
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SaveCheckpointAsync(bool overwrite, CancellationToken cancellationToken)
    {
        var checkpoint = new Checkpoint
        {
            TransferId = Descriptor.TransferId,
            SourcePath = _sourcePath,
            FileName = Descriptor.FileName,
            TotalBytes = Descriptor.TotalBytes,
            Sha256 = Descriptor.Sha256,
            ChunkCount = Descriptor.ChunkCount,
            AcknowledgedChunks = _acknowledgedChunks.ToList(),
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(checkpoint, JsonOptions);
        if (bytes.LongLength > MaximumCheckpointBytes)
            throw new InvalidDataException("互传发送检查点超过大小限制。");
        var temporary = _checkpointPath + ".tmp";
        try
        {
            await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None,
                16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await output.WriteAsync(bytes, cancellationToken);
                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }
            File.Move(temporary, _checkpointPath, overwrite);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    public ValueTask DisposeAsync() => _sender.DisposeAsync();

    private static string GetCheckpointPath(string root, Guid id) =>
        Path.Combine(root, id.ToString("N") + ".send.json");

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private sealed class Checkpoint
    {
        public Guid TransferId { get; set; }
        public string SourcePath { get; set; } = "";
        public string FileName { get; set; } = "";
        public long TotalBytes { get; set; }
        public string Sha256 { get; set; } = "";
        public long ChunkCount { get; set; }
        public List<long> AcknowledgedChunks { get; set; } = new();
        public DateTimeOffset UpdatedAt { get; set; }
    }
}
