namespace KuaiJian.WinUI;

public static class QaImportMergePolicy
{
    public static int MergeInto(List<QaEntry> current, IEnumerable<QaEntry> incoming)
    {
        var byId = new Dictionary<string, QaEntry>(StringComparer.Ordinal);
        foreach (var entry in current)
            if (!string.IsNullOrWhiteSpace(entry.Id)) byId.TryAdd(entry.Id, entry);

        var inserted = new List<QaEntry>();
        var importedCount = 0;
        foreach (var entry in incoming)
        {
            entry.Id = string.IsNullOrWhiteSpace(entry.Id) ? Guid.NewGuid().ToString() : entry.Id;
            entry.Question ??= "";
            entry.Answer ??= "";
            entry.Aliases ??= "";
            entry.Keywords ??= "";
            entry.Category ??= "";
            entry.Note ??= "";
            if (string.IsNullOrWhiteSpace(entry.Question) && string.IsNullOrWhiteSpace(entry.Answer)) continue;

            if (byId.TryGetValue(entry.Id, out var existing))
            {
                existing.Question = entry.Question;
                existing.Answer = entry.Answer;
                existing.Aliases = entry.Aliases;
                existing.Keywords = entry.Keywords;
                existing.Category = entry.Category;
                existing.Note = entry.Note;
                existing.Enabled = entry.Enabled;
            }
            else
            {
                byId.Add(entry.Id, entry);
                inserted.Add(entry);
            }
            importedCount++;
        }
        inserted.Reverse();
        current.InsertRange(0, inserted);
        return importedCount;
    }
}
