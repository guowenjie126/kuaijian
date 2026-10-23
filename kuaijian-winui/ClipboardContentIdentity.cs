using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace KuaiJian.WinUI;

public static class ClipboardContentIdentity
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<string>>> ContentHashCache = new(StringComparer.OrdinalIgnoreCase);

    public static async Task<string> CreateKeyAsync(
        bool isImage,
        string kind,
        string? text,
        string? html,
        string? imagePath,
        string? rtfPath,
        IEnumerable<string>? fileUrls)
    {
        var imageIdentity = isImage
            ? await GetFileIdentityAsync(imagePath, "image")
            : imagePath ?? "";
        var rtfIdentity = !isImage && !string.IsNullOrWhiteSpace(rtfPath)
            ? await GetFileIdentityAsync(rtfPath, "rtf")
            : "";
        var files = string.Join("\u001F", (fileUrls ?? Array.Empty<string>())
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
        return string.Join("\u001E", kind, text ?? "", html ?? "", imageIdentity, rtfIdentity, files);
    }

    private static async Task<string> GetFileIdentityAsync(string? path, string contentType)
    {
        if (string.IsNullOrWhiteSpace(path)) return contentType + "-path:";
        string fullPath;
        FileInfo info;
        try
        {
            fullPath = Path.GetFullPath(path);
            info = new FileInfo(fullPath);
            if (!info.Exists) return $"missing-{contentType}:" + fullPath;
        }
        catch
        {
            return $"invalid-{contentType}-path:" + path;
        }

        var cacheKey = $"{fullPath}\u001F{info.Length}\u001F{info.LastWriteTimeUtc.Ticks}";
        if (ContentHashCache.Count > 4096) ContentHashCache.Clear();
        var hash = ContentHashCache.GetOrAdd(cacheKey, _ => new Lazy<Task<string>>(
            async () =>
            {
                await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read,
                    FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                var digest = await SHA256.HashDataAsync(stream);
                return Convert.ToHexString(digest);
            }, LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return $"{contentType}-sha256:" + await hash.Value;
        }
        catch
        {
            ContentHashCache.TryRemove(cacheKey, out _);
            return $"unreadable-{contentType}:" + fullPath;
        }
    }
}
