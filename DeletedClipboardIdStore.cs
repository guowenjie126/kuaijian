using System.Collections.Concurrent;
using System.Text.Json;

namespace KuaiJian.WinUI;

public sealed class DeletedClipboardIdStore
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> FileLocks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<List<string>> LoadAsync(string filePath, Action<string>? warning = null)
    {
        if (!File.Exists(filePath)) return new();

        try
        {
            await using var stream = File.OpenRead(filePath);
            var ids = await JsonSerializer.DeserializeAsync<List<string>>(stream, JsonOptions);
            if (ids is null) throw new JsonException("删除清单不是 JSON 数组。");
            return Normalize(ids);
        }
        catch (JsonException)
        {
            var backupPath = filePath + ".corrupt-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff");
            File.Copy(filePath, backupPath, overwrite: false);
            warning?.Invoke("本机剪贴板删除清单损坏，原文件已备份；本次可能重新显示此前隐藏的云端记录。");
            return new();
        }
    }

    public async Task SaveAsync(string filePath, IEnumerable<string> ids)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(ids);

        var gate = FileLocks.GetOrAdd(Path.GetFullPath(filePath), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        string? temporaryPath = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            temporaryPath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16 * 1024, useAsync: true))
                await JsonSerializer.SerializeAsync(stream, Normalize(ids), JsonOptions);
            File.Move(temporaryPath, filePath, overwrite: true);
        }
        finally
        {
            try { if (temporaryPath is not null && File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch { }
            gate.Release();
        }
    }

    private static List<string> Normalize(IEnumerable<string> ids) => ids
        .Where(id => !string.IsNullOrWhiteSpace(id) && id.Length <= ClipboardSyncPolicy.MaxIdLength)
        .Distinct(StringComparer.Ordinal)
        .ToList();
}
