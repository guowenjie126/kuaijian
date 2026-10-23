namespace KuaiJian.WinUI;

public static class DownloadTaskOperationPolicy
{
    public static bool CanStart(string? taskId, IEnumerable<string> activeTaskIds) =>
        !string.IsNullOrWhiteSpace(taskId)
        && !activeTaskIds.Contains(taskId, StringComparer.Ordinal);
}
