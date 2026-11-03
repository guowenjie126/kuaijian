namespace KuaiJian.WinUI;

public static class ClipboardSyncPolicy
{
    public const int MaxRecords = 500;
    public const int MaxPayloadBytes = 8 * 1024 * 1024;
    public const int MaxIdLength = 128;
    public const int MaxTitleLength = 4096;
    public const int MaxTextLength = 256 * 1024;
    public const int MaxHtmlLength = 512 * 1024;
    public const int MaxSourceLength = 1024;
    public const int MaxNoteLength = 64 * 1024;

    public static bool IsSyncable(bool isImage, bool hasImagePath, bool hasRichTextPath, int filePathCount) =>
        !isImage && !hasImagePath && !hasRichTextPath && filePathCount == 0;

    public static string? ContentKey(string? text, string? html)
    {
        text = (text ?? "").Replace("\r\n", "\n", StringComparison.Ordinal);
        html = (html ?? "").Replace("\r\n", "\n", StringComparison.Ordinal);
        // Text is the portable identity; HTML serialization often differs
        // between macOS and Windows for the same visible clipboard value.
        if (!string.IsNullOrWhiteSpace(text)) return "text\u001f" + text;
        return string.IsNullOrWhiteSpace(html) ? null : "html\u001f" + html;
    }

    public static bool IsValidRecord(
        string? id,
        string? title,
        string? text,
        string? html,
        string? source,
        string? note) =>
        !string.IsNullOrWhiteSpace(id) && id.Length <= MaxIdLength
        && (title?.Length ?? 0) <= MaxTitleLength
        && (text?.Length ?? 0) <= MaxTextLength
        && (html?.Length ?? 0) <= MaxHtmlLength
        && (source?.Length ?? 0) <= MaxSourceLength
        && (note?.Length ?? 0) <= MaxNoteLength;

    public static bool IsPayloadLengthAllowed(long? length) =>
        length is null || (length.Value >= 0 && length.Value <= MaxPayloadBytes);
}
