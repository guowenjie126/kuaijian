namespace KuaiJian.WinUI;

public static class ClipboardCaptureCleanup
{
    public static void DeleteOrphanedFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Cleanup is best-effort; capture failures must not escape the clipboard callback.
        }
    }
}
