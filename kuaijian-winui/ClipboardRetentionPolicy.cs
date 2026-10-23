namespace KuaiJian.WinUI;

public static class ClipboardRetentionPolicy
{
    public const int DefaultLimit = 200;
    public const int MinimumLimit = 50;
    public const int MaximumLimit = 500;

    public static int NormalizeLimit(int limit) => Math.Clamp(limit, MinimumLimit, MaximumLimit);

    public static List<T> Retain<T>(IEnumerable<T> items, int limit, Func<T, bool> isPinned,
        Func<T, DateTimeOffset> createdAt) => items
        .OrderByDescending(isPinned)
        .ThenByDescending(createdAt)
        .Take(NormalizeLimit(limit))
        .ToList();
}
