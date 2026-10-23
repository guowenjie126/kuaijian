namespace KuaiJian.WinUI;

public static class ClipboardWritePolicy
{
    public static bool TryWrite(Action markIgnored, Action writeClipboard, Action flush, Action cancelIgnore)
    {
        ArgumentNullException.ThrowIfNull(markIgnored);
        ArgumentNullException.ThrowIfNull(writeClipboard);
        ArgumentNullException.ThrowIfNull(flush);
        ArgumentNullException.ThrowIfNull(cancelIgnore);

        var ignored = false;
        var written = false;
        try
        {
            markIgnored();
            ignored = true;
            writeClipboard();
            written = true;
            flush();
            return true;
        }
        catch
        {
            if (ignored && !written)
            {
                try { cancelIgnore(); } catch { }
            }
            return false;
        }
    }
}
