using System.Net;
using System.Net.Http.Headers;

namespace KuaiJian.WinUI;

public sealed record DownloadResumeMetadata(
    string FileName,
    string? ETag,
    DateTimeOffset? LastModified,
    long? TotalBytes);

public sealed record ResumableDownloadResult(string OutputPath, long TotalBytes);

public static class DownloadResumePolicy
{
    public static bool HasStableValidator(DownloadResumeMetadata? metadata) =>
        metadata is not null && (HasStrongETag(metadata.ETag) || metadata.LastModified is not null);

    public static bool HasStrongETag(string? value) =>
        EntityTagHeaderValue.TryParse(value, out var parsed) && !parsed.IsWeak && parsed.Tag != "*";

    public static bool CanAppendRange(long expectedOffset, HttpStatusCode statusCode, long? rangeStart,
        string? expectedETag, DateTimeOffset? expectedLastModified,
        string? responseETag, DateTimeOffset? responseLastModified) =>
        expectedOffset > 0
        && statusCode == HttpStatusCode.PartialContent
        && rangeStart == expectedOffset
        && (expectedETag is null || responseETag is null || string.Equals(expectedETag, responseETag, StringComparison.Ordinal))
        && (expectedLastModified is null || responseLastModified is null || expectedLastModified == responseLastModified);
}

public static class ResumableHttpDownload
{
    private const int BufferSize = 128 * 1024;

    public static async Task<ResumableDownloadResult> DownloadAsync(
        HttpClient client,
        Uri source,
        string outputDirectory,
        string partialPath,
        DownloadResumeMetadata? previous,
        Func<DownloadResumeMetadata, long, Task> checkpoint,
        Action<long, long?> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(progress);
        if (source.Scheme is not ("http" or "https"))
            throw new ArgumentException("仅支持 HTTP/HTTPS 下载。", nameof(source));

        Directory.CreateDirectory(outputDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(partialPath)!);
        var offset = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
        if (offset == 0 || !DownloadResumePolicy.HasStableValidator(previous))
        {
            if (File.Exists(partialPath)) File.Delete(partialPath);
            offset = 0;
            previous = null;
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var request = new HttpRequestMessage(HttpMethod.Get, source);
            if (offset > 0 && previous is not null)
            {
                request.Headers.Range = new RangeHeaderValue(offset, null);
                request.Headers.IfRange = DownloadResumePolicy.HasStrongETag(previous.ETag)
                    ? new RangeConditionHeaderValue(EntityTagHeaderValue.Parse(previous.ETag!))
                    : new RangeConditionHeaderValue(previous.LastModified!.Value);
            }

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var append = false;
            if (offset > 0)
            {
                if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable
                    && response.Content.Headers.ContentRange?.Length == offset
                    && File.Exists(partialPath)
                    && previous is not null
                    && ValidatorConfirmed(previous, response))
                {
                    var completed = await DownloadFileStore.MoveToUniquePathAsync(
                        partialPath, outputDirectory, previous.FileName, cancellationToken);
                    return new ResumableDownloadResult(completed, offset);
                }

                if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && attempt == 0)
                {
                    if (File.Exists(partialPath)) File.Delete(partialPath);
                    offset = 0;
                    previous = null;
                    continue;
                }

                append = DownloadResumePolicy.CanAppendRange(
                    offset,
                    response.StatusCode,
                    response.Content.Headers.ContentRange?.From,
                    previous?.ETag,
                    previous?.LastModified,
                    response.Headers.ETag?.ToString(),
                    response.Content.Headers.LastModified);

                if (!append && response.StatusCode != HttpStatusCode.OK)
                {
                    if (attempt == 0)
                    {
                        response.EnsureSuccessStatusCode();
                        if (response.StatusCode == HttpStatusCode.PartialContent)
                        {
                            File.Delete(partialPath);
                            offset = 0;
                            previous = null;
                            continue;
                        }
                    }
                    response.EnsureSuccessStatusCode();
                    throw new InvalidDataException("服务器返回了与断点不匹配的下载范围。");
                }
            }
            else
            {
                response.EnsureSuccessStatusCode();
                if (response.StatusCode == HttpStatusCode.PartialContent
                    && response.Content.Headers.ContentRange?.From != 0)
                    throw new InvalidDataException("服务器返回的下载范围无效。");
            }

            if (!append) offset = 0;
            var fileName = append && previous is not null
                ? previous.FileName
                : GetFileName(response, source);
            var etag = response.Headers.ETag is { IsWeak: false } responseTag
                ? responseTag.ToString()
                : (append ? previous?.ETag : null);
            var lastModified = response.Content.Headers.LastModified ?? (append ? previous?.LastModified : null);
            var total = response.Content.Headers.ContentRange?.Length
                ?? (response.Content.Headers.ContentLength is long contentLength ? offset + contentLength : null);
            var metadata = new DownloadResumeMetadata(fileName, etag, lastModified, total);

            if (!append)
            {
                await using var truncate = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.Read, BufferSize, FileOptions.Asynchronous);
                await truncate.FlushAsync(cancellationToken);
            }
            await checkpoint(metadata, offset);

            await using (var output = new FileStream(partialPath, append ? FileMode.Open : FileMode.Open,
                FileAccess.ReadWrite, FileShare.Read, BufferSize, FileOptions.Asynchronous))
            {
                output.Position = output.Length;
                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
                var buffer = new byte[BufferSize];
                var written = output.Length;
                var bytesSinceFlush = 0L;
                int count;
                while ((count = await input.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                    written += count;
                    bytesSinceFlush += count;
                    progress(written, total);
                    if (bytesSinceFlush >= 1024 * 1024)
                    {
                        await output.FlushAsync(cancellationToken);
                        await checkpoint(metadata, written);
                        bytesSinceFlush = 0;
                    }
                }
                await output.FlushAsync(cancellationToken);
                if (total is long expectedTotal && written != expectedTotal)
                    throw new EndOfStreamException($"下载内容不完整（{written}/{expectedTotal} 字节）。");
                await checkpoint(metadata, written);
            }

            var destination = await DownloadFileStore.MoveToUniquePathAsync(
                partialPath, outputDirectory, fileName, cancellationToken);
            return new ResumableDownloadResult(destination, total ?? new FileInfo(destination).Length);
        }

        throw new InvalidDataException("服务器无法提供可验证的续传范围。");
    }

    private static string GetFileName(HttpResponseMessage response, Uri uri)
    {
        var headerName = response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName;
        var fileName = string.IsNullOrWhiteSpace(headerName)
            ? Path.GetFileName(Uri.UnescapeDataString(uri.LocalPath))
            : Uri.UnescapeDataString(headerName.Trim().Trim('"'));
        fileName = Path.GetFileName(fileName);
        if (!string.IsNullOrWhiteSpace(fileName) && fileName.Length <= 120)
            return fileName;

        var extension = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "video/mp4" => ".mp4",
            "audio/mpeg" => ".mp3",
            "application/pdf" => ".pdf",
            _ => ".bin"
        };
        return $"快剪下载-{DateTime.Now:yyyyMMdd-HHmmss}{extension}";
    }

    private static bool ValidatorConfirmed(DownloadResumeMetadata expected, HttpResponseMessage response) =>
        DownloadResumePolicy.HasStrongETag(expected.ETag)
            ? string.Equals(expected.ETag, response.Headers.ETag?.ToString(), StringComparison.Ordinal)
            : expected.LastModified is not null && expected.LastModified == response.Content.Headers.LastModified;
}
