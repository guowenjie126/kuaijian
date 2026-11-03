using System.Text;

namespace KuaiJian.WinUI;

public sealed record ClipboardHistoryCsvRecord(string Text, string Title, bool IsUrl, string Source, string Note);

public static class ClipboardHistoryCsvCodec
{
    public static List<ClipboardHistoryCsvRecord> Parse(string text)
    {
        var rows = ParseRows(text);
        if (rows.Count == 0) return new();

        var header = rows[0].Select(value => value.Trim().ToLowerInvariant()).ToArray();
        var hasHeader = header.Any(value => value is "text" or "content" or "value" or "title" or "内容" or "文本");
        var textIndex = FindColumn(header, "text", "content", "value", "内容", "文本");
        var titleIndex = FindColumn(header, "title", "标题");
        var sourceIndex = FindColumn(header, "source", "app", "来源", "应用");
        var noteIndex = FindColumn(header, "note", "remark", "备注");
        if (textIndex < 0) textIndex = 0;

        var converted = new List<ClipboardHistoryCsvRecord>();
        foreach (var row in rows.Skip(hasHeader ? 1 : 0))
        {
            var value = Field(row, textIndex).Trim();
            if (value.Length == 0) continue;
            var title = Field(row, titleIndex).Trim();
            var source = Field(row, sourceIndex).Trim();
            converted.Add(new ClipboardHistoryCsvRecord(
                value,
                title.Length > 0 ? title : value,
                IsUrl(value),
                source.Length > 0 ? source : "旧版历史",
                Field(row, noteIndex).Trim()));
        }
        return converted;
    }

    private static List<string[]> ParseRows(string text)
    {
        var rows = new List<string[]>();
        var fields = new List<string>();
        var value = new StringBuilder();
        var quoted = false;
        var afterQuote = false;
        for (var index = 0; index < text.Length; index++)
        {
            var ch = text[index];
            if (quoted)
            {
                if (ch == '"' && index + 1 < text.Length && text[index + 1] == '"')
                {
                    value.Append('"');
                    index++;
                }
                else if (ch == '"')
                {
                    quoted = false;
                    afterQuote = true;
                }
                else value.Append(ch);
                continue;
            }

            if (afterQuote)
            {
                if (ch == ',')
                {
                    fields.Add(value.ToString());
                    value.Clear();
                    afterQuote = false;
                    continue;
                }
                if (ch is '\r' or '\n')
                {
                    fields.Add(value.ToString());
                    rows.Add(fields.ToArray());
                    fields.Clear();
                    value.Clear();
                    afterQuote = false;
                    if (ch == '\r' && index + 1 < text.Length && text[index + 1] == '\n') index++;
                    continue;
                }
                throw new FormatException("历史 CSV 的闭合引号后只能出现逗号或换行。");
            }

            if (ch == '"')
            {
                if (value.Length > 0) throw new FormatException("历史 CSV 的字段中存在未转义的双引号。");
                quoted = true;
                continue;
            }
            if (ch == ',')
            {
                fields.Add(value.ToString());
                value.Clear();
                continue;
            }
            if (ch is '\r' or '\n')
            {
                fields.Add(value.ToString());
                rows.Add(fields.ToArray());
                fields.Clear();
                value.Clear();
                if (ch == '\r' && index + 1 < text.Length && text[index + 1] == '\n') index++;
                continue;
            }
            value.Append(ch);
        }

        if (quoted) throw new FormatException("历史 CSV 中存在未闭合的双引号。");
        if (value.Length > 0 || fields.Count > 0)
        {
            fields.Add(value.ToString());
            rows.Add(fields.ToArray());
        }
        return rows;
    }

    private static int FindColumn(IReadOnlyList<string> header, params string[] names)
    {
        for (var index = 0; index < header.Count; index++)
        {
            if (names.Any(name => string.Equals(header[index], name, StringComparison.OrdinalIgnoreCase))) return index;
        }
        return -1;
    }

    private static string Field(string[] row, int index) => index >= 0 && index < row.Length ? row[index] : "";

    private static bool IsUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeFtp);
}
