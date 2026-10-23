using System.Text.Json;

namespace KuaiJian.WinUI;

public static class ClipboardImportFormatPolicy
{
    public static bool ShouldTryCopyQJson(string content)
    {
        var first = content.AsSpan().TrimStart();
        return !first.IsEmpty && first[0] is '{' or '[';
    }

    public static async Task<T?> TryParseCopyQJsonAsync<T>(string content, Func<string, Task<T?>> parser)
    {
        if (!ShouldTryCopyQJson(content)) return default;
        try
        {
            return await parser(content);
        }
        catch (JsonException)
        {
            return default;
        }
    }
}
