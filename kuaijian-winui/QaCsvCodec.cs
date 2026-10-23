namespace KuaiJian.WinUI;

public static class QaCsvCodec
{
    public const string Header = "问题,答案,别名,关键词,分类,备注,是否启用";

    public static string Serialize(IEnumerable<QaEntry> entries)
    {
        var rows = new List<string> { Header };
        rows.AddRange(entries.Select(entry => string.Join(",", new[]
        {
            entry.Question, entry.Answer, entry.Aliases, entry.Keywords,
            entry.Category, entry.Note, entry.Enabled ? "是" : "否"
        }.Select(Escape))));
        return string.Join("\n", rows);
    }

    public static string Escape(string? value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";

    public static List<string[]> ParseRows(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var rows = new List<string[]>();
        var fields = new List<string>();
        var value = new System.Text.StringBuilder();
        var quoted = false;
        var afterQuote = false;

        void FinishField()
        {
            fields.Add(value.ToString());
            value.Clear();
            afterQuote = false;
        }

        void FinishRow()
        {
            FinishField();
            rows.Add(fields.ToArray());
            fields.Clear();
        }

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (quoted)
            {
                if (character == '"' && index + 1 < text.Length && text[index + 1] == '"')
                {
                    value.Append('"');
                    index++;
                }
                else if (character == '"')
                {
                    quoted = false;
                    afterQuote = true;
                }
                else value.Append(character);
                continue;
            }

            if (afterQuote)
            {
                if (character == ',') FinishField();
                else if (character is '\r' or '\n')
                {
                    FinishRow();
                    if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n') index++;
                }
                else if (character is ' ' or '\t') { }
                else throw new FormatException("CSV 引号后包含无效字符。");
                continue;
            }

            if (character == ',') FinishField();
            else if (character is '\r' or '\n')
            {
                FinishRow();
                if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n') index++;
            }
            else if (character == '"')
            {
                if (value.Length != 0) throw new FormatException("CSV 字段中的引号必须成对转义。");
                quoted = true;
            }
            else value.Append(character);
        }

        if (quoted) throw new FormatException("CSV 文件包含未闭合的引号。");
        if (afterQuote || value.Length > 0 || fields.Count > 0) FinishRow();
        return rows;
    }
}
