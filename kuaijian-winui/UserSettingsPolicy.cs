namespace KuaiJian.WinUI;

public static class UserSettingsPolicy
{
    private static readonly HashSet<string> UserScopedKeys = new(StringComparer.Ordinal)
    {
        "clipboardRetentionLimit",
        "downloadDirectory",
        "downloadBrowser",
        "downloadConcurrency",
        "transferReceiveDirectory",
        "captureText",
        "captureImages",
        "captureURLs",
        "captureRichText",
        "qaAssistantEnabled",
        "qaAssistantSources",
        "alwaysOnTop",
        "closeToTrayEnabled",
        "doubleClickCollapseEnabled"
    };

    public static IReadOnlyCollection<string> AccountKeys => UserScopedKeys;

    public static string? StorageKey(string key, string? userId)
    {
        if (!UserScopedKeys.Contains(key)) return key;
        if (!AuthResponsePolicy.IsSafeAccountId(userId)) return null;
        return $"user:{userId}:{key}";
    }

    public static void MigrateLegacyValues(IDictionary<string, object> values, string userId)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (!AuthResponsePolicy.IsSafeAccountId(userId))
            throw new InvalidDataException("账号标识格式无效，无法迁移偏好设置。");

        foreach (var key in UserScopedKeys)
        {
            var scopedKey = StorageKey(key, userId)!;
            if (!values.ContainsKey(scopedKey) && values.TryGetValue(key, out var legacyValue))
                values[scopedKey] = legacyValue;
            values.Remove(key);
        }
    }
}
