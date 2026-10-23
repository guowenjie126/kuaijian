namespace KuaiJian.WinUI;

public sealed class LegacyClipboardImportBudget
{
    public const long MaximumBytes = 64L * 1024 * 1024;

    private long _inputRemaining = MaximumBytes;
    private long _expandedRemaining = MaximumBytes;

    public long InputRemaining => _inputRemaining;
    public long ExpandedRemaining => _expandedRemaining;

    public void ReserveInput(long bytes)
    {
        if (bytes < 0 || bytes > _inputRemaining)
            throw new InvalidDataException($"历史导入文件总量超过 {MaximumBytes / (1024 * 1024)} MB 限制。");
        _inputRemaining -= bytes;
    }

    public void ReserveExpanded(long bytes)
    {
        if (bytes < 0 || bytes > _expandedRemaining)
            throw new InvalidDataException($"解压后的历史文件总量超过 {MaximumBytes / (1024 * 1024)} MB 限制。");
        _expandedRemaining -= bytes;
    }
}
