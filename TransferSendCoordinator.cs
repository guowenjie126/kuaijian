using System.Security.Cryptography;

namespace KuaiJian.WinUI;

/// <summary>
/// Transport-neutral messages. A concrete adapter maps these to the server's
/// actual routes and must only return an acknowledgement after it has accepted
/// the identified chunk durably.
/// </summary>
public sealed record TransferChunkUpload(
    Guid TransferId,
    long Index,
    string Sha256,
    ReadOnlyMemory<byte> Data);

public sealed record TransferChunkAcknowledgement(Guid TransferId, long Index);

/// <summary>
/// Saved must mean the remote side has verified and committed the whole file,
/// not merely that it received the final request.
/// </summary>
public sealed record TransferCompletionAcknowledgement(Guid TransferId, string Sha256, bool Saved);

public interface ITransferSendTransport
{
    /// <summary>
    /// Must be idempotent for a transfer ID and reject an existing ID whose
    /// immutable file descriptor differs.
    /// </summary>
    Task BeginAsync(TransferFileDescriptor descriptor, CancellationToken cancellationToken);

    /// <summary>
    /// Must be idempotent for the same transfer ID, index and chunk hash; a
    /// successful result means that exact chunk is durably accepted.
    /// </summary>
    Task<TransferChunkAcknowledgement> UploadChunkAsync(
        TransferChunkUpload chunk, CancellationToken cancellationToken);

    /// <summary>
    /// Must be safe to repeat after a lost response and only report Saved once
    /// the complete file has been hash-verified and committed remotely.
    /// </summary>
    Task<TransferCompletionAcknowledgement> CompleteAsync(
        TransferFileDescriptor descriptor, CancellationToken cancellationToken);
}

public sealed record TransferSendProgress(
    int AcknowledgedChunks,
    long TotalChunks,
    long AcknowledgedBytes,
    long TotalBytes);

/// <summary>
/// Drives a resumable sender through a concrete transport adapter. Local
/// checkpoints advance only after matching remote acknowledgements; failed or
/// interrupted requests remain safe to retry with the same transfer ID.
/// </summary>
public static class TransferSendCoordinator
{
    public static async Task SendAsync(
        ResumableTransferSendSession session,
        ITransferSendTransport transport,
        IProgress<TransferSendProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(transport);

        var descriptor = session.Descriptor;
        await transport.BeginAsync(descriptor, cancellationToken);

        long nextIndex = 0;
        while (nextIndex < descriptor.ChunkCount)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pending = await session.GetPendingChunkIndicesAsync(nextIndex, 1, cancellationToken);
            if (pending.Count == 0) break;

            var index = pending[0];
            var bytes = await session.ReadChunkAsync(index, cancellationToken);
            var upload = new TransferChunkUpload(
                descriptor.TransferId,
                index,
                Convert.ToHexString(SHA256.HashData(bytes)),
                bytes);
            var acknowledgement = await transport.UploadChunkAsync(upload, cancellationToken);
            if (acknowledgement.TransferId != descriptor.TransferId || acknowledgement.Index != index)
                throw new InvalidDataException("互传服务返回了不匹配的分块确认，发送检查点未推进。");

            await session.AcknowledgeChunkAsync(index, cancellationToken);
            progress?.Report(new TransferSendProgress(
                session.AcknowledgedChunkCount,
                descriptor.ChunkCount,
                session.AcknowledgedByteCount,
                descriptor.TotalBytes));
            nextIndex = index + 1;
        }

        var completion = await transport.CompleteAsync(descriptor, cancellationToken);
        if (completion.TransferId != descriptor.TransferId
            || !completion.Saved
            || !string.Equals(completion.Sha256, descriptor.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("互传服务未确认文件已完整校验并保存；发送检查点已保留。");

        await session.RemoveCheckpointAfterRemoteCompletionAsync(cancellationToken);
    }
}
