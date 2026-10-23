using System.Security.Cryptography;
using System.Text.Json;

namespace KuaiJian.WinUI;

/// <summary>
/// Durable, transport-agnostic receive core. A server adapter may acknowledge a
/// transfer only after CompleteAsync returns its committed destination path.
/// </summary>
public sealed class ResumableTransferReceiver
{
    private const long MaximumCheckpointBytes = 16 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly string _stageDirectory;
    private readonly string _partPath;
    private readonly string _checkpointPath;
    private readonly string _receiveDirectory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SortedSet<long> _receivedChunks;
    private int _receivedChunkCount;
    private long _receivedByteCount;

    public Guid TransferId { get; }
    public string FileName { get; }
    public long TotalBytes { get; }
    public string Sha256 { get; }
    public long ChunkCount => TransferChunkPolicy.GetChunkCount(TotalBytes);
    public int ReceivedChunkCount => Volatile.Read(ref _receivedChunkCount);
    public long ReceivedByteCount => Interlocked.Read(ref _receivedByteCount);

    public Task<IReadOnlyList<long>> GetPendingChunkIndicesAsync(int maximumCount = 1,
        CancellationToken cancellationToken = default) =>
        GetPendingChunkIndicesAsync(0, maximumCount, cancellationToken);

    public async Task<IReadOnlyList<long>> GetPendingChunkIndicesAsync(long startingAt, int maximumCount,
        CancellationToken cancellationToken = default)
    {
        if (maximumCount <= 0) throw new ArgumentOutOfRangeException(nameof(maximumCount));
        if (startingAt < 0 || startingAt > ChunkCount)
            throw new ArgumentOutOfRangeException(nameof(startingAt));
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var pending = new List<long>(maximumCount);
            for (long index = startingAt; index < ChunkCount && pending.Count < maximumCount; index++)
                if (!_receivedChunks.Contains(index)) pending.Add(index);
            return pending;
        }
        finally
        {
            _gate.Release();
        }
    }

    private ResumableTransferReceiver(
        Guid transferId,
        string fileName,
        long totalBytes,
        string sha256,
        string receiveDirectory,
        string stageDirectory,
        SortedSet<long> receivedChunks)
    {
        TransferId = transferId;
        FileName = fileName;
        TotalBytes = totalBytes;
        Sha256 = sha256;
        _receiveDirectory = Path.GetFullPath(receiveDirectory);
        _stageDirectory = stageDirectory;
        _partPath = Path.Combine(stageDirectory, "payload.part");
        _checkpointPath = Path.Combine(stageDirectory, "checkpoint.json");
        _receivedChunks = receivedChunks;
        _receivedChunkCount = receivedChunks.Count;
        _receivedByteCount = receivedChunks.Sum(index =>
            (long)TransferChunkPolicy.GetChunkLength(totalBytes, index));
    }

    public static async Task<ResumableTransferReceiver> BeginAsync(
        string transferId,
        string fileName,
        long totalBytes,
        string sha256,
        string receiveDirectory,
        string stagingRoot,
        CancellationToken cancellationToken = default)
    {
        var id = ParseTransferId(transferId);
        ValidateMetadata(fileName, totalBytes, sha256, receiveDirectory, stagingRoot);
        cancellationToken.ThrowIfCancellationRequested();
        var stageDirectory = GetStageDirectory(stagingRoot, id);
        Directory.CreateDirectory(Path.GetDirectoryName(stageDirectory)!);
        if (Directory.Exists(stageDirectory))
            throw new IOException("此互传已存在检查点；请恢复已有接收任务，而不是覆盖它。");
        Directory.CreateDirectory(stageDirectory);
        var partPath = Path.Combine(stageDirectory, "payload.part");
        var partCreated = false;
        try
        {
            await using (new FileStream(partPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                1, FileOptions.Asynchronous)) { partCreated = true; }
            var receiver = new ResumableTransferReceiver(id, SafeFileName(fileName), totalBytes,
                sha256.ToUpperInvariant(), receiveDirectory, stageDirectory, new SortedSet<long>());
            await receiver.SaveCheckpointAsync(cancellationToken);
            return receiver;
        }
        catch
        {
            if (partCreated) TryDelete(partPath);
            TryDelete(Path.Combine(stageDirectory, "checkpoint.json"));
            TryDelete(Path.Combine(stageDirectory, "checkpoint.json.tmp"));
            try { Directory.Delete(stageDirectory); } catch { }
            throw;
        }
    }

    public static async Task<ResumableTransferReceiver> ResumeAsync(
        string transferId,
        string receiveDirectory,
        string stagingRoot,
        CancellationToken cancellationToken = default)
    {
        var id = ParseTransferId(transferId);
        var stageDirectory = GetStageDirectory(stagingRoot, id);
        var checkpointPath = Path.Combine(stageDirectory, "checkpoint.json");
        var info = new FileInfo(checkpointPath);
        if (!info.Exists || info.Length <= 0 || info.Length > MaximumCheckpointBytes)
            throw new InvalidDataException("互传检查点不存在或超出大小限制。");

        await using var input = new FileStream(checkpointPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var checkpoint = await JsonSerializer.DeserializeAsync<Checkpoint>(input, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("互传检查点格式无效。");
        if (checkpoint.TransferId != id)
            throw new InvalidDataException("互传检查点标识不匹配。");
        ValidateMetadata(checkpoint.FileName, checkpoint.TotalBytes, checkpoint.Sha256,
            receiveDirectory, stagingRoot);
        var count = TransferChunkPolicy.GetChunkCount(checkpoint.TotalBytes);
        if (checkpoint.ReceivedChunks is null
            || checkpoint.ReceivedChunks.Any(index => index < 0 || index >= count)
            || checkpoint.ReceivedChunks.Distinct().Count() != checkpoint.ReceivedChunks.Count)
            throw new InvalidDataException("互传检查点中的分块序号无效。");
        if (!File.Exists(Path.Combine(stageDirectory, "payload.part")))
            throw new InvalidDataException("互传临时文件不存在，无法继续接收。");

        return new ResumableTransferReceiver(id, SafeFileName(checkpoint.FileName), checkpoint.TotalBytes,
            checkpoint.Sha256.ToUpperInvariant(), receiveDirectory, stageDirectory,
            new SortedSet<long>(checkpoint.ReceivedChunks));
    }

    public async Task ReceiveChunkAsync(long index, ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var expectedLength = TransferChunkPolicy.GetChunkLength(TotalBytes, index);
            if (data.Length != expectedLength)
                throw new InvalidDataException("互传分块长度与文件布局不匹配。");
            cancellationToken.ThrowIfCancellationRequested();
            var alreadyReceived = _receivedChunks.Contains(index);

            await using (var output = new FileStream(_partPath, FileMode.Open, FileAccess.Write, FileShare.None,
                128 * 1024, FileOptions.Asynchronous | FileOptions.RandomAccess))
            {
                output.Seek(TransferChunkPolicy.GetChunkOffset(index), SeekOrigin.Begin);
                await output.WriteAsync(data, cancellationToken);
                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }

            if (!alreadyReceived)
            {
                _receivedChunks.Add(index);
                try
                {
                    await SaveCheckpointAsync(cancellationToken);
                    Interlocked.Increment(ref _receivedChunkCount);
                    Interlocked.Add(ref _receivedByteCount, data.Length);
                }
                catch
                {
                    _receivedChunks.Remove(index);
                    throw;
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string> CompleteAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var count = ChunkCount;
            if (_receivedChunks.Count != count)
                throw new InvalidOperationException("文件分块尚未全部接收，不能提交或确认接收。");
            for (long index = 0; index < count; index++)
                if (!_receivedChunks.Contains(index))
                    throw new InvalidOperationException("文件分块不完整，不能提交或确认接收。");

            cancellationToken.ThrowIfCancellationRequested();
            await using (var input = new FileStream(_partPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                if (input.Length != TotalBytes)
                    throw new InvalidDataException("接收文件大小与声明值不一致。");
                var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken));
                if (!CryptographicOperations.FixedTimeEquals(
                        Convert.FromHexString(actualHash), Convert.FromHexString(Sha256)))
                    throw new InvalidDataException("接收文件 SHA-256 校验失败，未保存或确认接收。");
            }

            var destination = await DownloadFileStore.MoveToUniquePathAsync(
                _partPath, _receiveDirectory, FileName, cancellationToken);
            TryDelete(_checkpointPath);
            TryDelete(_checkpointPath + ".tmp");
            try { Directory.Delete(_stageDirectory); } catch { }
            return destination;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SaveCheckpointAsync(CancellationToken cancellationToken)
    {
        var checkpoint = new Checkpoint
        {
            TransferId = TransferId,
            FileName = FileName,
            TotalBytes = TotalBytes,
            Sha256 = Sha256,
            ReceivedChunks = _receivedChunks.ToList(),
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(checkpoint, JsonOptions);
        if (bytes.LongLength > MaximumCheckpointBytes)
            throw new InvalidDataException("互传检查点超过大小限制。");
        var temporary = _checkpointPath + ".tmp";
        await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None,
            16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await output.WriteAsync(bytes, cancellationToken);
            await output.FlushAsync(cancellationToken);
            output.Flush(flushToDisk: true);
        }
        File.Move(temporary, _checkpointPath, overwrite: true);
    }

    private static Guid ParseTransferId(string transferId) =>
        Guid.TryParse(transferId, out var id) && id != Guid.Empty
            ? id
            : throw new InvalidDataException("互传标识格式无效。");

    private static void ValidateMetadata(string fileName, long totalBytes, string sha256,
        string receiveDirectory, string stagingRoot)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName is "." or ".."
            || fileName.IndexOfAny(new[] { '/', '\\' }) >= 0
            || !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
            throw new InvalidDataException("接收文件名无效。");
        _ = TransferChunkPolicy.GetChunkCount(totalBytes);
        if (!TransferChunkPolicy.IsValidSha256(sha256))
            throw new InvalidDataException("接收文件 SHA-256 格式无效。");
        if (string.IsNullOrWhiteSpace(receiveDirectory) || string.IsNullOrWhiteSpace(stagingRoot))
            throw new InvalidDataException("互传接收目录配置无效。");
    }

    private static string SafeFileName(string value) => Path.GetFileName(value);

    private static string GetStageDirectory(string root, Guid id) =>
        Path.Combine(Path.GetFullPath(root), id.ToString("N"));

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private sealed class Checkpoint
    {
        public Guid TransferId { get; set; }
        public string FileName { get; set; } = "";
        public long TotalBytes { get; set; }
        public string Sha256 { get; set; } = "";
        public List<long> ReceivedChunks { get; set; } = new();
        public DateTimeOffset UpdatedAt { get; set; }
    }
}
