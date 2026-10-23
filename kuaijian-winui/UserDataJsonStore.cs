using System.Collections.Concurrent;
using System.Text.Json;

namespace KuaiJian.WinUI;

internal static class UserDataJsonStore
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SaveLocks = new(StringComparer.OrdinalIgnoreCase);

    public static async Task SaveAtomicallyAsync<T>(string path, T value, JsonSerializerOptions options)
    {
        var saveLock = SaveLocks.GetOrAdd(path, static _ => new SemaphoreSlim(1, 1));
        await saveLock.WaitAsync();
        var temp = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using (var stream = File.Create(temp))
            {
                await JsonSerializer.SerializeAsync(stream, value, options);
            }
            File.Move(temp, path, true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            saveLock.Release();
        }
    }

    public static async Task<T?> LoadPreservingCorruptAsync<T>(
        string path,
        JsonSerializerOptions options,
        Action<string> reportWarning)
    {
        if (!File.Exists(path)) return default;
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(stream, options);
        }
        catch (JsonException)
        {
            var backup = path + ".corrupt-" + DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss-fff")
                + "-" + Guid.NewGuid().ToString("N")[..6];
            try
            {
                File.Copy(path, backup, false);
                reportWarning($"{Path.GetFileName(path)} 格式损坏，原文件已备份为 {Path.GetFileName(backup)}。请检查备份后再重新导入数据。");
            }
            catch (Exception backupError)
            {
                reportWarning($"{Path.GetFileName(path)} 格式损坏，自动备份失败（{backupError.Message}）；原文件仍保留，请勿覆盖。");
            }
            return default;
        }
    }
}
